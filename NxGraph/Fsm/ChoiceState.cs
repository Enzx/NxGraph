using NxGraph.Graphs;

namespace NxGraph.Fsm;

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