using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>
    /// Represents an async switch statement in the FSM graph, allowing for multiple branches based on an async key selector.
    /// </summary>
    /// <typeparam name="TKey">The type of the key used to select the branch.</typeparam>
    public readonly struct AsyncSwitchBuilder<TKey> where TKey : notnull
    {
        private readonly GraphBuilder _builder;
        private readonly StateToken _prev;
        private readonly Dictionary<TKey, NodeId> _map = new();
        private readonly AsyncSwitchState<TKey> _switchNode;
        private readonly bool _isStart;

        internal AsyncSwitchBuilder(StateToken prev, Func<ValueTask<TKey>> selector)
        {
            _prev = prev;
            _builder = prev.Builder;
            _isStart = false;
            _switchNode = new AsyncSwitchState<TKey>(selector, _map);
        }

        internal AsyncSwitchBuilder(StartToken start, Func<ValueTask<TKey>> selector)
        {
            _prev = new StateToken(NodeId.Default, start.Builder);
            _builder = start.Builder;
            _isStart = true;
            _switchNode = new AsyncSwitchState<TKey>(selector, _map);
        }

        /// <summary>
        /// Adds an async case to the switch statement.
        /// </summary>
        public AsyncSwitchBuilder<TKey> CaseAsync(TKey key, IAsyncLogic asyncLogic)
        {
            NodeId id = _builder.AddNode(asyncLogic);
            _map[key] = id;
            return this;
        }

        /// <summary>
        /// Adds a sync case to the switch statement.
        /// </summary>
        public AsyncSwitchBuilder<TKey> Case(TKey key, ILogic syncLogic)
        {
            NodeId id = _builder.AddNode(syncLogic);
            _map[key] = id;
            return this;
        }

        /// <summary>
        /// Adds an async default case to the switch statement.
        /// </summary>
        public AsyncSwitchBuilder<TKey> DefaultAsync(IAsyncLogic asyncLogic)
        {
            NodeId defaultNode = _builder.AddNode(asyncLogic);
            _switchNode.SetDefault(defaultNode);
            return this;
        }

        /// <summary>
        /// Adds a sync default case to the switch statement.
        /// </summary>
        public AsyncSwitchBuilder<TKey> Default(ILogic syncLogic)
        {
            NodeId defaultNode = _builder.AddNode(syncLogic);
            _switchNode.SetDefault(defaultNode);
            return this;
        }

        /// <summary>
        /// Ends the switch statement and returns a <see cref="StateToken"/> representing the switch state.
        /// </summary>
        public StateToken End()
        {
            NodeId switchId = _builder.AddNode((IAsyncLogic)_switchNode, _isStart);
            if (_prev.Id != NodeId.Default)
            {
                _builder.AddTransition(_prev.Id, switchId);
            }

            return new StateToken(switchId, _builder);
        }
    }
}

