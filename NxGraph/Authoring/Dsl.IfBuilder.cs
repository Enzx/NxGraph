using NxGraph.Fsm;
using NxGraph.Fsm.Async;
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
            _truePad = _builder.AddNode(new EmptyLogic());
            _falsePad = _builder.AddNode(new EmptyLogic());
            NodeId choiceId = _builder.AddNode(new ChoiceState(predicate, _truePad, _falsePad));
            _builder.AddTransition(prev.Id, choiceId);
        }

        internal IfBuilder(StartToken root, Func<bool> predicate)
        {
            _builder = root.Builder;
            _truePad = _builder.AddNode(new EmptyLogic());
            _falsePad = _builder.AddNode(new EmptyLogic());
            _builder.AddNode(new AsyncChoiceState(() => new ValueTask<bool>(predicate()), _truePad, _falsePad), true);
        }

        /// <summary>Adds an async "then" branch.</summary>
        public BranchBuilder ThenAsync(IAsyncLogic asyncLogic)
        {
            NodeId firstTrue = _builder.AddNode(asyncLogic);
            _builder.AddTransition(_truePad, firstTrue);
            return new BranchBuilder(_builder, firstTrue, _falsePad);
        }

        /// <summary>Adds a sync "then" branch.</summary>
        public BranchBuilder Then(ILogic syncLogic)
        {
            NodeId firstTrue = _builder.AddNode(syncLogic);
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

        /// <summary>Chains a new async state onto the "then" branch.</summary>
        public BranchBuilder ToAsync(IAsyncLogic asyncLogic)
        {
            NodeId next = Builder.AddNode(asyncLogic);
            Builder.AddTransition(Tip, next);
            return new BranchBuilder(Builder, next, _falsePad);
        }

        /// <summary>Chains a new sync state onto the "then" branch.</summary>
        public BranchBuilder To(ILogic syncLogic)
        {
            NodeId next = Builder.AddNode(syncLogic);
            Builder.AddTransition(Tip, next);
            return new BranchBuilder(Builder, next, _falsePad);
        }

        /// <summary>Chains a wait state onto the "then" branch (async).</summary>
        public BranchBuilder WaitForAsync(TimeSpan delay)
        {
            return ToAsync(AsyncWait.For(delay));
        }

        /// <summary>Adds an async "else" branch.</summary>
        public BranchEnd ElseAsync(IAsyncLogic asyncLogic)
        {
            NodeId firstElse = Builder.AddNode(asyncLogic);
            Builder.AddTransition(_falsePad, firstElse);
            return new BranchEnd(Builder, firstElse);
        }

        /// <summary>Adds a sync "else" branch.</summary>
        public BranchEnd Else(ILogic syncLogic)
        {
            NodeId firstElse = Builder.AddNode(syncLogic);
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

        /// <summary>Adds a new async state and wires a transition from the "else" tip.</summary>
        public StateToken ToAsync(IAsyncLogic asyncLogic)
        {
            NodeId next = Builder.AddNode(asyncLogic);
            Builder.AddTransition(Tip, next);
            return new StateToken(next, Builder);
        }

        /// <summary>Adds a new sync state and wires a transition from the "else" tip.</summary>
        public StateToken To(ILogic syncLogic)
        {
            NodeId next = Builder.AddNode(syncLogic);
            Builder.AddTransition(Tip, next);
            return new StateToken(next, Builder);
        }

        /// <summary>Adds a wait state after the "else" branch (async).</summary>
        public StateToken WaitForAsync(TimeSpan delay)
        {
            return ToAsync(AsyncWait.For(delay));
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

     
    }
}