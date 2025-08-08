namespace NxGraph.Authoring;

/// <summary>
/// Represents the start of a graph.
/// </summary>
public readonly struct StartToken
{
    internal readonly GraphBuilder Builder;
    internal StartToken(GraphBuilder b) => Builder = b;
    
    /// <summary>
    /// Creates the first state of the graph and marks it as <c>Start</c>.
    /// </summary>
    /// <param name="predicate">The function that determines whether the state should be executed.</param>
    /// <returns>A builder that allows for further configuration of the state.</returns>
    public DslExtensions.IfBuilder If(Func<bool> predicate) => new(this, predicate);

     /// <summary>
     /// Creates a switch statement that allows branching based on a key selector.
     /// </summary>
     /// <param name="selector">The function that selects the key for branching.</param>
     /// <typeparam name="TKey">The type of the key used for branching.</typeparam>
     /// <returns>A SwitchBuilder instance for defining branches based on the selected key.</returns>
    public DslExtensions.SwitchBuilder<TKey> Switch<TKey>(Func<TKey> selector)
        where TKey : notnull
    {
        return new DslExtensions.SwitchBuilder<TKey>(this, selector);
    }
}