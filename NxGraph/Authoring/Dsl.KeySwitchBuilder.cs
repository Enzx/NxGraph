using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>
    /// Builds a <b>data-built</b> switch (spec 023): the tested value is a blackboard key and the
    /// arms are literals, so the node is a serializable <see cref="SwitchState{T}"/> rather than
    /// the delegate-backed <see cref="RelaySwitchState{TKey}"/> that
    /// <see cref="SwitchBuilder{TKey}"/> produces.
    /// <para>
    /// The two builders are separate types because their lifecycles differ — the delegate builder
    /// mutates one mutable state as arms arrive, while this one accumulates arms and constructs an
    /// immutable state at <see cref="End"/>. Their authoring surfaces are identical, so swapping a
    /// selector for a key changes the <c>.Switch(...)</c> call and nothing else.
    /// </para>
    /// <para>
    /// A value cased twice is rejected at <see cref="End"/> by the state's constructor, naming the
    /// offending value: a switch is a lookup, so at most one case may match.
    /// </para>
    /// </summary>
    /// <typeparam name="TKey">The tested key's value type.</typeparam>
    public readonly struct KeySwitchBuilder<TKey> where TKey : notnull
    {
        private readonly GraphBuilder _builder;
        private readonly StateToken _prev;
        private readonly BlackboardKey<TKey> _key;

        // The state is immutable and only exists at End(), so the arms and the default accumulate
        // in reference-typed cells — this is a readonly struct that every chaining call returns by
        // value, and a value-typed cell would drop the writes made through the returned copy.
        private readonly List<SwitchCase<TKey>> _cases;
        private readonly NodeId[] _defaultCell;
        private readonly bool _isStart;

        internal KeySwitchBuilder(StateToken prev, BlackboardKey<TKey> key)
        {
            _prev = prev;
            _builder = prev.Builder;
            _isStart = false;
            _key = ValidatedKey(key);
            _cases = new List<SwitchCase<TKey>>();
            _defaultCell = [NodeId.Default];
        }

        internal KeySwitchBuilder(StartToken start, BlackboardKey<TKey> key)
        {
            _prev = new StateToken(NodeId.Default, start.Builder);
            _builder = start.Builder;
            _isStart = true;
            _key = ValidatedKey(key);
            _cases = new List<SwitchCase<TKey>>();
            _defaultCell = [NodeId.Default];
        }

        private static BlackboardKey<TKey> ValidatedKey(BlackboardKey<TKey> key)
        {
            // Rejected here rather than at End() so the stack trace points at the .Switch(...)
            // call that supplied the bad key.
            if (!key.IsValid)
            {
                throw new ArgumentException(
                    "Invalid blackboard key — obtain keys via BlackboardSchema.Register<T>(...).", nameof(key));
            }

            return key;
        }

        /// <summary>
        /// Adds an async case to the switch statement.
        /// </summary>
        public KeySwitchBuilder<TKey> CaseAsync(TKey key, IAsyncLogic asyncLogic)
        {
            NodeId id = _builder.AddNode(asyncLogic);
            _cases.Add(new SwitchCase<TKey>(key, id));
            return this;
        }

        /// <summary>
        /// Adds a sync case to the switch statement.
        /// </summary>
        public KeySwitchBuilder<TKey> Case(TKey key, ILogic syncLogic)
        {
            NodeId id = _builder.AddNode(syncLogic);
            _cases.Add(new SwitchCase<TKey>(key, id));
            return this;
        }

        /// <summary>
        /// Adds an async default case to the switch statement.
        /// </summary>
        /// <param name="asyncLogic">The logic to execute if no case matches.</param>
        /// <returns>Returns the current instance of <see cref="KeySwitchBuilder{TKey}"/>.</returns>
        public KeySwitchBuilder<TKey> DefaultAsync(IAsyncLogic asyncLogic)
        {
            _defaultCell[0] = _builder.AddNode(asyncLogic);
            return this;
        }

        /// <summary>
        /// Adds a sync default case to the switch statement.
        /// </summary>
        /// <param name="syncLogic">The synchronous logic to execute if no case matches.</param>
        /// <returns>Returns the current instance of <see cref="KeySwitchBuilder{TKey}"/>.</returns>
        public KeySwitchBuilder<TKey> Default(ILogic syncLogic)
        {
            _defaultCell[0] = _builder.AddNode(syncLogic);
            return this;
        }

        /// <summary>
        /// Ends the switch statement and returns a <see cref="StateToken"/> representing the switch state.
        /// </summary>
        /// <returns>Returns a <see cref="StateToken"/> representing the switch state.</returns>
        public StateToken End()
        {
            // Added through the IAsyncLogic overload: SwitchState<T> implements both logic slots,
            // so the node exposes the same instance on Logic and AsyncLogic and runs unchanged
            // under either runtime family.
            NodeId switchId = _builder.AddNode(
                (IAsyncLogic)new SwitchState<TKey>(_key, _cases, _defaultCell[0]), _isStart);
            if (_prev.Id != NodeId.Default)
            {
                _builder.AddTransition(_prev.Id, switchId);
            }

            return new StateToken(switchId, _builder);
        }
    }
}
