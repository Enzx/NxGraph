namespace NxGraph.Graphs;

/// <summary>
/// The wiring seam for decorator nodes: a node logic that wraps another logic instance
/// (e.g. the timeout wrappers) exposes the wrapped instance so machine wiring that cannot be
/// forwarded interface-to-interface reaches through the decorator — agent stamping (generic
/// in the agent type, so a non-generic wrapper cannot implement it) and the log-report
/// resolution the machines perform at construction (the sync slot lives on the
/// <c>State</c> base class, not on an interface). Blackboard stamping is forwarded by the
/// wrapper itself via <see cref="NxGraph.Blackboards.IBlackboardSettable"/> — a decorator
/// holds no wiring state of its own, it only passes the machine's wiring through.
/// </summary>
public interface ILogicWrapper
{
    /// <summary>The logic instance this decorator wraps.</summary>
    object WrappedLogic { get; }
}

/// <summary>
/// Resolution through decorator layers, shared by the wiring walks and the machines'
/// construction-time report tables. Cold path by contract (construction / run-start walks);
/// the per-layer step is two type tests, no allocation.
/// </summary>
internal static class LogicWrappers
{
    /// <summary>
    /// Finds the first <typeparamref name="T"/> reachable from the node's logic slots by
    /// stepping through decorator (<see cref="ILogicWrapper"/>) and
    /// <see cref="SyncLogicAdapter"/> layers. The node's own top-level logic is deliberately
    /// not probed — callers keep their existing direct probes; this helper only reaches
    /// <i>through</i> wrappers. Wrapper chains are acyclic by construction (the wrapped
    /// instance is a constructor argument), so the walk terminates.
    /// </summary>
    internal static T? ResolveThroughWrappers<T>(LogicNode node) where T : class
    {
        return FromChain(node.AsyncLogic) ?? (node.Logic is { } logic ? FromChain(logic) : null);

        static T? FromChain(object outer)
        {
            object? current = (outer as ILogicWrapper)?.WrappedLogic;
            while (current is not null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = current switch
                {
                    ILogicWrapper wrapper => wrapper.WrappedLogic,
                    SyncLogicAdapter adapter => adapter.Logic,
                    _ => null,
                };
            }

            return null;
        }
    }
}
