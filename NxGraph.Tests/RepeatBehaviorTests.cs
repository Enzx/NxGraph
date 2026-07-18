using NxGraph.Authoring;
using NxGraph.Behaviors;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// Repeat — bounded sub-node iteration for behavior sequences (spec 015). Codifies the
/// contract: the trip count resolves <b>once at entry</b> (literal negative rejected at
/// construction, key-bound ≤ 0 vacuous <c>Success</c>), the optional 0-based index key is
/// written before each iteration, fail-fast applies at every level (entry → iteration →
/// sequence → node), the node fault model is untouched (a <c>.Retry</c> re-runs all
/// iterations), the context passes through to nested bodies (reports route to the owning
/// node), the async paths observe cancellation between iterations, and the agent channel
/// stays a call parameter through nested bodies.
/// </summary>
[TestFixture]
[Category("behaviors")]
public class RepeatBehaviorTests
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

    /// <summary>Dual-interface probe: adds a delta to an int key — the count-mutation probe.</summary>
    private sealed class AddToKey(BlackboardKey<int> key, int delta) : IBehavior, IAsyncBehavior
    {
        public Result Execute(in BehaviorContext ctx)
        {
            ctx.Bb.Set(key, ctx.Bb.Get(key) + delta);
            return Result.Success;
        }

        public ValueTask<Result> ExecuteAsync(BehaviorContext ctx, CancellationToken ct)
        {
            ctx.Bb.Set(key, ctx.Bb.Get(key) + delta);
            return ResultHelpers.Success;
        }
    }

    /// <summary>Cancels the shared source and succeeds — the between-iterations probe.</summary>
    private sealed class CancelSource(CancellationTokenSource cts) : IAsyncBehavior
    {
        public ValueTask<Result> ExecuteAsync(BehaviorContext ctx, CancellationToken ct)
        {
            cts.Cancel();
            return ResultHelpers.Success;
        }
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

    private sealed class AsyncVillainOnly : IAsyncBehavior<Villain>
    {
        public ValueTask<Result> ExecuteAsync(Villain agent, BehaviorContext ctx, CancellationToken ct) =>
            ResultHelpers.Success;
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

    // ── Trip count ───────────────────────────────────────────────────────

    [Test]
    public async Task Literal_count_runs_the_body_that_many_times([Values] bool sync)
    {
        List<string> trace = [];
        Repeat repeat = new(3, new TraceBehavior("a", trace), new TraceBehavior("b", trace));

        Graph graph = sync
            ? GraphBuilder.Start().ToBehaviors(repeat).Build()
            : GraphBuilder.Start().ToBehaviorsAsync(repeat).Build();

        Result result = sync
            ? RunToEnd(graph.ToStateMachine())
            : await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "a", "b", "a", "b", "a", "b" }));
        });
    }

    [Test]
    public async Task Key_bound_count_reads_the_trip_count_from_the_board()
    {
        BlackboardSchema schema = new("loops");
        BlackboardKey<int> countKey = schema.Register("trips", 0);

        List<string> trace = [];
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(new AsyncRepeat(countKey, new TraceBehavior("tick", trace)))
            .WithSchema(schema)
            .Build();

        Blackboard board = new(schema);
        board.Set(countKey, 4);
        Result result = await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public async Task Count_is_resolved_once_at_entry_even_when_the_body_writes_the_bound_key([Values] bool sync)
    {
        BlackboardSchema schema = new("loops");
        BlackboardKey<int> countKey = schema.Register("trips", 2);

        List<string> trace = [];
        // The body inflates its own count key every iteration — the trip count must stay the
        // entry-resolved 2, or Repeat would be a While in disguise.
        Repeat repeat = new(countKey, new TraceBehavior("tick", trace), new AddToKey(countKey, 5));

        Graph graph = sync
            ? GraphBuilder.Start().ToBehaviors(repeat).WithSchema(schema).Build()
            : GraphBuilder.Start().ToBehaviorsAsync(repeat).WithSchema(schema).Build();

        Blackboard board = new(schema);
        Result result = sync
            ? RunToEnd(graph.ToStateMachine().WithBlackboard(board))
            : await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Has.Count.EqualTo(2), "The trip count is fixed at entry.");
            Assert.That(board.Get(countKey), Is.EqualTo(12), "The body's writes did land — they just don't steer.");
        });
    }

    [Test]
    public async Task Key_bound_count_of_zero_or_negative_is_vacuous_success([Values(0, -5)] int boundCount)
    {
        BlackboardSchema schema = new("loops");
        BlackboardKey<int> countKey = schema.Register("trips", 0);

        List<string> trace = [];
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(new AsyncRepeat(countKey, new TraceBehavior("never", trace)))
            .WithSchema(schema)
            .Build();

        Blackboard board = new(schema);
        board.Set(countKey, boundCount);
        Result result = await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success), "Runtime data must not throw — vacuous Success.");
            Assert.That(trace, Is.Empty);
        });
    }

    [Test]
    public void Literal_negative_count_is_rejected_at_construction()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Repeat(-1, new Log("x")));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Repeat<Hero>(-1, new Log("x")));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new AsyncRepeat(-1, new Log("x")));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new AsyncRepeat<Hero>(-1, new Log("x")));
        });
    }

    // ── Index key ────────────────────────────────────────────────────────

    [Test]
    public async Task Index_key_is_written_zero_based_before_each_iteration([Values] bool sync)
    {
        BlackboardSchema schema = new("loops");
        BlackboardKey<int> indexKey = schema.Register("i", -1);

        List<int> captured = [];
        Repeat repeat = new(3, indexKey, new CaptureBehavior<int>(indexKey, captured));

        Graph graph = sync
            ? GraphBuilder.Start().ToBehaviors(repeat).WithSchema(schema).Build()
            : GraphBuilder.Start().ToBehaviorsAsync(repeat).WithSchema(schema).Build();

        Blackboard board = new(schema);
        Result result = sync
            ? RunToEnd(graph.ToStateMachine().WithBlackboard(board))
            : await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(captured, Is.EqualTo(new[] { 0, 1, 2 }));
        });
    }

    [Test]
    public async Task Absent_index_key_writes_nothing()
    {
        BlackboardSchema schema = new("loops");
        BlackboardKey<int> indexKey = schema.Register("i", 99);

        List<int> captured = [];
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(new AsyncRepeat(2, new CaptureBehavior<int>(indexKey, captured)))
            .WithSchema(schema)
            .Build();

        Result result = await graph.ToAsyncStateMachine()
            .WithBlackboard(new Blackboard(schema))
            .ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(captured, Is.EqualTo(new[] { 99, 99 }),
                "With no index key the slot keeps its registered default — no write, zero cost.");
        });
    }

    // ── Fail-fast and the node fault model ───────────────────────────────

    [Test]
    public async Task Fail_fast_stops_the_iteration_and_all_later_iterations([Values] bool sync)
    {
        List<string> trace = [];
        int flakyCalls = 0;
        Repeat repeat = new(3,
            new TraceBehavior("a", trace),
            new TraceBehavior("flaky", trace, () => ++flakyCalls == 2 ? Result.Failure : Result.Success),
            new TraceBehavior("tail", trace));

        Graph graph = sync
            ? GraphBuilder.Start().ToBehaviors(repeat).Build()
            : GraphBuilder.Start().ToBehaviorsAsync(repeat).Build();

        Result result = sync
            ? RunToEnd(graph.ToStateMachine())
            : await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(trace, Is.EqualTo(new[] { "a", "flaky", "tail", "a", "flaky" }),
                "The failing entry stops its iteration's tail and every later iteration — one rule at every level.");
        });
    }

    [Test]
    public async Task Node_retry_reruns_all_iterations()
    {
        List<string> trace = [];
        int calls = 0;
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(new AsyncRepeat(2,
                new TraceBehavior("tick", trace, () => ++calls == 1 ? Result.Failure : Result.Success)))
            .Retry(maxAttempts: 2)
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "tick", "tick", "tick" }),
                "Attempt 1 died at iteration 0; the retry re-ran all iterations — idempotency compounds with trip count.");
        });
    }

    // ── Composition, context pass-through, runtime parity ────────────────

    [Test]
    public async Task Nested_repeat_multiplies_the_trip_counts([Values] bool sync)
    {
        List<string> trace = [];
        Repeat repeat = new(2, new Repeat(2, new TraceBehavior("inner", trace)));

        Graph graph = sync
            ? GraphBuilder.Start().ToBehaviors(repeat).Build()
            : GraphBuilder.Start().ToBehaviorsAsync(repeat).Build();

        Result result = sync
            ? RunToEnd(graph.ToStateMachine())
            : await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public async Task Nested_log_reaches_the_observer_through_the_pass_through_context([Values] bool sync)
    {
        Repeat repeat = new(2, new Log("inside"));
        Graph graph = sync
            ? GraphBuilder.Start().ToBehaviors(repeat).Build()
            : GraphBuilder.Start().ToBehaviorsAsync(repeat).Build();

        Result result;
        List<string> messages;
        if (sync)
        {
            LogCapturingSyncObserver observer = new();
            result = RunToEnd(graph.ToStateMachine(observer));
            messages = observer.Messages;
        }
        else
        {
            LogCapturingAsyncObserver observer = new();
            result = await graph.ToAsyncStateMachine(observer).ExecuteAsync();
            messages = observer.Messages;
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(messages, Is.EqualTo(new[] { "[Info] inside", "[Info] inside" }),
                "Reports from the body route to the owning node's report channel — no new plumbing.");
        });
    }

    [Test]
    public async Task Sync_repeat_runs_under_the_async_machine_via_the_adapter()
    {
        List<string> trace = [];
        LogCapturingAsyncObserver observer = new();
        Graph graph = GraphBuilder.Start()
            .ToBehaviors(new Repeat(2, new TraceBehavior("tick", trace), new Log("adapted")))
            .Build();

        Result result = await graph.ToAsyncStateMachine(observer).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "tick", "tick" }));
            Assert.That(observer.Messages, Is.EqualTo(new[] { "[Info] adapted", "[Info] adapted" }));
        });
    }

    // ── Cancellation ─────────────────────────────────────────────────────

    [Test]
    public void Async_repeat_observes_cancellation_between_iterations()
    {
        List<string> trace = [];
        CancellationTokenSource cts = new();
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync(new AsyncRepeat(3,
                new CancelSource(cts),
                new TraceBehavior("tick", trace)))
            .Build();

        AsyncStateMachine machine = graph.ToAsyncStateMachine();

        Assert.Multiple(() =>
        {
            Assert.That(async () => await machine.ExecuteAsync(cts.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(trace, Is.EqualTo(new[] { "tick" }),
                "Iteration 0 completes; the check between iterations stops iteration 1.");
        });
    }

    // ── Wiring-time rejections ───────────────────────────────────────────

    [Test]
    public void Empty_and_null_bodies_are_rejected()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => _ = new Repeat(1));
            Assert.Throws<ArgumentException>(() => _ = new AsyncRepeat(1));
            Assert.Throws<ArgumentException>(() => _ = new Repeat(1, new Log("a"), null!));
            Assert.Throws<ArgumentException>(() => _ = new AsyncRepeat(1, new Log("a"), null!));
            Assert.Throws<ArgumentException>(() => _ = new Repeat<Hero>(1));
            Assert.Throws<ArgumentException>(() => _ = new AsyncRepeat<Hero>(1));
        });
    }

    [Test]
    public void Untyped_repeat_rejects_agent_typed_body_entries_naming_the_typed_twin()
    {
        ArgumentException? syncEx =
            Assert.Throws<ArgumentException>(() => _ = new Repeat(1, new GreetHero()));
        ArgumentException? asyncEx =
            Assert.Throws<ArgumentException>(() => _ = new AsyncRepeat(1, new GreetHero()));

        Assert.Multiple(() =>
        {
            Assert.That(syncEx!.Message, Does.Contain("Repeat<TAgent>").And.Contain("GreetHero"));
            Assert.That(asyncEx!.Message, Does.Contain("AsyncRepeat<TAgent>").And.Contain("GreetHero"));
        });
    }

    [Test]
    public void Typed_repeat_rejects_wrong_agent_body_entries_naming_both_types()
    {
        ArgumentException? syncEx =
            Assert.Throws<ArgumentException>(() => _ = new Repeat<Hero>(1, new VillainOnly()));
        ArgumentException? asyncEx =
            Assert.Throws<ArgumentException>(() => _ = new AsyncRepeat<Hero>(1, new AsyncVillainOnly()));

        Assert.Multiple(() =>
        {
            Assert.That(syncEx!.Message, Does.Contain("Villain").And.Contain("Hero"));
            Assert.That(asyncEx!.Message, Does.Contain("Villain").And.Contain("Hero"));
        });
    }

    [Test]
    public void Untyped_composite_rejects_a_typed_repeat_entry()
    {
        // Repeat<TAgent> is itself an IAgentBehavior, so the composite-level rules apply to it
        // automatically — nothing repeat-specific needed.
        ArgumentException? ex = Assert.Throws<ArgumentException>(
            () => _ = new BehaviorState(new Repeat<Hero>(1, new GreetHero())));

        Assert.That(ex!.Message, Does.Contain("ToBehaviors<TAgent>").And.Contain("Repeat"));
    }

    [Test]
    public void Dim_guards_throw_on_the_untyped_entry_points_of_the_typed_twins()
    {
        IBehavior syncRepeat = new Repeat<Hero>(1, new GreetHero());
        IAsyncBehavior asyncRepeat = new AsyncRepeat<Hero>(1, new GreetHero());
        BehaviorContext ctx = default;

        NotSupportedException? syncEx =
            Assert.Throws<NotSupportedException>(() => syncRepeat.Execute(in ctx));
        NotSupportedException? asyncEx = Assert.ThrowsAsync<NotSupportedException>(
            async () => await asyncRepeat.ExecuteAsync(default, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(syncEx!.Message, Does.Contain("Repeat").And.Contain("agent-typed"));
            Assert.That(asyncEx!.Message, Does.Contain("AsyncRepeat").And.Contain("agent-typed"));
        });
    }

    // ── Agent-typed channel ──────────────────────────────────────────────

    [Test]
    public async Task Typed_repeat_delivers_the_machine_bound_agent_every_iteration([Values] bool sync)
    {
        List<string> trace = [];
        Hero hero = new() { Name = "Asta" };
        Graph graph = sync
            ? GraphBuilder.Start()
                .ToBehaviors<Hero>(new Repeat<Hero>(3, new TraceBehavior("plain", trace), new GreetHero()))
                .Build()
            : GraphBuilder.Start()
                .ToBehaviorsAsync<Hero>(new AsyncRepeat<Hero>(3, new TraceBehavior("plain", trace), new GreetHero()))
                .Build();

        Result result;
        if (sync)
        {
            result = RunToEnd(graph.ToStateMachine<Hero>().WithAgent(hero));
        }
        else
        {
            result = await graph.ToAsyncStateMachine<Hero>().WithAgent(hero).ExecuteAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "plain", "plain", "plain" }),
                "Plain entries run agent-blind in the mixed body.");
            Assert.That(hero.Visits, Is.EqualTo(new[] { "greet:Asta", "greet:Asta", "greet:Asta" }),
                "The agent reaches typed body entries as a call parameter, per iteration.");
        });
    }

    [Test]
    public async Task Two_machines_sharing_one_graph_deliver_their_own_agent_through_the_body()
    {
        Graph graph = GraphBuilder.Start()
            .ToBehaviorsAsync<Hero>(new AsyncRepeat<Hero>(2, new GreetHero()))
            .Build();

        Hero first = new() { Name = "Asta" };
        Hero second = new() { Name = "Yuno" };
        AsyncStateMachine<Hero> machineA = graph.ToAsyncStateMachine<Hero>().WithAgent(first);
        AsyncStateMachine<Hero> machineB = graph.ToAsyncStateMachine<Hero>().WithAgent(second);

        // Non-overlapping runs — the standard shared-graph contract; the agent re-stamps at
        // every run start and flows through the nested body as a call parameter.
        await machineA.ExecuteAsync();
        await machineB.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.Visits, Is.EqualTo(new[] { "greet:Asta", "greet:Asta" }));
            Assert.That(second.Visits, Is.EqualTo(new[] { "greet:Yuno", "greet:Yuno" }));
        });
    }
}
