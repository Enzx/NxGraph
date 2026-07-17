using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests.Blackboards;

/// <summary>
/// Step I/O ports (spec 010): a port is an ordinary Graph-scoped <see cref="BlackboardKey{T}"/>
/// and the port DSL overloads read/write it around the step lambda. Producer/pipe steps cannot
/// fail; Node-scoped keys are rejected at wiring time; Graph scope keeps templates shareable.
/// </summary>
[TestFixture]
public class PortRelayTests
{
    private static BlackboardSchema NewIoSchema(out BlackboardKey<int> raw, out BlackboardKey<int> doubled)
    {
        BlackboardSchema io = new("flow-io");
        raw = io.Register<int>("raw", 41);
        doubled = io.Register<int>("doubled");
        return io;
    }

    private static BlackboardSchema NewNodeSchema(out BlackboardKey<int> scratch)
    {
        BlackboardSchema schema = new("scratch", BlackboardScope.Node);
        scratch = schema.Register<int>("value");
        return schema;
    }

    // ── Producer: step computes, relay writes and succeeds ────────────────

    [Test]
    public async Task async_producer_writes_port_and_returns_success()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out _);
        Blackboard board = new(io);

        Graph graph = GraphBuilder
            .Start()
            .ToAsync(raw, (bb, _) => new ValueTask<int>(7))
            .WithSchema(io)
            .Build();

        Result result = await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(raw), Is.EqualTo(7));
        });
    }

    [Test]
    public void sync_producer_writes_port_and_returns_success()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out _);
        Blackboard board = new(io);

        Graph graph = GraphBuilder
            .Start()
            .To(raw, bb => 7)
            .WithSchema(io)
            .Build();

        Result result = RunToCompletion(graph.ToStateMachine().WithBlackboard(board));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(raw), Is.EqualTo(7));
        });
    }

    // ── Consumer: relay reads, step decides ───────────────────────────────

    [Test]
    public async Task async_consumer_receives_the_produced_value()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out _);
        int seen = -1;

        Graph graph = GraphBuilder
            .Start()
            .ToAsync(raw, (bb, _) => new ValueTask<int>(7))
            .ToAsync(raw, (value, bb, _) =>
            {
                seen = value;
                return ResultHelpers.Success;
            })
            .WithSchema(io)
            .Build();

        Result result = await graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(io)).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(seen, Is.EqualTo(7));
        });
    }

    [Test]
    public void sync_consumer_receives_the_produced_value()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out _);
        int seen = -1;

        Graph graph = GraphBuilder
            .Start()
            .To(raw, bb => 7)
            .To(raw, (value, bb) =>
            {
                seen = value;
                return Result.Success;
            })
            .WithSchema(io)
            .Build();

        Result result = RunToCompletion(graph.ToStateMachine().WithBlackboard(new Blackboard(io)));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(seen, Is.EqualTo(7));
        });
    }

    // ── Consumer result flows into the fault model ────────────────────────

    [Test]
    public async Task async_consumer_failure_is_an_ordinary_node_failure()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out _);

        Graph graph = GraphBuilder
            .Start()
            .ToAsync(raw, (bb, _) => new ValueTask<int>(-1))
            .ToAsync(raw, (value, bb, _) => value > 0 ? ResultHelpers.Success : ResultHelpers.Failure)
            .WithSchema(io)
            .Build();

        Result result = await graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(io)).ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Failure));
    }

    // ── Pipe: read → transform → write ────────────────────────────────────

    [Test]
    public async Task async_pipe_chains_produce_transform_consume()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out BlackboardKey<int> doubled);
        Blackboard board = new(io);
        int seen = -1;

        Graph graph = GraphBuilder
            .Start()
            .ToAsync(raw, (bb, _) => new ValueTask<int>(7))
            .ToAsync(raw, doubled, (value, bb, _) => new ValueTask<int>(value * 2))
            .ToAsync(doubled, (value, bb, _) =>
            {
                seen = value;
                return ResultHelpers.Success;
            })
            .WithSchema(io)
            .Build();

        Result result = await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(seen, Is.EqualTo(14));
            Assert.That(board.Get(doubled), Is.EqualTo(14));
        });
    }

    [Test]
    public void sync_pipe_chains_produce_transform_consume()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out BlackboardKey<int> doubled);
        Blackboard board = new(io);
        int seen = -1;

        Graph graph = GraphBuilder
            .Start()
            .To(raw, bb => 7)
            .To(raw, doubled, (value, bb) => value * 2)
            .To(doubled, (value, bb) =>
            {
                seen = value;
                return Result.Success;
            })
            .WithSchema(io)
            .Build();

        Result result = RunToCompletion(graph.ToStateMachine().WithBlackboard(board));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(seen, Is.EqualTo(14));
            Assert.That(board.Get(doubled), Is.EqualTo(14));
        });
    }

    // ── Consumer before any producer reads the registered default ─────────

    [Test]
    public async Task async_consumer_before_any_producer_sees_the_registered_default()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out _);
        int seen = -1;

        Graph graph = GraphBuilder
            .Start()
            .ToAsync(raw, (value, bb, _) =>
            {
                seen = value;
                return ResultHelpers.Success;
            })
            .WithSchema(io)
            .Build();

        await graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(io)).ExecuteAsync();

        Assert.That(seen, Is.EqualTo(41), "Consumers ahead of any producer read the key's registered default.");
    }

    [Test]
    public void sync_consumer_before_any_producer_sees_the_registered_default()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out _);
        int seen = -1;

        Graph graph = GraphBuilder
            .Start()
            .To(raw, (value, bb) =>
            {
                seen = value;
                return Result.Success;
            })
            .WithSchema(io)
            .Build();

        RunToCompletion(graph.ToStateMachine().WithBlackboard(new Blackboard(io)));

        Assert.That(seen, Is.EqualTo(41));
    }

    // ── Wiring-time validation ────────────────────────────────────────────

    [Test]
    public void node_scoped_key_throws_at_wiring_time_for_all_six_overloads()
    {
        _ = NewNodeSchema(out BlackboardKey<int> scratch);
        StateToken prev = GraphBuilder.StartWith(() => Result.Success);

        Assert.Multiple(() =>
        {
            Assert.That(() => prev.To(scratch, bb => 1),
                Throws.ArgumentException.With.Message.Contain("'value'").And.Message.Contain("Graph-scoped"),
                "sync producer");
            Assert.That(() => prev.ToAsync(scratch, (bb, _) => new ValueTask<int>(1)),
                Throws.ArgumentException.With.Message.Contain("'value'").And.Message.Contain("Graph-scoped"),
                "async producer");
            Assert.That(() => prev.To(scratch, (value, bb) => Result.Success),
                Throws.ArgumentException.With.Message.Contain("'value'").And.Message.Contain("Graph-scoped"),
                "sync consumer");
            Assert.That(() => prev.ToAsync(scratch, (value, bb, _) => ResultHelpers.Success),
                Throws.ArgumentException.With.Message.Contain("'value'").And.Message.Contain("Graph-scoped"),
                "async consumer");
            Assert.That(() => prev.To(scratch, scratch, (value, bb) => value),
                Throws.ArgumentException.With.Message.Contain("'value'").And.Message.Contain("Graph-scoped"),
                "sync pipe");
            Assert.That(() => prev.ToAsync(scratch, scratch, (value, bb, _) => new ValueTask<int>(value)),
                Throws.ArgumentException.With.Message.Contain("'value'").And.Message.Contain("Graph-scoped"),
                "async pipe");
        });
    }

    [Test]
    public void pipe_rejects_a_node_scoped_output_even_with_a_graph_scoped_input()
    {
        _ = NewIoSchema(out BlackboardKey<int> raw, out _);
        _ = NewNodeSchema(out BlackboardKey<int> scratch);
        StateToken prev = GraphBuilder.StartWith(() => Result.Success);

        Assert.That(() => prev.To(raw, scratch, (value, bb) => value),
            Throws.ArgumentException.With.Message.Contain("'value'").And.Message.Contain("Graph-scoped"));
    }

    [Test]
    public void invalid_default_key_throws_at_wiring_time()
    {
        BlackboardKey<int> invalid = default;
        StateToken prev = GraphBuilder.StartWith(() => Result.Success);

        Assert.Multiple(() =>
        {
            Assert.That(() => prev.To(invalid, bb => 1),
                Throws.ArgumentException.With.Message.Contain("Invalid port key"));
            Assert.That(() => prev.ToAsync(invalid, (value, bb, _) => ResultHelpers.Success),
                Throws.ArgumentException.With.Message.Contain("Invalid port key"));
        });
    }

    [Test]
    public async Task global_scoped_key_is_accepted()
    {
        BlackboardSchema world = new("world", BlackboardScope.Global);
        BlackboardKey<int> sightings = world.Register<int>("sightings");
        Blackboard board = new(world);
        int seen = -1;

        Graph graph = GraphBuilder
            .Start()
            .ToAsync(sightings, (bb, _) => new ValueTask<int>(bb.Get(sightings) + 1))
            .ToAsync(sightings, (value, bb, _) =>
            {
                seen = value;
                return ResultHelpers.Success;
            })
            .WithSchema(world)
            .Build();

        Result result = await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(seen, Is.EqualTo(1), "Global ports pipe mechanically — they are just shared across machines.");
        });
    }

    // ── Template shareability: the feature's core promise ─────────────────

    [Test]
    public async Task async_two_machines_over_one_template_never_see_each_others_port_values()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out BlackboardKey<int> doubled);

        // The consumer succeeds only when the piped value matches this machine's own board.
        Graph shared = GraphBuilder
            .Start()
            .ToAsync(raw, doubled, (value, bb, _) => new ValueTask<int>(value * 2))
            .ToAsync(doubled, (value, bb, _) =>
                value == bb.Get(raw) * 2 ? ResultHelpers.Success : ResultHelpers.Failure)
            .WithSchema(io)
            .Build();

        Blackboard boardA = new(io);
        Blackboard boardB = new(io);
        boardA.Set(raw, 10);
        boardB.Set(raw, 20);

        AsyncStateMachine machineA = shared.ToAsyncStateMachine().WithBlackboard(boardA);
        AsyncStateMachine machineB = shared.ToAsyncStateMachine().WithBlackboard(boardB);

        Result firstA = await machineA.ExecuteAsync();
        Result firstB = await machineB.ExecuteAsync();
        Result secondA = await machineA.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstA, Is.EqualTo(Result.Success));
            Assert.That(firstB, Is.EqualTo(Result.Success));
            Assert.That(secondA, Is.EqualTo(Result.Success));
            Assert.That(boardA.Get(doubled), Is.EqualTo(20), "Machine A's ports live on machine A's board.");
            Assert.That(boardB.Get(doubled), Is.EqualTo(40), "Machine B's ports live on machine B's board.");
        });
    }

    [Test]
    public void sync_two_machines_over_one_template_never_see_each_others_port_values()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out BlackboardKey<int> doubled);

        Graph shared = GraphBuilder
            .Start()
            .To(raw, doubled, (value, bb) => value * 2)
            .To(doubled, (value, bb) => value == bb.Get(raw) * 2 ? Result.Success : Result.Failure)
            .WithSchema(io)
            .Build();

        Blackboard boardA = new(io);
        Blackboard boardB = new(io);
        boardA.Set(raw, 10);
        boardB.Set(raw, 20);

        StateMachine machineA = shared.ToStateMachine().WithBlackboard(boardA);
        StateMachine machineB = shared.ToStateMachine().WithBlackboard(boardB);

        Result firstA = RunToCompletion(machineA);
        Result firstB = RunToCompletion(machineB);
        Result secondA = RunToCompletion(machineA);

        Assert.Multiple(() =>
        {
            Assert.That(firstA, Is.EqualTo(Result.Success));
            Assert.That(firstB, Is.EqualTo(Result.Success));
            Assert.That(secondA, Is.EqualTo(Result.Success));
            Assert.That(boardA.Get(doubled), Is.EqualTo(20));
            Assert.That(boardB.Get(doubled), Is.EqualTo(40));
        });
    }

    // ── Branch receivers ──────────────────────────────────────────────────

    [Test]
    public async Task async_branch_receivers_support_ports()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out BlackboardKey<int> doubled);
        Blackboard board = new(io);

        // StartToken producer → If(true) → BranchBuilder pipe on the "then" branch.
        Graph graph = GraphBuilder
            .Start()
            .ToAsync(raw, (bb, _) => new ValueTask<int>(3))
            .If(bb => bb.Get(raw) > 0)
            .ThenAsync((bb, _) => ResultHelpers.Success)
            .ToAsync(raw, doubled, (value, bb, _) => new ValueTask<int>(value * 2))
            .ElseAsync((bb, _) => ResultHelpers.Success)
            .WithSchema(io)
            .Build();

        Result result = await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(doubled), Is.EqualTo(6));
        });
    }

    [Test]
    public void sync_branch_receivers_support_ports()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out _);
        int seen = -1;

        // StartToken producer → If(false) → BranchEnd consumer after the "else" branch.
        Graph graph = GraphBuilder
            .Start()
            .To(raw, bb => 3)
            .If(bb => bb.Get(raw) < 0)
            .Then(bb => Result.Success)
            .Else(bb => Result.Success)
            .To(raw, (value, bb) =>
            {
                seen = value;
                return Result.Success;
            })
            .WithSchema(io)
            .Build();

        Result result = RunToCompletion(graph.ToStateMachine().WithBlackboard(new Blackboard(io)));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(seen, Is.EqualTo(3));
        });
    }

    // ── Unbound Graph scope keeps the precise runtime error ───────────────

    [Test]
    public void async_unbound_graph_scope_at_run_time_keeps_the_precise_error()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out _);
        _ = io;

        Graph graph = GraphBuilder
            .Start()
            .ToAsync(raw, (bb, _) => new ValueTask<int>(7))
            .Build();

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await graph.ToAsyncStateMachine().ExecuteAsync());
        Assert.That(ex!.Message, Does.Contain("graph key 'raw'").And.Contain("WithBlackboard"));
    }

    [Test]
    public void sync_unbound_graph_scope_at_run_time_keeps_the_precise_error()
    {
        BlackboardSchema io = NewIoSchema(out BlackboardKey<int> raw, out _);
        _ = io;

        Graph graph = GraphBuilder
            .Start()
            .To(raw, bb => 7)
            .Build();

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
            () => RunToCompletion(graph.ToStateMachine()));
        Assert.That(ex!.Message, Does.Contain("graph key 'raw'").And.Contain("WithBlackboard"));
    }

    private static Result RunToCompletion(StateMachine machine)
    {
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        return result;
    }
}
