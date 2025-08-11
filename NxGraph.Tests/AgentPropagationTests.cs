using NxGraph.Authoring;
using NxGraph.Fsm;

namespace NxGraph.Tests;

[TestFixture]
[Category("agent_propagation")]
public class AgentPropagationTests
{
    private class DummyAgent
    {
        public bool Visited;
    }

    [Test]
    public async Task agent_should_be_injected_into_generic_state()
    {
        RelayState<DummyAgent> node = new((agent, _) =>
        {
            agent.Visited = true;
            return ResultHelpers.Success;
        });

        DummyAgent agent = new();
        StateMachine<DummyAgent> fsm = GraphBuilder
            .StartWith(node)
            .ToStateMachine<DummyAgent>()
            .Add(agent);
        await fsm.ExecuteAsync();

        Assert.That(agent.Visited, Is.True);
    }

    [Test]
    public void agent_should_throw_exception_if_fsm_has_no_generic_state()
    {
        DummyAgent agent = new();
        StateMachine<DummyAgent> fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .ToStateMachine<DummyAgent>();

        Assert.Throws<InvalidOperationException>(() => fsm.SetAgent(agent));
    }
}