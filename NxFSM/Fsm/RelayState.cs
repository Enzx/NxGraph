using NxFSM.Graphs;

namespace NxFSM.Fsm;

public sealed class RelayState(
    Func<CancellationToken, ValueTask<Result>> run,
    Func<CancellationToken, ValueTask>? onEnter = null,
    Func<CancellationToken, ValueTask>? onExit = null)
    : State
{
    private readonly Func<CancellationToken, ValueTask<Result>> _run =
        run ?? throw new ArgumentNullException(nameof(run));

    protected override ValueTask OnEnterAsync(CancellationToken ct) => onEnter?.Invoke(ct) ?? default;
    protected override ValueTask<Result> OnRunAsync(CancellationToken ct) => _run(ct);
    protected override ValueTask OnExitAsync(CancellationToken ct) => onExit?.Invoke(ct) ?? default;
}

public sealed class RelayState<TAgent>(
    Func<TAgent, CancellationToken, ValueTask<Result>> run,
    Func<TAgent, CancellationToken, ValueTask>? onEnter = null,
    Func<TAgent, CancellationToken, ValueTask>? onExit = null)
    : State<TAgent>
{
    private readonly Func<TAgent, CancellationToken, ValueTask<Result>> _run =
        run ?? throw new ArgumentNullException(nameof(run));

    protected override ValueTask OnEnterAsync(CancellationToken ct) => onEnter?.Invoke(Agent, ct) ?? default;
    protected override ValueTask<Result> OnRunAsync(CancellationToken ct) => _run(Agent, ct);
    protected override ValueTask OnExitAsync(CancellationToken ct) => onExit?.Invoke(Agent, ct) ?? default;
}

/// <summary>
/// A director is a node that selects the next node to run based on some logic.
/// </summary>
public interface IDirector
{
    /// <summary>
    /// Selects the next node to run based on some logic.
    /// </summary>
    /// <returns></returns>
    NodeId SelectNext();
}

/// <summary>
/// Executes a predicate and immediately returns <see cref="Result.Success"/>; the
/// destination is selected by the single transition wired to this node.
/// </summary>
public sealed class ChoiceState(Func<bool> predicate, NodeId trueNode, NodeId falseNode) : State, IDirector
{
    private readonly Func<bool> _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));


    protected override ValueTask<Result> OnRunAsync(CancellationToken _) => ResultHelpers.Success;

    /// <summary>
    /// Selects the next node based on the predicate.
    /// </summary>
    /// <returns>The next node to run.</returns>
    public NodeId SelectNext()
    {
        return _predicate() ? trueNode : falseNode;
    }
}

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