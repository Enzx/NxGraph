using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// Observers must see the graph's <b>built</b> node ids — display names included — for every
/// node a run enters, no matter how the machine got there. Ordinary edge destinations get
/// their names applied at <c>Build()</c>, but director-selected targets (event dispatch,
/// the plain-run <c>Otherwise</c> chain, choice/switch branches) are captured at authoring
/// time, before names exist; the machines canonicalize them against the graph so
/// <c>OnTransition</c>/<c>OnStateEntered</c>/<c>OnStateExited</c> report the same id an edge
/// hop would.
/// </summary>
[TestFixture]
public class DirectorTargetObservationTests
{
    private sealed record Ping(string Payload);

    private static (Graph Graph, BlackboardSchema Schema, BlackboardKey<Ping> Key) EventGraph(
        bool singleStepChain = false)
    {
        BlackboardSchema schema = new("events");
        BlackboardKey<Ping> ping = schema.Register<Ping>("ping");

        EventsToken token = GraphBuilder.StartWithEvents();
        token = singleStepChain
            ? token.On(ping, e => e.ToAsync(_ => ResultHelpers.Success).SetName("handle-ping"))
            : token.On(ping, e => e
                .ToAsync(_ => ResultHelpers.Success).SetName("handle-ping")
                .ToAsync(_ => ResultHelpers.Success).SetName("after-ping"));

        Graph graph = token
            .Otherwise(e => e.ToAsync(_ => ResultHelpers.Success).SetName("manual"))
            .WithSchema(schema)
            .Build();
        return (graph, schema, ping);
    }

    private static (Graph Graph, BlackboardSchema Schema, BlackboardKey<Ping> Key) SyncEventGraph()
    {
        BlackboardSchema schema = new("events");
        BlackboardKey<Ping> ping = schema.Register<Ping>("ping");

        Graph graph = GraphBuilder.StartWithEvents()
            .On(ping, e => e
                .To(() => Result.Success).SetName("handle-ping")
                .To(() => Result.Success).SetName("after-ping"))
            .Otherwise(e => e.To(() => Result.Success).SetName("manual"))
            .WithSchema(schema)
            .Build();
        return (graph, schema, ping);
    }

    // ── Event dispatch (async) ───────────────────────────────────────────

    [Test]
    public async Task async_event_dispatch_target_is_observed_by_its_built_id()
    {
        (Graph graph, BlackboardSchema schema, BlackboardKey<Ping> ping) = EventGraph();
        RecordingObserver observer = new();
        AsyncStateMachine fsm = graph.ToAsyncStateMachine(observer).WithBlackboard(new Blackboard(schema));

        await fsm.ExecuteAsync(new Ping("p-1"));

        Assert.Multiple(() =>
        {
            Assert.That(observer.EnteredNames, Does.Contain("handle-ping"),
                "The node the dispatch jumps to must be entered under its built id.");
            Assert.That(observer.ExitedNames, Does.Contain("handle-ping"),
                "The node the dispatch jumps to must be exited under its built id.");
            Assert.That(observer.TransitionTargets, Does.Contain("handle-ping"),
                "The dispatch transition must name the built target id.");
            Assert.That(observer.EnteredNames, Does.Contain("after-ping"),
                "Nodes after the dispatch target observe via the ordinary edge path.");
        });
    }

    [Test]
    public async Task async_single_step_entry_chain_observes_its_one_node()
    {
        (Graph graph, BlackboardSchema schema, BlackboardKey<Ping> ping) = EventGraph(singleStepChain: true);
        RecordingObserver observer = new();
        AsyncStateMachine fsm = graph.ToAsyncStateMachine(observer).WithBlackboard(new Blackboard(schema));

        await fsm.ExecuteAsync(new Ping("p-1"));

        Assert.Multiple(() =>
        {
            Assert.That(observer.EnteredNames, Does.Contain("handle-ping"));
            Assert.That(observer.ExitedNames, Does.Contain("handle-ping"));
        });
    }

