using System.Text.Json;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// Deep suspend/resume (spec 011): <c>SuspendDeep()</c>/<c>ResumeDeep(...)</c> capture
/// composite-internal position — nested machine positions, history's remembered child,
/// the sync RoundPerTick composites' mid-visit bookkeeping — into
/// <see cref="StateMachineDeepSnapshot"/>, resumable on a fresh machine over an equivalent
/// (rebuilt) graph. The shallow pair and <see cref="StateMachineSnapshot"/> are untouched;
/// <see cref="SuspendResumeTests"/>/<see cref="SyncSuspendResumeTests"/> pin that separately.
/// </summary>
[TestFixture]
public class DeepSuspendResumeTests
{
    private static Result RunToCompletion(StateMachine machine)
    {
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        return result;
    }

    private static async Task<Result> RunToCompletionAsync(AsyncStateMachine machine)
    {
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = await machine.StepAsync();
        }

        return result;
    }

    private static StateMachineDeepSnapshot JsonRoundTrip(StateMachineDeepSnapshot snapshot)
    {
        string json = JsonSerializer.Serialize(snapshot);
        return JsonSerializer.Deserialize<StateMachineDeepSnapshot>(json)!;
    }

    private static Graph SyncLoggingChain(List<string> log, string prefix, int length)
    {
        StateToken token = GraphBuilder.StartWith(() =>
        {
            log.Add($"{prefix}0");
            return Result.Success;
        });

        for (int i = 1; i < length; i++)
        {
            int step = i;
            token = token.To(() =>
            {
                log.Add($"{prefix}{step}");
                return Result.Success;
            });
        }

        return token.Build();
    }

    // ── Async history: the flagship durable case ──────────────────────────

    [Test]
    public async Task async_history_deep_snapshot_resumes_child_position_on_a_fresh_machine()
    {
        List<string> log = [];
        bool repaired = false;

        Graph BuildParent()
        {
            Graph child = GraphBuilder
                .StartWithAsync(_ =>
                {
                    log.Add("c0");
                    return ResultHelpers.Success;
                })
                .ToAsync(_ =>
                {
                    log.Add("c1");
                    return repaired ? ResultHelpers.Success : ResultHelpers.Failure;
                })
                .ToAsync(_ =>
                {
                    log.Add("c2");
                    return ResultHelpers.Success;
                })
                .Build();

            StateToken sub = GraphBuilder.Start().SubGraph(child, history: true).SetName("Sub");
            StateToken repair = sub.Builder.TokenFor(sub.Builder.AddNode(new AsyncRelayState(_ =>
            {
                repaired = true;
                log.Add("repair");
                return ResultHelpers.Success;
            })));
            repair.Goto("Sub");
            return sub.OnError(repair).Build();
        }

        AsyncStateMachine first = BuildParent().ToAsyncStateMachine();
        Result tick = await first.StepAsync(); // child runs c0, fails at c1; parent reroutes to repair
        Assert.Multiple(() =>
        {
            Assert.That(tick, Is.EqualTo(Result.InProgress));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c1" }));
        });

        StateMachineDeepSnapshot deep = first.SuspendDeep();
        Assert.That(deep.Composites, Has.Length.EqualTo(1), "The history composite emitted one entry.");

        StateMachineDeepSnapshot restored = JsonRoundTrip(deep);

        AsyncStateMachine second = BuildParent().ToAsyncStateMachine();
        second.ResumeDeep(restored);
        Result result = await RunToCompletionAsync(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c1", "repair", "c1", "c2" }),
                "The fresh machine's history child resumed at the failed node c1 — c0 did not re-run.");
        });
    }

    [Test]
    public async Task completed_child_restarts_from_top_after_deep_resume()
    {
        List<string> log = [];
        int laps = 0;

        Graph BuildParent()
        {
            Graph child = GraphBuilder
                .StartWithAsync(_ =>
                {
                    log.Add("c0");
                    return ResultHelpers.Success;
                })
                .Build();

            return GraphBuilder
                .Start()
                .SubGraph(child, history: true).SetName("Sub")
                .ToAsync(_ => ++laps < 2 ? ResultHelpers.Success : ResultHelpers.Failure)
                .Goto("Sub")
                .Build();
        }

        AsyncStateMachine first = BuildParent().ToAsyncStateMachine();
        Result tick = await first.StepAsync(); // child completed; parent at the lap node
        Assert.That(tick, Is.EqualTo(Result.InProgress));

        StateMachineDeepSnapshot deep = JsonRoundTrip(first.SuspendDeep());

        AsyncStateMachine second = BuildParent().ToAsyncStateMachine();
        second.ResumeDeep(deep);
        Result result = await RunToCompletionAsync(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c0" }),
                "A child captured as Completed restarts from its start node on re-entry — history only remembers failures.");
        });
    }

    [Test]
    public async Task deep_history_two_levels_down_survives_a_durable_boundary()
    {
        List<string> log = [];
        bool repaired = false;

        Graph BuildParent()
        {
            Graph inner = GraphBuilder
                .StartWithAsync(_ =>
                {
                    log.Add("i0");
                    return ResultHelpers.Success;
                })
                .ToAsync(_ =>
                {
                    log.Add("i1");
                    return repaired ? ResultHelpers.Success : ResultHelpers.Failure;
                })
                .Build();

            Graph middle = GraphBuilder
                .StartWithAsync(_ =>
                {
                    log.Add("m0");
                    return ResultHelpers.Success;
                })
                .SubGraph(inner, history: true)
                .Build();

            StateToken sub = GraphBuilder.Start().SubGraph(middle, history: true).SetName("Sub");
            StateToken repair = sub.Builder.TokenFor(sub.Builder.AddNode(new AsyncRelayState(_ =>
            {
                repaired = true;
                log.Add("repair");
                return ResultHelpers.Success;
            })));
            repair.Goto("Sub");
            return sub.OnError(repair).Build();
        }

        AsyncStateMachine first = BuildParent().ToAsyncStateMachine();
        Result tick = await first.StepAsync();
        Assert.Multiple(() =>
        {
            Assert.That(tick, Is.EqualTo(Result.InProgress));
            Assert.That(log, Is.EqualTo(new[] { "m0", "i0", "i1" }));
        });

        StateMachineDeepSnapshot deep = JsonRoundTrip(first.SuspendDeep());

        AsyncStateMachine second = BuildParent().ToAsyncStateMachine();
        second.ResumeDeep(deep);
        Result result = await RunToCompletionAsync(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "m0", "i0", "i1", "repair", "i1" }),
                "Both nesting levels resumed their own position on the fresh machine: neither m0 nor i0 re-ran.");
        });
    }

    // ── Sync RoundPerTick composites: mid-visit capture ───────────────────

    [Test]
    public void sync_round_per_tick_parallel_resumes_mid_visit_on_a_fresh_machine()
    {
        List<string> log = [];

        Graph BuildParent()
        {
            return GraphBuilder
                .Start()
                .Parallel(ParallelStepMode.RoundPerTick,
                    SyncLoggingChain(log, "a", 2), SyncLoggingChain(log, "b", 3))
                .Build();
        }

        StateMachine first = BuildParent().ToStateMachine();
        first.Execute(); // round 1: a0 b0
        first.Execute(); // round 2: a1 (region a joins) b1
        Assert.That(log, Is.EqualTo(new[] { "a0", "b0", "a1", "b1" }));

        StateMachineDeepSnapshot deep = JsonRoundTrip(first.SuspendDeep());

        StateMachine second = BuildParent().ToStateMachine();
        second.ResumeDeep(deep);
        Result result = RunToCompletion(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "a0", "b0", "a1", "b1", "b2" }),
                "Regions continued from their exact nodes: a did not re-run, b picked up at b2.");
        });
    }

    [Test]
    public void sync_parallel_failure_aggregation_survives_deep_resume()
    {
        List<string> log = [];

        Graph BuildParent()
        {
            Graph failing = GraphBuilder
                .StartWith(() =>
                {
                    log.Add("f0");
                    return Result.Failure;
                })
                .Build();

            return GraphBuilder
                .Start()
                .Parallel(ParallelStepMode.RoundPerTick, failing, SyncLoggingChain(log, "b", 3))
                .Build();
        }

        StateMachine first = BuildParent().ToStateMachine();
        first.Execute(); // round 1: f0 fails (region done, failed), b0
        Assert.That(log, Is.EqualTo(new[] { "f0", "b0" }));

        StateMachineDeepSnapshot deep = JsonRoundTrip(first.SuspendDeep());

        StateMachine second = BuildParent().ToStateMachine();
        second.ResumeDeep(deep);
        Result result = RunToCompletion(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure),
                "The pre-suspend region failure still fails the join — aggregation is recomputed, not lost.");
            Assert.That(log, Is.EqualTo(new[] { "f0", "b0", "b1", "b2" }),
                "The healthy region still ran to completion before the join.");
        });
    }

    [Test]
    public void dynamic_parallel_deselection_survives_deep_resume()
    {
        List<string> log = [];
        int selectorRuns = 0;

        Graph BuildParent()
        {
            return GraphBuilder
                .Start()
                .Parallel(ParallelStepMode.RoundPerTick, _ =>
                    {
                        selectorRuns++;
                        return RegionMask.Of(0, 2);
                    },
                    SyncLoggingChain(log, "a", 2), SyncLoggingChain(log, "b", 2), SyncLoggingChain(log, "c", 2))
                .Build();
        }

        StateMachine first = BuildParent().ToStateMachine();
        first.Execute(); // selector fixes {a, c}; round 1: a0 c0
        Assert.Multiple(() =>
        {
            Assert.That(selectorRuns, Is.EqualTo(1));
            Assert.That(log, Is.EqualTo(new[] { "a0", "c0" }));
        });

        StateMachineDeepSnapshot deep = JsonRoundTrip(first.SuspendDeep());

        StateMachine second = BuildParent().ToStateMachine();
        second.ResumeDeep(deep);
        Result result = RunToCompletion(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(selectorRuns, Is.EqualTo(1),
                "The selector is not re-evaluated on resume — the captured Done bits carry the visit's selection.");
            Assert.That(log, Is.EqualTo(new[] { "a0", "c0", "a1", "c1" }),
                "The deselected region b stayed deselected after the resume (Done is captured, not derived).");
        });
    }

    [Test]
    public void sync_nested_machine_mid_run_resumes_in_place()
    {
        List<string> log = [];

        Graph BuildParent()
        {
            Graph child = SyncLoggingChain(log, "c", 3);
            return GraphBuilder
                .StartWith(() =>
                {
                    log.Add("p0");
                    return Result.Success;
                })
                .SubGraph(ParallelStepMode.RoundPerTick, child)
                .To(() =>
                {
                    log.Add("p1");
                    return Result.Success;
                })
                .Build();
        }

        StateMachine first = BuildParent().ToStateMachine();
        first.Execute(); // p0
        first.Execute(); // child c0
        first.Execute(); // child c1
        Assert.That(log, Is.EqualTo(new[] { "p0", "c0", "c1" }));

        StateMachineDeepSnapshot deep = JsonRoundTrip(first.SuspendDeep());

        StateMachine second = BuildParent().ToStateMachine();
        second.ResumeDeep(deep);
        Result result = RunToCompletion(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "p0", "c0", "c1", "c2", "p1" }),
                "The nested machine resumed mid-run at c2; the parent then continued past the composite.");
        });
    }

    // ── Cross-runtime interchange ─────────────────────────────────────────

    private static Graph BuildCrossRuntimeHistoryParent(List<string> log, Func<bool> isRepaired,
        Action markRepaired)
    {
        Graph child = GraphBuilder
            .StartWith(() =>
            {
                log.Add("c0");
                return Result.Success;
            })
            .To(() =>
            {
                log.Add("c1");
                return isRepaired() ? Result.Success : Result.Failure;
            })
            .To(() =>
            {
                log.Add("c2");
                return Result.Success;
            })
            .Build();

        StateToken sub = GraphBuilder
            .Start()
            .SubGraph(ParallelStepMode.RunToJoin, child, history: true)
            .SetName("Sub");
        StateToken repair = sub.Builder.TokenFor(sub.Builder.AddNode(new RelayState(() =>
        {
            markRepaired();
            log.Add("repair");
            return Result.Success;
        })));
        repair.Goto("Sub");
        return sub.OnError(repair).Build();
    }

    [Test]
    public async Task async_captured_history_deep_snapshot_resumes_on_the_sync_machine()
    {
        List<string> log = [];
        bool repaired = false;

        AsyncStateMachine first = BuildCrossRuntimeHistoryParent(log, () => repaired, () => repaired = true)
            .ToAsyncStateMachine();
        Result tick = await first.StepAsync(); // sync history composite runs under the adapter, child fails
        Assert.Multiple(() =>
        {
            Assert.That(tick, Is.EqualTo(Result.InProgress));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c1" }));
        });

        StateMachineDeepSnapshot deep = JsonRoundTrip(first.SuspendDeep());

        StateMachine second = BuildCrossRuntimeHistoryParent(log, () => repaired, () => repaired = true)
            .ToStateMachine();
        second.ResumeDeep(deep);
        Result result = RunToCompletion(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c1", "repair", "c1", "c2" }),
                "History captured under the async runtime resumed at c1 on the sync machine.");
        });
    }

    [Test]
    public async Task sync_captured_history_deep_snapshot_resumes_on_the_async_machine()
    {
        List<string> log = [];
        bool repaired = false;

        StateMachine first = BuildCrossRuntimeHistoryParent(log, () => repaired, () => repaired = true)
            .ToStateMachine();
        Result tick = first.Execute(); // history composite fails, parent reroutes to repair
        Assert.Multiple(() =>
        {
            Assert.That(tick, Is.EqualTo(Result.InProgress));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c1" }));
        });

        StateMachineDeepSnapshot deep = JsonRoundTrip(first.SuspendDeep());

        AsyncStateMachine second = BuildCrossRuntimeHistoryParent(log, () => repaired, () => repaired = true)
            .ToAsyncStateMachine();
        second.ResumeDeep(deep);
        Result result = await RunToCompletionAsync(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c1", "repair", "c1", "c2" }),
                "History captured under the sync runtime resumed at c1 on the async machine.");
        });
    }

    // ── Sparse capture semantics ──────────────────────────────────────────

    [Test]
    public async Task stateless_deep_snapshot_behaves_exactly_like_shallow()
    {
        List<int> executed = [];

        Graph BuildGraph()
        {
            return GraphBuilder
                .StartWithAsync(_ =>
                {
                    executed.Add(0);
                    return ResultHelpers.Success;
                })
                .ToAsync(_ =>
                {
                    executed.Add(1);
                    return ResultHelpers.Success;
                })
                .ToAsync(_ =>
                {
                    executed.Add(2);
                    return ResultHelpers.Success;
                })
                .Build();
        }

        AsyncStateMachine first = BuildGraph().ToAsyncStateMachine();
        await first.StepAsync();

        StateMachineDeepSnapshot deep = first.SuspendDeep();
        Assert.That(deep.Composites, Is.Empty, "No composite holds durable state — sparse capture is empty.");

        StateMachineDeepSnapshot restored = JsonRoundTrip(deep);

        AsyncStateMachine second = BuildGraph().ToAsyncStateMachine();
        second.ResumeDeep(restored);
        Result result = await RunToCompletionAsync(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(executed, Is.EqualTo(new[] { 0, 1, 2 }),
                "Identical to the shallow contract: the resumed machine continued at node 1.");
        });
    }

    [Test]
    public async Task composite_absent_from_the_snapshot_reenters_fresh()
    {
        List<string> log = [];
        bool repaired = false;

        Graph BuildParent()
        {
            Graph child = GraphBuilder
                .StartWithAsync(_ =>
                {
                    log.Add("c0");
                    return ResultHelpers.Success;
                })
                .ToAsync(_ =>
                {
                    log.Add("c1");
                    return repaired ? ResultHelpers.Success : ResultHelpers.Failure;
                })
                .ToAsync(_ =>
                {
                    log.Add("c2");
                    return ResultHelpers.Success;
                })
                .Build();

            StateToken sub = GraphBuilder.Start().SubGraph(child, history: true).SetName("Sub");
            StateToken repair = sub.Builder.TokenFor(sub.Builder.AddNode(new AsyncRelayState(_ =>
            {
                repaired = true;
                log.Add("repair");
                return ResultHelpers.Success;
            })));
            repair.Goto("Sub");
            return sub.OnError(repair).Build();
        }

        AsyncStateMachine first = BuildParent().ToAsyncStateMachine();
        await first.StepAsync();

        StateMachineDeepSnapshot withoutComposites = first.SuspendDeep() with { Composites = [] };

        AsyncStateMachine second = BuildParent().ToAsyncStateMachine();
        second.ResumeDeep(withoutComposites);
        Result result = await RunToCompletionAsync(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "c0", "c1", "repair", "c0", "c1", "c2" }),
                "Without a captured entry the history composite re-entered fresh — c0 ran again.");
        });
    }

    // ── Resume validation ─────────────────────────────────────────────────

    [Test]
    public void resume_deep_rejects_out_of_range_composite_node_index()
    {
        Graph graph = GraphBuilder.StartWith(() => Result.Success).Build();
        StateMachineDeepSnapshot deep = graph.ToStateMachine().SuspendDeep() with
        {
            Composites = [new CompositeSnapshot(42, false, [], [])],
        };

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
            () => graph.ToStateMachine().ResumeDeep(deep));
        Assert.That(ex!.Message, Does.Contain("42"));
    }

    [Test]
    public void resume_deep_rejects_a_non_composite_node_claim()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Success)
            .Build();
        StateMachineDeepSnapshot deep = graph.ToStateMachine().SuspendDeep() with
        {
            Composites = [new CompositeSnapshot(1, false, [], [])],
        };

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
            () => graph.ToStateMachine().ResumeDeep(deep));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("node index 1"));
            Assert.That(ex.Message, Does.Contain(nameof(ISuspendableComposite)));
        });
    }

    [Test]
    public async Task resume_deep_rejects_a_children_count_mismatch()
    {
        Graph BuildParent()
        {
            Graph child = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success).Build();
            return GraphBuilder.Start().SubGraph(child, history: true).Build();
        }

        AsyncStateMachine first = BuildParent().ToAsyncStateMachine();
        await first.StepAsync();
        StateMachineDeepSnapshot deep = first.SuspendDeep();
        int compositeIndex = deep.Composites[0].NodeIndex;
        StateMachineDeepSnapshot tampered = deep with
        {
            Composites = [deep.Composites[0] with { Children = [] }],
        };

        AsyncStateMachine second = BuildParent().ToAsyncStateMachine();
        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
            () => second.ResumeDeep(tampered));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain($"composite node {compositeIndex}"));
            Assert.That(ex.Message, Does.Contain("child snapshot"));
        });
    }

    [Test]
    public void resume_deep_rejects_a_done_length_mismatch()
    {
        Graph BuildParent()
        {
            return GraphBuilder
                .Start()
                .Parallel(ParallelStepMode.RoundPerTick,
                    GraphBuilder.StartWith(() => Result.Success).Build(),
                    GraphBuilder.StartWith(() => Result.Success).Build())
                .Build();
        }

        StateMachineDeepSnapshot deep = BuildParent().ToStateMachine().SuspendDeep();
        int compositeIndex = deep.Composites[0].NodeIndex;
        StateMachineDeepSnapshot tampered = deep with
        {
            Composites = [deep.Composites[0] with { Done = [true] }],
        };

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
            () => BuildParent().ToStateMachine().ResumeDeep(tampered));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain($"composite node {compositeIndex}"));
            Assert.That(ex.Message, Does.Contain("done bit"));
        });
    }

    [Test]
    public async Task resume_deep_rejects_a_transient_status_child_snapshot()
    {
        Graph BuildParent()
        {
            Graph child = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success).Build();
            return GraphBuilder.Start().SubGraph(child, history: true).Build();
        }

        AsyncStateMachine first = BuildParent().ToAsyncStateMachine();
        await first.StepAsync();
        StateMachineDeepSnapshot deep = first.SuspendDeep();
        CompositeSnapshot entry = deep.Composites[0];
        StateMachineDeepSnapshot tampered = deep with
        {
            Composites =
            [
                entry with
                {
                    Children =
                    [
                        entry.Children[0] with
                        {
                            Self = entry.Children[0].Self with { Status = ExecutionStatus.Starting },
                        },
                    ],
                },
            ],
        };

        AsyncStateMachine second = BuildParent().ToAsyncStateMachine();
        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
            () => second.ResumeDeep(tampered));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain($"composite node {entry.NodeIndex}"));
            Assert.That(ex.Message, Does.Contain("transient"));
        });
    }

    // ── Node boards stay transient at every level ─────────────────────────

    [Test]
    public void node_board_is_back_at_defaults_after_deep_resume_at_a_nested_level()
    {
        BlackboardSchema schema = new("deep-scratch", BlackboardScope.Node);
        BlackboardKey<int> counter = schema.Register<int>("n");
        List<int> seen = [];

        Graph BuildParent()
        {
            Graph child = GraphBuilder
                .StartWith(bb =>
                {
                    int n = bb.Get(counter);
                    bb.Set(counter, n + 1);
                    seen.Add(n);
                    return n >= 2 ? Result.Success : Result.Failure;
                })
                .Retry(maxAttempts: 5)
                .WithSchema(schema)
                .Build();

            return GraphBuilder
                .Start()
                .SubGraph(ParallelStepMode.RoundPerTick, child)
                .Build();
        }

        StateMachine first = BuildParent().ToStateMachine();
        first.Execute(); // child attempt 1: scratch 0 → 1
        first.Execute(); // child attempt 2 (in-place retry keeps the scratch): 1 → 2
        Assert.That(seen, Is.EqualTo(new[] { 0, 1 }));

        StateMachineDeepSnapshot deep = JsonRoundTrip(first.SuspendDeep());

        StateMachine second = BuildParent().ToStateMachine();
        second.ResumeDeep(deep);
        Result result = RunToCompletion(second);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(seen, Is.EqualTo(new[] { 0, 1, 0, 1, 2 }),
                "The nested machine's Node scratch is transient: the deep resume restored the retry budget " +
                "(attempts survived) but the scratch restarted from its registered default.");
        });
    }
}
