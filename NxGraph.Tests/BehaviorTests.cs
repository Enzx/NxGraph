using NxGraph.Authoring;
using NxGraph.Behaviors;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// Behavior composition (spec 014): declarative, reusable, blackboard-bound sequences inside
/// one node. Codifies the contract: in-order fail-fast execution under the node's fault
/// model, the binding primitive across all scopes, the report channel, runtime parity of the
/// dual-interface standard set, and the agent-typed channel (agent as call parameter, never
/// stamped onto behaviors).
/// </summary>
[TestFixture]
[Category("behaviors")]
public class BehaviorTests
{
    // ── Test doubles ─────────────────────────────────────────────────────

    /// <summary>Dual-interface probe: appends its id to a shared trace, returns a fixed result.</summary>
    private sealed class TraceBehavior(string id, List<string> trace, Func<Result>? result = null)
        : IBehavior, IAsyncBehavior
    {
        public Result Execute(in BehaviorContext ctx)
        {
            trace.Add(id);
            return result?.Invoke() ?? Result.Success;
        }

        public ValueTask<Result> ExecuteAsync(BehaviorContext ctx, CancellationToken ct)
        {
            trace.Add(id);
            return new ValueTask<Result>(result?.Invoke() ?? Result.Success);
        }
    }

    /// <summary>Dual-interface probe: resolves a binding and records the value.</summary>
    private sealed class CaptureBehavior<T>(BlackboardValue<T> value, List<T?> captured)
        : IBehavior, IAsyncBehavior
    {
        public Result Execute(in BehaviorContext ctx)
        {
            captured.Add(ctx.Resolve(value));
            return Result.Success;
        }

        public ValueTask<Result> ExecuteAsync(BehaviorContext ctx, CancellationToken ct)
        {
            captured.Add(ctx.Resolve(value));
            return ResultHelpers.Success;
        }
    }

    private sealed class ThrowingBehavior : IBehavior, IAsyncBehavior
    {
        public Result Execute(in BehaviorContext ctx) => throw new InvalidOperationException("boom");

