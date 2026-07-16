using System.Runtime.CompilerServices;
using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tokens;

/// <summary>
/// A synchronous token machine with an agent, mirroring <see cref="TokenMachine"/> the way
/// <see cref="StateMachine{TAgent}"/> mirrors <see cref="StateMachine"/>.
/// </summary>
public class TokenMachine<TAgent>(Graph graph, ITokenMachineObserver? observer = null, int maxTokens = TokenMachine.DefaultMaxTokens)
    : TokenMachine(graph, observer, maxTokens), IAgentSettable<TAgent>
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
/// The synchronous token runtime: N pooled tokens flow through <b>one flat graph</b>
/// concurrently via cooperative round scheduling — each round advances every runnable token
/// by one node. <see cref="ForkState"/> nodes fan tokens out, <see cref="JoinState"/> nodes
/// merge them (all / any / M-of-N), and every ordinary node keeps its FSM semantics
/// (retry policy, failure edge, enter/exit hooks) applied <i>per token</i>.
/// <para>
/// This is the third runtime beside the sync/async FSMs — the single-active-node FSM
/// machines are untouched, and a graph without fork/join nodes runs identically under
/// either family. Like the sync <see cref="StateMachine"/>, the machine inherits
/// <see cref="State"/> so it can be nested as a node inside a parent graph, steps one
/// scheduling round per <see cref="State.Execute"/> call by default
/// (<see cref="ParallelStepMode.RoundPerTick"/>), and drains to the terminal result in one
/// call under <see cref="ParallelStepMode.RunToJoin"/>. Scheduling is deliberately not
/// thread-concurrent, preserving the 0 B hot-path guarantee.
/// </para>
/// <para>
/// A run starts with one token at the start node and ends when no runnable token remains:
/// if tokens are still parked at joins the machine fails (join starvation); otherwise it
/// succeeds only when no token died (failed terminally). Machine status stays
/// <see cref="ExecutionStatus.Running"/> between rounds — the FSM's per-edge
/// <see cref="ExecutionStatus.Transitioning"/> status is a single-active-node concept and is
/// not reported by the token machines.
/// </para>
/// </summary>
public class TokenMachine : State, ISubGraphProvider, IBlackboardBindable, IBlackboardSettable
{
    public const int DefaultMaxTokens = 64;

    /// <summary>
    /// Cap on fork/join arrival cascades resolved within a single token step. Fork→fork or
    /// join→fork chains are resolved recursively at arrival time; a cycle of forks would
    /// otherwise recurse forever while spawning tokens.
    /// </summary>
    private const int MaxCascadeDepth = 64;

    public readonly Graph Graph;
    private readonly ITokenMachineObserver? _observer;
    private BlackboardContext _blackboards;

    private ExecutionStatus _status = ExecutionStatus.Created;
    private RestartPolicy _restartPolicy = RestartPolicy.Auto;
    private Result _terminalResult = Result.Success;
    private bool _executeGate;
    private bool _reentranceGuard;

    private readonly RetryPolicy[]? _retryPolicies;
    private readonly ForkState?[] _forks; // index = NodeId.Index; null for non-fork nodes
    private readonly JoinState?[] _joins; // index = NodeId.Index; null for non-join nodes
    private readonly IBlackboardSettable?[] _settables; // per-node, for per-token scratch stamping
    private readonly State?[] _logReportStates; // indexed by NodeId.Index, resolved once
    private readonly Action<string> _cachedLogReportCallback;
    private readonly bool _hasNodeSchema;

    // ── Token pool ──────────────────────────────────────────────────────
    private readonly Token[] _pool;
    private readonly int[] _freeStack; // slot indices; top = _freeCount - 1
    private int _freeCount;
    private int _runnable;
    private int _parked;
    private bool _anyDied;
    private int _round;
    private int _nextTokenId;
    private readonly int[]? _joinArrivals; // index = NodeId.Index; null when the graph has no joins
    private readonly bool[]? _joinFired; // "fired at least once this run" per join node

    // Attribution for log reports and exception observers while a token is being stepped.
    private int _steppingTokenId = -1;
    private NodeId _steppingNodeId = NodeId.Default;

    IEnumerable<Graph> ISubGraphProvider.SubGraphs => [Graph];

