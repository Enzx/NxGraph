using NxGraph.Blackboards;
using NxGraph.Graphs;

namespace NxGraph.Fsm.Async;

/// <summary>
/// Async switch/case director. The blackboard-context overload receives the machine-bound
/// routed context (see <see cref="BlackboardContext"/>), so the selector can read shared
/// memory instead of closing over ad-hoc state.
/// </summary>
public sealed class AsyncSwitchState<TKey> : IAsyncLogic, IAsyncDirector, IBlackboardSettable
    where TKey : notnull
{
    private readonly Func<ValueTask<TKey>>? _selector;
    private readonly Func<BlackboardContext, ValueTask<TKey>>? _bbSelector;
    private readonly IReadOnlyDictionary<TKey, NodeId> _cases;
    // When no explicit default is supplied, fall back to NodeId.Default — the async runtime
    // (AsyncStateMachine.InternalRunAsync) treats that as a terminal-success exit from the
    // director. Defaulting to default(NodeId) would silently route to Start (index 0).
    private NodeId _defaultNode;
    private BlackboardContext _blackboards;

    public AsyncSwitchState(
        Func<ValueTask<TKey>> selector,
        IReadOnlyDictionary<TKey, NodeId> cases,
        NodeId defaultNode = default)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _cases = cases ?? throw new ArgumentNullException(nameof(cases));
        _defaultNode = defaultNode.Equals(default(NodeId)) ? NodeId.Default : defaultNode;
    }

    public AsyncSwitchState(
        Func<BlackboardContext, ValueTask<TKey>> selector,
        IReadOnlyDictionary<TKey, NodeId> cases,
        NodeId defaultNode = default)
    {
        _bbSelector = selector ?? throw new ArgumentNullException(nameof(selector));
        _cases = cases ?? throw new ArgumentNullException(nameof(cases));
        _defaultNode = defaultNode.Equals(default(NodeId)) ? NodeId.Default : defaultNode;
    }

    void IBlackboardSettable.SetBlackboards(in BlackboardContext context) => _blackboards = context;

    public async ValueTask<NodeId> SelectNextAsync(CancellationToken ct = default)
    {
        TKey key = _bbSelector is not null
            ? await _bbSelector(_blackboards).ConfigureAwait(false)
            : await _selector!().ConfigureAwait(false);
        return _cases.GetValueOrDefault(key, _defaultNode);
    }

    /// <inheritdoc />
    public IEnumerable<NodeId> EnumerateStaticTargets()
    {
        foreach (NodeId target in _cases.Values)
            yield return target;
        yield return _defaultNode;
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
