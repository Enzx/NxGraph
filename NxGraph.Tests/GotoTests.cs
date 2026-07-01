using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
public class GotoTests
{
    [Test]
    public void goto_wires_a_back_edge_to_the_named_node()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success).SetName("A")
            .To(() => Result.Success).SetName("B")
            .Goto("A")
            .Build();

        Transition backEdge = graph.GetTransitionByIndex(1);
        Assert.Multiple(() =>
        {
            Assert.That(backEdge.IsEmpty, Is.False);
            Assert.That(backEdge.Destination.Index, Is.Zero);
            Assert.That(backEdge.Destination.Name, Is.EqualTo("A"));
        });
    }

    [Test]
    public void goto_loop_executes_until_a_node_breaks_it()
    {
        int visits = 0;
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success).SetName("A")
            .To(() => ++visits < 3 ? Result.Success : Result.Fail("done"))
            .Goto("A")
            .Build();

        StateMachine machine = graph.ToStateMachine();
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(visits, Is.EqualTo(3));
        });
    }

    [Test]
    public void goto_target_may_be_named_after_the_goto_is_declared()
    {
        StateToken start = GraphBuilder.StartWith(() => Result.Success);
        GotoToken chain = start.To(() => Result.Fail()).Goto("Late");
        _ = start.SetName("Late");

        Graph graph = chain.Build();
        Assert.That(graph.GetTransitionByIndex(1).Destination.Index, Is.Zero);
    }

    [Test]
    public void goto_with_unknown_name_fails_the_build()
    {
        GotoToken chain = GraphBuilder
            .StartWith(() => Result.Success).SetName("A")
            .To(() => Result.Success)
            .Goto("Missing");

        Assert.Throws<InvalidOperationException>(() => chain.Build());
    }

    [Test]
    public void goto_with_ambiguous_name_fails_the_build()
    {
        GotoToken chain = GraphBuilder
            .StartWith(() => Result.Success).SetName("X")
            .To(() => Result.Success).SetName("X")
            .To(() => Result.Success)
            .Goto("X");

        Assert.Throws<InvalidOperationException>(() => chain.Build());
    }

    [Test]
    public void goto_conflicts_with_an_existing_transition()
    {
        StateToken start = GraphBuilder.StartWith(() => Result.Success).SetName("A");
        StateToken next = start.To(() => Result.Success);

        Assert.Throws<InvalidOperationException>(() => start.Goto("A"));
        Assert.DoesNotThrow(() => next.Goto("A"));
    }

    [Test]
    public void transition_after_goto_from_same_node_throws()
    {
        StateToken start = GraphBuilder.StartWith(() => Result.Success).SetName("A");
        StateToken second = start.To(() => Result.Success);
        second.Goto("A");

        Assert.Throws<InvalidOperationException>(() => second.ToAsync(_ => ResultHelpers.Success));
    }

    [Test]
    public void goto_with_blank_name_throws_immediately()
    {
        StateToken start = GraphBuilder.StartWith(() => Result.Success);
        Assert.Throws<ArgumentException>(() => start.Goto(" "));
    }
}
