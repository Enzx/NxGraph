using System.Diagnostics;
using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Diagnostics.Replay;
using NxGraph.Graphs;

namespace NxGraph.Fsm.Async;

/// <summary>
/// An async state machine that executes a graph of nodes with full lifecycle/observer support.
/// </summary>
public class AsyncStateMachine<TAgent>(Graph graph, IAsyncStateMachineObserver? observer = null)
    : AsyncStateMachine(graph, observer), IAgentSettable<TAgent>
{
    private TAgent? _agent;
    private bool _hasAgent;

    /// <summary>
    /// Binds the agent (typed context) to this machine. It is applied to the graph's nodes
    /// immediately and re-applied at the start of every execution, so several machines can
    /// share one <see cref="Graph"/> with distinct contexts as long as their executions do
    /// not overlap. For concurrently-running machines, give each its own graph instance.
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
/// Non-generic AsyncStateMachine.
/// </summary>
public class AsyncStateMachine : AsyncState, ISubGraphProvider, IBlackboardBindable, IBlackboardSettable
{
    public readonly Graph Graph;
    private BlackboardContext _blackboards;

    IEnumerable<Graph> ISubGraphProvider.SubGraphs => [Graph];
    private readonly IAsyncStateMachineObserver? _observer;

    private int _statusInt = (int)ExecutionStatus.Created; // atomic state

    private NodeId _current;
    private readonly NodeId _initial;

    // Reset behaviour after reaching a terminal status.
    private RestartPolicy _restartPolicy = RestartPolicy.Auto;
    private Result _terminalResult = Result.Success;
    private int _executeGate;
    private readonly Func<string, CancellationToken, ValueTask> _cachedLogReportCallback;
    private readonly ILogReporter?[] _reporters; // indexed by NodeId.Index, resolved once at construction
    private readonly RetryPolicy[]? _retryPolicies; // graph-owned; null when no node declares one
    private int _attempts; // executions of the current node in this run
    private bool _stepping; // a StepAsync-driven run is in flight (status stays Running between steps)
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

    /// <summary>Public execution status (volatile for visibility).</summary>
    public ExecutionStatus Status => (ExecutionStatus)Volatile.Read(ref _statusInt);

    public AsyncStateMachine(Graph graph, IAsyncStateMachineObserver? observer = null)
    {
        Graph = graph;
        _observer = observer;
        _initial = graph.StartNode.Id;
        _current = _initial;
        _cachedLogReportCallback = LogReportCallback;
        _reporters = BuildReporterTable(graph);
        _retryPolicies = graph.RetryPolicies;
        _outcomeCodes = graph.OutcomeCodes;
        // _statusInt already initialized to Created at field declaration; no transition needed.
    }

    /// <summary>
    /// Hook for derived machines to (re)apply per-machine execution context (e.g. the typed
    /// agent) to the graph's nodes. Runs under the execute gate, right before Starting.
    /// The base implementation re-stamps the bound blackboard context.
    /// </summary>
    private protected virtual void ApplyExecutionContext()
    {
        if (!_blackboards.IsEmpty)
        {
            Graph.SetBlackboards(in _blackboards);
        }
    }

    /// <summary>
    /// Binds a blackboard into the scope slot its schema declares (replace semantics — like
    /// <c>SetAgent</c>, rebinding swaps the board, which enables machine pooling). Applied to
    /// the graph's nodes immediately and re-applied at the start of every execution.
    /// Validated against the graph's schema declarations when present.
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

    private int OutcomeOf(NodeId id) => _outcomeCodes is null ? 0 : _outcomeCodes[id.Index];

    private static ILogReporter?[] BuildReporterTable(Graph graph)
    {
        ILogReporter?[] table = new ILogReporter?[graph.NodeCount];
        for (int i = 0; i < table.Length; i++)
        {
            if (graph.TryGetNodeByIndex(i, out INode? node) && node is LogicNode logicNode)
            {
                table[i] = logicNode.AsyncLogic as ILogReporter ?? logicNode.Logic as ILogReporter;
            }
        }

        return table;
    }

    /// <summary>
    /// Controls how the machine behaves after reaching a terminal status.
    /// <list type="bullet">
    /// <item><see cref="RestartPolicy.Auto"/>: automatically resets to Ready after completion/failure/cancellation.</item>
    /// <item><see cref="RestartPolicy.Manual"/>: requires explicit <see cref="Reset"/>; re-execution throws.</item>
    /// <item><see cref="RestartPolicy.Ignore"/>: ignores further execution attempts and returns the terminal result until <see cref="Reset"/> is called.</item>
    /// </list>
    /// </summary>
    public void SetRestartPolicy(RestartPolicy policy) => _restartPolicy = policy;

    /// <summary>
    /// Backwards-compatible switch for the legacy auto-reset behaviour.
    /// <para>Maps to <see cref="RestartPolicy.Auto"/> (true) and <see cref="RestartPolicy.Manual"/> (false).</para>
    /// </summary>
    public void SetAutoReset(bool enabled) => _restartPolicy = enabled ? RestartPolicy.Auto : RestartPolicy.Manual;

    /// <summary>
    /// Reset to the initial node. Disallowed while Running/Transitioning.
    /// Status moves: (any non-running) -> Resetting -> Ready.
    /// </summary>
    /// <remarks>
    /// Public entry point acquires <see cref="_executeGate"/> so an external caller
    /// cannot race a running machine. Internal auto-restart paths must call
    /// <see cref="ResetCore"/> directly because the gate is already held by the
    /// surrounding <see cref="OnEnterAsync"/>/<see cref="OnRunAsync"/>/<see cref="OnExitAsync"/> frame.
    /// </remarks>
    public async ValueTask<Result> Reset(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _executeGate, 1) == 1)
        {
            throw new InvalidOperationException(
                "Cannot reset while the machine is executing. Await completion before calling Reset.");
        }

        try
        {
            return await ResetCore(ct).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _executeGate, 0);
        }
    }

    private async ValueTask<Result> ResetCore(CancellationToken ct)
    {
        while (true)
        {
            ExecutionStatus status = Status;
            switch (status)
            {
                case ExecutionStatus.Running:
                case ExecutionStatus.Transitioning:
                case ExecutionStatus.Starting:
                    throw new InvalidOperationException("Cannot reset while starting, running, or transitioning.");
                case ExecutionStatus.Created:
                case ExecutionStatus.Ready:
                    return Result.Success;
                case ExecutionStatus.Resetting:
                    // Another caller is already resetting — be idempotent rather than firing duplicate notifications.
                    return Result.Success;
                case ExecutionStatus.Completed:
                case ExecutionStatus.Failed:
                case ExecutionStatus.Cancelled:
                    break;
                default:
                    throw new IndexOutOfRangeException(nameof(status));
            }

            // Own the transition to Resetting from whatever terminal state we observed.
            if (!TryTransition(status, ExecutionStatus.Resetting, out ExecutionStatus previous))
            {
                continue;
            }

            await NotifyMachineStatusChangeAsync(previous, ExecutionStatus.Resetting).ConfigureAwait(false);
            break; // we won; proceed
            // else: raced with another transition; retry
        }

        _current = _initial;
        _attempts = 0;
        _nodeEntered = false;

        await TransitionTo(ExecutionStatus.Ready).ConfigureAwait(false);
        return Result.Success;
    }

    protected override async ValueTask<Result> OnEnterAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _executeGate, 1) == 1) // Prevent re-entrance
        {
            throw new InvalidOperationException("AsyncStateMachine is already executing.");
        }

        if (_stepping)
        {
            Volatile.Write(ref _executeGate, 0);
            throw new InvalidOperationException(
                "A stepped run is in progress. Finish it with StepAsync or Reset() before calling ExecuteAsync.");
        }

        // If we're terminal, apply reset policy.
        ExecutionStatus status = Status;
        if (status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            switch (_restartPolicy)
            {
                case RestartPolicy.Ignore:
                    // Ignore further execution attempts until a manual Reset() occurs.
                    // Keep the gate held so a concurrent caller cannot enter while this
                    // ExecuteAsync is still on its way to OnRunAsync (which returns the
                    // cached terminal result) and OnExitAsync (which releases the gate).
                    return Result.InProgress;
                case RestartPolicy.Manual:
                    Volatile.Write(ref _executeGate, 0); // Release the gate before throwing.
                    throw new InvalidOperationException(
                        $"AsyncStateMachine is in terminal state '{status}'. Call Reset() before executing again.");
                default:
                    // Auto: tolerate unexpected terminal state by resetting on entry.
                    // ResetCore (not Reset) because OnEnterAsync already holds the gate.
                    await ResetCore(ct).ConfigureAwait(false);
                    break;
            }
        }

        // Re-stamp this machine's context onto the (potentially shared) graph before running.
        ApplyExecutionContext();

        // First entry or re-entry after Ready: Starting -> (enter first node) -> Running
        await TransitionTo(ExecutionStatus.Starting).ConfigureAwait(false);
        _current = _initial;
        _attempts = 0;
        _nodeEntered = false;
        LastOutcome = 0;
        if (_observer is not null)
        {
            await _observer.OnStateEntered(_current, ct).ConfigureAwait(false);
        }

        await TransitionTo(ExecutionStatus.Running).ConfigureAwait(false);
        
        return Result.InProgress;
    }

    protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        // Ignore policy: once terminal, do not re-run.
        ExecutionStatus status = Status;
        if (_restartPolicy == RestartPolicy.Ignore &&
            status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            return _terminalResult;
        }

        try
        {
            Result result = await InternalRunAsync(ct).ConfigureAwait(false);
            _terminalResult = result;

            // If we get here, loop has returned a terminal result already.
            // Machine completion notification + optional auto-reset-to-Ready.
            await TransitionTo(result == Result.Success ? ExecutionStatus.Completed : ExecutionStatus.Failed)
                .ConfigureAwait(false);

            if (_restartPolicy == RestartPolicy.Auto)
            {
                // ResetCore (not Reset) because the gate is held by the surrounding ExecuteAsync.
                await ResetCore(ct).ConfigureAwait(false);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            // TransitionTo(Cancelled) already fires OnStateMachineCancelled via NotifyMachineStatusChangeAsync.
            await TransitionTo(ExecutionStatus.Cancelled).ConfigureAwait(false);

            _terminalResult = Result.Failure;

            if (_restartPolicy == RestartPolicy.Auto)
            {
                // CancellationToken.None: ct is already cancelled; passing it would make
                // any cancellable await inside the reset throw, stranding status at Resetting.
                await ResetCore(CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception ex)
        {
            // Unexpected error outside node execution (node failures are returned as Failure below)
            if (_observer is not null)
                await _observer.OnStateFailed(_current, ex, ct).ConfigureAwait(false);

            // TransitionTo(Failed) already fires OnStateMachineCompleted(Failure) via NotifyMachineStatusChangeAsync.
            await TransitionTo(ExecutionStatus.Failed).ConfigureAwait(false);

            _terminalResult = Result.Failure;

            if (_restartPolicy == RestartPolicy.Auto)
            {
                // CancellationToken.None: ct may already be cancelled/faulted; reset must complete cleanly.
                await ResetCore(CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }

    protected override ValueTask<Result> OnExitAsync(CancellationToken ct)
    {
        // For Ignore policy, OnEnterAsync returns early with the gate held.
        // We want OnRunAsync to run and return the terminal result, and then
        // OnExitAsync will release the gate in the normal way.
        Volatile.Write(ref _executeGate, 0);
        return ResultHelpers.Success;
    }

    /// <summary>
    /// Captures the machine's runtime position into a serializable snapshot. Legal only at a
    /// step boundary: between <see cref="StepAsync"/> calls of a stepped run, or on a machine
    /// that is not currently executing. Suspending never touches the run loop.
    /// </summary>
    public StateMachineSnapshot Suspend()
    {
        if (Interlocked.Exchange(ref _executeGate, 1) == 1)
        {
            throw new InvalidOperationException(
                "Cannot suspend while the machine is executing. Suspend between StepAsync calls.");
        }

        try
        {
            ExecutionStatus status = Status;
            if (status is ExecutionStatus.Starting or ExecutionStatus.Transitioning or ExecutionStatus.Resetting)
            {
                throw new InvalidOperationException($"Cannot suspend in transient status '{status}'.");
            }

            return new StateMachineSnapshot(_current.Index, status, _attempts, _nodeEntered, _stepping, LastOutcome);
        }
        finally
        {
            Volatile.Write(ref _executeGate, 0);
        }
    }

    /// <summary>
    /// Restores a snapshot produced by <see cref="Suspend"/> onto this machine. The graph must
    /// be structurally equivalent to the one the snapshot was taken from (same node indices).
    /// A mid-run snapshot is continued with <see cref="StepAsync"/> until it returns a terminal
    /// result; re-attach the user context via <c>SetAgent</c> before stepping if the graph
    /// uses one. No observer events are replayed by the restore itself.
    /// </summary>
    public void Resume(StateMachineSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (Interlocked.Exchange(ref _executeGate, 1) == 1)
        {
            throw new InvalidOperationException("Cannot resume while the machine is executing.");
        }

        try
        {
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
            _stepping = snapshot.MidRun;
            LastOutcome = snapshot.LastOutcome;
            Volatile.Write(ref _statusInt, (int)snapshot.Status);
        }
        finally
        {
            Volatile.Write(ref _executeGate, 0);
        }
    }

    /// <summary>
    /// Advances the machine by exactly one node execution, mirroring the sync machine's
    /// frame-stepped <c>Execute()</c>. Returns <see cref="Result.InProgress"/> while more nodes
    /// remain and the terminal result when the run finishes. The first call begins a run
    /// (applying the restart policy exactly like <c>ExecuteAsync</c>); between steps the
    /// machine stays <see cref="ExecutionStatus.Running"/>. Observer events and terminal
    /// status transitions are identical to a full <c>ExecuteAsync</c> run.
    /// </summary>
    public async ValueTask<Result> StepAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _executeGate, 1) == 1)
        {
            throw new InvalidOperationException("AsyncStateMachine is already executing.");
        }

        try
        {
            if (!_stepping)
            {
                ExecutionStatus status = Status;
                if (status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
                {
                    switch (_restartPolicy)
                    {
                        case RestartPolicy.Ignore:
                            return _terminalResult;
                        case RestartPolicy.Manual:
                            throw new InvalidOperationException(
                                $"AsyncStateMachine is in terminal state '{status}'. Call Reset() before stepping again.");
                        default:
                            await ResetCore(ct).ConfigureAwait(false);
                            break;
                    }
                }

                ApplyExecutionContext();

                await TransitionTo(ExecutionStatus.Starting).ConfigureAwait(false);
                _current = _initial;
                _attempts = 0;
                _nodeEntered = false;
                LastOutcome = 0;
                if (_observer is not null)
                {
                    await _observer.OnStateEntered(_current, ct).ConfigureAwait(false);
                }

                await TransitionTo(ExecutionStatus.Running).ConfigureAwait(false);
                _stepping = true;
            }

            Result result;
            try
            {
                result = await StepCoreAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _stepping = false;
                await TransitionTo(ExecutionStatus.Cancelled).ConfigureAwait(false);
                _terminalResult = Result.Failure;
                if (_restartPolicy == RestartPolicy.Auto)
                {
                    await ResetCore(CancellationToken.None).ConfigureAwait(false);
                }

                throw;
            }
            catch (Exception ex)
            {
                _stepping = false;
                if (_observer is not null)
                {
                    await _observer.OnStateFailed(_current, ex, ct).ConfigureAwait(false);
                }

                await TransitionTo(ExecutionStatus.Failed).ConfigureAwait(false);
                _terminalResult = Result.Failure;
                if (_restartPolicy == RestartPolicy.Auto)
                {
                    await ResetCore(CancellationToken.None).ConfigureAwait(false);
                }

                throw;
            }

            if (!result.IsCompleted)
            {
                return Result.InProgress;
            }

            _stepping = false;
            _terminalResult = result;
            await TransitionTo(result == Result.Success ? ExecutionStatus.Completed : ExecutionStatus.Failed)
                .ConfigureAwait(false);

            if (_restartPolicy == RestartPolicy.Auto)
            {
                await ResetCore(ct).ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            Volatile.Write(ref _executeGate, 0);
        }
    }

    private async ValueTask<Result> InternalRunAsync(CancellationToken ct)
    {
        while (true)
        {
            Result step = await StepCoreAsync(ct).ConfigureAwait(false);
            if (step.IsCompleted)
            {
                return step;
            }
        }
    }

    /// <summary>
    /// Advances the machine by exactly one node execution. Returns <see cref="Result.InProgress"/>
    /// while more nodes remain (including in-place retries and failure-edge reroutes) and a
    /// terminal <see cref="Result.Success"/>/<see cref="Result.Failure"/> when the run finished.
    /// </summary>
    private async ValueTask<Result> StepCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        {
            if (!Graph.TryGetNode(_current, out INode? node))
            {
                throw new InvalidOperationException($"Node '{_current}' not found.");
            }

            ILogReporter? reporter = _reporters[_current.Index];
            if (reporter is not null)
            {
                // Reassigned on every visit so interleaved machines sharing a graph each
                // attribute log reports to their own observer.
                reporter.LogReport = _cachedLogReportCallback;
            }

            LogicNode logic = (LogicNode)node;
            if (!_nodeEntered)
            {
                _nodeEntered = true;
                logic.EnterAction?.Invoke();
            }

            Result result = await logic.AsyncLogic.ExecuteAsync(ct).ConfigureAwait(false);
            _attempts++;
            switch (result.Code)
            {
                case Result.StatusCode.Success:
                {
                    _nodeEntered = false;
                    logic.ExitAction?.Invoke();

                    if (_observer is not null)
                    {
                        await _observer.OnStateExited(_current, ct).ConfigureAwait(false);
                    }

                    NodeId next;

                    if (logic.AsyncLogic is IAsyncDirector director)
                    {
                        next = await director.SelectNextAsync(ct).ConfigureAwait(false);
                        if (next.Equals(NodeId.Default))
                        {
                            LastOutcome = OutcomeOf(_current);
                            return Result.Success;
                        }
                    }
                    // Sync directors (ChoiceState/SwitchState behind a SyncLogicAdapter) route
                    // here too — mirroring the sync runtime's `Logic is IDirector` check, and
                    // matching the validator/exporter, which already probe both logic slots.
                    else if (logic.Logic is IDirector syncDirector)
                    {
                        next = syncDirector.SelectNext();
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
                            if (_observer is not null)
                            {
                                await _observer.OnStateFailed(
                                    _current,
                                    new InvalidOperationException($"No transition found for state '{_current}'."),
                                    ct
                                ).ConfigureAwait(false);
                            }

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

                    await TransitionTo(ExecutionStatus.Transitioning).ConfigureAwait(false);

                    if (_observer is not null)
                    {
                        await _observer.OnTransition(_current, next, ct).ConfigureAwait(false);
                    }

                    _current = next;
                    _attempts = 0;

                    if (_observer is not null)
                    {
                        await _observer.OnStateEntered(_current, ct).ConfigureAwait(false);
                    }

                    await TransitionTo(ExecutionStatus.Running).ConfigureAwait(false);

                    return Result.InProgress;
                }
                case Result.StatusCode.Failure:
                {
                    if (_retryPolicies is not null)
                    {
                        RetryPolicy retry = _retryPolicies[_current.Index];
                        if (_attempts < retry.MaxAttempts)
                        {
                            TimeSpan delay = retry.DelayForAttempt(_attempts);
                            if (delay > TimeSpan.Zero)
                            {
                                await Task.Delay(delay, ct).ConfigureAwait(false);
                            }

                            return Result.InProgress;
                        }
                    }

                    _nodeEntered = false;
                    logic.ExitAction?.Invoke();

                    if (!Graph.TryGetTransition(_current, out Transition failEdge) ||
                        !failEdge.HasFailureDestination)
                    {
                        LastOutcome = OutcomeOf(_current);
                        return Result.Failure;
                    }

                    if (_observer is not null)
                    {
                        await _observer.OnStateFailed(_current, null, ct).ConfigureAwait(false);
                    }

                    NodeId handler = failEdge.FailureDestination;

                    await TransitionTo(ExecutionStatus.Transitioning).ConfigureAwait(false);

                    if (_observer is not null)
                    {
                        await _observer.OnTransition(_current, handler, ct).ConfigureAwait(false);
                    }

                    _current = handler;
                    _attempts = 0;

                    if (_observer is not null)
                    {
                        await _observer.OnStateEntered(_current, ct).ConfigureAwait(false);
                    }

                    await TransitionTo(ExecutionStatus.Running).ConfigureAwait(false);

                    return Result.InProgress;
                }
                case Result.StatusCode.InProgress:
                    throw new InvalidOperationException(
                        $"Node '{_current}' returned Result.InProgress, which is reserved for the " +
                        "stepped-execution runtime. Node logic must return Success or Failure.");
                default:
                    throw new InvalidOperationException("Unknown node result.");
            }
        }
    }

    private async ValueTask LogReportCallback(string message, CancellationToken ct)
    {
        if (_observer is not null)
        {
            await _observer.OnLogReport(_current, message, ct).ConfigureAwait(false);
        }
    }

    private bool TryTransition(ExecutionStatus from, ExecutionStatus to, out ExecutionStatus prev)
    {
        int f = (int)from, t = (int)to;
        int seen = Interlocked.CompareExchange(ref _statusInt, t, f);
        prev = (ExecutionStatus)seen;

        return seen == f;
    }

    /// <summary>
    /// Centralized status transition. Sets the status unconditionally and then
    /// kicks machine-lifecycle notifications outside the lock.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="Interlocked.Exchange(ref int, int)"/> (unconditional) rather than CAS.
    /// Only call this when the caller owns the execution context (i.e. from the
    /// run-loop or enter/exit methods that are guarded by <see cref="_executeGate"/>).
    /// For external transitions that must validate the predecessor state, use
    /// <see cref="TryTransition"/> instead.
    /// </remarks>
    private async ValueTask TransitionTo(ExecutionStatus next)
    {
        ExecutionStatus prev = (ExecutionStatus)Interlocked.Exchange(ref _statusInt, (int)next);
        Debug.Assert(prev != next, $"Redundant status transition: {prev} -> {next}");
        if (prev != next && _observer is not null)
        {
            await NotifyMachineStatusChangeAsync(prev, next).ConfigureAwait(false);
        }
    }

    private async ValueTask NotifyMachineStatusChangeAsync(ExecutionStatus prev, ExecutionStatus next)
    {
        if (_observer is null)
        {
            return;
        }

        NodeId graphId = Graph.Id;
        await _observer.StateMachineStatusChanged(graphId, prev, next).ConfigureAwait(false);
        switch (next)
        {
            case ExecutionStatus.Starting:
                await _observer.OnStateMachineStarted(graphId).ConfigureAwait(false);
                break;

            case ExecutionStatus.Completed:
                await _observer.OnStateMachineCompleted(graphId, Result.Success).ConfigureAwait(false);
                break;

            case ExecutionStatus.Failed:
                await _observer.OnStateMachineCompleted(graphId, Result.Failure).ConfigureAwait(false);
                break;

            case ExecutionStatus.Cancelled:
                await _observer.OnStateMachineCancelled(graphId).ConfigureAwait(false);
                break;

            case ExecutionStatus.Resetting:
                await _observer.OnStateMachineReset(graphId).ConfigureAwait(false);
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