namespace NxGraph.Graphs;

public sealed class Node(NodeId id, INode logic)
{
    public NodeId Id { get; } = id;
    public INode Logic { get; } = logic;

    public static readonly Node Empty = new(NodeId.Default, new EmptyNodeLogic());
}

public sealed record EmptyNodeLogic : INode
{
    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        return ValueTask.FromResult(Result.Success);
    }
}