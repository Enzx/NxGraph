using NxFSM.Graphs;

namespace NxFSM.Fsm;

/// <summary>
/// A composite state that contains a single child node.
/// </summary>
/// <param name="child">The child node to execute.</param>
public class CompositeState(INode child) : State
{
    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
        => child.ExecuteAsync(ct);
}