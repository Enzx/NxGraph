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
    public LogicNode(NodeId id, IAsyncLogic asyncLogic, Action? enterAction = null, Action? exitAction = null)
    {
        Id = id;
        AsyncLogic = asyncLogic;
        Logic = asyncLogic is SyncLogicAdapter sla ? sla.Logic : asyncLogic as ILogic;
        EnterAction = enterAction;
        ExitAction = exitAction;
    }

    public NodeId Id { get; }
    public IAsyncLogic AsyncLogic { get; }

    /// <summary>
    /// Synchronous logic extracted from the <see cref="AsyncLogic"/>:
    /// unwraps <see cref="SyncLogicAdapter"/>, or detects direct <see cref="ILogic"/> implementations.
    /// </summary>
    public ILogic? Logic { get; }

    /// <summary>
    /// Optional action invoked by the run loops when the machine enters this node —
    /// once per visit, before the node's first execution (retries do not re-fire it).
    /// Cached at build time; a null check plus a cached-delegate invoke costs nothing.
    /// </summary>
    public Action? EnterAction { get; }

    /// <summary>
    /// Optional action invoked by the run loops when the machine leaves this node —
    /// once per visit, after its final execution, regardless of outcome.
    /// </summary>
    public Action? ExitAction { get; }

    public static readonly LogicNode Empty = new(NodeId.Default, new EmptyAsyncLogic());

    /// <summary>
    /// Sentinel for nested-state-machine owner nodes during (de)serialization. Uses the
    /// dedicated <see cref="NodeId.StateMachineMarker"/> id so the wire marker string is
    /// "StateMachine" rather than colliding with <see cref="NodeId.Default"/>'s "Default".
    /// </summary>
    public static readonly LogicNode StateMachineMarker = new(NodeId.StateMachineMarker, new EmptyAsyncLogic());

    /// <summary>
    /// Sentinel for nested <b>sync</b> state-machine owner nodes during (de)serialization —
    /// the composite-kind discriminator that lets a payload round-trip a sync-nested graph
    /// back into a sync-runnable one (wire marker string "SyncStateMachine").
    /// </summary>
    public static readonly LogicNode SyncStateMachineMarker = new(NodeId.SyncStateMachineMarker, new EmptyAsyncLogic());

    /// <summary>
    /// Sentinel for async history-composite owner nodes during (de)serialization
    /// (wire marker string "HistoryState", payload version 4).
    /// </summary>
    public static readonly LogicNode HistoryStateMarker = new(NodeId.HistoryStateMarker, new EmptyAsyncLogic());

    /// <summary>
    /// Sentinel for <b>sync</b> history-composite owner nodes during (de)serialization
    /// (wire marker string "SyncHistoryState", payload version 4).
    /// </summary>
    public static readonly LogicNode SyncHistoryStateMarker = new(NodeId.SyncHistoryStateMarker, new EmptyAsyncLogic());

    /// <summary>
    /// Sentinel for async parallel-composite owner nodes during (de)serialization
    /// (wire marker string "ParallelState", payload version 4).
    /// </summary>
    public static readonly LogicNode ParallelStateMarker = new(NodeId.ParallelStateMarker, new EmptyAsyncLogic());

    /// <summary>
    /// Sentinel for <b>sync</b> parallel-composite owner nodes during (de)serialization
    /// (wire marker string "SyncParallelState", payload version 4).
    /// </summary>
    public static readonly LogicNode SyncParallelStateMarker = new(NodeId.SyncParallelStateMarker, new EmptyAsyncLogic());

    /// <summary>
    /// Sentinel for token fork owner nodes during (de)serialization (wire marker string
    /// "ForkState", payload version 6). One marker for both runtimes: fork nodes never
    /// execute and populate both logic slots, so no sync twin is needed.
    /// </summary>
    public static readonly LogicNode ForkStateMarker = new(NodeId.ForkStateMarker, new EmptyAsyncLogic());

    /// <summary>
    /// Sentinel for token join owner nodes during (de)serialization (wire marker string
    /// "JoinState", payload version 6). One marker for both runtimes — see
    /// <see cref="ForkStateMarker"/>.
    /// </summary>
    public static readonly LogicNode JoinStateMarker = new(NodeId.JoinStateMarker, new EmptyAsyncLogic());

    /// <summary>
    /// Sentinel for async dynamic-parallel composite owner nodes during (de)serialization
    /// (wire marker string "DynamicParallelState", payload version 6).
    /// </summary>
    public static readonly LogicNode DynamicParallelStateMarker =
        new(NodeId.DynamicParallelStateMarker, new EmptyAsyncLogic());

    /// <summary>
    /// Sentinel for <b>sync</b> dynamic-parallel composite owner nodes during
    /// (de)serialization (wire marker string "SyncDynamicParallelState", payload version 6).
    /// </summary>
    public static readonly LogicNode SyncDynamicParallelStateMarker =
        new(NodeId.SyncDynamicParallelStateMarker, new EmptyAsyncLogic());
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