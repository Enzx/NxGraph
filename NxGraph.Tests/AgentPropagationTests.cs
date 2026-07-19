using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

[TestFixture]
[Category("agent_propagation")]
public class AgentPropagationTests
{
    private sealed class DummyAgent
    {
        public bool Visited;
    }

    [Test]
    public async Task agent_should_be_injected_into_generic_state()
    {
        AsyncRelayState<DummyAgent> node = new((agent, _) =>
        {
            agent.Visited = true;
            return ResultHelpers.Success;
        });

        DummyAgent agent = new();
        AsyncStateMachine<DummyAgent> fsm = GraphBuilder
            .StartWithAsync(node)
            .ToAsyncStateMachine<DummyAgent>()
            .Add(agent);
        await fsm.ExecuteAsync();

        Assert.That(agent.Visited, Is.True);
    }

    [Test]
    public void agent_should_throw_exception_if_fsm_has_no_generic_state()
    {
        DummyAgent agent = new();
        AsyncStateMachine<DummyAgent> fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine<DummyAgent>();

        Assert.Throws<InvalidOperationException>(() => fsm.SetAgent(agent));
    }

    [Test]
    public async Task agent_should_propagate_into_nested_non_generic_async_state_machine()
    {
        // Regression: a plain (non-generic) AsyncStateMachine embedded as a node previously
        // did not have its inner graph walked by Graph.SetAgent because it doesn't
        // implement IAgentSettable<TAgent> directly. The inner IAgentSettable<DummyAgent>
        // would silently miss the agent and fail at execution time.
        AsyncRelayState<DummyAgent> innerLeaf = new((a, _) =>
        {
            a.Visited = true;
            return ResultHelpers.Success;
        });

        AsyncStateMachine innerMachine = GraphBuilder
            .StartWithAsync(innerLeaf)
            .ToAsyncStateMachine();

        AsyncStateMachine<DummyAgent> outerMachine = GraphBuilder
            .StartWithAsync(innerMachine)
            .ToAsyncStateMachine<DummyAgent>();

        DummyAgent agent = new();
        outerMachine.SetAgent(agent);

        await outerMachine.ExecuteAsync();

        Assert.That(agent.Visited, Is.True);
    }
}