    /// <summary>Public execution status.</summary>
    public ExecutionStatus Status
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _status;
    }

    /// <summary>Live tokens (runnable + parked). 0 outside a run.</summary>
    public int LiveTokenCount => _runnable + _parked;

    public TokenMachine(Graph graph, ITokenMachineObserver? observer = null, int maxTokens = DefaultMaxTokens)
    {
        Guard.NotNull(graph, nameof(graph));
        if (maxTokens < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "A token machine needs at least one token.");
        }

        // Fail fast on graphs the sync runtime cannot execute (fork/join nodes implement
        // ILogic, so only genuinely async-only user logic trips this) — mirrors StateMachine.
        for (int i = 0; i < graph.NodeCount; i++)
        {
            if (graph.TryGetNodeByIndex(i, out INode? node) && node!.Logic is null)
            {
                throw new ArgumentException(
                    $"Node '{node.Id}' logic ({node.AsyncLogic.GetType().Name}) does not implement ILogic " +
                    "and cannot be executed by the sync TokenMachine. Use AsyncTokenMachine for this graph, " +
                    "or author the node with sync logic.", nameof(graph));
            }
        }

        Graph = graph;
        _observer = observer;
        _retryPolicies = graph.RetryPolicies;
        _cachedLogReportCallback = LogReportCallback;

        _forks = new ForkState?[graph.NodeCount];
        _joins = new JoinState?[graph.NodeCount];
        _settables = new IBlackboardSettable?[graph.NodeCount];
        _logReportStates = new State?[graph.NodeCount];
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
            _logReportStates[i] = logicNode.Logic as State;
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
            // Fill the stack so slot 0 is popped first (deterministic slot assignment).
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
        // Unconditional re-stamp, empty context included — same rationale as the FSM machines:
        // a board-less machine over a shared graph must hit the unbound-scope throw, not
        // silently read a previous machine's boards. The context carries no Node slot at the
        // machine level; per-token scratch is stamped per node execution instead.
        Graph.SetBlackboards(in _blackboards);
    }

    /// <summary>
    /// Binds a blackboard into the scope slot its schema declares (replace semantics).
    /// Applied to the graph's nodes immediately and re-applied at the start of every run.
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
    /// Receives the whole context from a parent machine's stamping walk (nested composite
    /// path). The parent's Node slot is dropped — Node boards are per-token scratch owned by
    /// this machine.
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

    // ── Machine configuration ───────────────────────────────────────────

    /// <summary>
    /// Controls how the machine behaves after reaching a terminal status
    /// (same contract as the FSM machines).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetRestartPolicy(RestartPolicy policy) => _restartPolicy = policy;

    /// <summary>
    /// How <see cref="State.Execute"/> maps scheduling rounds onto ticks:
    /// <see cref="ParallelStepMode.RoundPerTick"/> (default) advances one round per call and
    /// returns <see cref="Result.InProgress"/> while tokens remain;
    /// <see cref="ParallelStepMode.RunToJoin"/> drains the run inside one call, which also
    /// makes the machine executable as a node under the async runtime via the adapter.
    /// </summary>
    public ParallelStepMode StepMode { get; private set; } = ParallelStepMode.RoundPerTick;

    /// <summary>Sets <see cref="StepMode"/>. Machine-level runtime configuration — not graph structure.</summary>
    public void SetStepMode(ParallelStepMode mode) => StepMode = mode;

    // ── Reset / suspend / resume ────────────────────────────────────────

    /// <summary>
    /// Reset to the initial single-token state. Disallowed while Running/Transitioning.
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
        ClearRunState();
        TransitionTo(ExecutionStatus.Ready);
        return Result.Success;
    }

    /// <summary>Returns every token slot to the pool and zeroes run bookkeeping. No observer events.</summary>
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

    /// <summary>
    /// Captures the machine's runtime state into a serializable snapshot. The sync runtime is
    /// frame-stepped, so any point between <see cref="State.Execute"/> calls is a legal round
    /// boundary — including the middle of a run. Only calling from inside node logic is rejected.
    /// </summary>
    public TokenMachineSnapshot Suspend()
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

        return BuildSnapshot(status, midRun: _executeGate);
    }

    private TokenMachineSnapshot BuildSnapshot(ExecutionStatus status, bool midRun)
    {
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
        return new TokenMachineSnapshot(status, midRun, _nextTokenId, records, arrivals)
        {
            AnyTokenDied = _anyDied,
            JoinsFired = fired,
        };
    }

    /// <summary>
    /// Restores a snapshot produced by <see cref="Suspend"/> (from either token runtime) onto
    /// this machine. The graph must be structurally equivalent (same node indices) and the
    /// pool must be large enough for the snapshot's live tokens. A mid-run snapshot is
    /// continued by calling <see cref="State.Execute"/> until it returns a terminal result.
    /// Node-scope scratch is transient and comes back as defaults.
    /// </summary>
    public void Resume(TokenMachineSnapshot snapshot)
    {
        Guard.NotNull(snapshot, nameof(snapshot));
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

        RestoreSnapshotCore(snapshot);
        ApplyExecutionContext();
        _terminalResult = snapshot.Status is ExecutionStatus.Failed or ExecutionStatus.Cancelled
            ? Result.Failure
            : Result.Success;
        // Mid-run: the gate stays held between ticks and the State lifecycle must skip
        // OnEnter (which would restart the run) on the next Execute() call.
        _executeGate = snapshot.MidRun;
        HasEntered = snapshot.MidRun;
        _status = snapshot.Status;
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
            t.SpawnRound = -1; // every restored token runs in the next round
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

        // Free stack now starts above the restored tokens.
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

    // ── State overrides — Execute() is the stepped entry point ─────────

    protected override void OnEnter()
    {
        ExecutionStatus status = _status;

        if (status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            if (_restartPolicy == RestartPolicy.Ignore)
                return; // OnRun will return the cached result; no init needed
            if (_restartPolicy == RestartPolicy.Manual)
                throw new InvalidOperationException(
                    $"TokenMachine is in terminal state '{status}'. Call Reset() before executing again.");
            Reset(); // Auto
        }

        if (_executeGate)
            throw new InvalidOperationException("TokenMachine is already executing.");

        _executeGate = true;

        // Re-stamp this machine's context onto the (potentially shared) graph before running.
        ApplyExecutionContext();

        TransitionTo(ExecutionStatus.Starting);
        ClearRunState();
        SpawnRootToken();
        TransitionTo(ExecutionStatus.Running);
    }

    private void SpawnRootToken()
    {
        Token root = Alloc();
        _observer?.OnTokenSpawned(root.Id, -1, Graph.StartNode.Id);
        ArriveAt(root, Graph.StartNode.Id.Index, 0);
    }

    protected override Result OnRun()
    {
        ExecutionStatus status = _status;
        if (_restartPolicy == RestartPolicy.Ignore &&
            status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            return _terminalResult;
        }

        if (_reentranceGuard)
            throw new InvalidOperationException("TokenMachine is already executing.");

        _reentranceGuard = true;
        try
        {
            Result result = RunRound();
            if (StepMode == ParallelStepMode.RunToJoin)
            {
                while (result == Result.InProgress)
                {
                    result = RunRound();
                }
            }

            if (result == Result.InProgress)
                return Result.InProgress;

            Finalise(result);
            return result;
        }
        catch (Exception ex)
        {
            _observer?.OnStateFailed(_steppingTokenId, _steppingNodeId, ex);
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
        // Intentionally empty — Finalise() releases the gate on the terminal path, and the
        // gate must stay held while Execute() returns InProgress between rounds.
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Finalise(Result result)
    {
        try
        {
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
            _executeGate = false;
        }
    }

    // ── The round scheduler (the hot path) ─────────────────────────────

    /// <summary>
    /// Advances every runnable token by one node execution and returns
    /// <see cref="Result.InProgress"/> while runnable tokens remain, or the terminal
    /// aggregate when the run has drained.
    /// </summary>
    private Result RunRound()
    {
        _round++;
        for (int i = 0; i < _pool.Length; i++)
        {
            Token t = _pool[i];
            if (t.Phase != TokenSlotPhase.Runnable || t.SpawnRound == _round)
            {
                continue;
            }

            StepToken(t);
        }

        if (_runnable > 0)
        {
            return Result.InProgress;
        }

        if (_parked > 0)
        {
            // Tokens parked at a join that already fired this run are benign quorum leftovers
            // (e.g. the late arrival of an M-of-N join) and absorb quietly. Tokens parked at a
            // join that never fired are starved — their suppliers died or completed elsewhere —
            // and fail the machine.
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
                    RetireToken(t, IdOf(t.NodeIndex), TokenRetireReason.Absorbed);
                }
                else
                {
                    anyStarved = true;
                    RetireToken(t, IdOf(t.NodeIndex), TokenRetireReason.Starved);
                }
            }

            if (anyStarved)
            {
                return Result.Failure;
            }
        }

        return _anyDied ? Result.Failure : Result.Success;
    }

    /// <summary>Executes one node for one token and routes the result (per-token fault model).</summary>
    private void StepToken(Token t)
    {
        int idx = t.NodeIndex;
        if (!Graph.TryGetNodeByIndex(idx, out INode? node) || node is not LogicNode logicNode)
        {
            throw new InvalidOperationException($"Node #{idx} not found.");
        }

        _steppingTokenId = t.Id;
        _steppingNodeId = logicNode.Id;

        // Per-token Node-scope scratch: two tokens can occupy the same node instance, so the
        // token's board is stamped onto this node only, right before it executes. Graphs with
        // no Node schema skip this entirely.
        if (_hasNodeSchema && _settables[idx] is { } settable)
        {
            settable.SetBlackboards(_blackboards.With(t.NodeBoard!));
        }

        State? stateForLog = _logReportStates[idx];
        if (stateForLog is not null)
        {
            stateForLog.SyncLogReport = _cachedLogReportCallback;
        }

        ILogic syncLogic = logicNode.Logic!;
        if (!t.NodeEntered)
        {
            t.NodeEntered = true;
            logicNode.EnterAction?.Invoke();
        }

        Result result = syncLogic.Execute();
        if (result.Code != Result.StatusCode.InProgress)
        {
            t.Attempts++;
        }

        switch (result.Code)
        {
            case Result.StatusCode.Success:
            {
                t.NodeEntered = false;
                logicNode.ExitAction?.Invoke();
                _observer?.OnStateExited(t.Id, logicNode.Id);

                NodeId next;
                if (logicNode.Logic is IDirector director)
                {
                    next = director.SelectNext();
                    if (next.Equals(NodeId.Default))
                    {
                        RetireToken(t, logicNode.Id, TokenRetireReason.Completed);
                        return;
                    }
                }
                else
                {
                    if (!Graph.TryGetTransition(logicNode.Id, out Transition edge))
                    {
                        _observer?.OnStateFailed(t.Id, logicNode.Id,
                            new InvalidOperationException($"No transition found for state '{logicNode.Id}'."));
                        _anyDied = true;
                        RetireToken(t, logicNode.Id, TokenRetireReason.Failed);
                        return;
                    }

                    if (edge.IsEmpty)
                    {
                        RetireToken(t, logicNode.Id, TokenRetireReason.Completed);
                        return;
                    }

                    next = edge.Destination;
                }

                _observer?.OnTransition(t.Id, logicNode.Id, next);
                t.Attempts = 0;
                t.NodeBoard?.ResetToDefaults();
                ArriveAt(t, next.Index, 0);
                return;
            }
            case Result.StatusCode.Failure:
            {
                if (_retryPolicies is not null)
                {
                    RetryPolicy retry = _retryPolicies[idx];
                    if (t.Attempts < retry.MaxAttempts)
                    {
                        // Frame-stepped runtime: the retry happens next round; the configured
                        // backoff is intentionally not honored (never block a frame).
                        return;
                    }
                }

                t.NodeEntered = false;
                logicNode.ExitAction?.Invoke();

                if (Graph.TryGetTransition(logicNode.Id, out Transition failEdge) &&
                    failEdge.HasFailureDestination)
                {
                    _observer?.OnStateFailed(t.Id, logicNode.Id, null);
                    NodeId handler = failEdge.FailureDestination;
                    _observer?.OnTransition(t.Id, logicNode.Id, handler);
                    t.Attempts = 0;
                    t.NodeBoard?.ResetToDefaults();
                    ArriveAt(t, handler.Index, 0);
                    return;
                }

                _observer?.OnStateFailed(t.Id, logicNode.Id, null);
                _anyDied = true;
                RetireToken(t, logicNode.Id, TokenRetireReason.Failed);
                return;
            }
            case Result.StatusCode.InProgress:
                // Multi-tick sync node: the token stays; next round re-runs it.
                return;
            default:
                throw new InvalidOperationException("Unknown node result.");
        }
    }

    /// <summary>
    /// Lands <paramref name="t"/> on node <paramref name="idx"/>, resolving fork/join structure
    /// at arrival: forks fan out (branch 0 continues this token, the rest spawn), joins park or
    /// fire per policy. Cascades recursively — a token never rests on a fork.
    /// </summary>
    private void ArriveAt(Token t, int idx, int depth)
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
            PassThroughFork(t, idx, fork, depth);
            return;
        }

        JoinState? join = _joins[idx];
        if (join is not null)
        {
            ArriveAtJoin(t, idx, join, depth);
            return;
        }

        t.NodeIndex = idx;
        t.NodeEntered = false;
        _observer?.OnStateEntered(t.Id, IdOf(idx));
    }

    private void PassThroughFork(Token t, int idx, ForkState fork, int depth)
    {
        LogicNode forkNode = (LogicNode)Graph.GetNodeByIndex(idx);
        NodeId forkId = forkNode.Id;
        _observer?.OnStateEntered(t.Id, forkId);
        forkNode.EnterAction?.Invoke();
        forkNode.ExitAction?.Invoke();
        _observer?.OnStateExited(t.Id, forkId);

        // Branch 0 continues the arriving token; every other branch spawns a new one.
        // Branch ids are resolved through the graph so observers see the applied display
        // names (the fork stores pre-Build ids, whose names are empty).
        int firstIndex = fork.BranchAt(0).Index;
        _observer?.OnTransition(t.Id, forkId, IdOf(firstIndex));
        ArriveAt(t, firstIndex, depth + 1);

        for (int b = 1; b < fork.BranchCount; b++)
        {
            Token child = Alloc();
            int branchIndex = fork.BranchAt(b).Index;
            _observer?.OnTokenSpawned(child.Id, t.Id, forkId);
            _observer?.OnTransition(child.Id, forkId, IdOf(branchIndex));
            ArriveAt(child, branchIndex, depth + 1);
        }
    }

    private void ArriveAtJoin(Token t, int idx, JoinState join, int depth)
    {
        LogicNode joinNode = (LogicNode)Graph.GetNodeByIndex(idx);
        NodeId joinId = joinNode.Id;
        _observer?.OnStateEntered(t.Id, joinId);
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
                RetireToken(parked, joinId, TokenRetireReason.Joined);
                toConsume--;
            }
        }

        _observer?.OnJoinFired(joinId, t.Id);
        joinNode.ExitAction?.Invoke();
        _observer?.OnStateExited(t.Id, joinId);

        t.Attempts = 0;
        t.NodeBoard?.ResetToDefaults();

        if (!Graph.TryGetTransition(joinId, out Transition edge) || edge.IsEmpty)
        {
            RetireToken(t, joinId, TokenRetireReason.Completed);
            return;
        }

        _observer?.OnTransition(t.Id, joinId, edge.Destination);
        ArriveAt(t, edge.Destination.Index, depth + 1);
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

    private void RetireToken(Token t, NodeId at, TokenRetireReason reason)
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
        _observer?.OnTokenRetired(t.Id, at, reason);
    }

    private NodeId IdOf(int index) => Graph.GetNodeByIndex(index).Id;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LogReportCallback(string message)
    {
        // Attribution uses the token currently being stepped.
        _observer?.OnLogReport(_steppingTokenId, _steppingNodeId, message);
    }

    // ── Status plumbing ─────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TransitionTo(ExecutionStatus next)
    {
        ExecutionStatus prev = _status;
        _status = next;
        if (prev != next)
        {
            NotifyMachineStatusChange(prev, next);
        }
    }

    private void NotifyMachineStatusChange(ExecutionStatus prev, ExecutionStatus next)
    {
        if (_observer is null)
        {
            return;
        }

        NodeId graphId = Graph.Id;
        _observer.TokenMachineStatusChanged(graphId, prev, next);

        switch (next)
        {
            case ExecutionStatus.Starting:
                _observer.OnTokenMachineStarted(graphId);
                break;
            case ExecutionStatus.Completed:
                _observer.OnTokenMachineCompleted(graphId, Result.Success);
                break;
            case ExecutionStatus.Failed:
                _observer.OnTokenMachineCompleted(graphId, Result.Failure);
                break;
            case ExecutionStatus.Resetting:
                _observer.OnTokenMachineReset(graphId);
                break;
            case ExecutionStatus.Cancelled:
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
