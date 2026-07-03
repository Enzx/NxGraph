using System.Diagnostics;
using System.Runtime.CompilerServices;
using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// A synchronous state machine with an agent, mirroring <see cref="AsyncStateMachine{TAgent}"/>.
/// </summary>
public class StateMachine<TAgent>(Graph graph, IStateMachineObserver? observer = null)
    : StateMachine(graph, observer), IAgentSettable<TAgent>
{
    private TAgent? _agent;
    private bool _hasAgent;

    /// <summary>
    /// Binds the agent (typed context) to this machine. It is applied to the graph's nodes
    /// immediately and re-applied at the start of every run, so several machines can share
    /// one <see cref="Graph"/> with distinct contexts as long as their runs do not interleave.
    /// </summary>
    public void SetAgent(TAgent agent)
    {
        _agent = agent;
        _hasAgent = true;
        Graph.SetAgent(agent);
    }

    private protected override void ApplyExecutionContext()
    {
        base.ApplyExecutionContext(); // blackboards first, then the agent
        if (_hasAgent)
        {
            Graph.SetAgent(_agent!);
        }
    }
}

/// <summary>
/// Synchronous, zero-allocation state machine that inherits <see cref="State"/> so it
/// can be nested as a node inside a parent graph — mirroring the
/// <see cref="AsyncStateMachine"/> → <see cref="AsyncState"/> relationship.
/// <para>
/// All threading overhead has been removed:
/// <list type="bullet">
///   <item><c>_status</c> is a plain field (no <see cref="Volatile"/>/<see cref="Interlocked"/>).</item>
///   <item><c>_executeGate</c> is a plain <see langword="bool"/>.</item>
///   <item><c>_autoReset</c> is a plain <see langword="bool"/>.</item>
///   <item>No <see cref="CancellationToken"/>, no <c>async</c>/<c>await</c>, no <c>ValueTask</c>.</item>
///   <item>Observer callbacks are <c>void</c> (see <see cref="IStateMachineObserver"/>).</item>
/// </list>
/// </para>
/// <para>
/// <b>Stepped execution</b> — call the inherited <see cref="State.Execute"/> once per
/// frame (e.g. from Unity's <c>Update</c> method):
/// <code>Result r = fsm.Execute(); // Continue while running, Success/Failure when done</code>
/// Each call advances exactly one node. Nested <see cref="StateMachine"/> instances
/// used as nodes are also stepped one level at a time.
/// To run to completion in a non-Unity context, loop until the result is not Continue.
/// </para>
/// <para>
/// Node logic is executed via <see cref="ILogic.Execute"/> — every node in the graph
/// must have its <see cref="INode.Logic"/> populated (i.e. implement <see cref="ILogic"/>).
/// </para>
/// </summary>
public class StateMachine : State, ISubGraphProvider, IBlackboardBindable, IBlackboardSettable
{
    public readonly Graph Graph;
    private BlackboardContext _blackboards;

    IEnumerable<Graph> ISubGraphProvider.SubGraphs => [Graph];
    private readonly IStateMachineObserver? _observer;

    private ExecutionStatus _status = ExecutionStatus.Created;

    private NodeId _current;
    private readonly NodeId _initial;

    private RestartPolicy _restartPolicy = RestartPolicy.Auto;
    private Result _terminalResult = Result.Success;
    private bool _executeGate;
    // Guards OnRun against reentrance from inside node logic that calls Execute() on the
    // owning machine. _executeGate alone is not sufficient: State.Execute() only invokes
    // OnEnter once per lifecycle (via its own _hasEntered flag), so a re-entrant Execute()
    // skips OnEnter entirely and never observes _executeGate.
    private bool _reentranceGuard;
    private readonly Action<string> _cachedLogReportCallback;
    private readonly State?[] _logReportStates; // indexed by NodeId.Index, resolved once at construction
    private readonly RetryPolicy[]? _retryPolicies; // graph-owned; null when no node declares one
    private int _attempts; // executions of the current node in this run
    private bool _nodeEntered; // the current node's EnterAction has fired for this visit
    private readonly int[]? _outcomeCodes; // graph-owned; null when no node declares one

    /// <summary>
    /// The outcome code of the node the last run terminated at (default 0).
    /// Reset when a new run starts.
    /// </summary>
    public int LastOutcome { get; private set; }

