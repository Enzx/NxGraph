using NxGraph.Blackboards;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// <b>Delegate-backed</b> two-way branch (the <c>Relay*</c> family — a state whose decision
/// closes over code): executes a predicate and immediately returns
/// <see cref="Result.Success"/>; the destination is selected via
/// <see cref="IDirector.SelectNext"/>.
/// The blackboard-context overload receives the machine-bound routed context (see
/// <see cref="BlackboardContext"/>), so branching can read shared memory instead of
/// closing over ad-hoc state.
/// Purely synchronous — the authoring layer wraps this in a <see cref="SyncLogicAdapter"/>
/// so that async runtimes can also execute it.
/// <para>
/// A closure cannot ride a serialization payload, so a graph branching through this state
/// cannot round-trip and therefore cannot survive suspend/resume. When the decision is data
/// — a comparison against a blackboard slot — use the data-built <see cref="ChoiceState"/>
/// instead, which serializes and renders labelled Mermaid arms.
/// </para>
/// </summary>
public sealed class RelayChoiceState : ILogic, IDirector, IBlackboardSettable
{
    private readonly Func<bool>? _predicate;
    private readonly Func<BlackboardContext, bool>? _bbPredicate;
    private readonly NodeId _trueNode;
    private readonly NodeId _falseNode;
    private BlackboardContext _blackboards;

    public RelayChoiceState(Func<bool> predicate, NodeId trueNode, NodeId falseNode)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _trueNode = trueNode;
        _falseNode = falseNode;
    }

    public RelayChoiceState(Func<BlackboardContext, bool> predicate, NodeId trueNode, NodeId falseNode)
    {
        _bbPredicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _trueNode = trueNode;
        _falseNode = falseNode;
    }

    void IBlackboardSettable.SetBlackboards(in BlackboardContext context) => _blackboards = context;

    /// <inheritdoc />
    public Result Execute() => Result.Success;

    /// <summary>
    /// Selects the next node based on the predicate.
    /// </summary>
    /// <returns>The next node to run.</returns>
    public NodeId SelectNext()
    {
        bool taken = _bbPredicate is not null ? _bbPredicate(_blackboards) : _predicate!();
        return taken ? _trueNode : _falseNode;
    }

    /// <inheritdoc />
    public IEnumerable<NodeId> EnumerateStaticTargets()
    {
        yield return _trueNode;
        yield return _falseNode;
    }
}
