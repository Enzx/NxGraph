using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>
    ///  Creates an async state that waits for a specified duration before transitioning to the next state.
    /// </summary>
    /// <param name="token">The previous state token, which is the source of the transition.</param>
    /// <param name="delay">The duration to wait before transitioning to the next state.</param>
    /// <returns>A new state token that represents the waiting state.</returns>
    public static StateToken WaitForAsync(this StateToken token, TimeSpan delay)
    {
        return token.ToAsync(AsyncWait.For(delay));
    }

    /// <summary>
    /// Creates an async state that waits for a specified duration before transitioning to the next state.
    /// </summary>
    /// <param name="token">The start token, which is the entry point of the FSM graph.</param>
    /// <param name="delay">The duration to wait before transitioning to the next state.</param>
    /// <returns>A new state token that represents the waiting state.</returns>
    public static StateToken WaitForAsync(this StartToken token, TimeSpan delay)
    {
        AsyncState node = AsyncWait.For(delay);
        NodeId id = token.Builder.AddNode(node, true);
        return new StateToken(id, token.Builder);
    }

    /// <summary>
    /// Creates a <b>sync</b> multi-tick wait state and wires a transition to it — the
    /// runtime-parity twin of <see cref="WaitForAsync(StateToken, TimeSpan)"/> (see
    /// <see cref="WaitState"/>). Returns <see cref="Result.InProgress"/> across ticks, so it
    /// runs under the sync <see cref="StateMachine"/> only.
    /// </summary>
    public static StateToken WaitFor(this StateToken token, TimeSpan delay)
    {
        return token.To(new WaitState(delay));
    }

    /// <summary>
    /// Starts the graph with a sync multi-tick wait state (see <see cref="WaitState"/>).
    /// </summary>
    public static StateToken WaitFor(this StartToken token, TimeSpan delay)
    {
        NodeId id = token.Builder.AddNode(new WaitState(delay), true);
        return new StateToken(id, token.Builder);
    }
}