namespace NxGraph.Graphs;

public interface INode
{
    NodeId Id { get; }
    public ILogic Logic { get; }

}

public class LogicNode(NodeId id, ILogic logic) : INode
{
    public NodeId Id { get; } = id;
    public ILogic Logic { get; } = logic;

    public static readonly LogicNode Empty = new(NodeId.Default, new EmptyLogic());
    public static readonly LogicNode StateMachineMarker = new(NodeId.Default, new EmptyLogic());
}

public sealed record EmptyLogic : ILogic
{
    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        return ValueTask.FromResult(Result.Success);
    }
}