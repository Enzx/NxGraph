using NxGraph.Graphs;

namespace NxGraph.Authoring;

/// <summary>
/// Returned by the DSL whenever you add a state.  It lets you fluently wire
/// transitions and continue building without exposing NodeId.
/// </summary>
public readonly struct StateToken
{
    internal StateToken(NodeId id, GraphBuilder builder)
    {
        Id = id;
        Builder = builder;
    }

    public GraphBuilder Builder { get; }

    public NodeId Id { get; }

    /// <summary> Adds a transition from the current state to <paramref name="target"/>. </summary>
    public StateToken To(StateToken target)
    {
        Builder.AddTransition(Id, target.Id);
        return target;
    }

    /// <summary> Adds a brand-new state and immediately wires a transition to it. </summary>
    public StateToken To(INode nextStateLogic)
    {
        NodeId next = Builder.AddNode(nextStateLogic);
        Builder.AddTransition(Id, next);
        return new StateToken(next, Builder);
    }

    /// <summary> Finishes the DSL and produces an immutable <see cref="Graph"/>. </summary>
    public Graph Build()
    {
        return Builder.Build();
    }
}