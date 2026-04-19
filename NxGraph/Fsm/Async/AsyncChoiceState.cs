using NxGraph.Graphs;

namespace NxGraph.Fsm.Async;

public sealed class AsyncChoiceState(Func<ValueTask<bool>> predicate, NodeId trueNode, NodeId falseNode) : IAsyncLogic, IAsyncDirector
{
    private readonly Func<ValueTask<bool>> _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    
    public async ValueTask<NodeId> SelectNextAsync(CancellationToken ct = default)
    {
        bool result = await _predicate().ConfigureAwait(false);
        return result ? trueNode : falseNode;
    }

    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        return ResultHelpers.Success;
    }
}