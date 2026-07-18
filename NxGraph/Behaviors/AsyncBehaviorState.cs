using NxGraph.Fsm.Async;

namespace NxGraph.Behaviors;

/// <summary>
/// Async twin of <see cref="BehaviorState"/>: runs a declarative sequence of
/// <see cref="IAsyncBehavior"/> entries inside one node — in order, <b>fail-fast</b>: the
/// first non-<c>Success</c> entry stops the sequence and the node returns <c>Failure</c>.
/// Fail-fast is the deliberate opposite of <c>AsyncAllState</c>'s run-all-then-combine:
/// sequence entries may depend on earlier entries' writes, while <c>AllState</c>'s works are
/// concurrent and independent.
/// <para>
/// The node keeps the whole fault model: <c>.Retry(...)</c> re-runs the <b>whole</b> list in
/// place (behaviors should be idempotent or tolerate re-execution), <c>.OnError(...)</c>
/// reroutes, <c>.WithOutcome(...)</c> codes, and exceptions propagate as from any node logic.
/// Behavior output goes through the report channel (<see cref="BehaviorContext.Report"/> →
/// observer <c>OnLogReport</c>), never the console. Agent-typed entries
/// (<see cref="IAsyncAgentBehavior"/>) are rejected at wiring time — use
/// <see cref="AsyncBehaviorState{TAgent}"/> / <c>ToBehaviorsAsync&lt;TAgent&gt;</c>.
/// </para>
/// </summary>
public sealed class AsyncBehaviorState : AsyncState, IBehaviorComposite, IBehaviorReportSink
{
    private readonly IAsyncBehavior[] _behaviors;
    private CancellationToken _reportCt;

    /// <param name="behaviors">The behaviors to run in order. At least one is required;
    /// null entries and agent-typed (<see cref="IAsyncAgentBehavior"/>) entries are rejected.</param>
    public AsyncBehaviorState(params IAsyncBehavior[] behaviors)
    {
        _behaviors = BehaviorComposition.ValidateEntries(behaviors);
        for (int i = 0; i < behaviors.Length; i++)
        {
            if (behaviors[i] is IAsyncAgentBehavior)
            {
                throw BehaviorComposition.AgentEntryInUntyped(behaviors[i], i, "ToBehaviorsAsync<TAgent>");
            }
        }
    }

    /// <summary>The behavior entries in sequence order.</summary>
    public IReadOnlyList<IAsyncBehavior> Behaviors => _behaviors;

    protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        _reportCt = ct;
        BehaviorContext ctx = new(Bb, this);
        IAsyncBehavior[] behaviors = _behaviors;
        for (int i = 0; i < behaviors.Length; i++)
        {
            Result result = await behaviors[i].ExecuteAsync(ctx, ct).ConfigureAwait(false);
            if (result != Result.Success)
            {
                return Result.Failure;
            }
        }

        return Result.Success;
    }

    bool IBehaviorComposite.IsSync => false;
    Type? IBehaviorComposite.AgentType => null;
    IReadOnlyList<object> IBehaviorComposite.Entries => _behaviors;

    bool IBehaviorReportSink.HasReporter => LogReport is not null;

    void IBehaviorReportSink.Report(string message)
    {
        if (LogReport is { } report)
        {
            BehaviorComposition.Await(report(message, _reportCt));
        }
    }
}

/// <summary>
/// Agent-typed twin of <see cref="AsyncBehaviorState"/> — see
/// <see cref="BehaviorState{TAgent}"/> for the full contract: the agent arrives through the
/// standard <c>IAgentSettable</c> stamping walk and is passed to
/// <see cref="IAsyncBehavior{TAgent}"/> entries as a call parameter per execution (never
/// stamped onto behavior instances); plain entries run agent-blind; wrong-agent-type entries
/// are rejected at wiring time; dispatch is pre-split at construction so the run loop is a
/// branch plus a call.
/// </summary>
/// <typeparam name="TAgent">The agent type delivered to typed entries.</typeparam>
public sealed class AsyncBehaviorState<TAgent> : AsyncState<TAgent>, IBehaviorComposite, IBehaviorReportSink
{
    private readonly IAsyncBehavior[] _behaviors;
    private readonly IAsyncBehavior<TAgent>?[] _typed;
    private CancellationToken _reportCt;

    /// <param name="behaviors">The behaviors to run in order — plain and agent-typed entries
    /// may mix. At least one is required; null entries are rejected, as is any
    /// <see cref="IAsyncAgentBehavior"/> entry whose agent type is not <typeparamref name="TAgent"/>.</param>
    public AsyncBehaviorState(params IAsyncBehavior[] behaviors)
    {
        _behaviors = BehaviorComposition.ValidateEntries(behaviors);
        _typed = new IAsyncBehavior<TAgent>?[behaviors.Length];
        for (int i = 0; i < behaviors.Length; i++)
        {
            if (behaviors[i] is not IAsyncAgentBehavior)
            {
                continue;
            }

            _typed[i] = behaviors[i] as IAsyncBehavior<TAgent> ?? throw BehaviorComposition.AgentTypeMismatch(
                behaviors[i], i, typeof(TAgent), typeof(IAsyncBehavior<>));
        }
    }

    /// <summary>The behavior entries in sequence order.</summary>
    public IReadOnlyList<IAsyncBehavior> Behaviors => _behaviors;

    protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        _reportCt = ct;
        BehaviorContext ctx = new(Bb, this);
        IAsyncBehavior[] behaviors = _behaviors;
        IAsyncBehavior<TAgent>?[] typed = _typed;
        for (int i = 0; i < behaviors.Length; i++)
        {
            Result result = typed[i] is { } agentBehavior
                ? await agentBehavior.ExecuteAsync(Agent, ctx, ct).ConfigureAwait(false)
                : await behaviors[i].ExecuteAsync(ctx, ct).ConfigureAwait(false);
            if (result != Result.Success)
            {
                return Result.Failure;
            }
        }

        return Result.Success;
    }

    bool IBehaviorComposite.IsSync => false;
    Type? IBehaviorComposite.AgentType => typeof(TAgent);
    IReadOnlyList<object> IBehaviorComposite.Entries => _behaviors;

    bool IBehaviorReportSink.HasReporter => LogReport is not null;

    void IBehaviorReportSink.Report(string message)
    {
        if (LogReport is { } report)
        {
            BehaviorComposition.Await(report(message, _reportCt));
        }
    }
}
