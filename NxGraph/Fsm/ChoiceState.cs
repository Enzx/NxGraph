using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Executes a predicate and immediately returns <see cref="Result.Success"/>; the
/// destination is selected via <see cref="IDirector.SelectNext"/>.
/// Purely synchronous — the authoring layer wraps this in a <see cref="SyncLogicAdapter"/>
/// so that async runtimes can also execute it.
/// </summary>
public sealed class ChoiceState(Func<bool> predicate, NodeId trueNode, NodeId falseNode) : ILogic, IDirector
{
    private readonly Func<bool> _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));


    /// <inheritdoc />
    public Result Execute() => Result.Success;

    /// <summary>
    /// Selects the next node based on the predicate.
    /// </summary>
    /// <returns>The next node to run.</returns>
    public NodeId SelectNext()
    {
        return _predicate() ? trueNode : falseNode;
    }
}