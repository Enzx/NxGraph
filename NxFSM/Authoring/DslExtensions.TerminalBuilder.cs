using NxFSM.Graphs;

namespace NxFSM.Authoring;

public static partial class DslExtensions
{
    /// <summary>
    /// Represents the final stage of building an FSM graph after a conditional statement.
    /// </summary>
    public readonly struct TerminalBuilder
    {
        internal TerminalBuilder(GraphBuilder builder) => Builder = builder;
        public GraphBuilder Builder { get; }

        /// <summary>
        /// Finalizes the FSM graph and returns the built graph.
        /// </summary>
        /// <returns>Returns the constructed <see cref="Graph"/>.</returns>
        public Graph Build() => Builder.Build();
    }
}