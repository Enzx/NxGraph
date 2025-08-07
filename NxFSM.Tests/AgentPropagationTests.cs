using NxFSM.Authoring;
using NxFSM.Fsm;

namespace NxFSM.Tests;

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
            .Build()
            .ToStateMachine<DummyAgent>()
            .Add(agent);

        await fsm.ExecuteAsync();

        Assert.That(agent.Visited, Is.True);
    }
}