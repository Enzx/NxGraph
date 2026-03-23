namespace NxGraph.Graphs;

public interface INode
{
    NodeId Id { get; }
    public IAsyncLogic AsyncLogic { get; }
}

public class LogicNode(NodeId id, IAsyncLogic asyncLogic) : INode
{
    public NodeId Id { get; } = id;
    public IAsyncLogic AsyncLogic { get; } = asyncLogic;

    public static readonly LogicNode Empty = new(NodeId.Default, new EmptyAsyncLogic());
    public static readonly LogicNode StateMachineMarker = new(NodeId.Default, new EmptyAsyncLogic());
}

public sealed record EmptyAsyncLogic : IAsyncLogic, ILogic
{
    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        return ResultHelpers.Success;
    }

    public Result Execute()
    {
        return Result.Success;
    }
}