using NxGraph.Blackboards;
using NxGraph.Graphs;

namespace NxGraph.Fsm.Async;

/// <summary>
/// <b>Delegate-backed</b> async director that picks between two destinations via a predicate
/// (the <c>Relay*</c> family — a state whose decision closes over code; it cannot ride a
/// serialization payload, so prefer the data-built <c>ChoiceState</c> when the decision is
/// data). The
/// blackboard-context overload receives the machine-bound routed context (see
/// <see cref="BlackboardContext"/>), so branching can read shared memory instead of
/// closing over ad-hoc state.
/// </summary>
public sealed class AsyncRelayChoiceState : IAsyncLogic, IAsyncDirector, IBlackboardSettable
{
    private readonly Func<ValueTask<bool>>? _predicate;
    private readonly Func<BlackboardContext, ValueTask<bool>>? _bbPredicate;
    private readonly NodeId _trueNode;
    private readonly NodeId _falseNode;
    private BlackboardContext _blackboards;

    public AsyncRelayChoiceState(Func<ValueTask<bool>> predicate, NodeId trueNode, NodeId falseNode)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _trueNode = trueNode;
        _falseNode = falseNode;
    }

    public AsyncRelayChoiceState(Func<BlackboardContext, ValueTask<bool>> predicate, NodeId trueNode, NodeId falseNode)
    {
        _bbPredicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _trueNode = trueNode;
        _falseNode = falseNode;
    }

    void IBlackboardSettable.SetBlackboards(in BlackboardContext context) => _blackboards = context;

    public async ValueTask<NodeId> SelectNextAsync(CancellationToken ct = default)
    {
        bool result = _bbPredicate is not null
            ? await _bbPredicate(_blackboards).ConfigureAwait(false)
            : await _predicate!().ConfigureAwait(false);
        return result ? _trueNode : _falseNode;
    }

    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        return ResultHelpers.Success;
    }

    /// <inheritdoc />
    public IEnumerable<NodeId> EnumerateStaticTargets()
    {
        yield return _trueNode;
        yield return _falseNode;
    }
}
