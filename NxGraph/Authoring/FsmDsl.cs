using NxGraph.Graphs;

namespace NxGraph.Authoring;

public static class FsmDsl
{
    /// <summary>Create the first state of the graph and mark it as <c>Start</c>.</summary>
    public static StartToken Start() => new(new GraphBuilder());

    /// <summary>
    /// Create the first state of the graph and mark it as <c>Start</c>.
    /// </summary>
    /// <param name="logic">The logic to be executed in the start state.</param>
    /// <returns>A <see cref="StateToken"/> representing the start state of the FSM graph.</returns>
    public static StateToken StartWith(INode logic)
    {
        GraphBuilder builder = new();
        NodeId id = builder.AddNode(logic, isStart: true);
        return new StateToken(id, builder);
    }
}