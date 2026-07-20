using System.Diagnostics;
using System.Runtime.CompilerServices;
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

    // Machine-owned transient scratch created from the graph's declared Node schema; null when
    // none is declared, so board-less graphs pay nothing beyond a null check per transition.
    private readonly Blackboard? _nodeBoard;

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

    // Event dispatch (spec 013): node 0's dispatcher, cached at construction (BuildReporterTable
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
        _eventEntry = FindEventEntry(graph);
        _retryPolicies = graph.RetryPolicies;
        _outcomeCodes = graph.OutcomeCodes;
        if (graph.NodeSchema is { } nodeSchema)
        {
            _nodeBoard = new Blackboard(nodeSchema);
            _blackboards = new BlackboardContext(null, null, _nodeBoard);
        }
        // _statusInt already initialized to Created at field declaration; no transition needed.
    }

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
    /// the graph's nodes immediately and re-applied at the start of every execution.
    /// Validated against the graph's schema declarations when present.
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

    private int OutcomeOf(NodeId id) => _outcomeCodes is null ? 0 : _outcomeCodes[id.Index];

    private static EventEntryState? FindEventEntry(Graph graph)
    {
        return graph.TryGetNodeByIndex(NodeId.Start.Index, out INode? node) && node is LogicNode logicNode
            ? logicNode.AsyncLogic as EventEntryState ?? logicNode.Logic as EventEntryState
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
    /// Runs the machine to its terminal result (see <see cref="AsyncState.ExecuteAsync"/>).
    /// Declared here (hiding the base method by forwarding) so overload resolution keeps
    /// routing <c>ExecuteAsync()</c>/<c>ExecuteAsync(ct)</c> to the plain run — without this,
    /// the typed raise overload below would capture a lone <see cref="CancellationToken"/>
    /// argument as an event payload.
    /// </summary>
    public new ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => base.ExecuteAsync(ct);

    /// <summary>
    /// Raises a typed event: resolves <typeparamref name="TEvent"/> against the graph's event
    /// entries, delivers the payload through the registration's blackboard key into this
    /// machine's bound boards (typed end-to-end — struct events never box), and starts an
    /// ordinary run at the entry's chain. One event = one run: the machine must be idle, and
    /// the existing restart policies apply verbatim (under <see cref="RestartPolicy.Auto"/>
    /// the machine is re-raisable after each run; under <see cref="RestartPolicy.Manual"/> a
    /// raise after a terminal run throws the usual "call Reset()" error). Hosts that want
    /// buffering feed this from their own queue (e.g. a <c>Channel&lt;T&gt;</c>).
    /// </summary>
    public ValueTask<Result> ExecuteAsync<TEvent>(TEvent evt, CancellationToken ct = default)
    {
        ArmRaise(evt);
        return RunRaisedAsync(ct);
    }

    /// <summary>
    /// Typed-raise twin of <see cref="StepAsync(CancellationToken)"/>: raises the event and
    /// advances the run by exactly one node. Legal only as a run's <b>first</b> step — the
    /// dispatch decision is made at run start, so raising mid-stepped-run throws. Continue
    /// the run with plain <see cref="StepAsync(CancellationToken)"/> calls.
    /// </summary>
    public ValueTask<Result> StepAsync<TEvent>(TEvent evt, CancellationToken ct = default)
    {
        if (_stepping)
        {
            throw new InvalidOperationException(
                "A stepped run is in progress — a typed event can only start a run. Finish it with StepAsync " +
                "or Reset() before raising.");
        }

        ArmRaise(evt);
        return StepRaisedAsync(ct);
    }

    private void ArmRaise<TEvent>(TEvent evt)
    {
        EventEntryState? dispatcher = _eventEntry;
        if (dispatcher is null)
        {
            ThrowNoEventEntries();
        }

        // Best-effort idle guard — the execute gate in the plain path remains the real barrier.
        ExecutionStatus status = Status;
        if (status is ExecutionStatus.Starting or ExecutionStatus.Running or ExecutionStatus.Transitioning)
        {
            ThrowRaiseWhileExecuting();
        }

        _pendingEntry = dispatcher!.RaiseInto(in _blackboards, evt);
    }

    private async ValueTask<Result> RunRaisedAsync(CancellationToken ct)
    {
        try
        {
            return await ExecuteAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            // Consumed at run start; anything left means the run never started (terminal-Manual
            // throw, Ignore policy) — clear so the entry cannot leak into a later plain run.
            _pendingEntry = NodeId.Default;
        }
    }

    private async ValueTask<Result> StepRaisedAsync(CancellationToken ct)
    {
        try
        {
            return await StepAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            // Same guarantee as RunRaisedAsync: a step that never started a run must not
            // leave the entry armed. A successfully started stepped run consumed it already.
            if (!_stepping)
            {
                _pendingEntry = NodeId.Default;
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNoEventEntries() =>
        throw new InvalidOperationException(
            "This graph has no event entries — author it with GraphBuilder.StartWithEvents() to raise typed events.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowRaiseWhileExecuting() =>
        throw new InvalidOperationException(
            "Cannot raise an event while the machine is executing — an event starts a new run. Await completion " +
            "(or finish the stepped run) before raising.");

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
        _nodeBoard?.ResetToDefaults();

        await TransitionTo(ExecutionStatus.Ready).ConfigureAwait(false);
        return Result.Success;
    }

    protected override async ValueTask<Result> OnEnterAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _executeGate, 1) == 1) // Prevent re-entrance
        {
            throw new InvalidOperationException("AsyncStateMachine is already executing.");
        }

        // Everything after the gate acquisition can throw (observer callbacks fire from
        // ResetCore/TransitionTo/OnStateEntered, and observer exceptions bubble by design).
        // OnExitAsync does not run when OnEnterAsync throws, so the gate must be released
        // here or the machine is permanently locked. Mirrors sync StateMachine.Finalise.
        try
        {
            if (_stepping)
            {
                throw new InvalidOperationException(
                    "A stepped run is in progress. Finish it with StepAsync or Reset() before calling ExecuteAsync.");
            }

            // Ignore policy returns false: the init is skipped, but the gate is kept held so
            // a concurrent caller cannot enter while this ExecuteAsync is still on its way to
            // OnRunAsync (which returns the cached terminal result) and OnExitAsync (which
            // releases the gate). Either way OnEnterAsync hands off with InProgress.
            _ = await TryBeginRunAsync(stepped: false, ct).ConfigureAwait(false);

            return Result.InProgress;
        }
        catch
        {
            RepairTransientStatus();
            Volatile.Write(ref _executeGate, 0);
            throw;
        }
    }

    /// <summary>
    /// The run-start init shared by <see cref="OnEnterAsync"/> and the first
    /// <see cref="StepAsync"/> of a stepped run: applies the restart policy to a terminal
    /// machine, re-stamps the execution context, and moves the machine
    /// Starting → (enter first node) → Running. Returns <c>false</c> for the
    /// <see cref="RestartPolicy.Ignore"/> early-out — the callers translate that into their
    /// distinct surface results (full-run: <see cref="Result.InProgress"/> with the gate held
    /// so OnRunAsync returns the cached terminal result; stepped: the cached result
    /// directly). Must run inside the caller's gate-repair try/catch: observer callbacks
    /// fired here may throw, and which exceptions repair status and release the gate is
    /// caller-specific behavior.
    /// </summary>
    private async ValueTask<bool> TryBeginRunAsync(bool stepped, CancellationToken ct)
    {
        // If we're terminal, apply reset policy.
        ExecutionStatus status = Status;
        if (status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            switch (_restartPolicy)
            {
                case RestartPolicy.Ignore:
                    // Ignore further execution attempts until a manual Reset() occurs.
                    return false;
                case RestartPolicy.Manual:
                    throw new InvalidOperationException(
                        $"AsyncStateMachine is in terminal state '{status}'. Call Reset() before " +
                        $"{(stepped ? "stepping" : "executing")} again.");
                default:
                    // Auto: tolerate unexpected terminal state by resetting on entry.
                    // ResetCore (not Reset) because the caller already holds the gate.
                    await ResetCore(ct).ConfigureAwait(false);
                    break;
            }
        }

        // Re-stamp this machine's context onto the (potentially shared) graph before running.
        ApplyExecutionContext();
        StampPendingEntry();

        // First entry or re-entry after Ready: Starting -> (enter first node) -> Running
        await TransitionTo(ExecutionStatus.Starting).ConfigureAwait(false);
        _current = _initial;
        _attempts = 0;
        _nodeEntered = false;
        _nodeBoard?.ResetToDefaults();
        LastOutcome = 0;
        if (_observer is not null)
        {
            await _observer.OnStateEntered(_current, ct).ConfigureAwait(false);
        }

        await TransitionTo(ExecutionStatus.Running).ConfigureAwait(false);
        if (stepped)
        {
            _stepping = true;
        }

        return true;
    }

    /// <summary>
    /// If an observer threw while the machine was in a transient status (Starting/Resetting/
    /// Transitioning), restore Ready without notifications so the machine stays usable —
    /// Suspend/Reset reject transient statuses and a redundant re-transition would trip the
    /// debug assert in <see cref="TransitionTo"/>.
    /// </summary>
    private void RepairTransientStatus()
    {
        ExecutionStatus status = Status;
        if (status is ExecutionStatus.Starting or ExecutionStatus.Resetting or ExecutionStatus.Transitioning)
        {
            Volatile.Write(ref _statusInt, (int)ExecutionStatus.Ready);
        }
    }

    private bool IsTerminal => Status is ExecutionStatus.Completed or ExecutionStatus.Failed
        or ExecutionStatus.Cancelled;

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

            // If we get here, loop has returned a terminal result already.
            await CompleteRunAsync(result, ct).ConfigureAwait(false);

            return result;
        }
        catch (OperationCanceledException)
        {
            // The `throw;` stays here (not in the handler) so the stack trace is unchanged.
            await HandleRunCancelledAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await HandleRunFaultAsync(ex, ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// The terminal cascade shared by <see cref="OnRunAsync"/> and <see cref="StepAsync"/>:
    /// caches the terminal result, transitions to Completed/Failed (machine completion
    /// notification), and applies the optional auto-reset-to-Ready. ResetCore (not Reset)
    /// because the gate is held by the surrounding ExecuteAsync/StepAsync frame.
    /// </summary>
    private async ValueTask CompleteRunAsync(Result result, CancellationToken ct)
    {
        _terminalResult = result;

        await TransitionTo(result == Result.Success ? ExecutionStatus.Completed : ExecutionStatus.Failed)
            .ConfigureAwait(false);

        if (_restartPolicy == RestartPolicy.Auto)
        {
            await ResetCore(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Fault cascade for a cancellation escaping the run loop, shared by
    /// <see cref="OnRunAsync"/> and <see cref="StepAsync"/> — the callers keep their own
    /// <c>throw;</c> so the original stack trace is preserved. The <see cref="IsTerminal"/>
    /// guard skips the cascade when the run already reached a terminal status — e.g. an
    /// observer threw inside the Completed notification. Re-transitioning would fire
    /// OnStateMachineCompleted twice and flip a successful run to Cancelled/Failed.
    /// Mirrors sync StateMachine.Finalise's idempotence guard.
    /// </summary>
    private async ValueTask HandleRunCancelledAsync()
    {
        if (IsTerminal)
        {
            return;
        }

        // TransitionTo(Cancelled) already fires OnStateMachineCancelled via NotifyMachineStatusChangeAsync.
        await TransitionTo(ExecutionStatus.Cancelled).ConfigureAwait(false);

        _terminalResult = Result.Failure;

        if (_restartPolicy == RestartPolicy.Auto)
        {
            // CancellationToken.None: ct is already cancelled; passing it would make
            // any cancellable await inside the reset throw, stranding status at Resetting.
            await ResetCore(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Fault cascade for an unexpected exception escaping the run loop (node failures are
    /// returned as <see cref="Result.Failure"/> from the step core, not thrown), shared by
    /// <see cref="OnRunAsync"/> and <see cref="StepAsync"/> — the callers keep their own
    /// <c>throw;</c> so the original stack trace is preserved. Same
    /// <see cref="IsTerminal"/> idempotence guard as <see cref="HandleRunCancelledAsync"/>.
    /// </summary>
    private async ValueTask HandleRunFaultAsync(Exception ex, CancellationToken ct)
    {
        if (IsTerminal)
        {
            return;
        }

        if (_observer is not null)
        {
            await _observer.OnStateFailed(_current, ex, ct).ConfigureAwait(false);
        }

        // TransitionTo(Failed) already fires OnStateMachineCompleted(Failure) via NotifyMachineStatusChangeAsync.
        await TransitionTo(ExecutionStatus.Failed).ConfigureAwait(false);

        _terminalResult = Result.Failure;

        if (_restartPolicy == RestartPolicy.Auto)
        {
            // CancellationToken.None: ct may already be cancelled/faulted; reset must complete cleanly.
            await ResetCore(CancellationToken.None).ConfigureAwait(false);
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
            _stepping = snapshot.MidRun;
            LastOutcome = snapshot.LastOutcome;
            // Snapshots are primitives-only: Node-scoped scratch is transient by definition
            // and comes back as defaults after a resume.
            _nodeBoard?.ResetToDefaults();
            // A mid-run resume continues via StepAsync, which skips the run-start init where
            // the context is normally stamped — stamp here so a fresh machine's own boards
            // (including the machine-owned Node board) reach the graph's nodes.
            ApplyExecutionContext();
            // Reconstruct the cached terminal result so RestartPolicy.Ignore reports the
            // restored run's true outcome instead of the field initializer's Success.
            _terminalResult = snapshot.Status is ExecutionStatus.Failed or ExecutionStatus.Cancelled
                ? Result.Failure
                : Result.Success;
            Volatile.Write(ref _statusInt, (int)snapshot.Status);
        }
        finally
        {
            Volatile.Write(ref _executeGate, 0);
        }
    }

    /// <summary>
    /// Deep variant of <see cref="Suspend"/>: captures this machine's position plus the
    /// internal state of every composite in its graph that holds durable state (nested
    /// machine positions, history's remembered child position, sync RoundPerTick mid-visit
    /// bookkeeping) into a <see cref="StateMachineDeepSnapshot"/>. Same gates as
    /// <see cref="Suspend"/>; the extra cost is one linear walk over the graph's nodes plus
    /// the composites' own recursive captures — cold path by contract, allocation is expected.
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
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        Resume(snapshot.Self);
        DeepSnapshots.ResumeComposites(Graph, snapshot.Composites);
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
                // Run-start init fires observer callbacks (ResetCore/TransitionTo/OnStateEntered)
                // which may throw; repair transient status so the machine stays usable.
                try
                {
                    if (!await TryBeginRunAsync(stepped: true, ct).ConfigureAwait(false))
                    {
                        // Ignore policy: the stepped surface returns the cached result directly.
                        return _terminalResult;
                    }
                }
                catch
                {
                    RepairTransientStatus();
                    throw;
                }
            }

            Result result;
            try
            {
                result = await StepCoreAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _stepping = false;
                await HandleRunCancelledAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                _stepping = false;
                await HandleRunFaultAsync(ex, ct).ConfigureAwait(false);
                throw;
            }

            if (!result.IsCompleted)
            {
                return Result.InProgress;
            }

            _stepping = false;
            await CompleteRunAsync(result, ct).ConfigureAwait(false);

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
                // attribute log reports to their own observer; null when this machine has no
                // observer, so nodes that gate report formatting on a wired callback
                // (behavior composites) pay nothing on observer-less machines.
                reporter.LogReport = _observer is null ? null : _cachedLogReportCallback;

                // Sync states read both slots (State.Log prefers the sync one), so the sync
                // slot is cleared too: a callback left by a sync machine that ran this shared
                // graph earlier must neither shadow this machine's observer nor receive
                // reports from this run.
                if (reporter is State syncState)
                {
                    syncState.SyncLogReport = null;
                }
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
                    // New visit begins: transient Node-scoped scratch resets with the
                    // attempt counter (in-place retries above keep both).
                    _nodeBoard?.ResetToDefaults();

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
                    _nodeBoard?.ResetToDefaults();

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