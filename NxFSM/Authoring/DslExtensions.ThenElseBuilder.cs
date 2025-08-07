using NxFSM.Graphs;

namespace NxFSM.Authoring;

public static partial class DslExtensions
{
    /// <summary>
    /// Represents the "else" branch of a conditional statement in the FSM graph.
    /// </summary>
    public readonly struct ThenElseBuilder
    {
        private readonly NodeId _falsePad;

        private readonly GraphBuilder _builder;

        internal ThenElseBuilder(GraphBuilder builder, NodeId falsePad)
        {
            _builder = builder;
            _falsePad = falsePad;
        }

        /// <summary>
        /// Creates the "else" branch of the conditional statement.
        /// </summary>
        /// <param name="logic">The logic to execute if the condition is false.</param>
        /// <returns>Returns a <see cref="TerminalBuilder"/> to finalize the FSM graph.</returns>
        public TerminalBuilder Else(INode logic)
        {
            NodeId id = _builder.AddNode(logic);
            _builder.AddTransition(_falsePad, id);
            return new TerminalBuilder(_builder);
        }
    }
}