using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
[Category("composite_state")]
public class CompositeStateTests
{
    [Test]
    public async Task composite_state_should_wrap_child_fsm()
    {
        // Child graph: A (success)
        GraphBuilder builder = new();
        builder.AddNode(new RelayState(_ => ResultHelpers.Success), true);
        Graph childGraph = builder.Build();
        StateMachine childFsm = new(childGraph);
        CompositeState composite = new(childFsm);

        // Parent graph: single composite node
        GraphBuilder parentBuilder = new();
        parentBuilder.AddNode(composite, true);
        Graph parentGraph = parentBuilder.Build();
        StateMachine parentFsm = new(parentGraph);
        Result result = await parentFsm.ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Success));
    }
}