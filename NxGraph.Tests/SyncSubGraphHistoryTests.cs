using NxGraph.Authoring;
using NxGraph.Diagnostics.Validations;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// Sync twins of <see cref="SubGraphHistoryTests"/>: DSL-authored subgraph nesting and
/// history composites running under the sync <see cref="StateMachine"/>, in both
/// <see cref="ParallelStepMode"/>s, plus the cross-runtime rules (RunToJoin composites run
/// under the async machine too; RoundPerTick is sync-only).
/// </summary>
[TestFixture]
public class SyncSubGraphHistoryTests
{
    private static Result RunToCompletion(StateMachine machine, out int ticks)
    {
        ticks = 0;
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            ticks++;
            result = machine.Execute();
            if (ticks > 1_000)
            {
                Assert.Fail("Machine did not complete within 1000 ticks.");
            }
        }

        return result;
    }

    private static Graph TwoNodeChild(List<string> log)
    {
        return GraphBuilder
            .StartWith(() =>
            {
                log.Add("child:0");
                return Result.Success;
            })
            .To(() =>
            {
                log.Add("child:1");
                return Result.Success;
            })
            .Build();
    }

    [Test]
    public void run_to_join_subgraph_completes_the_child_within_one_parent_tick()
    {
        List<string> log = [];
        Graph parent = GraphBuilder
            .StartWith(() =>
            {
                log.Add("parent:start");
                return Result.Success;
            })
            .SubGraph(ParallelStepMode.RunToJoin, TwoNodeChild(log))
            .To(() =>
            {
                log.Add("parent:end");
                return Result.Success;
            })
            .Build();

        Result result = RunToCompletion(parent.ToStateMachine(), out int ticks);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "parent:start", "child:0", "child:1", "parent:end" }));
            Assert.That(ticks, Is.EqualTo(3), "Three parent nodes, one tick each — the child joined within its tick.");
        });
    }

    [Test]
    public void round_per_tick_subgraph_advances_one_child_node_per_parent_tick()
    {
        List<string> log = [];
        Graph parent = GraphBuilder
            .Start()
            .SubGraph(ParallelStepMode.RoundPerTick, TwoNodeChild(log))
            .Build();

        Result result = RunToCompletion(parent.ToStateMachine(), out int ticks);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "child:0", "child:1" }));
            Assert.That(ticks, Is.GreaterThan(1),
                "One child node per parent tick — the two-node child cannot finish in a single tick.");
        });
    }

    [Test]
    public void run_to_join_subgraph_is_executable_by_the_async_runtime()
    {
        List<string> log = [];
        Graph parent = GraphBuilder
            .Start()
            .SubGraph(ParallelStepMode.RunToJoin, TwoNodeChild(log))
            .Build();

        Result result = parent.ToAsyncStateMachine().ExecuteAsync().AsTask().GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "child:0", "child:1" }),
                "A RunToJoin sync composite returns a terminal result per tick, so the adapter can run it.");
        });
    }

    [Test]
    public void round_per_tick_subgraph_is_rejected_by_the_async_runtime()
    {
        Graph parent = GraphBuilder
            .Start()
            .SubGraph(ParallelStepMode.RoundPerTick, TwoNodeChild([]))
            .Build();

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await parent.ToAsyncStateMachine().ExecuteAsync(),
            "Node-level InProgress is reserved in the async runtime — RoundPerTick is sync-only.");
    }

    [Test]
    public void history_composite_resumes_at_the_failed_child_node_on_reentry(
        [Values(ParallelStepMode.RunToJoin, ParallelStepMode.RoundPerTick)] ParallelStepMode mode)
    {
        List<string> log = [];
        bool repaired = false;

        Graph child = GraphBuilder
            .StartWith(() =>
            {
                log.Add("c0");
                return Result.Success;
            })
            .To(() =>
            {
                log.Add("c1");
                return repaired ? Result.Success : Result.Failure;
            })
            .To(() =>
            {
                log.Add("c2");
                return Result.Success;
            })
            .Build();

        StateToken sub = GraphBuilder
            .Start()
            .SubGraph(mode, child, history: true)
            .SetName("Sub");

        StateToken repair = sub.Builder.TokenFor(sub.Builder.AddNode(new RelayState(() =>
        {
            repaired = true;
            log.Add("repair");
            return Result.Success;
        })));
        repair.Goto("Sub");

        Graph parent = sub.OnError(repair).Build();

        Result result = RunToCompletion(parent.ToStateMachine(), out _);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c1", "repair", "c1", "c2" }),
                "After the repair, the child resumed at the failed node c1 — c0 did not re-run.");
        });
    }

    [Test]
    public void without_history_reentry_restarts_the_child_from_its_start()
    {
        List<string> log = [];
        bool repaired = false;

        Graph child = GraphBuilder
            .StartWith(() =>
            {
                log.Add("c0");
                return Result.Success;
            })
            .To(() =>
            {
                log.Add("c1");
                return repaired ? Result.Success : Result.Failure;
            })
            .Build();

        StateToken sub = GraphBuilder
            .Start()
            .SubGraph(ParallelStepMode.RunToJoin, child)
            .SetName("Sub");

        StateToken repair = sub.Builder.TokenFor(sub.Builder.AddNode(new RelayState(() =>
        {
            repaired = true;
            return Result.Success;
        })));
        repair.Goto("Sub");

        Graph parent = sub.OnError(repair).Build();

        Result result = RunToCompletion(parent.ToStateMachine(), out _);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c1", "c0", "c1" }),
                "Without history the child restarts from c0 on re-entry.");
        });
    }

    [Test]
    public void completed_history_composite_restarts_from_the_top_on_reentry()
    {
        List<string> log = [];
        int laps = 0;

        Graph child = GraphBuilder
            .StartWith(() =>
            {
                log.Add("c0");
                return Result.Success;
            })
            .Build();

        Graph parent = GraphBuilder
            .Start()
            .SubGraph(ParallelStepMode.RunToJoin, child, history: true).SetName("Sub")
            .To(() => ++laps < 2 ? Result.Success : Result.Failure)
            .Goto("Sub")
            .Build();

        Result result = RunToCompletion(parent.ToStateMachine(), out _);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c0" }),
                "A completed child restarts from its start node on the next visit.");
        });
    }

    [Test]
    public void resumed_child_node_gets_a_fresh_retry_budget()
    {
        List<string> log = [];
        int failures = 0;

        // The child node consumes its full retry budget (2 attempts) and fails the child on
        // the first visit. On re-entry the resumed node must start from a zeroed attempt
        // counter — a stale counter would skip the retry and fail immediately.
        Graph child = GraphBuilder
            .StartWith(() =>
            {
                log.Add("try");
                return ++failures < 4 ? Result.Failure : Result.Success;
            })
            .Retry(maxAttempts: 2)
            .Build();

        StateToken sub = GraphBuilder
            .Start()
            .SubGraph(ParallelStepMode.RunToJoin, child, history: true)
            .SetName("Sub");

        StateToken repair = sub.Builder.TokenFor(sub.Builder.AddNode(new RelayState(() =>
        {
            log.Add("repair");
            return Result.Success;
        })));
        repair.Goto("Sub");

        Graph parent = sub.OnError(repair).Build();

        Result result = RunToCompletion(parent.ToStateMachine(), out _);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "try", "try", "repair", "try", "try" }),
                "Visit 1 spends attempts 1+2; the resumed visit gets a fresh budget for attempts 3+4.");
        });
    }

    [Test]
    public void validator_flags_round_per_tick_composites_when_strict_async_compatible()
    {
        Graph roundPerTick = GraphBuilder
            .Start()
            .SubGraph(ParallelStepMode.RoundPerTick, TwoNodeChild([]))
            .Build();

        Graph runToJoin = GraphBuilder
            .Start()
            .SubGraph(ParallelStepMode.RunToJoin, TwoNodeChild([]))
            .Build();

        GraphValidationResult flagged = roundPerTick.Validate(new GraphValidationOptions
        {
            StrictAsyncCompatible = true,
        });
        GraphValidationResult cleanStrict = runToJoin.Validate(new GraphValidationOptions
        {
            StrictAsyncCompatible = true,
        });
        GraphValidationResult cleanDefault = roundPerTick.Validate();

        Assert.Multiple(() =>
        {
            Assert.That(flagged.Diagnostics.Any(d =>
                    d.Severity == Severity.Error && d.Message.Contains("RoundPerTick")),
                Is.True, "StrictAsyncCompatible must flag RoundPerTick composites.");
            Assert.That(cleanStrict.Diagnostics.Any(d => d.Message.Contains("RoundPerTick")), Is.False,
                "RunToJoin composites run under both runtimes and must stay clean.");
            Assert.That(cleanDefault.Diagnostics.Any(d => d.Message.Contains("RoundPerTick")), Is.False,
                "The lint is opt-in — default validation must not flag sync-destined graphs.");
        });
    }
}
