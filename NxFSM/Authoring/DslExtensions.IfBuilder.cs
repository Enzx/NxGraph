using NxFSM.Fsm;
using NxFSM.Graphs;

namespace NxFSM.Authoring;

public static partial class DslExtensions
{
    /// <summary>
    /// Creates a conditional branch in the FSM graph.
    /// </summary>
    public readonly struct IfBuilder
    {
        private readonly GraphBuilder _builder;
        private readonly NodeId _truePad;
        private readonly NodeId _falsePad;

        /// <summary>
        /// Initializes a new instance of the <see cref="prev"/> struct.
        /// </summary>
        /// <param name="prev">The previous state token, which is the source of the transition.</param>
        /// <param name="predicate">A function that returns
        /// <c>true</c> for the "then" branch and
        /// <c>false</c> for the "else" branch.</param>
        internal IfBuilder(StateToken prev, Func<bool> predicate)
        {
            _builder = prev.Builder;
            _truePad = _builder.AddNode(new RelayState(_ => ResultHelpers.Success));
            _falsePad = _builder.AddNode(new RelayState(_ => ResultHelpers.Success));
            ChoiceState choice = new(predicate, _truePad, _falsePad);
            _builder.AddNode(choice, isStart: true);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="root"/> struct.
        /// </summary>
        /// <param name="root">The root token of the FSM graph, which is the source of the transition.</param>
        /// <param name="test">A function that returns
        /// <c>true</c> for the "then" branch and
        /// <c>false</c> for the "else" branch.</param>
        internal IfBuilder(StartToken root, Func<bool> test)
        {
            _builder = root.Builder;
            _truePad = _builder.AddNode(new RelayState(_ => ResultHelpers.Success));
            _falsePad = _builder.AddNode(new RelayState(_ => ResultHelpers.Success));
            ChoiceState choice = new(test, _truePad, _falsePad);
            _builder.AddNode(choice, isStart: true);
        }

        /// <summary>
        /// Creates the "then" branch of the conditional statement.
        /// </summary>
        /// <param name="logic">The logic to execute if the condition is true.</param>
        /// <returns>Returns a <see cref="ThenElseBuilder"/> to continue building the "else" branch.</returns>
        public ThenElseBuilder Then(INode logic)
        {
            NodeId id = _builder.AddNode(logic);
            _builder.AddTransition(_truePad, id);
            return new ThenElseBuilder(_builder, _falsePad);
        }
    }
    
}