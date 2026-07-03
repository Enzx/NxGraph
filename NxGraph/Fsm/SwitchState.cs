using NxGraph.Blackboards;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Branches like a switch/case.  Finishes immediately with <see cref="Result.Success"/>; the
/// runtime asks <see cref="SelectNext"/> for the next node.
/// The blackboard-context overload receives the machine-bound routed context (see
/// <see cref="BlackboardContext"/>), so the selector can read shared memory instead of
/// closing over ad-hoc state.
/// Purely synchronous — the authoring layer wraps this in a <see cref="SyncLogicAdapter"/>
/// so that async runtimes can also execute it.
/// </summary>
public sealed class SwitchState<TKey> : ILogic, IDirector, IBlackboardSettable
    where TKey : notnull
{
    private readonly Func<TKey>? _selector;
    private readonly Func<BlackboardContext, TKey>? _bbSelector;
    private readonly IReadOnlyDictionary<TKey, NodeId> _cases;
    // When no explicit default is supplied, fall back to NodeId.Default — both the sync and
    // the async runtimes treat that as a terminal-success exit from the director. Defaulting
    // to default(NodeId) would silently route to Start (index 0) instead.
    private NodeId _defaultNode;
    private BlackboardContext _blackboards;

    public SwitchState(
        Func<TKey> selector,
        IReadOnlyDictionary<TKey, NodeId> cases,
        NodeId defaultNode = default)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _cases = cases ?? throw new ArgumentNullException(nameof(cases));
        _defaultNode = defaultNode.Equals(default(NodeId)) ? NodeId.Default : defaultNode;
    }

    public SwitchState(
        Func<BlackboardContext, TKey> selector,
        IReadOnlyDictionary<TKey, NodeId> cases,
        NodeId defaultNode = default)
    {
        _bbSelector = selector ?? throw new ArgumentNullException(nameof(selector));
        _cases = cases ?? throw new ArgumentNullException(nameof(cases));
        _defaultNode = defaultNode.Equals(default(NodeId)) ? NodeId.Default : defaultNode;
    }

    void IBlackboardSettable.SetBlackboards(in BlackboardContext context) => _blackboards = context;

    /// <summary>
    /// Selects the next node based on the selector function.
    /// </summary>
    /// <returns>The next node to run.</returns>
    public NodeId SelectNext()
    {
        TKey key = _bbSelector is not null ? _bbSelector(_blackboards) : _selector!();
        return _cases.GetValueOrDefault(key, _defaultNode);
    }

    /// <inheritdoc />
    public IEnumerable<NodeId> EnumerateStaticTargets()
    {
        foreach (NodeId target in _cases.Values)
            yield return target;
        yield return _defaultNode;
    }


    /// <inheritdoc />
    public Result Execute() => Result.Success;

    /// <summary>
    /// Sets the default node to be used when no case matches the selector's key.
    /// </summary>
    /// <param name="defaultNode">The default node to set.</param>
    internal void SetDefault(NodeId defaultNode)
    {
        _defaultNode = defaultNode;
    }
}
