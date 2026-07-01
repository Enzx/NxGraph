using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// The agent (typed context) is bound per machine and re-stamped onto the graph at the
/// start of every run, so one immutable <see cref="Graph"/> can be shared by several
/// machines with distinct contexts — provided their executions don't overlap.
/// </summary>
[TestFixture]
public class ContextInjectionTests
{
    private sealed class Blackboard
    {
        public string Name = "";
        public readonly List<string> Log = [];
    }

    private static Graph BuildGraph()
    {
        return GraphBuilder
            .StartWithAsync(new AsyncRelayState<Blackboard>((bb, _) =>
            {
                bb.Log.Add(bb.Name);
                return ResultHelpers.Success;
            }))
            .Build();
    }

    [Test]
    public async Task two_async_machines_share_a_graph_with_distinct_contexts()
    {
        Graph graph = BuildGraph();

        Blackboard first = new() { Name = "first" };
        Blackboard second = new() { Name = "second" };

        AsyncStateMachine<Blackboard> machineA = graph.ToAsyncStateMachine<Blackboard>().Add(first);
        AsyncStateMachine<Blackboard> machineB = graph.ToAsyncStateMachine<Blackboard>().Add(second);

        await machineA.ExecuteAsync();
        await machineB.ExecuteAsync();
        await machineA.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.Log, Is.EqualTo(new[] { "first", "first" }),
                "Machine A must see its own context on every run, even after B ran in between.");
            Assert.That(second.Log, Is.EqualTo(new[] { "second" }));
        });
    }

    [Test]
    public void two_sync_machines_share_a_graph_with_distinct_contexts()
    {
        Graph graph = GraphBuilder
            .StartWith(new RelayState<Blackboard>(bb =>
            {
                bb.Log.Add(bb.Name);
                return Result.Success;
            }))
            .Build();

        Blackboard first = new() { Name = "first" };
        Blackboard second = new() { Name = "second" };

        StateMachine<Blackboard> machineA = graph.ToStateMachine<Blackboard>().Add(first);
        StateMachine<Blackboard> machineB = graph.ToStateMachine<Blackboard>().Add(second);

        RunToCompletion(machineA);
        RunToCompletion(machineB);
        RunToCompletion(machineA);

        Assert.Multiple(() =>
        {
            Assert.That(first.Log, Is.EqualTo(new[] { "first", "first" }));
            Assert.That(second.Log, Is.EqualTo(new[] { "second" }));
        });
    }

    [Test]
    public void set_agent_still_applies_immediately()
    {
        Graph graph = BuildGraph();
        Blackboard context = new() { Name = "eager" };

        // Immediate propagation is preserved for callers that inspect node state before running.
        Assert.DoesNotThrow(() => graph.ToAsyncStateMachine<Blackboard>().Add(context));
    }

    private sealed class CustomComposite(Graph child) : IAsyncLogic, ISubGraphProvider
    {
        private readonly AsyncStateMachine _machine = new(child);

        public IEnumerable<Graph> SubGraphs => [_machine.Graph];

        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => _machine.ExecuteAsync(ct);
    }

    [Test]
    public async Task user_defined_composite_receives_the_agent_via_subgraph_provider()
    {
        Blackboard context = new() { Name = "nested" };
        Graph inner = BuildGraph();

        Graph outer = GraphBuilder
            .StartWithAsync(new CustomComposite(inner))
            .Build();

        AsyncStateMachine<Blackboard> machine = outer.ToAsyncStateMachine<Blackboard>().Add(context);
        await machine.ExecuteAsync();

        Assert.That(context.Log, Is.EqualTo(new[] { "nested" }),
            "Graph.SetAgent must reach nodes inside a composite it has no compile-time knowledge of.");
    }

    [Test]
    public void set_agent_with_no_accepting_node_still_throws()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .Build();

        AsyncStateMachine<Blackboard> machine = graph.ToAsyncStateMachine<Blackboard>();
        Assert.Throws<InvalidOperationException>(() => machine.SetAgent(new Blackboard()));
    }

    private static void RunToCompletion(StateMachine machine)
    {
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }
    }
}
