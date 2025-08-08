namespace NxGraph.Graphs;

public sealed class Node
{
    public NodeId Key { get; }
    public INode Logic { get; }
    public static readonly Node Empty = new(NodeId.Default, new EmptyNodeLogic());

    internal Node(NodeId key, INode logic)
    {
        Key = key;
        Logic = logic;
    }
}

public sealed record EmptyNodeLogic : INode
{
    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        return ValueTask.FromResult(Result.Success);
    }
}