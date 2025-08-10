using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>
    /// Represents a switch statement in the FSM graph, allowing for multiple branches based on a key.
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    public readonly struct SwitchBuilder<TKey> where TKey : notnull
    {
        private readonly GraphBuilder _builder;
        private readonly StateToken _prev;
        private readonly Dictionary<TKey, NodeId> _map = new();
        private readonly SwitchState<TKey> _switchNode;
        private readonly bool _isStart;

        internal SwitchBuilder(StateToken prev, Func<TKey> selector)
        {
            _prev = prev;
            _builder = prev.Builder;
            _isStart = false;
            _switchNode = new SwitchState<TKey>(selector, _map);
        }

        internal SwitchBuilder(StartToken start, Func<TKey> selector)
        {
            _prev = new StateToken(NodeId.Default, start.Builder);
            _builder = start.Builder;
            _isStart = true;
            _switchNode = new SwitchState<TKey>(selector, _map);
        }

        /// <summary>
        /// Adds a case to the switch statement.
        /// </summary>
        public SwitchBuilder<TKey> Case(TKey key, INode logic)
        {
            NodeId id = _builder.AddNode(logic);
            _map[key] = id;
            return this;
        }

        /// <summary>
        /// Adds a default case to the switch statement.
        /// </summary>
        /// <param name="logic">The logic to execute if no case matches.</param>
        /// <returns>Returns the current instance of <see cref="SwitchBuilder{TKey}"/>.</returns>
        public SwitchBuilder<TKey> Default(INode logic)
        {
            NodeId defaultNode = _builder.AddNode(logic);
            _switchNode.SetDefault(defaultNode);
            return this;
        }

        /// <summary>
        /// Ends the switch statement and returns a <see cref="StateToken"/> representing the switch state.
        /// </summary>
        /// <returns>Returns a <see cref="StateToken"/> representing the switch state.</returns>
        public StateToken End()
        {
            NodeId switchId = _builder.AddNode(_switchNode, _isStart);
            if (_prev.Id != NodeId.Default)
            {
                _builder.AddTransition(_prev.Id, switchId);
            }

            return new StateToken(switchId, _builder);
        }
    }
}