    [Test]
    public async Task async_plain_start_otherwise_target_is_observed_by_its_built_id()
    {
        (Graph graph, BlackboardSchema schema, _) = EventGraph();
        RecordingObserver observer = new();
        AsyncStateMachine fsm = graph.ToAsyncStateMachine(observer).WithBlackboard(new Blackboard(schema));

        await fsm.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(observer.EnteredNames, Does.Contain("manual"));
            Assert.That(observer.ExitedNames, Does.Contain("manual"));
        });
    }

    [Test]
    public async Task async_stepped_event_dispatch_target_is_observed_by_its_built_id()
    {
        (Graph graph, BlackboardSchema schema, BlackboardKey<Ping> ping) = EventGraph();
        RecordingObserver observer = new();
        AsyncStateMachine fsm = graph.ToAsyncStateMachine(observer).WithBlackboard(new Blackboard(schema));

        Result result = await fsm.StepAsync(new Ping("p-1"));
        while (result == Result.InProgress)
        {
            result = await fsm.StepAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(observer.EnteredNames, Does.Contain("handle-ping"));
            Assert.That(observer.ExitedNames, Does.Contain("handle-ping"));
        });
    }

    // ── Event dispatch (sync twin) ───────────────────────────────────────

    [Test]
    public void sync_event_dispatch_target_is_observed_by_its_built_id()
    {
        (Graph graph, BlackboardSchema schema, BlackboardKey<Ping> ping) = SyncEventGraph();
        SyncRecordingObserver observer = new();
        StateMachine fsm = graph.ToStateMachine(observer);
        fsm.SetBlackboard(new Blackboard(schema));

        Result result = fsm.Execute(new Ping("p-1"));
        while (result == Result.InProgress)
        {
            result = fsm.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(observer.EnteredNames, Does.Contain("handle-ping"));
            Assert.That(observer.ExitedNames, Does.Contain("handle-ping"));
            Assert.That(observer.TransitionTargets, Does.Contain("handle-ping"));
        });
    }

    [Test]
    public void sync_plain_start_otherwise_target_is_observed_by_its_built_id()
    {
        (Graph graph, BlackboardSchema schema, _) = SyncEventGraph();
        SyncRecordingObserver observer = new();
        StateMachine fsm = graph.ToStateMachine(observer);
        fsm.SetBlackboard(new Blackboard(schema));
        fsm.SetStepMode(ParallelStepMode.RunToJoin);

        fsm.Execute();

        Assert.That(observer.EnteredNames, Does.Contain("manual"));
    }

    // ── Ordinary directors (choice) share the same contract ─────────────

    [Test]
    public async Task async_choice_branch_target_is_observed_by_its_built_id()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success).SetName("ask")
            .If(() => true)
            .Then(() => Result.Success).SetName("yes")
            .Else(() => Result.Success).SetName("no")
            .Build();
        RecordingObserver observer = new();
        AsyncStateMachine fsm = graph.ToAsyncStateMachine(observer);

        await fsm.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(observer.EnteredNames, Does.Contain("yes"));
            Assert.That(observer.ExitedNames, Does.Contain("yes"));
        });
    }

    [Test]
    public void sync_choice_branch_target_is_observed_by_its_built_id()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success).SetName("ask")
            .If(() => true)
            .Then(() => Result.Success).SetName("yes")
            .Else(() => Result.Success).SetName("no")
            .Build();
        SyncRecordingObserver observer = new();
        StateMachine fsm = graph.ToStateMachine(observer);
        fsm.SetStepMode(ParallelStepMode.RunToJoin);

        fsm.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(observer.EnteredNames, Does.Contain("yes"));
            Assert.That(observer.ExitedNames, Does.Contain("yes"));
        });
    }

    // ── Plain single-node graph (spec pin: entry observation on a plain start) ──

    [Test]
    public async Task async_single_node_graph_observes_its_one_node()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success).SetName("only")
            .Build();
        RecordingObserver observer = new();
        AsyncStateMachine fsm = graph.ToAsyncStateMachine(observer);

        await fsm.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(observer.EnteredNames, Is.EqualTo(new[] { "only" }));
            Assert.That(observer.ExitedNames, Is.EqualTo(new[] { "only" }));
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private sealed class RecordingObserver : IAsyncStateMachineObserver
    {
        public readonly List<string> EnteredNames = [];
        public readonly List<string> ExitedNames = [];
        public readonly List<string> TransitionTargets = [];

        public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
        {
            EnteredNames.Add(id.Name);
            return default;
        }

        public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
        {
            ExitedNames.Add(id.Name);
            return default;
        }

        public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
        {
            TransitionTargets.Add(to.Name);
            return default;
        }
    }

    private sealed class SyncRecordingObserver : IStateMachineObserver
    {
        public readonly List<string> EnteredNames = [];
        public readonly List<string> ExitedNames = [];
        public readonly List<string> TransitionTargets = [];

        void IStateMachineObserver.OnStateEntered(NodeId id) => EnteredNames.Add(id.Name);

        void IStateMachineObserver.OnStateExited(NodeId id) => ExitedNames.Add(id.Name);

        void IStateMachineObserver.OnTransition(NodeId from, NodeId to) => TransitionTargets.Add(to.Name);
    }
}
