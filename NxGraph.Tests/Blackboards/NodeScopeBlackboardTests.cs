using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Diagnostics.Validations;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests.Blackboards;

/// <summary>
/// <see cref="BlackboardScope.Node"/>: transient per-visit scratch, machine-owned, reset at
/// every transition boundary (the "Node board resets iff the attempt counter resets" rule),
/// kept across in-place retries, never user-bound, never durable.
/// </summary>
[TestFixture]
public class NodeScopeBlackboardTests
{
    private static BlackboardSchema NewNodeSchema(out BlackboardKey<int> scratch)
    {
        BlackboardSchema schema = new("scratch", BlackboardScope.Node);
        scratch = schema.Register<int>("value");
        return schema;
    }

    // ── Reset at transition boundaries ────────────────────────────────────

    [Test]
    public async Task async_node_value_is_default_again_after_success_transition()
    {
        BlackboardSchema schema = NewNodeSchema(out BlackboardKey<int> scratch);
        int seenInB = -1;

        Graph graph = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(scratch, 42);
                return ResultHelpers.Success;
            })
            .ToAsync((bb, _) =>
            {
                seenInB = bb.Get(scratch);
                return ResultHelpers.Success;
            })
            .WithSchema(schema)
            .Build();

        await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.That(seenInB, Is.Zero, "Node scratch written in A must be default again in B.");
    }

    [Test]
    public void sync_node_value_is_default_again_after_success_transition()
    {
        BlackboardSchema schema = NewNodeSchema(out BlackboardKey<int> scratch);
        int seenInB = -1;

        Graph graph = GraphBuilder
            .StartWith(bb =>
            {
                bb.Set(scratch, 42);
                return Result.Success;
            })
            .To(bb =>
            {
                seenInB = bb.Get(scratch);
                return Result.Success;
            })
            .WithSchema(schema)
            .Build();

        RunToCompletion(graph.ToStateMachine());

        Assert.That(seenInB, Is.Zero, "Node scratch written in A must be default again in B.");
    }

    [Test]
    public async Task async_node_value_is_default_after_director_route()
    {
        BlackboardSchema schema = NewNodeSchema(out BlackboardKey<int> scratch);
        int seenInBranch = -1;

        Graph graph = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(scratch, 7);
                return ResultHelpers.Success;
            })
            .If(() => true)
            .ThenAsync((bb, _) =>
            {
                seenInBranch = bb.Get(scratch);
                return ResultHelpers.Success;
            })
            .ElseAsync((_, _) => ResultHelpers.Success)
            .WithSchema(schema)
            .Build();

        await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.That(seenInBranch, Is.Zero, "Node scratch must reset across a director route too.");
    }

    [Test]
    public async Task async_node_value_is_default_after_failure_edge_reroute()
    {
        BlackboardSchema schema = NewNodeSchema(out BlackboardKey<int> scratch);
        int seenInHandler = -1;

        Graph graph = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(scratch, 13);
                return ResultHelpers.Failure;
            })
            .OnErrorAsync(new AsyncRelayState((bb, _) =>
            {
                seenInHandler = bb.Get(scratch);
                return ResultHelpers.Success;
            }))
            .WithSchema(schema)
            .Build();

        await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.That(seenInHandler, Is.Zero, "Node scratch must reset when routing the failure edge.");
    }

    [Test]
    public void sync_node_value_is_default_after_failure_edge_reroute()
    {
        BlackboardSchema schema = NewNodeSchema(out BlackboardKey<int> scratch);
        int seenInHandler = -1;

        Graph graph = GraphBuilder
            .StartWith(bb =>
            {
                bb.Set(scratch, 13);
                return Result.Failure;
            })
            .OnError(new RelayState(bb =>
            {
                seenInHandler = bb.Get(scratch);
                return Result.Success;
            }))
            .WithSchema(schema)
            .Build();

        RunToCompletion(graph.ToStateMachine());

        Assert.That(seenInHandler, Is.Zero, "Node scratch must reset when routing the failure edge.");
    }

    // ── Retries keep the scratch (same visit) ─────────────────────────────

    [Test]
    public async Task async_node_value_persists_across_in_place_retries()
    {
        BlackboardSchema schema = NewNodeSchema(out BlackboardKey<int> scratch);
        List<int> attemptsSaw = [];

        Graph graph = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                attemptsSaw.Add(bb.Get(scratch));
                bb.GetRef(scratch)++;
                return bb.Get(scratch) < 3 ? ResultHelpers.Failure : ResultHelpers.Success;
            })
            .Retry(maxAttempts: 5)
            .WithSchema(schema)
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(attemptsSaw, Is.EqualTo(new[] { 0, 1, 2 }),
                "Retrying in place keeps the visit's scratch — partial progress across attempts.");
        });
    }

    [Test]
    public void sync_node_value_persists_across_in_place_retries()
    {
        BlackboardSchema schema = NewNodeSchema(out BlackboardKey<int> scratch);
        List<int> attemptsSaw = [];

        Graph graph = GraphBuilder
            .StartWith(bb =>
            {
                attemptsSaw.Add(bb.Get(scratch));
                bb.GetRef(scratch)++;
                return bb.Get(scratch) < 3 ? Result.Failure : Result.Success;
            })
            .Retry(maxAttempts: 5)
            .WithSchema(schema)
            .Build();

        Result result = RunToCompletion(graph.ToStateMachine());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(attemptsSaw, Is.EqualTo(new[] { 0, 1, 2 }));
        });
    }

    [Test]
    public async Task node_value_is_default_at_the_start_of_every_run()
    {
        BlackboardSchema schema = NewNodeSchema(out BlackboardKey<int> scratch);
        List<int> runsSaw = [];

        Graph graph = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                runsSaw.Add(bb.Get(scratch));
                bb.Set(scratch, 99);
                return ResultHelpers.Success;
            })
            .WithSchema(schema)
            .Build();

        AsyncStateMachine machine = graph.ToAsyncStateMachine();
        await machine.ExecuteAsync();
        await machine.ExecuteAsync();

        Assert.That(runsSaw, Is.EqualTo(new[] { 0, 0 }), "Each run starts with fresh scratch.");
    }

    // ── Ownership: machine-owned, never user-bound ────────────────────────

    [Test]
    public void binding_a_node_scoped_board_throws_on_both_machines()
    {
        BlackboardSchema schema = NewNodeSchema(out _);
        Graph graph = GraphBuilder.StartWith(() => Result.Success).Build();

        Assert.Multiple(() =>
        {
            Assert.That(() => graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(schema)),
                Throws.InvalidOperationException.With.Message.Contain("machine-owned"),
                "Node boards are transient runtime scratch and must not be shared or rebound.");
            Assert.That(() => graph.ToStateMachine().WithBlackboard(new Blackboard(schema)),
                Throws.InvalidOperationException.With.Message.Contain("machine-owned"));
        });
    }

    [Test]
    public void unbound_node_key_access_throws_with_guidance()
    {
        BlackboardSchema schema = NewNodeSchema(out BlackboardKey<int> scratch);
        _ = schema;

        // The schema is never declared on the graph, so no machine composes a board for it.
        Graph graph = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(scratch, 1);
                return ResultHelpers.Success;
            })
            .Build();

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await graph.ToAsyncStateMachine().ExecuteAsync());
        Assert.That(ex!.Message, Does.Contain("no node blackboard").And.Contain("WithSchema"));
    }

    [Test]
    public async Task two_machines_over_one_graph_never_see_each_others_node_values()
    {
        BlackboardSchema schema = NewNodeSchema(out BlackboardKey<int> scratch);
        List<int> firstAttemptSaw = [];

        // Retry visit: attempt 1 records what it finds, writes a marker, and fails;
        // attempt 2 succeeds. Any leakage between the machines' boards would surface
        // as a non-default value on a later machine's first attempt.
        Graph shared = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                if (bb.Get(scratch) == 0)
                {
                    firstAttemptSaw.Add(bb.Get(scratch));
                    bb.Set(scratch, 7);
                    return ResultHelpers.Failure;
                }

                return ResultHelpers.Success;
            })
            .Retry(maxAttempts: 3)
            .WithSchema(schema)
            .Build();

        AsyncStateMachine machineA = shared.ToAsyncStateMachine();
        AsyncStateMachine machineB = shared.ToAsyncStateMachine();

        Assert.That(await machineA.ExecuteAsync(), Is.EqualTo(Result.Success));
        Assert.That(await machineB.ExecuteAsync(), Is.EqualTo(Result.Success));
        Assert.That(await machineA.ExecuteAsync(), Is.EqualTo(Result.Success));

        Assert.That(firstAttemptSaw, Is.All.Zero,
            "Every machine (and every run) starts its visits from defaults — zero setup, no aliasing.");
    }

    // ── Not durable: suspend/resume resets scratch ────────────────────────

    [Test]
    public async Task async_resume_on_a_fresh_machine_resets_node_scratch_but_keeps_other_scopes()
    {
        BlackboardSchema nodeSchema = NewNodeSchema(out BlackboardKey<int> scratch);
        BlackboardSchema graphSchema = new("persistent");
        BlackboardKey<int> persistent = graphSchema.Register<int>("value");
        List<(int node, int graph)> attemptsSaw = [];

        Graph graph = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                attemptsSaw.Add((bb.Get(scratch), bb.Get(persistent)));
                bb.Set(scratch, 5);
                if (bb.Get(persistent) >= 10)
                {
                    return ResultHelpers.Success;
                }

                bb.Set(persistent, 5);
                return ResultHelpers.Failure;
            })
            .Retry(maxAttempts: 10)
            .WithSchema(nodeSchema)
            .WithSchema(graphSchema)
            .Build();

        Blackboard board = new(graphSchema);
        AsyncStateMachine machine = graph.ToAsyncStateMachine().WithBlackboard(board);

        // One step: attempt 1 writes scratch+persistent and fails (retry pending).
        Result step = await machine.StepAsync();
        Assert.That(step, Is.EqualTo(Result.InProgress));

        StateMachineSnapshot snapshot = machine.Suspend();

        AsyncStateMachine resumed = graph.ToAsyncStateMachine().WithBlackboard(board);
        resumed.Resume(snapshot);
        board.Set(persistent, 10); // makes the next attempt succeed

        Result result = await resumed.StepAsync();
        while (result == Result.InProgress)
        {
            result = await resumed.StepAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(attemptsSaw[0], Is.EqualTo((0, 0)));
            Assert.That(attemptsSaw[1].node, Is.Zero,
                "Node scratch is transient: a suspended visit loses it across resume.");
            Assert.That(attemptsSaw[1].graph, Is.EqualTo(10),
                "Graph-scoped values ride the user-owned board and survive.");
        });
    }

    [Test]
    public void sync_resume_on_a_fresh_machine_resets_node_scratch()
    {
        BlackboardSchema nodeSchema = NewNodeSchema(out BlackboardKey<int> scratch);
        List<int> attemptsSaw = [];
        bool failFirst = true;

        Graph graph = GraphBuilder
            .StartWith(bb =>
            {
                attemptsSaw.Add(bb.Get(scratch));
                bb.Set(scratch, 5);
                if (failFirst)
                {
                    failFirst = false;
                    return Result.Failure;
                }

                return Result.Success;
            })
            .Retry(maxAttempts: 5)
            .WithSchema(nodeSchema)
            .Build();

        StateMachine machine = graph.ToStateMachine();
        Result step = machine.Execute();
        Assert.That(step, Is.EqualTo(Result.InProgress));

        StateMachineSnapshot snapshot = machine.Suspend();

        StateMachine resumed = graph.ToStateMachine();
        resumed.Resume(snapshot);
        Result result = RunToCompletion(resumed);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(attemptsSaw, Is.All.Zero,
                "Node scratch is transient: a suspended visit loses it across resume.");
        });
    }

    // ── Composites own their own scratch ──────────────────────────────────

    [Test]
    public async Task parallel_regions_do_not_bleed_node_scratch_into_each_other()
    {
        BlackboardSchema schema = NewNodeSchema(out BlackboardKey<int> scratch);
        List<int> regionASaw = [];
        List<int> regionBSaw = [];

        // Interleaved regions: A attempt 1, B attempt 1, A attempt 2, B attempt 2. If the
        // regions shared one board, each second attempt would find the *other* region's marker.
        Graph regionA = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                regionASaw.Add(bb.Get(scratch));
                bb.Set(scratch, 1);
                return regionASaw.Count < 2 ? ResultHelpers.Failure : ResultHelpers.Success;
            })
            .Retry(maxAttempts: 3)
            .WithSchema(schema)
            .Build();

        Graph regionB = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                regionBSaw.Add(bb.Get(scratch));
                bb.Set(scratch, 2);
                return regionBSaw.Count < 2 ? ResultHelpers.Failure : ResultHelpers.Success;
            })
            .Retry(maxAttempts: 3)
            .WithSchema(schema)
            .Build();

        Graph parent = GraphBuilder.Start().Parallel(regionA, regionB).Build();

        Result result = await parent.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(regionASaw, Is.EqualTo(new[] { 0, 1 }), "Region A only ever sees its own scratch.");
            Assert.That(regionBSaw, Is.EqualTo(new[] { 0, 2 }), "Region B only ever sees its own scratch.");
        });
    }

    [Test]
    public async Task subgraph_child_scratch_is_independent_of_the_parent()
    {
        BlackboardSchema parentSchema = NewNodeSchema(out BlackboardKey<int> parentScratch);
        BlackboardSchema childSchema = new("child-scratch", BlackboardScope.Node);
        BlackboardKey<int> childScratch = childSchema.Register<int>("value");
        int childSaw = -1;

        // If the parent's Node slot leaked into the child machine, the child's key (from a
        // different Node schema) would hit the foreign-schema throw instead of its own board.
        Graph child = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(childScratch, 21);
                childSaw = bb.Get(childScratch);
                return ResultHelpers.Success;
            })
            .WithSchema(childSchema)
            .Build();

        Graph parent = GraphBuilder
            .StartWithAsync((bb, _) =>
            {
                bb.Set(parentScratch, 11);
                return ResultHelpers.Success;
            })
            .SubGraph(child)
            .WithSchema(parentSchema)
            .Build();

        Result result = await parent.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(childSaw, Is.EqualTo(21), "The child machine composes its own Node board.");
        });
    }

    // ── Diagnostics ───────────────────────────────────────────────────────

    [Test]
    public void validator_reports_node_schema_with_no_settable_nodes()
    {
        BlackboardSchema schema = NewNodeSchema(out _);

        Graph graph = GraphBuilder
            .StartWithAsync(new RawLogic())
            .WithSchema(schema)
            .Build();

        GraphValidationResult result = graph.Validate();

        Assert.That(result.Diagnostics.Any(d =>
                d.Severity == Severity.Info && d.Message.Contains("Node-scoped")),
            Is.True, "Expected an Info diagnostic about the unused Node schema.");
    }

    [Test]
    public void declaring_two_node_schemas_throws()
    {
        BlackboardSchema first = NewNodeSchema(out _);
        BlackboardSchema second = NewNodeSchema(out _);

        Assert.That(() => GraphBuilder
                .StartWithAsync((_, _) => ResultHelpers.Success)
                .WithSchema(first)
                .WithSchema(second),
            Throws.InvalidOperationException.With.Message.Contain("already been declared"));
    }

    private sealed class RawLogic : IAsyncLogic
    {
        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => ResultHelpers.Success;
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
