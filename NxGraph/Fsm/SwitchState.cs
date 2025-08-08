using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Branches like a switch/case.  Finish immediately with <see cref="Result.Success"/>; the
/// runtime asks <see cref="SelectNext"/> for the next node.
/// </summary>
public sealed class SwitchState<TKey>(
    Func<TKey> selector,
    IReadOnlyDictionary<TKey, NodeId> cases,
    NodeId defaultNode = default)
    : State, IDirector
    where TKey : notnull
{
    private readonly Func<TKey> _selector = selector ?? throw new ArgumentNullException(nameof(selector));
    private NodeId _defaultNode = defaultNode;

    /// <summary>
    /// Selects the next node based on the selector function.
    /// </summary>
    /// <returns>The next node to run.</returns>
    public NodeId SelectNext()
    {
        TKey key = _selector();
        return cases.GetValueOrDefault(key, _defaultNode);
    }

    protected override ValueTask<Result> OnRunAsync(CancellationToken _) => ResultHelpers.Success;

    /// <summary>
    /// Sets the default node to be used when no case matches the selector's key.
    /// </summary>
    /// <param name="defaultNode">The default node to set.</param>
    internal void SetDefault(NodeId defaultNode)
    {
        _defaultNode = defaultNode;
    }
}