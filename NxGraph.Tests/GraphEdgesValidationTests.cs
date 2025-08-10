using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
[Category("graph_validation")]
public class GraphEdgesValidationTests
{
    [Test]
    public void adding_second_outgoing_transition_from_same_node_should_throw()
    {
        GraphBuilder? builder = new();
        NodeId a = builder.AddNode(new RelayState(_ => ValueTask.FromResult(Result.Success)), true);
        NodeId b = builder.AddNode(new RelayState(_ => ValueTask.FromResult(Result.Success)));
        NodeId c = builder.AddNode(new RelayState(_ => ValueTask.FromResult(Result.Success)));

        builder.AddTransition(a, b);
        Assert.Throws<InvalidOperationException>(() => builder.AddTransition(a, c));
    }
}