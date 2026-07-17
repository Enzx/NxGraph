using NxGraph.Blackboards;
using NxGraph.Graphs;

namespace NxGraph.Fsm.Async;

/// <summary>
/// A composite state that contains a single child node. Surfaces the child's sub-graphs via
/// <see cref="ISubGraphProvider"/> and forwards the blackboard context, so agent and
/// blackboard stamping reach wrapped machines instead of stopping at this wrapper.
/// <para>
/// Deep suspend cannot capture an arbitrary <see cref="IAsyncLogic"/> child generically, so
/// this base contributes nothing to a <see cref="StateMachineDeepSnapshot"/> and re-enters
/// fresh after a deep resume. Subclasses (and custom containers) that hold durable position
/// opt in by implementing <see cref="ISuspendableComposite"/> — the third leg of the
/// container contract beside <see cref="ISubGraphProvider"/> and
/// <see cref="IBlackboardSettable"/> forwarding.
/// </para>
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
