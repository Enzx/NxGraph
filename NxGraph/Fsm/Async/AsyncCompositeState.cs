using NxGraph.Blackboards;
using NxGraph.Graphs;

namespace NxGraph.Fsm.Async;

/// <summary>
/// A composite state that contains a single child node. Surfaces the child's sub-graphs via
/// <see cref="ISubGraphProvider"/> and forwards the blackboard context, so agent and
/// blackboard stamping reach wrapped machines instead of stopping at this wrapper.
/// </summary>
/// <param name="child">The child node to execute.</param>
public class AsyncCompositeState(IAsyncLogic child) : AsyncState, ISubGraphProvider, IBlackboardSettable
{
    IEnumerable<Graph> ISubGraphProvider.SubGraphs =>
        child is ISubGraphProvider provider ? provider.SubGraphs : [];

    /// <summary>
    /// The wrapped child logic. Exposed to subclasses so a user container codec can read
    /// back what the primary constructor captured (e.g. to serialize a wrapped machine's
    /// graph or a logic-codec key for a non-graph child).
    /// </summary>
    protected IAsyncLogic Child => child;

    void IBlackboardSettable.SetBlackboards(in BlackboardContext context)
    {
        if (child is IBlackboardSettable settable)
        {
            settable.SetBlackboards(in context);
        }
    }

    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        return child.ExecuteAsync(ct);
    }
}
