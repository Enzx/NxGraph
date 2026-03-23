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
            _truePad = _builder.AddNode(new EmptyAsyncLogic());
            _falsePad = _builder.AddNode(new EmptyAsyncLogic());
            NodeId choiceId = _builder.AddNode(new ChoiceState(predicate, _truePad, _falsePad));
            _builder.AddTransition(prev.Id, choiceId);
        }

        internal IfBuilder(StartToken root, Func<bool> predicate)
        {
            _builder = root.Builder;
            _truePad = _builder.AddNode(new EmptyAsyncLogic());
            _falsePad = _builder.AddNode(new EmptyAsyncLogic());
            _builder.AddNode(new ChoiceState(predicate, _truePad, _falsePad), true);
        }


        public BranchBuilder Then(IAsyncLogic asyncLogic)
        {
            NodeId firstTrue = _builder.AddNode(asyncLogic);
            _builder.AddTransition(_truePad, firstTrue);
            return new BranchBuilder(_builder, firstTrue, _falsePad);
        }
    }

    public readonly struct BranchBuilder
    {
        private readonly NodeId _falsePad;

        internal BranchBuilder(GraphBuilder builder, NodeId tip, NodeId falsePad)
        {
            Builder = builder;
            Tip = tip;
            _falsePad = falsePad;
        }

        public GraphBuilder Builder { get; }

        /// <summary>The last node added on the "then" branch.</summary>
        public NodeId Tip { get; }

        public BranchBuilder To(IAsyncLogic asyncLogic)
        {
            NodeId next = Builder.AddNode(asyncLogic);
            Builder.AddTransition(Tip, next);
            return new BranchBuilder(Builder, next, _falsePad);
        }

        public BranchBuilder WaitFor(TimeSpan delay)
        {
            return To(Wait.For(delay));
        }

        public BranchEnd Else(IAsyncLogic asyncLogic)
        {
            NodeId firstElse = Builder.AddNode(asyncLogic);
            Builder.AddTransition(_falsePad, firstElse);
            return new BranchEnd(Builder, firstElse);
        }
    }

    public readonly struct BranchEnd
    {
        internal BranchEnd(GraphBuilder b, NodeId tip)
        {
            Builder = b;
            Tip = tip;
        }

        public GraphBuilder Builder { get; }

        /// <summary>The last node added on the "else" branch.</summary>
        public NodeId Tip { get; }

        /// <summary>Adds a new state and wires a transition from the "else" tip.</summary>
        public StateToken To(IAsyncLogic asyncLogic)
        {
            NodeId next = Builder.AddNode(asyncLogic);
            Builder.AddTransition(Tip, next);
            return new StateToken(next, Builder);
        }

        /// <summary>Adds a wait state after the "else" branch.</summary>
        public StateToken WaitFor(TimeSpan delay)
        {
            return To(Wait.For(delay));
        }

        public Graph Build(bool throwOnError = false)
        {
            return Builder.Build(throwOnError);
        }

        public AsyncStateMachine ToAsyncStateMachine(IAsyncStateMachineObserver? observer = null)
        {
            return Builder.Build().ToAsyncStateMachine(observer);
        }

        public AsyncStateMachine<T> ToAsyncStateMachine<T>(IAsyncStateMachineObserver? observer = null)
        {
            return new AsyncStateMachine<T>(Builder.Build(), observer);
        }

        public StateMachine ToStateMachine(IStateMachineObserver? observer = null)
        {
            return Builder.Build().ToStateMachine(observer);
        }

        public StateMachine<T> ToStateMachine<T>(IStateMachineObserver? observer = null)
        {
            return Builder.Build().ToStateMachine<T>(observer);
        }
    }
}