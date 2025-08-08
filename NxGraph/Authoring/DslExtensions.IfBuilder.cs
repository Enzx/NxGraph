using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

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
            _builder.AddNode(new ChoiceState(predicate, _truePad, _falsePad), isStart: true);
        }


        public BranchBuilder Then(INode logic)
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

        public BranchBuilder To(INode logic)
        {
            NodeId next = Builder.AddNode(logic);
            Builder.AddTransition(_tip, next);
            return new BranchBuilder(Builder, next, _falsePad);
        }

        public BranchBuilder WaitFor(TimeSpan delay)
            => To(Wait.For(delay));
        
        public BranchEnd Else(INode logic)
        {
            var firstElse = Builder.AddNode(logic);
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

        public BranchEnd To(INode logic)
        {
            NodeId next = _b.AddNode(logic);
            _b.AddTransition(_tip, next);
            return new BranchEnd(_b, next);
        }

        public BranchEnd WaitFor(TimeSpan delay) => To(Wait.For(delay));

        public StateMachine ToStateMachine() => _b.Build().ToStateMachine();
        public StateMachine<T> ToStateMachine<T>() => new(_b.Build());
    }
}