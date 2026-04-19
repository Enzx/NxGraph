using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

/// <summary>
/// Represents the start of a graph before any start-node logic has been added.
/// Chain with <c>.To()</c>, <c>.If()</c>, <c>.Switch()</c>, or <c>.WaitFor()</c>.
/// </summary>
public readonly struct StartToken
{
    internal readonly GraphBuilder Builder;

    internal StartToken(GraphBuilder b)
    {
        Builder = b;
    }

    /// <summary>
    /// Adds the first (start) node with the given async logic and returns a <see cref="StateToken"/>.
    /// </summary>
    /// <param name="asyncLogic">The async logic to run as the start node.</param>
    /// <returns>A <see cref="StateToken"/> pointing at the newly-created start node.</returns>
    public StateToken ToAsync(IAsyncLogic asyncLogic)
    {
        NodeId id = Builder.AddNode(asyncLogic, true);
        return new StateToken(id, Builder);
    }

    /// <summary>
    /// Adds the first (start) node with the given sync logic and returns a <see cref="StateToken"/>.
    /// </summary>
    /// <param name="syncLogic">The synchronous logic to run as the start node.</param>
    /// <returns>A <see cref="StateToken"/> pointing at the newly-created start node.</returns>
    public StateToken To(ILogic syncLogic)
    {
        NodeId id = Builder.AddNode(syncLogic, true);
        return new StateToken(id, Builder);
    }

    /// <summary>
    /// Adds the first (start) node that executes <paramref name="run"/> asynchronously and returns a <see cref="StateToken"/>.
    /// </summary>
    /// <param name="run">The async function to execute in the start node.</param>
    /// <returns>A <see cref="StateToken"/> pointing at the newly-created start node.</returns>
    public StateToken ToAsync(Func<CancellationToken, ValueTask<Result>> run)
    {
        return ToAsync(new AsyncRelayState(run));
    }

    /// <summary>
    /// Adds the first (start) node that executes <paramref name="run"/> synchronously and returns a <see cref="StateToken"/>.
    /// </summary>
    /// <param name="run">The sync function to execute in the start node.</param>
    /// <returns>A <see cref="StateToken"/> pointing at the newly-created start node.</returns>
    public StateToken To(Func<Result> run)
    {
        return To(new RelayState(run));
    }

    /// <summary>
    /// Creates a conditional branch in the FSM graph.
    /// </summary>
    /// <param name="predicate">The function that determines whether the state should be executed.</param>
    /// <returns>A builder that allows for further configuration of the state.</returns>
    public Dsl.IfBuilder If(Func<bool> predicate)
    {
        return new Dsl.IfBuilder(this, predicate);
    }

    /// <summary>
    /// Creates a switch statement that allows branching based on a key selector.
    /// </summary>
    /// <param name="selector">The function that selects the key for branching.</param>
    /// <typeparam name="TKey">The type of the key used for branching.</typeparam>
    /// <returns>A SwitchBuilder instance for defining branches based on the selected key.</returns>
    public Dsl.SwitchBuilder<TKey> Switch<TKey>(Func<TKey> selector)
        where TKey : notnull
    {
        return new Dsl.SwitchBuilder<TKey>(this, selector);
    }

    /// <summary>
    /// Creates an async switch statement that allows branching based on an async key selector.
    /// </summary>
    /// <param name="selector">The async function that selects the key for branching.</param>
    /// <typeparam name="TKey">The type of the key used for branching.</typeparam>
    /// <returns>An AsyncSwitchBuilder instance for defining branches based on the selected key.</returns>
    public Dsl.AsyncSwitchBuilder<TKey> SwitchAsync<TKey>(Func<ValueTask<TKey>> selector)
        where TKey : notnull
    {
        return new Dsl.AsyncSwitchBuilder<TKey>(this, selector);
    }

    /// <summary>
    /// Builds the graph. Only valid if a start node has already been added
    /// (e.g. via <see cref="ToAsync(IAsyncLogic)"/>, <see cref="Dsl.WaitForAsync(StartToken, TimeSpan)"/>, etc.).
    /// </summary>
    public Graph Build()
    {
        return Builder.Build();
    }
}