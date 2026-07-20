using NxGraph.Diagnostics.Replay;
using NxGraph.Fsm;

namespace NxGraph.Behaviors;

/// <summary>
/// Runs a declarative sequence of <see cref="IBehavior"/> entries inside one node — in order,
/// <b>fail-fast</b>: the first non-<c>Success</c> entry stops the sequence and the node
/// returns <c>Failure</c>. Fail-fast is the deliberate opposite of <c>AllState</c>'s
/// run-all-then-combine: sequence entries may depend on earlier entries' writes, while
/// <c>AllState</c>'s works are concurrent and independent.
/// <para>
/// Everything above the node is untouched: <c>.Retry(...)</c> re-runs the <b>whole</b> list
/// in place (behaviors should be idempotent or tolerate re-execution), <c>.OnError(...)</c>
/// reroutes, <c>.WithOutcome(...)</c> codes, and exceptions propagate as from any node logic.
/// Behavior output goes through the report channel (<see cref="BehaviorContext.Report"/> →
/// observer <c>OnLogReport</c>), never the console. The run loop is an array walk over one
/// stack-allocated context — 0 B. Agent-typed entries (<see cref="IAgentBehavior"/>) are
/// rejected at wiring time — use <see cref="BehaviorState{TAgent}"/> /
/// <c>ToBehaviors&lt;TAgent&gt;</c>. Being a <see cref="State"/>, this node also runs under
/// the async machine via the sync-logic adapter.
/// </para>
/// </summary>
public sealed class BehaviorState : State, IBehaviorComposite, IBehaviorReportSink
{
    private readonly IBehavior[] _behaviors;

    /// <param name="behaviors">The behaviors to run in order. At least one is required;
    /// null entries and agent-typed (<see cref="IAgentBehavior"/>) entries are rejected.</param>
    public BehaviorState(params IBehavior[] behaviors)
    {
        _behaviors = BehaviorComposition.ValidateEntries(behaviors);
        for (int i = 0; i < behaviors.Length; i++)
        {
            if (behaviors[i] is IAgentBehavior)
            {
                throw BehaviorComposition.AgentEntryInUntyped(behaviors[i], i, "ToBehaviors<TAgent>");
            }
        }
    }

    /// <summary>The behavior entries in sequence order.</summary>
    public IReadOnlyList<IBehavior> Behaviors => _behaviors;

    protected override Result OnRun()
    {
        BehaviorContext ctx = new(Bb, this);
        IBehavior[] behaviors = _behaviors;
        for (int i = 0; i < behaviors.Length; i++)
        {
            if (behaviors[i].Execute(in ctx) != Result.Success)
            {
                return Result.Failure;
            }
        }

        return Result.Success;
    }

    bool IBehaviorComposite.IsSync => true;
    Type? IBehaviorComposite.AgentType => null;
    IReadOnlyList<object> IBehaviorComposite.Entries => _behaviors;

    bool IBehaviorReportSink.HasReporter => BehaviorComposition.SyncHasReporter(this);
    void IBehaviorReportSink.Report(string message) => BehaviorComposition.SyncReport(this, message);
}

/// <summary>
/// Agent-typed twin of <see cref="BehaviorState"/>: receives the machine-bound agent through
/// the standard <c>IAgentSettable</c> stamping walk (so <c>Graph.SetAgent</c> acceptor
/// counting and the shared-graph contract apply unchanged) and passes it to
/// <see cref="IBehavior{TAgent}"/> entries <b>as a call parameter per execution</b> — the
/// agent is never stamped onto behavior instances, which stay shareable data objects. Plain
/// <see cref="IBehavior"/> entries run agent-blind in the same sequence; an
/// <see cref="IAgentBehavior"/> entry of a different agent type is rejected at wiring time.
/// Dispatch is pre-split at construction, so the run loop stays a branch plus a call — 0 B.
/// Sequence semantics are identical to the untyped composite (in order, fail-fast).
/// </summary>
/// <typeparam name="TAgent">The agent type delivered to typed entries.</typeparam>
public sealed class BehaviorState<TAgent> : State<TAgent>, IBehaviorComposite, IBehaviorReportSink
{
    private readonly IBehavior[] _behaviors;
    private readonly IBehavior<TAgent>?[] _typed;

