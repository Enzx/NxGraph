using System.Diagnostics;
using System.Runtime.CompilerServices;
using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Diagnostics.Replay;
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
public class StateMachine : State, ISubGraphProvider, IBlackboardBindable, IBlackboardSettable,
    ISuspendableComposite
{
    public readonly Graph Graph;
    private BlackboardContext _blackboards;

    // Machine-owned transient scratch created from the graph's declared Node schema; null when
    // none is declared, so board-less graphs pay nothing beyond a null check per transition.
    private readonly Blackboard? _nodeBoard;

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

    // Event dispatch (spec 013): node 0's dispatcher, cached at construction (log-report-table
    // precedent) so a raise never probes the graph; null for graphs without event entries.
    private readonly EventEntryState? _eventEntry;
    private NodeId _pendingEntry = NodeId.Default; // armed by a typed raise, consumed at run start

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
        Guard.NotNull(graph, nameof(graph));
        // Fail fast on graphs the sync runtime cannot execute, instead of throwing mid-run
        // after earlier nodes already produced side effects. The validator's StrictSyncOnly
        // option offers the same check as a lint.
        for (int i = 0; i < graph.NodeCount; i++)
        {
            if (graph.TryGetNodeByIndex(i, out INode? node) && node!.Logic is null)
            {
                throw new ArgumentException(
                    $"Node '{node.Id}' logic ({node.AsyncLogic.GetType().Name}) does not implement ILogic " +
                    "and cannot be executed by the sync StateMachine. Use the async runtime for this graph, " +
                    "or author the node with sync logic.", nameof(graph));
            }
        }

        Graph = graph;
        _observer = observer;
        _initial = graph.StartNode.Id;
        _current = _initial;
        _cachedLogReportCallback = LogReportCallback;
        _logReportStates = BuildLogReportTable(graph);
        _eventEntry = FindEventEntry(graph);
        _retryPolicies = graph.RetryPolicies;
        _outcomeCodes = graph.OutcomeCodes;
        if (graph.NodeSchema is { } nodeSchema)
        {
            _nodeBoard = new Blackboard(nodeSchema);
            _blackboards = new BlackboardContext(null, null, _nodeBoard);
        }
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
        ThrowIfNodeScoped(blackboard);
        ValidateBoardAgainstDeclarations(blackboard);
        _blackboards = _blackboards.With(blackboard);
        Graph.SetBlackboards(in _blackboards);
    }

    /// <summary>
    /// Receives the whole context from a parent machine's stamping walk (nested composite
    /// path). Validates against this machine's own graph declarations, so a conflicting
    /// child schema fails loudly at stamp time. The parent's Node slot is dropped — Node
    /// boards are per-machine scratch; this machine composes its own from its own graph.
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

        BlackboardContext own = context.WithoutNode();
        if (_nodeBoard is not null)
        {
            own = own.With(_nodeBoard);
        }

        _blackboards = own;
        Graph.SetBlackboards(in own);
    }

    private static void ThrowIfNodeScoped(Blackboard blackboard)
    {
        if (blackboard.Schema.Scope == BlackboardScope.Node)
        {
            throw new InvalidOperationException(
                "Node-scoped boards are machine-owned transient scratch and cannot be bound — " +
                "declare the schema on the graph via WithSchema(...) and each machine creates its own board.");
        }
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

    private static EventEntryState? FindEventEntry(Graph graph)
    {
        return graph.TryGetNodeByIndex(NodeId.Start.Index, out INode? node) && node is LogicNode logicNode
            ? logicNode.Logic as EventEntryState ?? logicNode.AsyncLogic as EventEntryState
            : null;
    }

    /// <summary>
    /// Stamps the pending event entry onto the dispatcher and clears the machine's field.
    /// Called at every run start, right after <see cref="ApplyExecutionContext"/> —
    /// unconditionally (<see cref="NodeId.Default"/> when no raise armed one), same
    /// philosophy as the blackboard re-stamp: a stale entry must never leak into a later
    /// plain run, and interleaved machines sharing a graph each stamp their own.
    /// </summary>
    private void StampPendingEntry()
    {
        if (_eventEntry is null)
        {
            return;
        }

        _eventEntry.SetPendingEntry(_pendingEntry);
        _pendingEntry = NodeId.Default;
    }

    // ── Typed event raise surface (spec 013) ────────────────────────────

    /// <summary>
    /// Raises a typed event, mirroring <see cref="AsyncStateMachine.ExecuteAsync{TEvent}"/>:
    /// resolves <typeparamref name="TEvent"/> against the graph's event entries, delivers the
    /// payload through the registration's blackboard key into this machine's bound boards
    /// (typed end-to-end — struct events never box), and arms a run at the entry's chain.
    /// The call advances the run like plain <see cref="State.Execute"/> (one node per tick by
    /// default); subsequent plain <c>Execute()</c> ticks continue it — normal frame-stepping.
    /// One event = one run: the machine must be idle, and the restart policies apply verbatim.
    /// </summary>
    public Result Execute<TEvent>(TEvent evt)
    {
        ArmRaise(evt);
        try
        {
            return Execute();
        }
        finally
        {
            // Consumed at run start; anything left means the run never started (terminal-Manual
            // throw, Ignore policy) — clear so the entry cannot leak into a later plain run.
            _pendingEntry = NodeId.Default;
        }
    }

    private void ArmRaise<TEvent>(TEvent evt)
    {
        EventEntryState? dispatcher = _eventEntry;
        if (dispatcher is null)
        {
            ThrowNoEventEntries();
        }

        // Best-effort idle guard — the execute gate in the plain path remains the real barrier.
        ExecutionStatus status = _status;
        if (status is ExecutionStatus.Starting or ExecutionStatus.Running or ExecutionStatus.Transitioning)
        {
            ThrowRaiseWhileExecuting();
        }

        _pendingEntry = dispatcher!.RaiseInto(in _blackboards, evt);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNoEventEntries() =>
        throw new InvalidOperationException(
            "This graph has no event entries — author it with GraphBuilder.StartWithEvents() to raise typed events.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowRaiseWhileExecuting() =>
        throw new InvalidOperationException(
            "Cannot raise an event while the machine is executing — an event starts a new run. Finish the " +
            "current run before raising.");

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
    public void SetRestartPolicy(RestartPolicy policy) => _restartPolicy = policy;

    /// <summary>
    /// Renamed alias of <see cref="SetRestartPolicy"/>. The sync and async machines shipped
    /// with two names for the same feature; both now converge on <c>SetRestartPolicy</c>,
    /// matching the <see cref="RestartPolicy"/> enum.
    /// </summary>
    [Obsolete("Renamed to SetRestartPolicy — the same feature carried two names across the sync/async machines. " +
              "This alias forwards and will not change behavior.")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResetPolicy(RestartPolicy policy) => SetRestartPolicy(policy);

    /// <summary>
    /// How <see cref="State.Execute"/> maps child work onto ticks when this machine is used
    /// directly or nested as a node: <see cref="ParallelStepMode.RoundPerTick"/> (default)
    /// advances exactly one node per call; <see cref="ParallelStepMode.RunToJoin"/> loops to
    /// the terminal result inside one call, which also makes the machine executable as a
    /// node under the async runtime via the sync-logic adapter.
    /// </summary>
    public ParallelStepMode StepMode { get; private set; } = ParallelStepMode.RoundPerTick;

    /// <summary>Sets <see cref="StepMode"/>. Machine-level runtime configuration — like the
    /// restart policy, it is not part of the graph structure and does not serialize.</summary>
    public void SetStepMode(ParallelStepMode mode) => StepMode = mode;

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
                // The gate is deliberately left alone here: a mid-run RoundPerTick machine
                // legitimately holds it between ticks.
                throw new InvalidOperationException("Cannot reset while starting, running, or transitioning.");
            case ExecutionStatus.Created:
            case ExecutionStatus.Ready:
                // Defensive gate release: under correct operation the gate is never held at
                // these statuses, so this is belt-and-braces against an enter-path throw
                // leaking it — Reset() must never report Success on a machine that would
                // still refuse to execute.
                _executeGate = false;
                return Result.Success;
            case ExecutionStatus.Resetting:
                // Another reset is already in flight — be idempotent rather than firing
                // duplicate notifications. Mirrors AsyncStateMachine.ResetCore.
                return Result.Success;
            case ExecutionStatus.Completed:
            case ExecutionStatus.Failed:
            case ExecutionStatus.Cancelled:
                break;
            default:
                // Unreachable enum-switch guard (CA2201: IndexOutOfRangeException is
                // runtime-reserved, so this defensive default uses a plain invalid-op).
                throw new InvalidOperationException($"Unexpected execution status '{status}'.");
        }

        TransitionTo(ExecutionStatus.Resetting);
        _current = _initial;
        _attempts = 0;
        _nodeEntered = false;
        _nodeBoard?.ResetToDefaults();
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
        Guard.NotNull(snapshot, nameof(snapshot));

        if (_reentranceGuard)
        {
            throw new InvalidOperationException("Cannot resume from inside node logic.");
        }

        ExecutionStatusValidation.ThrowIfUndefined(snapshot.Status);

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
        // Snapshots are primitives-only: Node-scoped scratch is transient by definition
        // and comes back as defaults after a resume.
        _nodeBoard?.ResetToDefaults();
        // A mid-run resume continues via Execute(), which skips the run-start init where
        // the context is normally stamped — stamp here so a fresh machine's own boards
        // (including the machine-owned Node board) reach the graph's nodes.
        ApplyExecutionContext();
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

    /// <summary>
    /// Deep variant of <see cref="Suspend"/>: captures this machine's position plus the
    /// internal state of every composite in its graph that holds durable state (nested
    /// machine positions, history's remembered child position, RoundPerTick mid-visit
    /// bookkeeping) into a <see cref="StateMachineDeepSnapshot"/>. Same gates as
    /// <see cref="Suspend"/> — legal between any two <see cref="State.Execute"/> ticks,
    /// mid-run included; the extra cost is one linear walk over the graph's nodes plus the
    /// composites' own recursive captures — cold path by contract, allocation is expected.
    /// The shallow pair and <see cref="StateMachineSnapshot"/> are untouched; use them when
    /// flows keep suspension points at the top level.
    /// </summary>
    public StateMachineDeepSnapshot SuspendDeep()
    {
        StateMachineSnapshot self = Suspend();
        return new StateMachineDeepSnapshot(self, DeepSnapshots.Capture(Graph));
    }

    /// <summary>
    /// Restores a snapshot produced by <see cref="SuspendDeep"/> onto this machine: first the
    /// machine's own position via <see cref="Resume"/> (all its gates and checks apply
    /// unchanged), then each captured composite via
    /// <see cref="ISuspendableComposite.ResumeComposite"/> after validating that the claimed
    /// node exists and implements the interface. A composite node absent from
    /// <see cref="StateMachineDeepSnapshot.Composites"/> re-enters fresh (sparse capture).
    /// Node-scoped scratch resumes as defaults at every nesting level.
    /// </summary>
    public void ResumeDeep(StateMachineDeepSnapshot snapshot)
    {
        Guard.NotNull(snapshot, nameof(snapshot));

        Resume(snapshot.Self);
        DeepSnapshots.ResumeComposites(Graph, snapshot.Composites);
    }

    // ── ISuspendableComposite — the sync machine nested as a node ─────────
    // A sync StateMachine used directly as node logic (.SubGraph(mode, child) without
    // history) persists cross-tick state under RoundPerTick: its child run is mid-flight
    // between parent ticks. It therefore captures itself as a single-child composite —
    // the mid-run flag rides inside the child snapshot's own MidRun, so InFlight stays
    // false. The async machine deliberately has no counterpart: it always runs its child
    // to terminal inside one ExecuteAsync and holds no durable visit state as a node.

    CompositeSnapshot ISuspendableComposite.SuspendComposite(int nodeIndex)
    {
        return new CompositeSnapshot(nodeIndex, InFlight: false, Done: [], Children: [SuspendDeep()]);
    }

    void ISuspendableComposite.ResumeComposite(CompositeSnapshot snapshot)
    {
        DeepSnapshots.ValidateShape(snapshot, expectedChildren: 1, expectedDoneBits: 0);
        DeepSnapshots.ResumeChild(this, snapshot, 0);
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

        // Everything after the gate acquisition can throw (ApplyExecutionContext's stamp-time
        // validation, and observer callbacks firing from TransitionTo/OnStateEntered — observer
        // exceptions bubble by design). State.Execute's enter-catch resets HasEntered but knows
        // nothing about the gate, so the gate must be released here or the machine is
        // permanently locked. Mirrors AsyncStateMachine.OnEnterAsync.
        try
        {
            // Re-stamp this machine's context onto the (potentially shared) graph before running.
            ApplyExecutionContext();
            StampPendingEntry();

            TransitionTo(ExecutionStatus.Starting);
            _current = _initial;
            _attempts = 0;
            _nodeEntered = false;
            _nodeBoard?.ResetToDefaults();
            LastOutcome = 0;
            _observer?.OnStateEntered(_current);
            TransitionTo(ExecutionStatus.Running);
        }
        catch
        {
            RepairTransientStatus();
            _executeGate = false;
            throw;
        }
    }

    /// <summary>
    /// If an observer threw while the machine was in a transient status (Starting/Resetting/
    /// Transitioning), restore Ready without notifications so the machine stays usable —
    /// Suspend/Reset reject transient statuses and a redundant re-transition would trip the
    /// debug assert in <see cref="TransitionTo"/>.
    /// </summary>
    private void RepairTransientStatus()
    {
        ExecutionStatus status = _status;
        if (status is ExecutionStatus.Starting or ExecutionStatus.Resetting or ExecutionStatus.Transitioning)
        {
            _status = ExecutionStatus.Ready;
        }
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
            if (StepMode == ParallelStepMode.RunToJoin)
            {
                // One-tick mode: complete the whole run inside this Execute() call. A node
                // that keeps returning InProgress (multi-tick state) busy-spins here — the
                // caller opted into the cost, mirroring ParallelState.RunToJoin.
                while (result == Result.InProgress)
                {
                    result = TickInternal();
                }
            }

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
        // interleaved machines sharing a graph each attribute reports to their own observer;
        // null when this machine has no observer, so nodes that gate report formatting on a
        // wired callback (behavior composites) pay nothing on observer-less machines.
        State? stateForLog = _logReportStates[_current.Index];
        if (stateForLog is not null)
        {
            stateForLog.SyncLogReport = _observer is null ? null : _cachedLogReportCallback;

            // Both slots are machine-owned per visit: State.Log (and the behavior-composite
            // report bridge) falls back to the async slot when the sync one is null, so a
            // callback left by an async machine that ran this shared graph earlier must not
            // receive reports from this run.
            ((ILogReporter)stateForLog).LogReport = null;
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
                // New visit begins: transient Node-scoped scratch resets with the
                // attempt counter (in-place retries keep both).
                _nodeBoard?.ResetToDefaults();

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
                _nodeBoard?.ResetToDefaults();

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

