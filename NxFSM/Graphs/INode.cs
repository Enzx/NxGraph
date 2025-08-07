namespace NxFSM.Graphs;

/// <summary>
/// Interface representing a node in the FSM graph.
/// </summary>
public interface INode
{
    ValueTask<Result> ExecuteAsync(CancellationToken ct = default);
}