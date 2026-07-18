using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Diagnostics.Export;
using NxGraph.Diagnostics.Validations;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxGraph.Tests;

/// <summary>
/// Behavior spec for event entry points (spec 013): typed multi-entry dispatch through
/// <see cref="EventEntryState"/> and the machines' typed raise overloads. An event is a run
/// trigger — one event, one run; delivery goes through the registration's Graph-scoped
/// blackboard key, so payload access, sharing, and durability are the blackboard's existing
/// machinery.
/// </summary>
[TestFixture]
public class EventEntryTests
{
    private sealed record OrderPlaced(string OrderId, decimal Amount);

    private readonly record struct OrderCanceled(string OrderId);

    private sealed record UnregisteredEvent;

    private sealed class ShopSetup
    {
        public required BlackboardSchema Schema;
        public required BlackboardKey<OrderPlaced> Placed;
        public required BlackboardKey<OrderCanceled> Canceled;
        public required Graph Graph;
        public required List<string> Log;
    }

    /// <summary>Async shop graph: two typed entries (class + struct payload) and an optional Otherwise chain.</summary>
    private static ShopSetup AsyncShop(bool withOtherwise = true)
    {
        BlackboardSchema schema = new("shop");
        BlackboardKey<OrderPlaced> placed = schema.Register<OrderPlaced>("orderPlaced");
        BlackboardKey<OrderCanceled> canceled = schema.Register<OrderCanceled>("orderCanceled");
        List<string> log = [];

        EventsToken token = GraphBuilder.StartWithEvents()
            .On(placed, e => e
                .ToAsync((bb, _) =>
                {
                    log.Add($"reserve:{bb.Get(placed).OrderId}"); // payload via Bb.Get
                    return ResultHelpers.Success;
                })
                .ToAsync(placed, (order, _, _) =>
                {
                    log.Add($"charge:{order.Amount}"); // payload via the spec-010 consumer sugar
                    return ResultHelpers.Success;
                })
                .WithOutcome(1, "Placed"))
            .On(canceled, e => e
                .ToAsync(canceled, (order, _, _) =>
                {
                    log.Add($"refund:{order.OrderId}");
                    return ResultHelpers.Success;
                })
                .WithOutcome(2, "Canceled"));

        if (withOtherwise)
        {
            token = token.Otherwise(e => e.ToAsync((_, _) =>
            {
                log.Add("otherwise");
                return ResultHelpers.Success;
            }));
        }

        Graph graph = token.WithSchema(schema).Build();
        return new ShopSetup { Schema = schema, Placed = placed, Canceled = canceled, Graph = graph, Log = log };
    }

