using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
[Category("composite_state")]
public class AsyncCompositeStateTests
{
    [Test]
    public async Task composite_state_should_wrap_child_fsm()
    {
        // Child graph: A (success)
        GraphBuilder builder = new();
        builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success), true);
        Graph childGraph = builder.Build();
        AsyncStateMachine childFsm = new(childGraph);
        AsyncCompositeState asyncComposite = new(childFsm);

        // Parent graph: single composite node
        GraphBuilder parentBuilder = new();
        parentBuilder.AddNode(asyncComposite, true);
        Graph parentGraph = parentBuilder.Build();
        AsyncStateMachine parentFsm = new(parentGraph);
        Result result = await parentFsm.ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Success));
    }
}