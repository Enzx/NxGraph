using NxGraph.Graphs;

namespace NxGraph.Authoring;

/// <summary>
/// <b>Obsolete.</b> Use <see cref="GraphBuilder"/> static methods instead.
/// </summary>
[Obsolete("Use GraphBuilder.Start() / GraphBuilder.StartWith() instead.")]
public static class FsmDsl
{
    /// <summary>Create the first state of the graph and mark it as <c>Start</c>.</summary>
    public static StartToken Start() => GraphBuilder.Start();

    /// <summary>Create the first state of the graph with <paramref name="logic"/> as the start node.</summary>
    public static StateToken StartWith(ILogic logic) => GraphBuilder.StartWith(logic);
}