namespace NxGraph.Graphs;

public interface INode
{
    NodeId Id { get; }
    public IAsyncLogic AsyncLogic { get; }

    /// <summary>
    /// The synchronous logic of this node, if available.
    /// Non-null when the node was created from an <see cref="ILogic"/> implementation
    /// (possibly wrapped in a <see cref="SyncLogicAdapter"/>).
    /// </summary>
    ILogic? Logic => null;
}

public class LogicNode : INode
{
    public LogicNode(NodeId id, IAsyncLogic asyncLogic)
    {
        Id = id;
        AsyncLogic = asyncLogic;
        Logic = asyncLogic is SyncLogicAdapter sla ? sla.Logic : asyncLogic as ILogic;
    }

    public NodeId Id { get; }
    public IAsyncLogic AsyncLogic { get; }

    /// <summary>
    /// Synchronous logic extracted from the <see cref="AsyncLogic"/>:
    /// unwraps <see cref="SyncLogicAdapter"/>, or detects direct <see cref="ILogic"/> implementations.
    /// </summary>
    public ILogic? Logic { get; }

    public static readonly LogicNode Empty = new(NodeId.Default, new EmptyAsyncLogic());
    public static readonly LogicNode StateMachineMarker = new(NodeId.Default, new EmptyAsyncLogic());
}

/// <summary>
/// Async-only empty logic. Used as a sentinel in <see cref="LogicNode.Empty"/>
/// and <see cref="LogicNode.StateMachineMarker"/>.
/// </summary>
public sealed class EmptyAsyncLogic : IAsyncLogic
{
    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        return ResultHelpers.Success;
    }
}

/// <summary>
/// Synchronous empty logic. Returns <see cref="Result.Success"/> immediately.
/// Used as pad/placeholder nodes that must be executable by both sync and async runtimes.
/// </summary>
public sealed class EmptyLogic : ILogic
{
    public Result Execute() => Result.Success;
}