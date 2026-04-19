using NxGraph.Graphs;

namespace NxGraph.Fsm.Async;

public sealed class AsyncSwitchState<TKey>(
    Func<ValueTask<TKey>> selector,
    IReadOnlyDictionary<TKey, NodeId> cases,
    NodeId defaultNode = default)
    : IAsyncLogic, IAsyncDirector
    where TKey : notnull
{
    private readonly Func<ValueTask<TKey>> _selector = selector ?? throw new ArgumentNullException(nameof(selector));
    private readonly IReadOnlyDictionary<TKey, NodeId> _cases = cases ?? throw new ArgumentNullException(nameof(cases));
    private NodeId _defaultNode = defaultNode;


    public async ValueTask<NodeId> SelectNextAsync(CancellationToken ct = default)
    {
        TKey key = await _selector();
        return _cases.GetValueOrDefault(key, _defaultNode);
        
    }

    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        return ResultHelpers.Success;
    }

    /// <summary>
    /// Sets the default node to be used when no case matches the selector's key.
    /// </summary>
    /// <param name="defaultNode">The default node to set.</param>
    internal void SetDefault(NodeId defaultNode)
    {
        _defaultNode = defaultNode;
    }
}