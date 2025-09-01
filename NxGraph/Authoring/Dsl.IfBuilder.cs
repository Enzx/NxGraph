using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

public static partial class Dsl
{
    /// <summary>
    /// Creates a conditional branch in the FSM graph.
    /// </summary>
    public readonly struct IfBuilder
    {
        private readonly GraphBuilder _builder;
        private readonly NodeId _truePad;
        private readonly NodeId _falsePad;

        internal IfBuilder(StateToken prev, Func<bool> predicate)
        {
            _builder = prev.Builder;
            _truePad = _builder.AddNode(new RelayState(_ => ResultHelpers.Success));
            _falsePad = _builder.AddNode(new RelayState(_ => ResultHelpers.Success));
            NodeId choiceId = _builder.AddNode(new ChoiceState(predicate, _truePad, _falsePad));
            _builder.AddTransition(prev.Id, choiceId);
        }

        internal IfBuilder(StartToken root, Func<bool> predicate)
        {
            _builder = root.Builder;
            _truePad = _builder.AddNode(new RelayState(_ => ResultHelpers.Success));
            _falsePad = _builder.AddNode(new RelayState(_ => ResultHelpers.Success));
            _builder.AddNode(new ChoiceState(predicate, _truePad, _falsePad), true);
        }


        public BranchBuilder Then(ILogic logic)
        {
            NodeId firstTrue = _builder.AddNode(logic);
            _builder.AddTransition(_truePad, firstTrue);
            return new BranchBuilder(_builder, firstTrue, _falsePad);
        }
    }

    public readonly struct BranchBuilder
    {
        private readonly NodeId _tip;
        private readonly NodeId _falsePad;

        internal BranchBuilder(GraphBuilder builder, NodeId tip, NodeId falsePad)
        {
            Builder = builder;
            _tip = tip;
            _falsePad = falsePad;
        }

        public GraphBuilder Builder { get; }

        public BranchBuilder To(ILogic logic)
        {
            NodeId next = Builder.AddNode(logic);
            Builder.AddTransition(_tip, next);
            return new BranchBuilder(Builder, next, _falsePad);
        }

        public BranchBuilder WaitFor(TimeSpan delay)
        {
            return To(Wait.For(delay));
        }

        public BranchEnd Else(ILogic logic)
        {
            NodeId firstElse = Builder.AddNode(logic);
            Builder.AddTransition(_falsePad, firstElse);
            return new BranchEnd(Builder, firstElse);
        }
    }

    public readonly struct BranchEnd
    {
        private readonly GraphBuilder _b;
        private readonly NodeId _tip;

        internal BranchEnd(GraphBuilder b, NodeId tip)
        {
            _b = b;
            _tip = tip;
        }

        // ReSharper disable once MemberCanBePrivate.Global
        internal StateToken To(ILogic logic)
        {
            NodeId next = _b.AddNode(logic);
            _b.AddTransition(_tip, next);
            return new StateToken(next, _b);
        }

        // ReSharper disable once UnusedMember.Global
        public StateToken WaitFor(TimeSpan delay)
        {
            return To(Wait.For(delay));
        }
        
        public Graph Build(bool throwOnError = false)
        {
            return _b.Build(throwOnError);
        }

        public StateMachine ToStateMachine()
        {
            return _b.Build().ToStateMachine();
        }

        public StateMachine<T> ToStateMachine<T>()
        {
            return new StateMachine<T>(_b.Build());
        }
    }
}