    /// <param name="behaviors">The behaviors to run in order — plain and agent-typed entries
    /// may mix. At least one is required; null entries are rejected, as is any
    /// <see cref="IAgentBehavior"/> entry whose agent type is not <typeparamref name="TAgent"/>.</param>
    public BehaviorState(params IBehavior[] behaviors)
    {
        _behaviors = BehaviorComposition.ValidateEntries(behaviors);
        _typed = new IBehavior<TAgent>?[behaviors.Length];
        for (int i = 0; i < behaviors.Length; i++)
        {
            if (behaviors[i] is not IAgentBehavior)
            {
                continue;
            }

            _typed[i] = behaviors[i] as IBehavior<TAgent> ?? throw BehaviorComposition.AgentTypeMismatch(
                behaviors[i], i, typeof(TAgent), typeof(IBehavior<>));
        }
    }

    /// <summary>The behavior entries in sequence order.</summary>
    public IReadOnlyList<IBehavior> Behaviors => _behaviors;

    protected override Result OnRun()
    {
        BehaviorContext ctx = new(Bb, this);
        IBehavior[] behaviors = _behaviors;
        IBehavior<TAgent>?[] typed = _typed;
        for (int i = 0; i < behaviors.Length; i++)
        {
            Result result = typed[i] is { } agentBehavior
                ? agentBehavior.Execute(Agent, in ctx)
                : behaviors[i].Execute(in ctx);
            if (result != Result.Success)
            {
                return Result.Failure;
            }
        }

        return Result.Success;
    }

    bool IBehaviorComposite.IsSync => true;
    Type? IBehaviorComposite.AgentType => typeof(TAgent);
    IReadOnlyList<object> IBehaviorComposite.Entries => _behaviors;

    bool IBehaviorReportSink.HasReporter => BehaviorComposition.SyncHasReporter(this);
    void IBehaviorReportSink.Report(string message) => BehaviorComposition.SyncReport(this, message);
}

/// <summary>
/// Shared wiring-time validation and report-channel plumbing for the four behavior
/// composites.
/// </summary>
internal static class BehaviorComposition
{
    internal const string ParamName = "behaviors";

    internal static TEntry[] ValidateEntries<TEntry>(TEntry[] behaviors) where TEntry : class
    {
        if (behaviors is null || behaviors.Length == 0)
        {
            throw new ArgumentException("At least one behavior is required.", ParamName);
        }

        foreach (TEntry? behavior in behaviors)
        {
            if (behavior is null)
            {
                throw new ArgumentException("Behaviors must not contain null entries.", ParamName);
            }
        }

        return behaviors;
    }

    internal static ArgumentException AgentEntryInUntyped(object entry, int index, string typedDsl)
    {
        return new ArgumentException(
            $"Behavior at index {index} ('{entry.GetType().Name}') is agent-typed and cannot run in an " +
            $"untyped composite — use {typedDsl}(...) so the machine-bound agent reaches it.", ParamName);
    }

    internal static ArgumentException AgentTypeMismatch(object entry, int index, Type compositeAgent,
        Type openInterface)
    {
        // Cold throw path: reflection is only used to name the entry's agent type in the
        // message — the check itself is the marker-interface pattern match above.
        Type? entryAgent = null;
        foreach (Type implemented in entry.GetType().GetInterfaces())
        {
            if (implemented.IsGenericType && implemented.GetGenericTypeDefinition() == openInterface)
            {
                entryAgent = implemented.GetGenericArguments()[0];
                break;
            }
        }

        return new ArgumentException(
            $"Behavior at index {index} ('{entry.GetType().Name}') expects agent type " +
            $"'{entryAgent?.Name ?? "<unknown>"}' but this composite binds agent type " +
            $"'{compositeAgent.Name}'.", ParamName);
    }

    internal static bool SyncHasReporter(State state) =>
        state.SyncLogReport is not null || ((ILogReporter)state).LogReport is not null;

    /// <summary>
    /// Delivers a report from a sync composite: through the sync callback when the sync
    /// machine wired it, else through the async callback (the async machine wires that slot
    /// when the composite runs behind the sync-logic adapter), waiting out a genuinely
    /// asynchronous observer so delivery-before-return holds on both runtimes.
    /// </summary>
    internal static void SyncReport(State state, string message)
    {
        if (state.SyncLogReport is { } sync)
        {
            sync(message);
            return;
        }

        if (((ILogReporter)state).LogReport is { } asyncReport)
        {
            ValueTaskSync.Await(asyncReport(message, CancellationToken.None));
        }
    }
}
