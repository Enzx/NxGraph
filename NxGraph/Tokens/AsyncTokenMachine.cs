using System.Diagnostics;
using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Diagnostics.Replay;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tokens;

/// <summary>
/// An async token machine with an agent, mirroring <see cref="AsyncTokenMachine"/> the way
/// <see cref="AsyncStateMachine{TAgent}"/> mirrors <see cref="AsyncStateMachine"/>.
/// </summary>
public class AsyncTokenMachine<TAgent>(Graph graph, IAsyncTokenMachineObserver? observer = null,
        int maxTokens = TokenMachine.DefaultMaxTokens)
    : AsyncTokenMachine(graph, observer, maxTokens), IAgentSettable<TAgent>
{
    private TAgent? _agent;
    private bool _hasAgent;

    /// <summary>
    /// Binds the agent (typed context) to this machine. It is applied to the graph's nodes
    /// immediately and re-applied at the start of every execution, so several machines can
    /// share one <see cref="Graph"/> with distinct contexts as long as their executions do
    /// not overlap.
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
/// The asynchronous token runtime — the <see cref="TokenMachine"/> twin with the async FSM's
/// mechanics: <c>ValueTask&lt;Result&gt;</c>, cancellation awareness, atomic status
/// transitions, and retry backoff honored via <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
/// (awaited inline during the failing token's turn). N pooled tokens flow through one flat
/// graph via cooperative round scheduling; <see cref="ForkState"/>/<see cref="JoinState"/>
/// nodes are interpreted structurally, and ordinary nodes keep their FSM semantics per token.
/// <para>
/// <c>ExecuteAsync</c> runs the whole flow; <c>StepAsync</c> advances exactly one scheduling
/// round and returns <see cref="Result.InProgress"/> while tokens remain, mirroring the sync
/// machine's <see cref="ParallelStepMode.RoundPerTick"/> granularity. Semantics (run
/// lifecycle, join firing, starvation, outcome aggregation) are identical to the sync twin;
/// only the mechanics differ, per the runtime-parity convention.
/// </para>
/// </summary>
public class AsyncTokenMachine : AsyncState, ISubGraphProvider, IBlackboardBindable, IBlackboardSettable
{
    private const int MaxCascadeDepth = 64;

    public readonly Graph Graph;
    private readonly IAsyncTokenMachineObserver? _observer;
    private BlackboardContext _blackboards;

    private int _statusInt = (int)ExecutionStatus.Created; // atomic state
    private RestartPolicy _restartPolicy = RestartPolicy.Auto;
    private Result _terminalResult = Result.Success;
    private int _executeGate;
    private bool _stepping; // a StepAsync-driven run is in flight

    private readonly RetryPolicy[]? _retryPolicies;
    private readonly ForkState?[] _forks;
    private readonly JoinState?[] _joins;
    private readonly IBlackboardSettable?[] _settables;
    private readonly ILogReporter?[] _reporters;
    private readonly Func<string, CancellationToken, ValueTask> _cachedLogReportCallback;
    private readonly bool _hasNodeSchema;

    // ── Token pool ──────────────────────────────────────────────────────
    private readonly Token[] _pool;
    private readonly int[] _freeStack;
    private int _freeCount;
    private int _runnable;
    private int _parked;
    private bool _anyDied;
    private int _round;
    private int _nextTokenId;
    private readonly int[]? _joinArrivals;
    private readonly bool[]? _joinFired; // "fired at least once this run" per join node

    private int _steppingTokenId = -1;
    private NodeId _steppingNodeId = NodeId.Default;

    IEnumerable<Graph> ISubGraphProvider.SubGraphs => [Graph];

    /// <summary>Public execution status (volatile for visibility).</summary>
    public ExecutionStatus Status => (ExecutionStatus)Volatile.Read(ref _statusInt);

    /// <summary>Live tokens (runnable + parked). 0 outside a run.</summary>
    public int LiveTokenCount => _runnable + _parked;

    public AsyncTokenMachine(Graph graph, IAsyncTokenMachineObserver? observer = null,
        int maxTokens = TokenMachine.DefaultMaxTokens)
    {
        Guard.NotNull(graph, nameof(graph));
        if (maxTokens < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "A token machine needs at least one token.");
        }

        Graph = graph;
        _observer = observer;
        _retryPolicies = graph.RetryPolicies;
        _cachedLogReportCallback = LogReportCallback;

        _forks = new ForkState?[graph.NodeCount];
        _joins = new JoinState?[graph.NodeCount];
        _settables = new IBlackboardSettable?[graph.NodeCount];
        _reporters = new ILogReporter?[graph.NodeCount];
        bool anyJoin = false;
        for (int i = 0; i < graph.NodeCount; i++)
        {
            if (!graph.TryGetNodeByIndex(i, out INode? node) || node is not LogicNode logicNode)
            {
                continue;
            }

            _forks[i] = logicNode.Logic as ForkState ?? logicNode.AsyncLogic as ForkState;
            _joins[i] = logicNode.Logic as JoinState ?? logicNode.AsyncLogic as JoinState;
            anyJoin |= _joins[i] is not null;
            _settables[i] = logicNode.AsyncLogic as IBlackboardSettable ?? logicNode.Logic as IBlackboardSettable;
            _reporters[i] = logicNode.AsyncLogic as ILogReporter ?? logicNode.Logic as ILogReporter;
        }

        _joinArrivals = anyJoin ? new int[graph.NodeCount] : null;
        _joinFired = anyJoin ? new bool[graph.NodeCount] : null;

        BlackboardSchema? nodeSchema = graph.NodeSchema;
        _hasNodeSchema = nodeSchema is not null;
        _pool = new Token[maxTokens];
        _freeStack = new int[maxTokens];
        for (int i = 0; i < maxTokens; i++)
        {
            _pool[i] = new Token
            {
                SlotIndex = i,
                NodeBoard = nodeSchema is null ? null : new Blackboard(nodeSchema),
            };
            _freeStack[i] = maxTokens - 1 - i;
        }

        _freeCount = maxTokens;
    }

    // ── Context binding (mirrors the FSM machines) ─────────────────────

    /// <summary>
    /// Hook for derived machines to (re)apply per-machine execution context (e.g. the typed
    /// agent) to the graph's nodes. Runs under the execute gate, right before Starting.
    /// The base implementation re-stamps the bound blackboard context.
    /// </summary>
    private protected virtual void ApplyExecutionContext()
    {
        // Unconditional re-stamp, empty context included — same rationale as the FSM machines.
        // The machine-level context carries no Node slot; per-token scratch is stamped per
        // node execution instead.
        Graph.SetBlackboards(in _blackboards);
    }

    /// <summary>
    /// Binds a blackboard into the scope slot its schema declares (replace semantics).
    /// Node-scoped boards are per-token and machine-owned — binding one throws.
    /// </summary>
    public void SetBlackboard(Blackboard blackboard)
    {
        Guard.NotNull(blackboard, nameof(blackboard));
        if (blackboard.Schema.Scope == BlackboardScope.Node)
        {
            throw new InvalidOperationException(
                "Node-scoped boards are per-token transient scratch owned by the token machine and cannot be " +
                "bound — declare the schema on the graph via WithSchema(...); the machine creates one board per " +
                "pooled token.");
        }

        ValidateBoardAgainstDeclarations(blackboard);
        _blackboards = _blackboards.With(blackboard);
        Graph.SetBlackboards(in _blackboards);
    }

    /// <summary>
    /// Receives the whole context from a parent machine's stamping walk. The parent's Node
    /// slot is dropped — Node boards are per-token scratch owned by this machine.
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
        _blackboards = own;
        Graph.SetBlackboards(in own);
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

    /// <summary>
    /// Controls how the machine behaves after reaching a terminal status
    /// (same contract as the FSM machines).
    /// </summary>
    public void SetRestartPolicy(RestartPolicy policy) => _restartPolicy = policy;

    // ── Reset ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reset to the initial single-token state. Disallowed while Running/Transitioning.
    /// Status moves: (any non-running) → Resetting → Ready.
    /// </summary>
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
        _ = ct;
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
                case ExecutionStatus.Resetting:
                    return Result.Success;
                case ExecutionStatus.Completed:
                case ExecutionStatus.Failed:
                case ExecutionStatus.Cancelled:
                    break;
                default:
                    throw new IndexOutOfRangeException(nameof(status));
            }

            if (!TryTransition(status, ExecutionStatus.Resetting, out ExecutionStatus previous))
            {
                continue;
            }

            await NotifyMachineStatusChangeAsync(previous, ExecutionStatus.Resetting).ConfigureAwait(false);
            break;
        }

        ClearRunState();
        await TransitionTo(ExecutionStatus.Ready).ConfigureAwait(false);
        return Result.Success;
    }

    private void ClearRunState()
    {
        for (int i = 0; i < _pool.Length; i++)
        {
            _pool[i].Phase = TokenSlotPhase.Free;
            _freeStack[i] = _pool.Length - 1 - i;
        }

        _freeCount = _pool.Length;
        _runnable = 0;
        _parked = 0;
        _anyDied = false;
        _round = 0;
        _nextTokenId = 0;
        if (_joinArrivals is not null)
        {
            Array.Clear(_joinArrivals, 0, _joinArrivals.Length);
            Array.Clear(_joinFired!, 0, _joinFired!.Length);
        }
    }

    // ── Suspend / resume ────────────────────────────────────────────────

    /// <summary>
    /// Captures the machine's runtime state into a serializable snapshot. Legal only at a
    /// round boundary: between <see cref="StepAsync"/> calls of a stepped run, or on a machine
    /// that is not currently executing.
    /// </summary>
    public TokenMachineSnapshot Suspend()
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

            int live = _runnable + _parked;
            TokenRecord[] records = live == 0 ? Array.Empty<TokenRecord>() : new TokenRecord[live];
            int w = 0;
            for (int i = 0; i < _pool.Length; i++)
            {
                Token t = _pool[i];
                if (t.Phase == TokenSlotPhase.Free)
                {
                    continue;
                }

                records[w++] = new TokenRecord(t.Id, t.NodeIndex, t.Attempts, t.NodeEntered,
                    t.Phase == TokenSlotPhase.Parked ? TokenPhase.Parked : TokenPhase.Runnable);
            }

            int[] arrivals = _joinArrivals is null ? Array.Empty<int>() : (int[])_joinArrivals.Clone();
            bool[] fired = _joinFired is null ? Array.Empty<bool>() : (bool[])_joinFired.Clone();
            return new TokenMachineSnapshot(status, _stepping, _nextTokenId, records, arrivals)
            {
                AnyTokenDied = _anyDied,
                JoinsFired = fired,
            };
        }
        finally
        {
            Volatile.Write(ref _executeGate, 0);
        }
    }

    /// <summary>
    /// Restores a snapshot produced by <c>Suspend()</c> (from either token runtime) onto this
    /// machine. The graph must be structurally equivalent (same node indices) and the pool
    /// large enough for the snapshot's live tokens. A mid-run snapshot is continued with
    /// <see cref="StepAsync"/> until it returns a terminal result. Node-scope scratch is
    /// transient and comes back as defaults.
    /// </summary>
    public void Resume(TokenMachineSnapshot snapshot)
    {
        Guard.NotNull(snapshot, nameof(snapshot));

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

            RestoreSnapshotCore(snapshot);
            _stepping = snapshot.MidRun;
            ApplyExecutionContext();
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

    private void RestoreSnapshotCore(TokenMachineSnapshot snapshot)
    {
        TokenRecord[] tokens = snapshot.Tokens ?? Array.Empty<TokenRecord>();
        if (tokens.Length > _pool.Length)
        {
            throw new InvalidOperationException(
                $"Snapshot holds {tokens.Length} live tokens but this machine's pool caps at {_pool.Length} " +
                "(maxTokens). Construct the machine with a larger pool to resume it.");
        }

        if (snapshot.JoinArrivals is { Length: > 0 } arrivals)
        {
            if (_joinArrivals is null || arrivals.Length != _joinArrivals.Length)
            {
                throw new InvalidOperationException(
                    "Snapshot join-arrival counts do not match this graph (join nodes or node count differ).");
            }
        }

        ClearRunState();

        for (int i = 0; i < tokens.Length; i++)
        {
            TokenRecord record = tokens[i];
            if ((uint)record.NodeIndex >= (uint)Graph.NodeCount)
            {
                throw new InvalidOperationException(
                    $"Snapshot token #{record.Id} node index {record.NodeIndex} is out of range for this graph " +
                    $"(0..{Graph.NodeCount - 1}).");
            }

            Token t = _pool[i];
            t.Id = record.Id;
            t.NodeIndex = record.NodeIndex;
            t.Attempts = record.Attempts;
            t.NodeEntered = record.NodeEntered;
            t.SpawnRound = -1;
            t.NodeBoard?.ResetToDefaults();
            if (record.Phase == TokenPhase.Parked)
            {
                t.Phase = TokenSlotPhase.Parked;
                _parked++;
            }
            else
            {
                t.Phase = TokenSlotPhase.Runnable;
                _runnable++;
            }
        }

        _freeCount = _pool.Length - tokens.Length;
        for (int i = 0; i < _freeCount; i++)
        {
            _freeStack[i] = _pool.Length - 1 - i;
        }

        _nextTokenId = snapshot.NextTokenId;
        _anyDied = snapshot.AnyTokenDied;
        if (snapshot.JoinArrivals is { Length: > 0 } restoredArrivals)
        {
            Array.Copy(restoredArrivals, _joinArrivals!, restoredArrivals.Length);
        }

        if (snapshot.JoinsFired is { Length: > 0 } restoredFired)
        {
            if (_joinFired is null || restoredFired.Length != _joinFired.Length)
            {
                throw new InvalidOperationException(
                    "Snapshot join-fired flags do not match this graph (join nodes or node count differ).");
            }

            Array.Copy(restoredFired, _joinFired, restoredFired.Length);
        }
    }

    // ── AsyncState overrides — full-run lifecycle ───────────────────────

    protected override async ValueTask<Result> OnEnterAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _executeGate, 1) == 1)
        {
            throw new InvalidOperationException("AsyncTokenMachine is already executing.");
        }

        try
        {
            if (_stepping)
            {
                throw new InvalidOperationException(
                    "A stepped run is in progress. Finish it with StepAsync or Reset() before calling ExecuteAsync.");
            }

            ExecutionStatus status = Status;
            if (status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
            {
                switch (_restartPolicy)
                {
                    case RestartPolicy.Ignore:
                        // Gate stays held; OnRunAsync returns the cached terminal result and
                        // OnExitAsync releases the gate — mirrors AsyncStateMachine.
                        return Result.InProgress;
                    case RestartPolicy.Manual:
                        throw new InvalidOperationException(
                            $"AsyncTokenMachine is in terminal state '{status}'. Call Reset() before executing again.");
                    default:
                        await ResetCore(ct).ConfigureAwait(false);
                        break;
                }
            }

            ApplyExecutionContext();

            await TransitionTo(ExecutionStatus.Starting).ConfigureAwait(false);
            ClearRunState();
            await SpawnRootTokenAsync(ct).ConfigureAwait(false);
            await TransitionTo(ExecutionStatus.Running).ConfigureAwait(false);

            return Result.InProgress;
        }
        catch
        {
            RepairTransientStatus();
            Volatile.Write(ref _executeGate, 0);
            throw;
        }
    }

    private async ValueTask SpawnRootTokenAsync(CancellationToken ct)
    {
        Token root = Alloc();
        if (_observer is not null)
        {
            await _observer.OnTokenSpawned(root.Id, -1, Graph.StartNode.Id, ct).ConfigureAwait(false);
        }

        await ArriveAtAsync(root, Graph.StartNode.Id.Index, 0, ct).ConfigureAwait(false);
    }

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
        ExecutionStatus status = Status;
        if (_restartPolicy == RestartPolicy.Ignore &&
            status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            return _terminalResult;
        }

        try
        {
            Result result;
            do
            {
                result = await RunRoundAsync(ct).ConfigureAwait(false);
            } while (!result.IsCompleted);

            _terminalResult = result;

            await TransitionTo(result == Result.Success ? ExecutionStatus.Completed : ExecutionStatus.Failed)
                .ConfigureAwait(false);

            if (_restartPolicy == RestartPolicy.Auto)
            {
                await ResetCore(ct).ConfigureAwait(false);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            if (!IsTerminal)
            {
                await TransitionTo(ExecutionStatus.Cancelled).ConfigureAwait(false);
                _terminalResult = Result.Failure;
                if (_restartPolicy == RestartPolicy.Auto)
                {
                    await ResetCore(CancellationToken.None).ConfigureAwait(false);
                }
            }

            throw;
        }
        catch (Exception ex)
        {
            if (!IsTerminal)
            {
                if (_observer is not null)
                {
                    await _observer.OnStateFailed(_steppingTokenId, _steppingNodeId, ex, ct).ConfigureAwait(false);
                }

                await TransitionTo(ExecutionStatus.Failed).ConfigureAwait(false);
                _terminalResult = Result.Failure;
                if (_restartPolicy == RestartPolicy.Auto)
                {
                    await ResetCore(CancellationToken.None).ConfigureAwait(false);
                }
            }

            throw;
        }
    }

    protected override ValueTask<Result> OnExitAsync(CancellationToken ct)
    {
        Volatile.Write(ref _executeGate, 0);
        return ResultHelpers.Success;
    }

    // ── Stepped execution ───────────────────────────────────────────────

    /// <summary>
    /// Advances the machine by exactly one scheduling round, mirroring the sync machine's
    /// frame-stepped <c>Execute()</c>. Returns <see cref="Result.InProgress"/> while tokens
    /// remain and the terminal result when the run finishes. The first call begins a run
    /// (applying the restart policy exactly like <c>ExecuteAsync</c>); between rounds the
    /// machine stays <see cref="ExecutionStatus.Running"/>.
    /// </summary>
    public async ValueTask<Result> StepAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _executeGate, 1) == 1)
        {
            throw new InvalidOperationException("AsyncTokenMachine is already executing.");
        }

        try
        {
            if (!_stepping)
            {
                try
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
                                    $"AsyncTokenMachine is in terminal state '{status}'. Call Reset() before stepping again.");
                            default:
                                await ResetCore(ct).ConfigureAwait(false);
                                break;
                        }
                    }

                    ApplyExecutionContext();

                    await TransitionTo(ExecutionStatus.Starting).ConfigureAwait(false);
                    ClearRunState();
                    await SpawnRootTokenAsync(ct).ConfigureAwait(false);
                    await TransitionTo(ExecutionStatus.Running).ConfigureAwait(false);
                    _stepping = true;
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
                result = await RunRoundAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _stepping = false;
                if (!IsTerminal)
                {
                    await TransitionTo(ExecutionStatus.Cancelled).ConfigureAwait(false);
                    _terminalResult = Result.Failure;
                    if (_restartPolicy == RestartPolicy.Auto)
                    {
                        await ResetCore(CancellationToken.None).ConfigureAwait(false);
                    }
                }

                throw;
            }
            catch (Exception ex)
            {
                _stepping = false;
                if (!IsTerminal)
                {
                    if (_observer is not null)
                    {
                        await _observer.OnStateFailed(_steppingTokenId, _steppingNodeId, ex, ct)
                            .ConfigureAwait(false);
                    }

                    await TransitionTo(ExecutionStatus.Failed).ConfigureAwait(false);
                    _terminalResult = Result.Failure;
                    if (_restartPolicy == RestartPolicy.Auto)
                    {
                        await ResetCore(CancellationToken.None).ConfigureAwait(false);
                    }
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

    // ── The round scheduler (the hot path) ─────────────────────────────

    private async ValueTask<Result> RunRoundAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        _round++;
        for (int i = 0; i < _pool.Length; i++)
        {
            Token t = _pool[i];
            if (t.Phase != TokenSlotPhase.Runnable || t.SpawnRound == _round)
            {
                continue;
            }

            await StepTokenAsync(t, ct).ConfigureAwait(false);
        }

        if (_runnable > 0)
        {
            return Result.InProgress;
        }

        if (_parked > 0)
        {
            // Tokens parked at a join that already fired this run are benign quorum leftovers
            // and absorb quietly; tokens parked at a join that never fired are starved — their
            // suppliers died or completed elsewhere — and fail the machine.
            bool anyStarved = false;
            for (int i = 0; i < _pool.Length; i++)
            {
                Token t = _pool[i];
                if (t.Phase != TokenSlotPhase.Parked)
                {
                    continue;
                }

                if (_joinFired![t.NodeIndex])
                {
                    await RetireTokenAsync(t, IdOf(t.NodeIndex), TokenRetireReason.Absorbed, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    anyStarved = true;
                    await RetireTokenAsync(t, IdOf(t.NodeIndex), TokenRetireReason.Starved, ct)
                        .ConfigureAwait(false);
                }
            }

            if (anyStarved)
            {
                return Result.Failure;
            }
        }

        return _anyDied ? Result.Failure : Result.Success;
    }

    private async ValueTask StepTokenAsync(Token t, CancellationToken ct)
    {
        int idx = t.NodeIndex;
        if (!Graph.TryGetNodeByIndex(idx, out INode? node) || node is not LogicNode logicNode)
        {
            throw new InvalidOperationException($"Node #{idx} not found.");
        }

        _steppingTokenId = t.Id;
        _steppingNodeId = logicNode.Id;

        // Per-token Node-scope scratch: stamped onto this node only, right before it executes.
        if (_hasNodeSchema && _settables[idx] is { } settable)
        {
            settable.SetBlackboards(_blackboards.With(t.NodeBoard!));
        }

        ILogReporter? reporter = _reporters[idx];
        if (reporter is not null)
        {
            reporter.LogReport = _observer is null ? null : _cachedLogReportCallback;
        }

        if (!t.NodeEntered)
        {
            t.NodeEntered = true;
            logicNode.EnterAction?.Invoke();
        }

        Result result = await logicNode.AsyncLogic.ExecuteAsync(ct).ConfigureAwait(false);
        t.Attempts++;
        switch (result.Code)
        {
            case Result.StatusCode.Success:
            {
                t.NodeEntered = false;
                logicNode.ExitAction?.Invoke();
                if (_observer is not null)
                {
                    await _observer.OnStateExited(t.Id, logicNode.Id, ct).ConfigureAwait(false);
                }

                NodeId next;
                if (logicNode.AsyncLogic is IAsyncDirector director)
                {
                    next = await director.SelectNextAsync(ct).ConfigureAwait(false);
                    if (next.Equals(NodeId.Default))
                    {
                        await RetireTokenAsync(t, logicNode.Id, TokenRetireReason.Completed, ct)
                            .ConfigureAwait(false);
                        return;
                    }
                }
                else if (logicNode.Logic is IDirector syncDirector)
                {
                    next = syncDirector.SelectNext();
                    if (next.Equals(NodeId.Default))
                    {
                        await RetireTokenAsync(t, logicNode.Id, TokenRetireReason.Completed, ct)
                            .ConfigureAwait(false);
                        return;
                    }
                }
                else
                {
                    if (!Graph.TryGetTransition(logicNode.Id, out Transition edge))
                    {
                        if (_observer is not null)
                        {
                            await _observer.OnStateFailed(t.Id, logicNode.Id,
                                    new InvalidOperationException($"No transition found for state '{logicNode.Id}'."),
                                    ct)
                                .ConfigureAwait(false);
                        }

                        _anyDied = true;
                        await RetireTokenAsync(t, logicNode.Id, TokenRetireReason.Failed, ct).ConfigureAwait(false);
                        return;
                    }

                    if (edge.IsEmpty)
                    {
                        await RetireTokenAsync(t, logicNode.Id, TokenRetireReason.Completed, ct)
                            .ConfigureAwait(false);
                        return;
                    }

                    next = edge.Destination;
                }

                if (_observer is not null)
                {
                    await _observer.OnTransition(t.Id, logicNode.Id, next, ct).ConfigureAwait(false);
                }

                t.Attempts = 0;
                t.NodeBoard?.ResetToDefaults();
                await ArriveAtAsync(t, next.Index, 0, ct).ConfigureAwait(false);
                return;
            }
            case Result.StatusCode.Failure:
            {
                if (_retryPolicies is not null)
                {
                    RetryPolicy retry = _retryPolicies[idx];
                    if (t.Attempts < retry.MaxAttempts)
                    {
                        TimeSpan delay = retry.DelayForAttempt(t.Attempts);
                        if (delay > TimeSpan.Zero)
                        {
                            // Awaited inline during this token's turn — the round resumes
                            // with the next token afterwards; the retry itself happens on
                            // this token's next round.
                            await Task.Delay(delay, ct).ConfigureAwait(false);
                        }

                        return;
                    }
                }

                t.NodeEntered = false;
                logicNode.ExitAction?.Invoke();

                if (Graph.TryGetTransition(logicNode.Id, out Transition failEdge) &&
                    failEdge.HasFailureDestination)
                {
                    if (_observer is not null)
                    {
                        await _observer.OnStateFailed(t.Id, logicNode.Id, null, ct).ConfigureAwait(false);
                    }

                    NodeId handler = failEdge.FailureDestination;
                    if (_observer is not null)
                    {
                        await _observer.OnTransition(t.Id, logicNode.Id, handler, ct).ConfigureAwait(false);
                    }

                    t.Attempts = 0;
                    t.NodeBoard?.ResetToDefaults();
                    await ArriveAtAsync(t, handler.Index, 0, ct).ConfigureAwait(false);
                    return;
                }

                if (_observer is not null)
                {
                    await _observer.OnStateFailed(t.Id, logicNode.Id, null, ct).ConfigureAwait(false);
                }

                _anyDied = true;
                await RetireTokenAsync(t, logicNode.Id, TokenRetireReason.Failed, ct).ConfigureAwait(false);
                return;
            }
            case Result.StatusCode.InProgress:
                throw new InvalidOperationException(
                    $"Node '{logicNode.Id}' returned Result.InProgress, which is reserved for the " +
                    "stepped-execution runtime. Node logic must return Success or Failure.");
            default:
                throw new InvalidOperationException("Unknown node result.");
        }
    }

    /// <summary>
    /// Lands <paramref name="t"/> on node <paramref name="idx"/>, resolving fork/join structure
    /// at arrival — same semantics as the sync twin.
    /// </summary>
    private async ValueTask ArriveAtAsync(Token t, int idx, int depth, CancellationToken ct)
    {
        if (depth > MaxCascadeDepth)
        {
            throw new InvalidOperationException(
                $"Fork/join arrival cascade exceeds {MaxCascadeDepth} levels — check for a cycle of fork nodes.");
        }

        if ((uint)idx >= (uint)_forks.Length)
        {
            throw new InvalidOperationException($"Transition points to non-existent node #{idx}.");
        }

        ForkState? fork = _forks[idx];
        if (fork is not null)
        {
            await PassThroughForkAsync(t, idx, fork, depth, ct).ConfigureAwait(false);
            return;
        }

        JoinState? join = _joins[idx];
        if (join is not null)
        {
            await ArriveAtJoinAsync(t, idx, join, depth, ct).ConfigureAwait(false);
            return;
        }

        t.NodeIndex = idx;
        t.NodeEntered = false;
        if (_observer is not null)
        {
            await _observer.OnStateEntered(t.Id, IdOf(idx), ct).ConfigureAwait(false);
        }
    }

    private async ValueTask PassThroughForkAsync(Token t, int idx, ForkState fork, int depth, CancellationToken ct)
    {
        LogicNode forkNode = (LogicNode)Graph.GetNodeByIndex(idx);
        NodeId forkId = forkNode.Id;
        if (_observer is not null)
        {
            await _observer.OnStateEntered(t.Id, forkId, ct).ConfigureAwait(false);
        }

        forkNode.EnterAction?.Invoke();
        forkNode.ExitAction?.Invoke();
        if (_observer is not null)
        {
            await _observer.OnStateExited(t.Id, forkId, ct).ConfigureAwait(false);
        }

        // Branch 0 continues the arriving token; every other branch spawns a new one.
        // Branch ids are resolved through the graph so observers see the applied display
        // names (the fork stores pre-Build ids, whose names are empty).
        int firstIndex = fork.BranchAt(0).Index;
        if (_observer is not null)
        {
            await _observer.OnTransition(t.Id, forkId, IdOf(firstIndex), ct).ConfigureAwait(false);
        }

        await ArriveAtAsync(t, firstIndex, depth + 1, ct).ConfigureAwait(false);

        for (int b = 1; b < fork.BranchCount; b++)
        {
            Token child = Alloc();
            int branchIndex = fork.BranchAt(b).Index;
            if (_observer is not null)
            {
                await _observer.OnTokenSpawned(child.Id, t.Id, forkId, ct).ConfigureAwait(false);
                await _observer.OnTransition(child.Id, forkId, IdOf(branchIndex), ct).ConfigureAwait(false);
            }

            await ArriveAtAsync(child, branchIndex, depth + 1, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask ArriveAtJoinAsync(Token t, int idx, JoinState join, int depth, CancellationToken ct)
    {
        LogicNode joinNode = (LogicNode)Graph.GetNodeByIndex(idx);
        NodeId joinId = joinNode.Id;
        if (_observer is not null)
        {
            await _observer.OnStateEntered(t.Id, joinId, ct).ConfigureAwait(false);
        }

        joinNode.EnterAction?.Invoke();

        int arrivals = ++_joinArrivals![idx];
        int required = join.Policy.RequiredCount;
        if (arrivals < required)
        {
            t.NodeIndex = idx;
            t.NodeEntered = false;
            t.Phase = TokenSlotPhase.Parked;
            _runnable--;
            _parked++;
            return;
        }

        // Fire: consume the requirement — this token plus (required - 1) parked ones.
        _joinArrivals[idx] = arrivals - required;
        _joinFired![idx] = true;
        int toConsume = required - 1;
        for (int i = 0; i < _pool.Length && toConsume > 0; i++)
        {
            Token parked = _pool[i];
            if (parked.Phase == TokenSlotPhase.Parked && parked.NodeIndex == idx)
            {
                await RetireTokenAsync(parked, joinId, TokenRetireReason.Joined, ct).ConfigureAwait(false);
                toConsume--;
            }
        }

        if (_observer is not null)
        {
            await _observer.OnJoinFired(joinId, t.Id, ct).ConfigureAwait(false);
        }

        joinNode.ExitAction?.Invoke();
        if (_observer is not null)
        {
            await _observer.OnStateExited(t.Id, joinId, ct).ConfigureAwait(false);
        }

        t.Attempts = 0;
        t.NodeBoard?.ResetToDefaults();

        if (!Graph.TryGetTransition(joinId, out Transition edge) || edge.IsEmpty)
        {
            await RetireTokenAsync(t, joinId, TokenRetireReason.Completed, ct).ConfigureAwait(false);
            return;
        }

        if (_observer is not null)
        {
            await _observer.OnTransition(t.Id, joinId, edge.Destination, ct).ConfigureAwait(false);
        }

        await ArriveAtAsync(t, edge.Destination.Index, depth + 1, ct).ConfigureAwait(false);
    }

    // ── Pool operations ─────────────────────────────────────────────────

    private Token Alloc()
    {
        if (_freeCount == 0)
        {
            throw new InvalidOperationException(
                $"Token pool exhausted ({_pool.Length} tokens live). Raise the machine's maxTokens " +
                "constructor argument to fit this graph's fan-out.");
        }

        int slot = _freeStack[--_freeCount];
        Token t = _pool[slot];
        t.Id = _nextTokenId++;
        t.NodeIndex = 0;
        t.Attempts = 0;
        t.NodeEntered = false;
        t.Phase = TokenSlotPhase.Runnable;
        t.SpawnRound = _round;
        t.NodeBoard?.ResetToDefaults();
        _runnable++;
        return t;
    }

    private async ValueTask RetireTokenAsync(Token t, NodeId at, TokenRetireReason reason, CancellationToken ct)
    {
        if (t.Phase == TokenSlotPhase.Runnable)
        {
            _runnable--;
        }
        else if (t.Phase == TokenSlotPhase.Parked)
        {
            _parked--;
        }

        t.Phase = TokenSlotPhase.Free;
        _freeStack[_freeCount++] = t.SlotIndex;
        if (_observer is not null)
        {
            await _observer.OnTokenRetired(t.Id, at, reason, ct).ConfigureAwait(false);
        }
    }

    private NodeId IdOf(int index) => Graph.GetNodeByIndex(index).Id;

    private async ValueTask LogReportCallback(string message, CancellationToken ct)
    {
        if (_observer is not null)
        {
            await _observer.OnLogReport(_steppingTokenId, _steppingNodeId, message, ct).ConfigureAwait(false);
        }
    }

    // ── Status plumbing (mirrors AsyncStateMachine) ─────────────────────

    private bool TryTransition(ExecutionStatus from, ExecutionStatus to, out ExecutionStatus prev)
    {
        int f = (int)from, t = (int)to;
        int seen = Interlocked.CompareExchange(ref _statusInt, t, f);
        prev = (ExecutionStatus)seen;

        return seen == f;
    }

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
        await _observer.TokenMachineStatusChanged(graphId, prev, next).ConfigureAwait(false);
        switch (next)
        {
            case ExecutionStatus.Starting:
                await _observer.OnTokenMachineStarted(graphId).ConfigureAwait(false);
                break;
            case ExecutionStatus.Completed:
                await _observer.OnTokenMachineCompleted(graphId, Result.Success).ConfigureAwait(false);
                break;
            case ExecutionStatus.Failed:
                await _observer.OnTokenMachineCompleted(graphId, Result.Failure).ConfigureAwait(false);
                break;
            case ExecutionStatus.Cancelled:
                await _observer.OnTokenMachineCancelled(graphId).ConfigureAwait(false);
                break;
            case ExecutionStatus.Resetting:
                await _observer.OnTokenMachineReset(graphId).ConfigureAwait(false);
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
