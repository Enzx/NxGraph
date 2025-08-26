namespace NxGraph.Graphs;


public sealed class Node(NodeId id, ILogic logic) 
{
    public NodeId Id { get; } = id;
    public ILogic Logic { get; } = logic;

    public static readonly Node Empty = new(NodeId.Default, new EmptyLogicLogic());
}

public sealed record EmptyLogicLogic : ILogic
{
    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        return ValueTask.FromResult(Result.Success);
    }
}