using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>
    /// Represents a switch statement in the FSM graph, allowing for multiple branches based on a key.
    /// <para>
    /// Two modes share this builder. The <b>delegate</b> mode (<c>.Switch(selector)</c>) builds a
    /// <see cref="RelaySwitchState{TKey}"/>; the <b>data</b> mode (<c>.Switch(blackboardKey)</c>,
    /// spec 023) builds a serializable <see cref="SwitchState{T}"/> whose cases are literals.
    /// Both take the same <c>.Case(...)</c> / <c>.Default(...)</c> / <c>.End()</c> chain; the data
    /// mode additionally rejects a value cased twice, at <c>.End()</c>.
    /// </para>
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    public readonly struct SwitchBuilder<TKey> where TKey : notnull
    {
        private readonly GraphBuilder _builder;
        private readonly StateToken _prev;
        private readonly Dictionary<TKey, NodeId> _map = new();
        private readonly RelaySwitchState<TKey>? _switchNode;

        // Data mode (spec 023): the state is immutable and built at End(), so the arms and the
        // default accumulate in reference-typed cells — this builder is a readonly struct that
        // every chaining call returns by value.
        private readonly List<SwitchCase<TKey>>? _cases;
        private readonly BlackboardKey<TKey> _dataKey;
        private readonly NodeId[]? _defaultCell;
        private readonly bool _isStart;

        internal SwitchBuilder(StateToken prev, Func<TKey> selector)
        {
            _prev = prev;
            _builder = prev.Builder;
            _isStart = false;
            _switchNode = new RelaySwitchState<TKey>(selector, _map);
        }

        internal SwitchBuilder(StartToken start, Func<TKey> selector)
        {
            _prev = new StateToken(NodeId.Default, start.Builder);
            _builder = start.Builder;
            _isStart = true;
            _switchNode = new RelaySwitchState<TKey>(selector, _map);
        }

        internal SwitchBuilder(StateToken prev, Func<BlackboardContext, TKey> selector)
        {
            _prev = prev;
            _builder = prev.Builder;
            _isStart = false;
            _switchNode = new RelaySwitchState<TKey>(selector, _map);
        }

        internal SwitchBuilder(StartToken start, Func<BlackboardContext, TKey> selector)
        {
            _prev = new StateToken(NodeId.Default, start.Builder);
            _builder = start.Builder;
            _isStart = true;
            _switchNode = new RelaySwitchState<TKey>(selector, _map);
        }

        internal SwitchBuilder(StateToken prev, BlackboardKey<TKey> key)
        {
            _prev = prev;
            _builder = prev.Builder;
            _isStart = false;
            _switchNode = null;
            _dataKey = ValidatedKey(key);
            _cases = new List<SwitchCase<TKey>>();
            _defaultCell = [NodeId.Default];
        }

        internal SwitchBuilder(StartToken start, BlackboardKey<TKey> key)
        {
            _prev = new StateToken(NodeId.Default, start.Builder);
            _builder = start.Builder;
            _isStart = true;
            _switchNode = null;
            _dataKey = ValidatedKey(key);
            _cases = new List<SwitchCase<TKey>>();
            _defaultCell = [NodeId.Default];
        }

        private static BlackboardKey<TKey> ValidatedKey(BlackboardKey<TKey> key)
        {
            if (!key.IsValid)
            {
                throw new ArgumentException(
                    "Invalid blackboard key — obtain keys via BlackboardSchema.Register<T>(...).", nameof(key));
            }

            return key;
        }

        private void Record(TKey key, NodeId id)
        {
            if (_cases is not null)
            {
                _cases.Add(new SwitchCase<TKey>(key, id));
                return;
            }

            _map[key] = id;
        }

        private void RecordDefault(NodeId id)
        {
            if (_defaultCell is not null)
            {
                _defaultCell[0] = id;
                return;
            }

            _switchNode!.SetDefault(id);
        }

        /// <summary>
        /// Adds an async case to the switch statement.
        /// </summary>
        public SwitchBuilder<TKey> CaseAsync(TKey key, IAsyncLogic asyncLogic)
        {
            NodeId id = _builder.AddNode(asyncLogic);
            Record(key, id);
            return this;
        }

        /// <summary>
        /// Adds a sync case to the switch statement.
        /// </summary>
        public SwitchBuilder<TKey> Case(TKey key, ILogic syncLogic)
        {
            NodeId id = _builder.AddNode(syncLogic);
            Record(key, id);
            return this;
        }

        /// <summary>
        /// Adds an async default case to the switch statement.
        /// </summary>
        /// <param name="asyncLogic">The logic to execute if no case matches.</param>
        /// <returns>Returns the current instance of <see cref="SwitchBuilder{TKey}"/>.</returns>
        public SwitchBuilder<TKey> DefaultAsync(IAsyncLogic asyncLogic)
        {
            NodeId defaultNode = _builder.AddNode(asyncLogic);
            RecordDefault(defaultNode);
            return this;
        }

        /// <summary>
        /// Adds a sync default case to the switch statement.
        /// </summary>
        /// <param name="syncLogic">The synchronous logic to execute if no case matches.</param>
        /// <returns>Returns the current instance of <see cref="SwitchBuilder{TKey}"/>.</returns>
        public SwitchBuilder<TKey> Default(ILogic syncLogic)
        {
            NodeId defaultNode = _builder.AddNode(syncLogic);
            RecordDefault(defaultNode);
            return this;
        }

        /// <summary>
        /// Ends the switch statement and returns a <see cref="StateToken"/> representing the switch state.
        /// </summary>
        /// <returns>Returns a <see cref="StateToken"/> representing the switch state.</returns>
        public StateToken End()
        {
            // Data mode adds the state through the IAsyncLogic overload: SwitchState<T> implements
            // both logic slots, so the node exposes the same instance on Logic and AsyncLogic and
            // runs unchanged under either runtime family.
            NodeId switchId = _switchNode is null
                ? _builder.AddNode((IAsyncLogic)new SwitchState<TKey>(_dataKey, _cases!, _defaultCell![0]), _isStart)
                : _builder.AddNode((ILogic)_switchNode, _isStart);
            if (_prev.Id != NodeId.Default)
            {
                _builder.AddTransition(_prev.Id, switchId);
            }

            return new StateToken(switchId, _builder);
        }
    }
}