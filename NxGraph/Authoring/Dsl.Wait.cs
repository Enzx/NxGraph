using System.Diagnostics.CodeAnalysis;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static partial class Dsl
{
    /// <summary>
    ///  Creates a state that waits for a specified duration before transitioning to the next state.
    /// </summary>
    /// <param name="token">The previous state token, which is the source of the transition.</param>
    /// <param name="delay">The duration to wait before transitioning to the next state.</param>
    /// <returns>A new state token that represents the waiting state.</returns>
    public static StateToken WaitFor(this StateToken token, TimeSpan delay)
    {
        return token.To(Wait.For(delay));
    }

    /// <summary>
    /// Creates a state that waits for a specified duration before transitioning to the next state.
    /// </summary>
    /// <param name="token">The start token, which is the entry point of the FSM graph.</param>
    /// <param name="delay">The duration to wait before transitioning to the next state.</param>
    /// <returns>A new state token that represents the waiting state.</returns>
    public static StateToken WaitFor(this StartToken token, TimeSpan delay)
    {
        State node = Wait.For(delay);
        NodeId id = token.Builder.AddNode(node, isStart: true);
        return new StateToken(id, token.Builder);
    }
}