    /// <summary>Sync twin of <see cref="AsyncShop"/>.</summary>
    private static ShopSetup SyncShop(bool withOtherwise = true)
    {
        BlackboardSchema schema = new("shop");
        BlackboardKey<OrderPlaced> placed = schema.Register<OrderPlaced>("orderPlaced");
        BlackboardKey<OrderCanceled> canceled = schema.Register<OrderCanceled>("orderCanceled");
        List<string> log = [];

        EventsToken token = GraphBuilder.StartWithEvents()
            .On(placed, e => e
                .To(bb =>
                {
                    log.Add($"reserve:{bb.Get(placed).OrderId}");
                    return Result.Success;
                })
                .To(placed, (order, _) =>
                {
                    log.Add($"charge:{order.Amount}");
                    return Result.Success;
                })
                .WithOutcome(1, "Placed"))
            .On(canceled, e => e
                .To(canceled, (order, _) =>
                {
                    log.Add($"refund:{order.OrderId}");
                    return Result.Success;
                })
                .WithOutcome(2, "Canceled"));

        if (withOtherwise)
        {
            token = token.Otherwise(e => e.To(_ =>
            {
                log.Add("otherwise");
                return Result.Success;
            }));
        }

        Graph graph = token.WithSchema(schema).Build();
        return new ShopSetup { Schema = schema, Placed = placed, Canceled = canceled, Graph = graph, Log = log };
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

    private static Result RaiseToCompletion<TEvent>(StateMachine machine, TEvent evt)
    {
        Result result = machine.Execute(evt);
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        return result;
    }

    // ── Dispatch ─────────────────────────────────────────────────────────

    [Test]
    public async Task async_raise_dispatches_class_payload_to_its_chain()
    {
        ShopSetup shop = AsyncShop();
        AsyncStateMachine machine = shop.Graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        Result result = await machine.ExecuteAsync(new OrderPlaced("o-1", 42m));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(shop.Log, Is.EqualTo(new[] { "reserve:o-1", "charge:42" }));
        });
    }

    [Test]
    public async Task async_raise_dispatches_struct_payload_to_its_chain()
    {
        ShopSetup shop = AsyncShop();
        AsyncStateMachine machine = shop.Graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        Result result = await machine.ExecuteAsync(new OrderCanceled("o-2"));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(shop.Log, Is.EqualTo(new[] { "refund:o-2" }));
        });
    }

    [Test]
    public void sync_raise_dispatches_both_payload_kinds()
    {
        ShopSetup shop = SyncShop();
        StateMachine machine = shop.Graph.ToStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        Result placed = RaiseToCompletion(machine, new OrderPlaced("o-3", 7m));
        Result canceled = RaiseToCompletion(machine, new OrderCanceled("o-3"));

        Assert.Multiple(() =>
        {
            Assert.That(placed, Is.EqualTo(Result.Success));
            Assert.That(canceled, Is.EqualTo(Result.Success));
            Assert.That(shop.Log, Is.EqualTo(new[] { "reserve:o-3", "charge:7", "refund:o-3" }));
        });
    }

    [Test]
    public async Task async_last_outcome_reports_the_entry_taken()
    {
        ShopSetup shop = AsyncShop();
        AsyncStateMachine machine = shop.Graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(shop.Schema));
        machine.SetRestartPolicy(RestartPolicy.Manual);

        await machine.ExecuteAsync(new OrderPlaced("o-4", 1m));

        Assert.Multiple(() =>
        {
            Assert.That(machine.LastOutcome, Is.EqualTo(1));
            Assert.That(machine.LastOutcomeName, Is.EqualTo("Placed"));
        });
    }

    [Test]
    public void sync_last_outcome_reports_the_entry_taken()
    {
        ShopSetup shop = SyncShop();
        StateMachine machine = shop.Graph.ToStateMachine().WithBlackboard(new Blackboard(shop.Schema));
        machine.SetRestartPolicy(RestartPolicy.Manual);

        RaiseToCompletion(machine, new OrderCanceled("o-5"));

        Assert.Multiple(() =>
        {
            Assert.That(machine.LastOutcome, Is.EqualTo(2));
            Assert.That(machine.LastOutcomeName, Is.EqualTo("Canceled"));
        });
    }

    // ── Misses, Otherwise, staleness ─────────────────────────────────────

    [Test]
    public void async_unregistered_event_type_throws_naming_registered_types()
    {
        ShopSetup shop = AsyncShop();
        AsyncStateMachine machine = shop.Graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync(new UnregisteredEvent()));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("No event entry is registered"));
            Assert.That(ex.Message, Does.Contain("OrderPlaced"));
            Assert.That(ex.Message, Does.Contain("OrderCanceled"));
        });
    }

    [Test]
    public void sync_unregistered_event_type_throws_naming_registered_types()
    {
        ShopSetup shop = SyncShop();
        StateMachine machine = shop.Graph.ToStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
            () => machine.Execute(new UnregisteredEvent()));
        Assert.That(ex!.Message, Does.Contain("OrderPlaced").And.Contain("OrderCanceled"));
    }

    [Test]
    public async Task async_plain_execute_routes_to_otherwise()
    {
        ShopSetup shop = AsyncShop();
        AsyncStateMachine machine = shop.Graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(shop.Log, Is.EqualTo(new[] { "otherwise" }));
        });
    }

    [Test]
    public void async_plain_execute_without_otherwise_throws_pointing_at_the_raise_api()
    {
        ShopSetup shop = AsyncShop(withOtherwise: false);
        AsyncStateMachine machine = shop.Graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync());
        Assert.That(ex!.Message, Does.Contain("ExecuteAsync<TEvent>"));
    }

    [Test]
    public void sync_plain_execute_without_otherwise_throws_pointing_at_the_raise_api()
    {
        ShopSetup shop = SyncShop(withOtherwise: false);
        StateMachine machine = shop.Graph.ToStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => machine.Execute());
        Assert.That(ex!.Message, Does.Contain("Execute<TEvent>"));
    }

    [Test]
    public void sync_plain_execute_routes_to_otherwise()
    {
        ShopSetup shop = SyncShop();
        StateMachine machine = shop.Graph.ToStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        Result result = RunToCompletion(machine);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(shop.Log, Is.EqualTo(new[] { "otherwise" }));
        });
    }

    [Test]
    public async Task async_stale_entry_never_leaks_into_a_later_plain_run()
    {
        ShopSetup shop = AsyncShop();
        AsyncStateMachine machine = shop.Graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        await machine.ExecuteAsync(new OrderPlaced("o-6", 5m));
        await machine.ExecuteAsync(); // plain run: must route to Otherwise, not the placed chain

        Assert.That(shop.Log, Is.EqualTo(new[] { "reserve:o-6", "charge:5", "otherwise" }));
    }

    [Test]
    public void sync_stale_entry_never_leaks_into_a_later_plain_run()
    {
        ShopSetup shop = SyncShop();
        StateMachine machine = shop.Graph.ToStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        RaiseToCompletion(machine, new OrderCanceled("o-7"));
        RunToCompletion(machine);

        Assert.That(shop.Log, Is.EqualTo(new[] { "refund:o-7", "otherwise" }));
    }

    // ── Restart policies and busy guards ─────────────────────────────────

    [Test]
    public async Task async_repeated_raises_under_auto_policy_each_start_a_fresh_run()
    {
        ShopSetup shop = AsyncShop();
        AsyncStateMachine machine = shop.Graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        await machine.ExecuteAsync(new OrderPlaced("a", 1m));
        await machine.ExecuteAsync(new OrderCanceled("a"));
        await machine.ExecuteAsync(new OrderPlaced("b", 2m));

        Assert.That(shop.Log, Is.EqualTo(new[]
        {
            "reserve:a", "charge:1", "refund:a", "reserve:b", "charge:2",
        }));
    }

    [Test]
    public async Task async_manual_policy_post_terminal_raise_throws_until_reset()
    {
        ShopSetup shop = AsyncShop();
        AsyncStateMachine machine = shop.Graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(shop.Schema));
        machine.SetRestartPolicy(RestartPolicy.Manual);

        await machine.ExecuteAsync(new OrderPlaced("o-8", 3m));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync(new OrderCanceled("o-8")));
        Assert.That(ex!.Message, Does.Contain("Reset()"));

        await machine.Reset();
        Result result = await machine.ExecuteAsync(new OrderCanceled("o-8"));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(shop.Log, Is.EqualTo(new[] { "reserve:o-8", "charge:3", "refund:o-8" }));
        });
    }

    [Test]
    public void sync_manual_policy_post_terminal_raise_throws_until_reset()
    {
        ShopSetup shop = SyncShop();
        StateMachine machine = shop.Graph.ToStateMachine().WithBlackboard(new Blackboard(shop.Schema));
        machine.SetRestartPolicy(RestartPolicy.Manual);

        RaiseToCompletion(machine, new OrderPlaced("o-9", 4m));

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
            () => machine.Execute(new OrderCanceled("o-9")));
        Assert.That(ex!.Message, Does.Contain("Reset()"));

        machine.Reset();
        Result result = RaiseToCompletion(machine, new OrderCanceled("o-9"));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(shop.Log, Is.EqualTo(new[] { "reserve:o-9", "charge:4", "refund:o-9" }));
        });
    }

    [Test]
    public async Task async_raise_while_running_throws()
    {
        ShopSetup shop = AsyncShop();
        AsyncStateMachine machine = shop.Graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        Result first = await machine.StepAsync(new OrderPlaced("o-10", 6m));
        Assert.That(first, Is.EqualTo(Result.InProgress));

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync(new OrderCanceled("o-10")));
        Assert.That(ex!.Message, Does.Contain("Cannot raise an event while the machine is executing"));

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = await machine.StepAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(shop.Log, Is.EqualTo(new[] { "reserve:o-10", "charge:6" }));
        });
    }

    [Test]
    public void sync_raise_mid_run_throws()
    {
        ShopSetup shop = SyncShop();
        StateMachine machine = shop.Graph.ToStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        Result first = machine.Execute(new OrderPlaced("o-11", 8m));
        Assert.That(first, Is.EqualTo(Result.InProgress));

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
            () => machine.Execute(new OrderCanceled("o-11")));
        Assert.That(ex!.Message, Does.Contain("Cannot raise an event while the machine is executing"));

        Result result = RunToCompletion(machine);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(shop.Log, Is.EqualTo(new[] { "reserve:o-11", "charge:8" }));
        });
    }

    [Test]
    public async Task async_typed_step_starts_a_run_and_mid_run_typed_step_throws()
    {
        ShopSetup shop = AsyncShop();
        AsyncStateMachine machine = shop.Graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(shop.Schema));

        Result first = await machine.StepAsync(new OrderCanceled("o-12"));
        Assert.That(first, Is.EqualTo(Result.InProgress), "First typed step executes the dispatcher.");

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.StepAsync(new OrderPlaced("o-12", 9m)));
        Assert.That(ex!.Message, Does.Contain("stepped run is in progress"));

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = await machine.StepAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(shop.Log, Is.EqualTo(new[] { "refund:o-12" }));
        });
    }

    [Test]
    public void async_raise_on_a_graph_without_event_entries_throws()
    {
        Graph graph = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success).Build();
        AsyncStateMachine machine = graph.ToAsyncStateMachine();

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync(new OrderPlaced("x", 0m)));
        Assert.That(ex!.Message, Does.Contain("no event entries"));
    }

    [Test]
    public void sync_raise_on_a_graph_without_event_entries_throws()
    {
        Graph graph = GraphBuilder.StartWith(() => Result.Success).Build();
        StateMachine machine = graph.ToStateMachine();

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
            () => machine.Execute(new OrderPlaced("x", 0m)));
        Assert.That(ex!.Message, Does.Contain("no event entries"));
    }

    [Test]
    public void async_raise_without_a_graph_board_hits_the_unbound_scope_routing_throw()
    {
        ShopSetup shop = AsyncShop();
        AsyncStateMachine machine = shop.Graph.ToAsyncStateMachine(); // no WithBlackboard

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await machine.ExecuteAsync(new OrderPlaced("o-13", 1m)));
        Assert.That(ex!.Message, Does.Contain("no graph blackboard bound"));
    }

    [Test]
    public void sync_raise_without_a_graph_board_hits_the_unbound_scope_routing_throw()
    {
        ShopSetup shop = SyncShop();
        StateMachine machine = shop.Graph.ToStateMachine(); // no WithBlackboard

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
            () => machine.Execute(new OrderCanceled("o-13")));
        Assert.That(ex!.Message, Does.Contain("no graph blackboard bound"));
    }

    // ── Sharing: one graph, several machines ─────────────────────────────

    [Test]
    public async Task two_machines_share_one_graph_with_distinct_boards_and_events()
    {
        BlackboardSchema schema = new("shared-shop");
        BlackboardKey<OrderPlaced> placed = schema.Register<OrderPlaced>("orderPlaced");
        BlackboardKey<OrderCanceled> canceled = schema.Register<OrderCanceled>("orderCanceled");
        BlackboardKey<string> handled = schema.Register<string>("handled", string.Empty);

        Graph graph = GraphBuilder.StartWithEvents()
            .On(placed, e => e.ToAsync((bb, _) =>
            {
                bb.Set(handled, $"placed:{bb.Get(placed).OrderId}");
                return ResultHelpers.Success;
            }))
            .On(canceled, e => e.ToAsync((bb, _) =>
            {
                bb.Set(handled, $"canceled:{bb.Get(canceled).OrderId}");
                return ResultHelpers.Success;
            }))
            .WithSchema(schema)
            .Build();

        Blackboard boardA = new(schema);
        Blackboard boardB = new(schema);
        AsyncStateMachine machineA = graph.ToAsyncStateMachine().WithBlackboard(boardA);
        AsyncStateMachine machineB = graph.ToAsyncStateMachine().WithBlackboard(boardB);

        await machineA.ExecuteAsync(new OrderPlaced("a-1", 10m));
        await machineB.ExecuteAsync(new OrderCanceled("b-1"));
        await machineA.ExecuteAsync(new OrderCanceled("a-2"));

        Assert.Multiple(() =>
        {
            Assert.That(boardA.Get(handled), Is.EqualTo("canceled:a-2"));
            Assert.That(boardB.Get(handled), Is.EqualTo("canceled:b-1"));
            Assert.That(boardA.Get(placed), Is.EqualTo(new OrderPlaced("a-1", 10m)),
                "Each machine's payload landed on its own board.");
        });
    }

    // ── Entry chains use the full DSL vocabulary ─────────────────────────

    [Test]
    public async Task entry_chains_support_retry_onerror_and_convergent_nodes()
    {
        BlackboardSchema schema = new("faulty-shop");
        BlackboardKey<OrderPlaced> placed = schema.Register<OrderPlaced>("orderPlaced");
        BlackboardKey<OrderCanceled> canceled = schema.Register<OrderCanceled>("orderCanceled");
        List<string> log = [];

        int attempts = 0;
        RelayState sharedFinish = new(() =>
        {
            log.Add("finish");
            return Result.Success;
        });

        Graph graph = GraphBuilder.StartWithEvents()
            .On(placed, e => e
                .To(() =>
                {
                    attempts++;
                    log.Add($"try:{attempts}");
                    return attempts < 3 ? Result.Failure : Result.Success;
                })
                .Retry(3)
                .OnError(() =>
                {
                    log.Add("handler");
                    return Result.Success;
                })
                .To(sharedFinish))
            .On(canceled, e => e
                .To(() =>
                {
                    log.Add("cancel");
                    return Result.Success;
                })
                .To(sharedFinish)) // convergence: both chains end on the same node
            .WithSchema(schema)
            .Build();

        AsyncStateMachine machine = graph.ToAsyncStateMachine().WithBlackboard(new Blackboard(schema));

        await machine.ExecuteAsync(new OrderPlaced("o-14", 1m));
        await machine.ExecuteAsync(new OrderCanceled("o-14"));

        Assert.That(log, Is.EqualTo(new[]
        {
            "try:1", "try:2", "try:3", "finish", "cancel", "finish",
        }), "The failing step retried in place, then both entries converged on the shared node.");
    }

    // ── Durability: suspend mid-handler, resume on a fresh machine ───────

    [Test]
    public async Task async_suspend_mid_handler_resumes_on_a_fresh_machine_and_reads_the_event()
    {
        ShopSetup shop = AsyncShop();
        Blackboard board = new(shop.Schema);
        AsyncStateMachine first = shop.Graph.ToAsyncStateMachine().WithBlackboard(board);

        Result step1 = await first.StepAsync(new OrderPlaced("o-15", 30m)); // dispatcher
        Result step2 = await first.StepAsync(); // reserve step
        Assert.Multiple(() =>
        {
            Assert.That(step1, Is.EqualTo(Result.InProgress));
            Assert.That(step2, Is.EqualTo(Result.InProgress));
        });

        StateMachineSnapshot snapshot = first.Suspend();

        AsyncStateMachine second = shop.Graph.ToAsyncStateMachine().WithBlackboard(board);
        second.Resume(snapshot);

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = await second.StepAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(shop.Log, Does.Contain("charge:30"),
                "The handler's later step read the event payload from the restored board.");
        });
    }

    [Test]
    public void sync_suspend_mid_handler_resumes_on_a_fresh_machine_and_reads_the_event()
    {
        ShopSetup shop = SyncShop();
        Blackboard board = new(shop.Schema);
        StateMachine first = shop.Graph.ToStateMachine().WithBlackboard(board);

        Result step1 = first.Execute(new OrderPlaced("o-16", 60m)); // dispatcher tick
        Result step2 = first.Execute(); // reserve tick
        Assert.Multiple(() =>
        {
            Assert.That(step1, Is.EqualTo(Result.InProgress));
            Assert.That(step2, Is.EqualTo(Result.InProgress));
        });

        StateMachineSnapshot snapshot = first.Suspend();

        StateMachine second = shop.Graph.ToStateMachine().WithBlackboard(board);
        second.Resume(snapshot);

        Result result = RunToCompletion(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(shop.Log, Does.Contain("charge:60"));
        });
    }

    // ── Builder / wiring rejections ──────────────────────────────────────

    [Test]
    public void duplicate_clr_event_type_throws_at_wiring_time()
    {
        BlackboardSchema schema = new("dup-type");
        BlackboardKey<OrderPlaced> first = schema.Register<OrderPlaced>("first");
        BlackboardKey<OrderPlaced> second = schema.Register<OrderPlaced>("second");

        ArgumentException? ex = Assert.Throws<ArgumentException>(() => GraphBuilder.StartWithEvents()
            .On(first, e => e.To(() => Result.Success))
            .On(second, e => e.To(() => Result.Success)));
        Assert.That(ex!.Message, Does.Contain("one entry per type"));
    }

    [Test]
    public void duplicate_event_key_throws_at_wiring_time()
    {
        BlackboardSchema schema = new("dup-key");
        BlackboardKey<OrderPlaced> placed = schema.Register<OrderPlaced>("orderPlaced");

        ArgumentException? ex = Assert.Throws<ArgumentException>(() => GraphBuilder.StartWithEvents()
            .On(placed, e => e.To(() => Result.Success))
            .On(placed, e => e.To(() => Result.Success)));
        Assert.That(ex!.Message, Does.Contain("already bound to an entry"));
    }

    [Test]
    public void node_scoped_event_key_throws_at_wiring_time()
    {
        BlackboardSchema schema = new("scratch", BlackboardScope.Node);
        BlackboardKey<OrderPlaced> placed = schema.Register<OrderPlaced>("orderPlaced");

        ArgumentException? ex = Assert.Throws<ArgumentException>(() => GraphBuilder.StartWithEvents()
            .On(placed, e => e.To(() => Result.Success)));
        Assert.That(ex!.Message, Does.Contain("Node-scoped"));
    }

    [Test]
    public void schema_less_event_key_throws_at_wiring_time()
    {
        ArgumentException? ex = Assert.Throws<ArgumentException>(() => GraphBuilder.StartWithEvents()
            .On(default(BlackboardKey<OrderPlaced>), e => e.To(() => Result.Success)));
        Assert.That(ex!.Message, Does.Contain("obtain keys via BlackboardSchema.Register"));
    }

    [Test]
    public void build_with_zero_registrations_throws()
    {
        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
            () => GraphBuilder.StartWithEvents().Build());
        Assert.That(ex!.Message, Does.Contain("at least one event entry"));
    }

    [Test]
    public void second_otherwise_chain_throws()
    {
        BlackboardSchema schema = new("dup-otherwise");
        BlackboardKey<OrderPlaced> placed = schema.Register<OrderPlaced>("orderPlaced");

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => GraphBuilder
            .StartWithEvents()
            .On(placed, e => e.To(() => Result.Success))
            .Otherwise(e => e.To(() => Result.Success))
            .Otherwise(e => e.To(() => Result.Success)));
        Assert.That(ex!.Message, Does.Contain("Otherwise chain has already been declared"));
    }

    // ── Validator lints ──────────────────────────────────────────────────

    [Test]
    public void validator_reports_event_entry_info_and_reaches_all_chains()
    {
        ShopSetup shop = AsyncShop();
        GraphValidationResult report = shop.Graph.Validate();

        Assert.Multiple(() =>
        {
            Assert.That(report.HasErrors, Is.False);
            Assert.That(report.Diagnostics.Any(d =>
                    d.Severity == Severity.Info && d.Message.Contains("event entry")),
                Is.True, "The presence Info fires for event graphs.");
            Assert.That(report.Diagnostics.Any(d =>
                    d.Severity == Severity.Warning && d.Message.Contains("unreachable")),
                Is.False, "All entry chains are reachable through the dispatcher's static targets.");
        });
    }

    [Test]
    public void validator_warns_when_event_graph_declares_no_graph_schema()
    {
        BlackboardSchema schema = new("undeclared");
        BlackboardKey<OrderPlaced> placed = schema.Register<OrderPlaced>("orderPlaced");

        Graph graph = GraphBuilder.StartWithEvents()
            .On(placed, e => e.To(() => Result.Success))
            .Build(); // no WithSchema

        GraphValidationResult report = graph.Validate();
        Assert.That(report.Diagnostics.Any(d =>
                d.Severity == Severity.Warning && d.Message.Contains("declares no Graph-scoped blackboard schema")),
            Is.True, "The missing-schema Warning fires.");
    }

    [Test]
    public void validator_warns_when_event_key_schema_differs_from_declared_graph_schema()
    {
        BlackboardSchema keySchema = new("keys");
        BlackboardKey<OrderPlaced> placed = keySchema.Register<OrderPlaced>("orderPlaced");
        BlackboardSchema declared = new("declared");
        declared.Register<int>("unrelated");

        Graph graph = GraphBuilder.StartWithEvents()
            .On(placed, e => e.To(() => Result.Success))
            .WithSchema(declared)
            .Build();

        GraphValidationResult report = graph.Validate();
        Assert.That(report.Diagnostics.Any(d =>
                d.Severity == Severity.Warning && d.Message.Contains("not the graph's declared")),
            Is.True, "The foreign-key-schema Warning fires.");
    }

    [Test]
    public void validator_warns_when_event_entry_is_combined_with_token_nodes()
    {
        BlackboardSchema schema = new("mixed");
        BlackboardKey<OrderPlaced> placed = schema.Register<OrderPlaced>("orderPlaced");
        JoinState join = new(JoinPolicy.Any);

        Graph graph = GraphBuilder.StartWithEvents()
            .On(placed, e => e
                .To(() => Result.Success)
                .To((ILogic)join))
            .WithSchema(schema)
            .Build();

        GraphValidationResult report = graph.Validate();
        Assert.That(report.Diagnostics.Any(d =>
                d.Severity == Severity.Warning && d.Message.Contains("token fork/join")),
            Is.True, "The token-mix Warning fires — event dispatch under the token runtime is unvalidated.");
    }

    // ── Mermaid export ───────────────────────────────────────────────────

    [Test]
    public void mermaid_labels_entry_edges_with_event_short_names_and_the_otherwise_edge()
    {
        ShopSetup shop = AsyncShop();
        string mermaid = shop.Graph.ToMermaid();

        Assert.Multiple(() =>
        {
            Assert.That(mermaid, Does.Contain("-. OrderPlaced .->"));
            Assert.That(mermaid, Does.Contain("-. OrderCanceled .->"));
            Assert.That(mermaid, Does.Contain("-. otherwise .->"));
        });
    }
}