        public ValueTask<Result> ExecuteAsync(BehaviorContext ctx, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class Hero
    {
        public required string Name;
        public List<string> Visits = [];
    }

    private sealed class Villain;

    /// <summary>Agent-typed dual-interface probe: records which agent instance it received.</summary>
    private sealed class GreetHero : IBehavior<Hero>, IAsyncBehavior<Hero>
    {
        public Result Execute(Hero agent, in BehaviorContext ctx)
        {
            agent.Visits.Add($"greet:{agent.Name}");
            return Result.Success;
        }

        public ValueTask<Result> ExecuteAsync(Hero agent, BehaviorContext ctx, CancellationToken ct)
        {
            agent.Visits.Add($"greet:{agent.Name}");
            return ResultHelpers.Success;
        }
    }

    private sealed class VillainOnly : IBehavior<Villain>
    {
        public Result Execute(Villain agent, in BehaviorContext ctx) => Result.Success;
    }

    private sealed class LogCapturingAsyncObserver : IAsyncStateMachineObserver
    {
        public readonly List<string> Messages = [];

        public ValueTask OnLogReport(NodeId nodeId, string message, CancellationToken ct)
        {
            Messages.Add(message);
            return default;
        }
    }

    private sealed class LogCapturingSyncObserver : IStateMachineObserver
    {
        public readonly List<string> Messages = [];

        void IStateMachineObserver.OnLogReport(NodeId nodeId, string message) => Messages.Add(message);
    }

    private static Result RunToEnd(StateMachine machine)
    {
        Result result = machine.Execute();
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        return result;
    }

    // ── Sequence semantics ───────────────────────────────────────────────

    [Test]
    public async Task Async_sequence_runs_in_order()
    {
        List<string> trace = [];
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(
                new TraceBehavior("a", trace),
                new TraceBehavior("b", trace),
                new TraceBehavior("c", trace))
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "a", "b", "c" }));
        });
    }

    [Test]
    public void Sync_sequence_runs_in_order()
    {
        List<string> trace = [];
        Graph graph = GraphBuilder.Start()
            .ToBehaviors(
                new TraceBehavior("a", trace),
                new TraceBehavior("b", trace),
                new TraceBehavior("c", trace))
            .Build();

        Result result = RunToEnd(graph.ToStateMachine());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "a", "b", "c" }));
        });
    }

    [Test]
    public async Task Fail_fast_stops_the_sequence_and_fails_the_node([Values] bool sync)
    {
        List<string> trace = [];
        TraceBehavior[] behaviors =
        [
            new("a", trace),
            new("fail", trace, () => Result.Failure),
            new("never", trace),
        ];

        Graph graph = sync
            ? GraphBuilder.Start().ToBehaviors(behaviors).Build()
            : GraphBuilder.Start().ToBehaviorsAsync(behaviors).Build();

        Result result = sync
            ? RunToEnd(graph.ToStateMachine())
            : await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(trace, Is.EqualTo(new[] { "a", "fail" }),
                "Entries after the first failure must not run — fail-fast, the opposite of AllState.");
        });
    }

    [Test]
    public async Task Node_retry_reruns_the_whole_list()
    {
        List<string> trace = [];
        int failuresLeft = 1;
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(
                new TraceBehavior("a", trace),
                new TraceBehavior("flaky", trace, () => failuresLeft-- > 0 ? Result.Failure : Result.Success),
                new TraceBehavior("b", trace))
            .Retry(maxAttempts: 2)
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "a", "flaky", "a", "flaky", "b" }),
                "The retry re-runs the whole sequence in place, not just the failed entry.");
        });
    }

    [Test]
    public async Task OnError_reroutes_a_failed_behavior_node()
    {
        List<string> trace = [];
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(new TraceBehavior("fail", trace, () => Result.Failure))
            .OnErrorAsync(_ =>
            {
                trace.Add("handler");
                return ResultHelpers.Success;
            })
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "fail", "handler" }));
        });
    }

    [Test]
    public async Task WithOutcome_codes_a_terminal_behavior_node()
    {
        List<string> trace = [];
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(new TraceBehavior("done", trace))
            .WithOutcome(7, "Done")
            .Build();

        AsyncStateMachine machine = graph.ToAsyncStateMachine();
        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(machine.LastOutcome, Is.EqualTo(7));
            Assert.That(machine.LastOutcomeName, Is.EqualTo("Done"));
        });
    }

    [Test]
    public void Behavior_exceptions_propagate_as_from_any_node_logic([Values] bool sync)
    {
        Graph graph = sync
            ? GraphBuilder.Start().ToBehaviors(new ThrowingBehavior()).Build()
            : GraphBuilder.Start().ToBehaviorsAsync(new ThrowingBehavior()).Build();

        if (sync)
        {
            InvalidOperationException? ex =
                Assert.Throws<InvalidOperationException>(() => graph.ToStateMachine().Execute());
            Assert.That(ex!.Message, Is.EqualTo("boom"));
        }
        else
        {
            InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await graph.ToAsyncStateMachine().ExecuteAsync());
            Assert.That(ex!.Message, Is.EqualTo("boom"));
        }
    }

    // ── BlackboardValue bindings across scopes ───────────────────────────

    [Test]
    public async Task Literal_and_key_bindings_resolve_across_all_three_scopes()
    {
        BlackboardSchema globalSchema = new("world", BlackboardScope.Global);
        BlackboardKey<string> globalKey = globalSchema.Register("motd", "hello-global");
        BlackboardSchema graphSchema = new("entity");
        BlackboardKey<int> graphKey = graphSchema.Register("score", 41);
        BlackboardSchema nodeSchema = new("scratch", BlackboardScope.Node);
        BlackboardKey<double> nodeKey = nodeSchema.Register("temp", 0d);

        List<string?> strings = [];
        List<int> ints = [];
        List<double> doubles = [];

        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(
                new SetValue<int>(graphKey, 42),
                new SetValue<double>(nodeKey, 1.5), // Node scratch: written and read within one visit
                new CaptureBehavior<string>(globalKey, strings),
                new CaptureBehavior<int>(graphKey, ints),
                new CaptureBehavior<double>(nodeKey, doubles),
                new CaptureBehavior<string>("a literal", strings))
            .WithSchema(globalSchema)
            .WithSchema(graphSchema)
            .WithSchema(nodeSchema)
            .Build();

        AsyncStateMachine machine = graph.ToAsyncStateMachine()
            .WithBlackboard(new Blackboard(globalSchema))
            .WithBlackboard(new Blackboard(graphSchema));

        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(strings, Is.EqualTo(new[] { "hello-global", "a literal" }));
            Assert.That(ints, Is.EqualTo(new[] { 42 }), "Key binding reads the same-visit write.");
            Assert.That(doubles, Is.EqualTo(new[] { 1.5 }),
                "Node-scoped bindings are legal — behavior bindings resolve within one visit.");
        });
    }

    [Test]
    public void Default_blackboard_value_is_a_literal_default()
    {
        BlackboardValue<int> value = default;
        Assert.Multiple(() =>
        {
            Assert.That(value.IsBound, Is.False);
            Assert.That(value.Literal, Is.Zero);
        });
    }

    [Test]
    public void Invalid_key_is_rejected_at_wiring_time()
    {
        Assert.Throws<ArgumentException>(() => _ = (BlackboardValue<int>)default(BlackboardKey<int>));
    }

    // ── Log and the report channel ───────────────────────────────────────

    [Test]
    public async Task Log_reaches_the_async_observer_with_the_severity_prefix()
    {
        BlackboardSchema schema = new("logs");
        BlackboardKey<string> messageKey = schema.Register("msg", "from-key");

        LogCapturingAsyncObserver observer = new();
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(
                new Log("plain info"),
                new Log(LogSeverity.Error, "bad thing"),
                new Log(LogSeverity.Warning, messageKey))
            .WithSchema(schema)
            .Build();

        Result result = await graph.ToAsyncStateMachine(observer)
            .WithBlackboard(new Blackboard(schema))
            .ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observer.Messages, Is.EqualTo(new[]
            {
                "[Info] plain info", "[Error] bad thing", "[Warning] from-key",
            }));
        });
    }

    [Test]
    public void Log_reaches_the_sync_observer_with_the_severity_prefix()
    {
        LogCapturingSyncObserver observer = new();
        Graph graph = GraphBuilder.Start()
            .ToBehaviors(new Log("hello"), new Log(LogSeverity.Trace, "detail"))
            .Build();

        Result result = RunToEnd(graph.ToStateMachine(observer));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observer.Messages, Is.EqualTo(new[] { "[Info] hello", "[Trace] detail" }));
        });
    }

    [Test]
    public async Task Observer_less_log_runs_clean([Values] bool sync)
    {
        Graph graph = sync
            ? GraphBuilder.Start().ToBehaviors(new Log("nobody listens")).Build()
            : GraphBuilder.Start().ToBehaviorsAsync(new Log("nobody listens")).Build();

        Result result = sync
            ? RunToEnd(graph.ToStateMachine())
            : await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Success));
    }

    // ── SetValue ─────────────────────────────────────────────────────────

    [Test]
    public async Task SetValue_writes_value_and_reference_types([Values] bool sync)
    {
        BlackboardSchema schema = new("data");
        BlackboardKey<int> countKey = schema.Register("count", 0);
        BlackboardKey<string> nameKey = schema.Register<string>("name");
        BlackboardKey<int> sourceKey = schema.Register("source", 9);

        SetValue<int> setCount = new(countKey, sourceKey); // key-to-key copy
        SetValue<string> setName = new(nameKey, "renamed");

        Graph graph = sync
            ? GraphBuilder.Start().ToBehaviors(setCount, setName).WithSchema(schema).Build()
            : GraphBuilder.Start().ToBehaviorsAsync(setCount, setName).WithSchema(schema).Build();

        Blackboard board = new(schema);
        Result result = sync
            ? RunToEnd(graph.ToStateMachine().WithBlackboard(board))
            : await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(countKey), Is.EqualTo(9));
            Assert.That(board.Get(nameKey), Is.EqualTo("renamed"));
        });
    }

    // ── Runtime parity ───────────────────────────────────────────────────

    [Test]
    public async Task Dual_interface_standard_set_behaves_identically_under_both_runtimes()
    {
        BlackboardSchema schema = new("parity");
        BlackboardKey<int> key = schema.Register("value", 0);

        // The very same behavior instances author both graphs.
        SetValue<int> set = new(key, 5);
        Log log = new(LogSeverity.Info, "parity");

        Graph syncGraph = GraphBuilder.Start().ToBehaviors(set, log).WithSchema(schema).Build();
        Graph asyncGraph = GraphBuilder.Start().ToBehaviorsAsync(set, log).WithSchema(schema).Build();

        LogCapturingSyncObserver syncObserver = new();
        Blackboard syncBoard = new(schema);
        Result syncResult = RunToEnd(syncGraph.ToStateMachine(syncObserver).WithBlackboard(syncBoard));

        LogCapturingAsyncObserver asyncObserver = new();
        Blackboard asyncBoard = new(schema);
        Result asyncResult = await asyncGraph.ToAsyncStateMachine(asyncObserver)
            .WithBlackboard(asyncBoard).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(syncResult, Is.EqualTo(asyncResult));
            Assert.That(syncBoard.Get(key), Is.EqualTo(asyncBoard.Get(key)));
            Assert.That(syncObserver.Messages, Is.EqualTo(asyncObserver.Messages));
        });
    }

    [Test]
    public async Task Sync_behavior_state_runs_under_the_async_machine_via_the_adapter()
    {
        List<string> trace = [];
        LogCapturingAsyncObserver observer = new();
        Graph graph = GraphBuilder.Start()
            .ToBehaviors(new TraceBehavior("sync-under-async", trace), new Log("adapted"))
            .Build();

        Result result = await graph.ToAsyncStateMachine(observer).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "sync-under-async" }));
            Assert.That(observer.Messages, Is.EqualTo(new[] { "[Info] adapted" }),
                "Reports from a sync composite behind the adapter reach the async observer.");
        });
    }

    // ── Wiring-time rejections ───────────────────────────────────────────

    [Test]
    public void Empty_and_null_entries_are_rejected()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => _ = new BehaviorState());
            Assert.Throws<ArgumentException>(() => _ = new AsyncBehaviorState());
            Assert.Throws<ArgumentException>(() => _ = new BehaviorState(new Log("a"), null!));
            Assert.Throws<ArgumentException>(() => _ = new AsyncBehaviorState(new Log("a"), null!));
            Assert.Throws<ArgumentException>(() => _ = new BehaviorState<Hero>());
            Assert.Throws<ArgumentException>(() => _ = new AsyncBehaviorState<Hero>());
        });
    }

    [Test]
    public void Untyped_composites_reject_agent_typed_entries_pointing_at_the_typed_dsl()
    {
        ArgumentException? syncEx =
            Assert.Throws<ArgumentException>(() => _ = new BehaviorState(new GreetHero()));
        ArgumentException? asyncEx =
            Assert.Throws<ArgumentException>(() => _ = new AsyncBehaviorState(new GreetHero()));

        Assert.Multiple(() =>
        {
            Assert.That(syncEx!.Message, Does.Contain("ToBehaviors<TAgent>").And.Contain("GreetHero"));
            Assert.That(asyncEx!.Message, Does.Contain("ToBehaviorsAsync<TAgent>").And.Contain("GreetHero"));
        });
    }

    [Test]
    public void Typed_composite_rejects_wrong_agent_type_naming_both_types()
    {
        ArgumentException? ex =
            Assert.Throws<ArgumentException>(() => _ = new BehaviorState<Hero>(new VillainOnly()));

        Assert.That(ex!.Message, Does.Contain("Villain").And.Contain("Hero"));
    }

    [Test]
    public void Dim_guard_throws_when_the_untyped_execute_of_a_typed_behavior_is_invoked()
    {
        IBehavior behavior = new GreetHero();
        BehaviorContext ctx = default;

        NotSupportedException? ex = Assert.Throws<NotSupportedException>(() => behavior.Execute(in ctx));
        Assert.That(ex!.Message, Does.Contain("GreetHero").And.Contain("agent-typed"));
    }

    // ── DSL surface matrix ───────────────────────────────────────────────

    [Test]
    public void Sync_dsl_matrix_covers_all_four_surfaces()
    {
        List<string> trace = [];
        bool takeThen = true;
        // ReSharper disable once AccessToModifiedClosure
        Graph graph = GraphBuilder.Start()
            .ToBehaviors(new TraceBehavior("start", trace)) // StartToken
            .ToBehaviors(new TraceBehavior("chain", trace)) // StateToken
            .If(() => takeThen)
            .Then(new EmptyLogic())
            .ToBehaviors(new TraceBehavior("then", trace)) // BranchBuilder
            .Else(new EmptyLogic())
            .ToBehaviors(new TraceBehavior("else", trace)) // BranchEnd (chains after the else tip)
            .Build();

        StateMachine machine = graph.ToStateMachine();
        Result thenResult = RunToEnd(machine);
        takeThen = false;
        Result elseResult = RunToEnd(machine);

        Assert.Multiple(() =>
        {
            Assert.That(thenResult, Is.EqualTo(Result.Success));
            Assert.That(elseResult, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "start", "chain", "then", "start", "chain", "else" }));
        });
    }

    [Test]
    public async Task Async_dsl_matrix_covers_all_four_surfaces()
    {
        List<string> trace = [];
        bool takeThen = true;
        // ReSharper disable once AccessToModifiedClosure
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(new TraceBehavior("start", trace))
            .ToBehaviorsAsync(new TraceBehavior("chain", trace))
            .If(() => takeThen)
            .Then(new EmptyLogic())
            .ToBehaviorsAsync(new TraceBehavior("then", trace))
            .Else(new EmptyLogic())
            .ToBehaviorsAsync(new TraceBehavior("else", trace))
            .Build();

        AsyncStateMachine machine = graph.ToAsyncStateMachine();
        Result thenResult = await machine.ExecuteAsync();
        takeThen = false;
        Result elseResult = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(thenResult, Is.EqualTo(Result.Success));
            Assert.That(elseResult, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "start", "chain", "then", "start", "chain", "else" }));
        });
    }

    // ── Agent-typed channel ──────────────────────────────────────────────

    [Test]
    public async Task Mixed_sequence_delivers_the_machine_bound_agent_per_execution([Values] bool sync)
    {
        List<string> trace = [];
        Hero hero = new() { Name = "Asta" };

        Graph graph = sync
            ? GraphBuilder.Start()
                .ToBehaviors<Hero>(new TraceBehavior("plain", trace), new GreetHero())
                .Build()
            : GraphBuilder.Start()
                .ToBehaviorsAsync<Hero>(new TraceBehavior("plain", trace), new GreetHero())
                .Build();

        Result result;
        if (sync)
        {
            StateMachine<Hero> machine = graph.ToStateMachine<Hero>().WithAgent(hero);
            result = RunToEnd(machine);
        }
        else
        {
            result = await graph.ToAsyncStateMachine<Hero>().WithAgent(hero).ExecuteAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "plain" }), "Plain entries run agent-blind in the mix.");
            Assert.That(hero.Visits, Is.EqualTo(new[] { "greet:Asta" }));
        });
    }

    [Test]
    public async Task Two_machines_sharing_one_graph_each_deliver_their_own_agent()
    {
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync<Hero>(new GreetHero())
            .Build();

        Hero first = new() { Name = "Asta" };
        Hero second = new() { Name = "Yuno" };
        AsyncStateMachine<Hero> machineA = graph.ToAsyncStateMachine<Hero>().WithAgent(first);
        AsyncStateMachine<Hero> machineB = graph.ToAsyncStateMachine<Hero>().WithAgent(second);

        // Non-overlapping runs — the standard shared-graph contract. The agent re-stamps at
        // every run start, so interleaved sequential runs deliver each machine's own agent.
        await machineA.ExecuteAsync();
        await machineB.ExecuteAsync();
        await machineA.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.Visits, Is.EqualTo(new[] { "greet:Asta", "greet:Asta" }));
            Assert.That(second.Visits, Is.EqualTo(new[] { "greet:Yuno" }));
        });
    }

    [Test]
    public void Graph_set_agent_counts_the_typed_composite_as_an_acceptor()
    {
        Graph graph = GraphBuilder.Start()
            .ToBehaviors<Hero>(new GreetHero())
            .Build();

        // Would throw InvalidOperationException if no node accepted the agent type.
        Assert.DoesNotThrow(() => graph.SetAgent(new Hero { Name = "Asta" }));
        Assert.Throws<InvalidOperationException>(() => graph.SetAgent(new Villain()),
            "A graph whose only acceptor is Hero-typed must reject a Villain agent.");
    }
}