    /// <summary>
    /// The display name registered for <see cref="LastOutcome"/>, or <c>null</c>.
    /// Resolved lazily — never on the run loop.
    /// </summary>
    public string? LastOutcomeName =>
        Graph.OutcomeNames is { } names && names.TryGetValue(LastOutcome, out string? name) ? name : null;

    /// <summary>Public execution status.</summary>
    public ExecutionStatus Status
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _status;
    }

    public StateMachine(Graph graph, IStateMachineObserver? observer = null)
    {
        Graph = graph;
        _observer = observer;
        _initial = graph.StartNode.Id;
        _current = _initial;
        _cachedLogReportCallback = LogReportCallback;
        _logReportStates = BuildLogReportTable(graph);
        _retryPolicies = graph.RetryPolicies;
        _outcomeCodes = graph.OutcomeCodes;
    }

    private int OutcomeOf(NodeId id) => _outcomeCodes is null ? 0 : _outcomeCodes[id.Index];

    /// <summary>
    /// Hook for derived machines to (re)apply per-machine execution context (e.g. the typed
    /// agent) to the graph's nodes. Runs under the execute gate, right before Starting.
    /// The base implementation re-stamps the bound blackboard context.
    /// </summary>
    private protected virtual void ApplyExecutionContext()
    {
        // Unconditional re-stamp, empty context included. Skipping the empty case let a
        // board-less machine sharing a Graph silently execute against the boards a previous
        // machine stamped — it must get the unbound-scope throw instead. Composite children
        // receive their context through IBlackboardSettable forwarding, so the empty re-stamp
        // no longer erases a parent's stamp.
        Graph.SetBlackboards(in _blackboards);
    }

    /// <summary>
    /// Binds a blackboard into the scope slot its schema declares (replace semantics — like
    /// <c>SetAgent</c>, rebinding swaps the board, which enables machine pooling). Applied to
    /// the graph's nodes immediately and re-applied at the start of every run. Validated
    /// against the graph's schema declarations when present.
    /// </summary>
    public void SetBlackboard(Blackboard blackboard)
    {
        Guard.NotNull(blackboard, nameof(blackboard));
        ValidateBoardAgainstDeclarations(blackboard);
        _blackboards = _blackboards.With(blackboard);
        Graph.SetBlackboards(in _blackboards);
    }

    /// <summary>
    /// Receives the whole context from a parent machine's stamping walk (nested composite
    /// path). Validates against this machine's own graph declarations, so a conflicting
    /// child schema fails loudly at stamp time.
    /// </summary>
    void IBlackboardSettable.SetBlackboards(in BlackboardContext context)
    {
        if (context.Graph is { } graphBoard)
        {
            ValidateBoardAgainstDeclarations(graphBoard);
        }

        if (context.Global is { } globalBoard)
        {
            ValidateBoardAgainstDeclarations(globalBoard);
        }

        _blackboards = context;
        Graph.SetBlackboards(in context);
    }

    private void ValidateBoardAgainstDeclarations(Blackboard blackboard)
    {
        BlackboardSchema? declared = blackboard.Schema.Scope == BlackboardScope.Global
            ? Graph.GlobalSchema
            : Graph.Schema;

        if (declared is not null && !ReferenceEquals(declared, blackboard.Schema))
        {
            throw new InvalidOperationException(
                $"Blackboard schema '{blackboard.Schema.Name ?? "<unnamed>"}' does not match the " +
                $"{blackboard.Schema.Scope} schema '{declared.Name ?? "<unnamed>"}' declared on graph '{Graph.Id}'.");
        }
    }

    private static State?[] BuildLogReportTable(Graph graph)
    {
        State?[] table = new State?[graph.NodeCount];
        for (int i = 0; i < table.Length; i++)
        {
            if (graph.TryGetNodeByIndex(i, out INode? node) && node is LogicNode logicNode)
            {
                table[i] = logicNode.Logic as State;
            }
        }

        return table;
    }

    /// <summary>
    /// Controls how the machine behaves after reaching a terminal status.
    /// <list type="bullet">
    /// <item><see cref="RestartPolicy.Auto"/>: automatically resets to Ready after completion/failure.</item>
    /// <item><see cref="RestartPolicy.Manual"/>: requires explicit <see cref="Reset"/>; re-execution throws.</item>
    /// <item><see cref="RestartPolicy.Ignore"/>: ignores further execution attempts and returns the terminal result until <see cref="Reset"/> is called.</item>
    /// </list>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResetPolicy(RestartPolicy policy) => _restartPolicy = policy;

    /// <summary>
    /// Backwards-compatible switch for the legacy auto-reset behaviour.
    /// <para>Maps to <see cref="RestartPolicy.Auto"/> (true) and <see cref="RestartPolicy.Manual"/> (false).</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetAutoReset(bool enabled) => _restartPolicy = enabled ? RestartPolicy.Auto : RestartPolicy.Manual;

    /// <summary>
    /// Reset to the initial node. Disallowed while Running/Transitioning.
    /// Status moves: (any non-running) → Resetting → Ready.
    /// </summary>
    public Result Reset()
    {
        ExecutionStatus status = _status;
        switch (status)
        {
            case ExecutionStatus.Running:
            case ExecutionStatus.Transitioning:
            case ExecutionStatus.Starting:
                throw new InvalidOperationException("Cannot reset while starting, running, or transitioning.");
            case ExecutionStatus.Created:
            case ExecutionStatus.Ready:
                return Result.Success;
            case ExecutionStatus.Completed:
            case ExecutionStatus.Failed:
            case ExecutionStatus.Cancelled:
            case ExecutionStatus.Resetting:
                break;
            default:
                throw new IndexOutOfRangeException(nameof(status));
        }

        TransitionTo(ExecutionStatus.Resetting);
        _current = _initial;
        _attempts = 0;
        _nodeEntered = false;
        TransitionTo(ExecutionStatus.Ready);
        return Result.Success;
    }

    /// <summary>
    /// Captures the machine's runtime position into a serializable snapshot. The sync runtime
    /// is frame-stepped, so any point between <see cref="State.Execute"/> calls is a legal
    /// step boundary — including the middle of a run (the machine stays Running between ticks).
    /// Only calling from <i>inside</i> node logic is rejected.
    /// </summary>
    public StateMachineSnapshot Suspend()
    {
        if (_reentranceGuard)
        {
            throw new InvalidOperationException(
                "Cannot suspend from inside node logic. Suspend between Execute() calls.");
        }

        ExecutionStatus status = _status;
        if (status is ExecutionStatus.Starting or ExecutionStatus.Transitioning or ExecutionStatus.Resetting)
        {
            throw new InvalidOperationException($"Cannot suspend in transient status '{status}'.");
        }

        return new StateMachineSnapshot(_current.Index, status, _attempts, _nodeEntered,
            MidRun: _executeGate, LastOutcome);
    }

    /// <summary>
    /// Restores a snapshot produced by <see cref="Suspend"/> onto this machine. The graph must
    /// be structurally equivalent to the one the snapshot was taken from (same node indices).
    /// A mid-run snapshot is continued by calling <see cref="State.Execute"/> until it returns
    /// a terminal result; re-attach the user context via <c>SetAgent</c> before resuming if
    /// the graph uses one. No observer events are replayed by the restore itself.
    /// </summary>
    public void Resume(StateMachineSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (_reentranceGuard)
        {
            throw new InvalidOperationException("Cannot resume from inside node logic.");
        }

        if (snapshot.Status is ExecutionStatus.Starting or ExecutionStatus.Transitioning
            or ExecutionStatus.Resetting)
        {
            throw new InvalidOperationException(
                $"Snapshot captured in transient status '{snapshot.Status}' cannot be resumed.");
        }

        if ((uint)snapshot.CurrentNodeIndex >= (uint)Graph.NodeCount)
        {
            throw new InvalidOperationException(
                $"Snapshot node index {snapshot.CurrentNodeIndex} is out of range for this graph " +
                $"(0..{Graph.NodeCount - 1}).");
        }

        _current = Graph.GetNodeByIndex(snapshot.CurrentNodeIndex).Id;
        _attempts = snapshot.Attempts;
        _nodeEntered = snapshot.NodeEntered;
        LastOutcome = snapshot.LastOutcome;
        // Reconstruct the cached terminal result so RestartPolicy.Ignore reports the
        // restored run's true outcome instead of the field initializer's Success.
        _terminalResult = snapshot.Status is ExecutionStatus.Failed or ExecutionStatus.Cancelled
            ? Result.Failure
            : Result.Success;
        // Mid-run: the gate stays held between ticks and the State lifecycle must skip
        // OnEnter (which would restart the run from the initial node) on the next Execute().
        _executeGate = snapshot.MidRun;
        HasEntered = snapshot.MidRun;
        _status = snapshot.Status;
    }

    // ── State overrides — Execute() is the stepped entry point ────────────
    // Call Execute() once per frame from Unity's Update().
    // It initialises on the first call, advances one node, and returns:
    //   Result.InProgress  — more nodes remain; call again next frame.
    //   Result.Success / Result.Failure — machine has finished.

    protected override void OnEnter()
    {
        ExecutionStatus status = _status;

        // Terminal state: apply restart policy before allowing a new run.
        if (status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            if (_restartPolicy == RestartPolicy.Ignore)
                return; // OnRun will return the cached result; no init needed
            if (_restartPolicy == RestartPolicy.Manual)
                throw new InvalidOperationException(
                    $"StateMachine is in terminal state '{status}'. Call Reset() before executing again.");
            Reset(); // Auto
        }

        if (_executeGate)
            throw new InvalidOperationException("StateMachine is already executing.");

        _executeGate = true;

        // Re-stamp this machine's context onto the (potentially shared) graph before running.
        ApplyExecutionContext();

        TransitionTo(ExecutionStatus.Starting);
        _current = _initial;
        _attempts = 0;
        _nodeEntered = false;
        LastOutcome = 0;
        _observer?.OnStateEntered(_current);
        TransitionTo(ExecutionStatus.Running);
    }

    protected override Result OnRun()
    {
        // Ignore policy: return cached result without advancing.
        ExecutionStatus status = _status;
        if (_restartPolicy == RestartPolicy.Ignore &&
            status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            return _terminalResult;
        }

        if (_reentranceGuard)
            throw new InvalidOperationException("StateMachine is already executing.");

        _reentranceGuard = true;
        try
        {
            Result result = TickInternal();
            if (result == Result.InProgress)
                return Result.InProgress; // not finished; caller will Execute() again next frame

            Finalise(result);
            return result;
        }
        catch (Exception ex)
        {
            _observer?.OnStateFailed(_current, ex);
            Finalise(Result.Failure);
            throw;
        }
        finally
        {
            _reentranceGuard = false;
        }
    }

    protected override void OnExit()
    {
        // Intentionally empty.
        // Finalise() releases _executeGate on the terminal path.
        // While Execute() returns Continue the machine stays Running and the gate
        // must remain held — so OnExit must not touch it.
    }



    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Finalise(Result result)
    {
        try
        {
            // Idempotent: if the first Finalise call partially completed (e.g. an observer
            // threw inside TransitionTo) the OnRun catch will call Finalise(Failure) again.
            // Skip the notification cascade in that case so OnStateMachineCompleted is not
            // fired twice for the same run.
            ExecutionStatus current = _status;
            if (current is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
            {
                return;
            }

            _terminalResult = result;
            TransitionTo(result.IsSuccess ? ExecutionStatus.Completed : ExecutionStatus.Failed);

            if (_restartPolicy == RestartPolicy.Auto)
            {
                Reset();
            }
        }
        finally
        {
            // Always release the gate, even if an observer or Reset threw. Without this the
            // gate stays held and every subsequent Execute() throws "already executing".
            _executeGate = false;
        }
    }

    /// <summary>
    /// Executes the current node. Returns <see cref="Result.InProgress"/> when the machine
    /// transitions to a next node, or a terminal result (<see cref="Result.Success"/> /
    /// <see cref="Result.Failure"/>) when the run is finished.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Result TickInternal()
    {
        if (!Graph.TryGetNode(_current, out INode? node))
        {
            throw new InvalidOperationException($"Node '{_current}' not found.");
        }

        LogicNode logicNode = (LogicNode)node;

        // Wire log-report callback for nodes that support it. Reassigned on every visit so
        // interleaved machines sharing a graph each attribute reports to their own observer.
        State? stateForLog = _logReportStates[_current.Index];
        if (stateForLog is not null)
        {
            stateForLog.SyncLogReport = _cachedLogReportCallback;
        }

        // Execute the node synchronously.
        ILogic syncLogic = logicNode.Logic
            ?? throw new InvalidOperationException(
                $"Node '{_current}' logic ({logicNode.AsyncLogic.GetType().Name}) does not implement ILogic. " +
                "All nodes in a StateMachine must implement ILogic.");

        if (!_nodeEntered)
        {
            _nodeEntered = true;
            logicNode.EnterAction?.Invoke();
        }

        Result result = syncLogic.Execute();
        if (result.Code != Result.StatusCode.InProgress)
        {
            _attempts++;
        }

        switch (result.Code)
        {
            case Result.StatusCode.Success:
            {
                _nodeEntered = false;
                logicNode.ExitAction?.Invoke();

                _observer?.OnStateExited(_current);

                NodeId next;

                if (logicNode.Logic is IDirector director)
                {
                    next = director.SelectNext();
                    if (next.Equals(NodeId.Default))
                    {
                        LastOutcome = OutcomeOf(_current);
                        return Result.Success;
                    }
                }
                else
                {
                    if (!Graph.TryGetTransition(_current, out Transition edge))
                    {
                        _observer?.OnStateFailed(
                            _current,
                            new InvalidOperationException($"No transition found for state '{_current}'."));

                        LastOutcome = OutcomeOf(_current);
                        return Result.Failure;
                    }

                    if (edge.IsEmpty)
                    {
                        LastOutcome = OutcomeOf(_current);
                        return Result.Success; // terminal
                    }

                    next = edge.Destination;
                }

                TransitionTo(ExecutionStatus.Transitioning);

                _observer?.OnTransition(_current, next);

                _current = next;
                _attempts = 0;

                _observer?.OnStateEntered(_current);

                TransitionTo(ExecutionStatus.Running);

                return Result.InProgress;
            }
            case Result.StatusCode.Failure:
            {
                if (_retryPolicies is not null)
                {
                    RetryPolicy retry = _retryPolicies[_current.Index];
                    if (_attempts < retry.MaxAttempts)
                    {
                        // Frame-stepped runtime: the retry happens on the next tick and the
                        // configured backoff is intentionally not honored (never block a frame).
                        return Result.InProgress;
                    }
                }

                _nodeEntered = false;
                logicNode.ExitAction?.Invoke();

                if (!Graph.TryGetTransition(_current, out Transition failEdge) ||
                    !failEdge.HasFailureDestination)
                {
                    LastOutcome = OutcomeOf(_current);
                    return Result.Failure;
                }

                _observer?.OnStateFailed(_current, null);

                NodeId handler = failEdge.FailureDestination;

                TransitionTo(ExecutionStatus.Transitioning);

                _observer?.OnTransition(_current, handler);

                _current = handler;
                _attempts = 0;

                _observer?.OnStateEntered(_current);

                TransitionTo(ExecutionStatus.Running);

                return Result.InProgress;
            }
            case Result.StatusCode.InProgress:
                // Node is still in progress (e.g. a multi-frame Unity state).
                // Tick() will call it again next frame; Execute()'s while-loop will spin.
                return Result.InProgress;
            default:
                throw new InvalidOperationException("Unknown node result.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LogReportCallback(string message)
    {
        _observer?.OnLogReport(_current, message);
    }

    /// <summary>
    /// Transitions the machine to a new execution status, invoking observer callbacks if the status changed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TransitionTo(ExecutionStatus next)
    {
        ExecutionStatus prev = _status;
        Debug.Assert(prev != next, $"Redundant status transition: {prev} -> {next}");
        _status = next;
        if (prev != next)
        {
            NotifyMachineStatusChange(prev, next);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NotifyMachineStatusChange(ExecutionStatus prev, ExecutionStatus next)
    {
        if (_observer is null)
        {
            return;
        }

        NodeId graphId = Graph.Id;
        _observer.StateMachineStatusChanged(graphId, prev, next);

        switch (next)
        {
            case ExecutionStatus.Starting:
                _observer.OnStateMachineStarted(graphId);
                break;

            case ExecutionStatus.Completed:
                _observer.OnStateMachineCompleted(graphId, Result.Success);
                break;

            case ExecutionStatus.Failed:
                _observer.OnStateMachineCompleted(graphId, Result.Failure);
                break;

            case ExecutionStatus.Resetting:
                _observer.OnStateMachineReset(graphId);
                break;

            case ExecutionStatus.Cancelled:
                // Synchronous StateMachine currently has no cancellation surface,
                // but tolerate the status for parity with the async machine.
                break;

            case ExecutionStatus.Ready:
            case ExecutionStatus.Created:
            case ExecutionStatus.Running:
            case ExecutionStatus.Transitioning:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(next), next, null);
        }
    }
}

