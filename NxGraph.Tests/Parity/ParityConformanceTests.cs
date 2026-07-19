using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests.Parity;

/// <summary>
/// Cross-runtime parity conformance matrix for the FSM runtimes: every scenario runs through
/// the four adapters (sync RunToJoin, sync stepped, async ExecuteAsync, async StepAsync) and
/// must produce the same observable trace, order-exact and unnormalized — stepped ≡ full-run
/// within each runtime, sync ≡ async across them. The per-feature fixtures stay the
/// authoritative spec of each feature's details; this suite pins only the cross-runtime
/// equivalence, so future drift is caught by CI instead of by review. New features should
/// add a scenario here alongside their twin fixtures.
/// </summary>
[TestFixture]
public class ParityConformanceTests
{
    // ── Recipes (invoked fresh per adapter so closure state resets) ─────

    private static ParityScenario LinearChain()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success).SetName("a")
            .To(() => Result.Success).SetName("b")
            .To(() => Result.Success).SetName("c")
            .Build();
        return new ParityScenario { Graph = graph };
    }

    private static ParityScenario TerminalFailure()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success).SetName("a")
            .To(() => Result.Failure).SetName("boom")
            .Build();
        return new ParityScenario { Graph = graph };
    }

    private static ParityScenario FailureEdgeReroute()
    {
        StateToken faulty = GraphBuilder.StartWith(() => Result.Failure).SetName("faulty");
        NodeId rescueId = faulty.Builder.AddNode(new RelayState(() => Result.Success));
        StateToken rescue = faulty.Builder.TokenFor(rescueId).SetName("rescue");
        faulty.OnError(rescue);
        return new ParityScenario { Graph = faulty.Build() };
    }

    private static ParityScenario RetryThenSucceed()
    {
        int executions = 0;
        Graph graph = GraphBuilder
            .StartWith(() =>
            {
                executions++;
                return executions < 3 ? Result.Failure : Result.Success;
            })
            .SetName("flaky").Retry(3) // zero backoff: timing never enters the trace
            .To(() => Result.Success).SetName("after")
            .Build();
        ParityScenario scenario = new() { Graph = graph };
        scenario.Probes.Add(("flaky-executions", () => executions));
        return scenario;
    }

    private static ParityScenario RetryExhaustedThenEdge()
    {
        int executions = 0;
        StateToken doomed = GraphBuilder
            .StartWith(() =>
            {
                executions++;
                return Result.Failure;
            })
            .SetName("doomed").Retry(2);
        NodeId rescueId = doomed.Builder.AddNode(new RelayState(() => Result.Success));
        doomed.OnError(doomed.Builder.TokenFor(rescueId).SetName("rescue"));
        ParityScenario scenario = new() { Graph = doomed.Build() };
        scenario.Probes.Add(("doomed-executions", () => executions));
        return scenario;
    }

    private static ParityScenario GotoLoop()
    {
        int laps = 0;
        StateToken top = GraphBuilder.StartWith(() => Result.Success).SetName("top");
        StateToken work = top.To(() =>
        {
            laps++;
            return Result.Success;
        }).SetName("work");
        Dsl.BranchBuilder thenBranch = work.If(() => laps < 3)
            .Then(() => Result.Success).SetName("again");
        thenBranch.Builder.TokenFor(thenBranch.Tip).Goto("top");
        Dsl.BranchEnd done = thenBranch.Else(() => Result.Success).SetName("done");
        ParityScenario scenario = new() { Graph = done.Build() };
        scenario.Probes.Add(("laps", () => laps));
        return scenario;
    }

    private static ParityScenario IfBranch(bool takeThen)
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success).SetName("ask")
            .If(() => takeThen)
            .Then(() => Result.Success).SetName("yes")
            .Else(() => Result.Success).SetName("no")
            .Build();
        return new ParityScenario { Graph = graph };
    }

    private static ParityScenario SwitchRouting()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success).SetName("pick")
            .Switch(() => "beta")
            .Case("alpha", () => Result.Success)
            .Case("beta", () => Result.Success)
            .Default(() => Result.Failure)
            .End().SetName("switch")
            .Build();
        return new ParityScenario { Graph = graph };
    }

    private static ParityScenario OutcomeCodes()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success).SetName("work")
            .To(() => Result.Success).SetName("finish").WithOutcome(42, "Done")
            .Build();
        return new ParityScenario { Graph = graph };
    }

    private static ParityScenario EnterExitActions()
    {
        int flakyEntered = 0, flakyExited = 0, executions = 0, afterEntered = 0, afterExited = 0;
        Graph graph = GraphBuilder
            .StartWith(() =>
            {
                executions++;
                return executions < 3 ? Result.Failure : Result.Success;
            })
            .SetName("flaky").Retry(3)
            .OnEnter(() => flakyEntered++).OnExit(() => flakyExited++)
            .To(() => Result.Success).SetName("after")
            .OnEnter(() => afterEntered++).OnExit(() => afterExited++)
            .Build();
        ParityScenario scenario = new() { Graph = graph };
        scenario.Probes.Add(("flaky-entered", () => flakyEntered));
        scenario.Probes.Add(("flaky-exited", () => flakyExited));
        scenario.Probes.Add(("flaky-executions", () => executions));
        scenario.Probes.Add(("after-entered", () => afterEntered));
        scenario.Probes.Add(("after-exited", () => afterExited));
        return scenario;
    }

    /// <summary>A sync state emitting one message per run via State.Log (the spec-017 bridge).</summary>
    private sealed class ChattyState(string message) : State
    {
        protected override Result OnRun()
        {
            Log(message);
            return Result.Success;
        }
    }

    private static ParityScenario StateLogChain()
    {
        Graph graph = GraphBuilder
            .StartWith(new ChattyState("hello-from-a")).SetName("a")
            .To(new ChattyState("hello-from-b")).SetName("b")
            .Build();
        return new ParityScenario { Graph = graph };
    }

    // ── Scenario matrix ─────────────────────────────────────────────────

    [Test]
    public async Task linear_success_chain_runs_identically()
    {
        List<string> baseline = await ParityRunner.AssertFsmParityAsync(LinearChain, ParityDrives.OneRunAsync);
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("run-result Success"));
            Assert.That(baseline, Does.Contain("final-status Ready"), "RestartPolicy.Auto resets to Ready.");
        });
    }

    [Test]
    public async Task terminal_failure_without_failure_edge_runs_identically()
    {
        List<string> baseline =
            await ParityRunner.AssertFsmParityAsync(TerminalFailure, ParityDrives.OneRunAsync);
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("run-result Failure"));
            Assert.That(baseline, Does.Contain("machine-completed Failure"));
        });
    }

    [Test]
    public async Task failure_edge_reroute_runs_identically()
    {
        List<string> baseline =
            await ParityRunner.AssertFsmParityAsync(FailureEdgeReroute, ParityDrives.OneRunAsync);
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("failed faulty result"),
                "The failure-edge reroute announces the failure before transitioning.");
            Assert.That(baseline, Does.Contain("transition faulty->rescue"));
            Assert.That(baseline, Does.Contain("run-result Success"));
        });
    }

    [Test]
    public async Task retry_then_succeed_runs_identically()
    {
        List<string> baseline =
            await ParityRunner.AssertFsmParityAsync(RetryThenSucceed, ParityDrives.OneRunAsync);
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("run-result Success"));
            Assert.That(baseline, Does.Contain("probe flaky-executions=3"),
                "The retry policy re-runs the node in place until it succeeds.");
        });
    }

    [Test]
    public async Task retry_exhausted_then_failure_edge_runs_identically()
    {
        List<string> baseline =
            await ParityRunner.AssertFsmParityAsync(RetryExhaustedThenEdge, ParityDrives.OneRunAsync);
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("probe doomed-executions=2"),
                "Retries are consumed before the failure edge is taken.");
            Assert.That(baseline, Does.Contain("transition doomed->rescue"));
            Assert.That(baseline, Does.Contain("run-result Success"));
        });
    }

    [Test]
    public async Task goto_back_edge_loop_with_bounded_exit_runs_identically()
    {
        List<string> baseline = await ParityRunner.AssertFsmParityAsync(GotoLoop, ParityDrives.OneRunAsync);
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("probe laps=3"));
            Assert.That(baseline, Does.Contain("entered done"));
            Assert.That(baseline, Does.Contain("run-result Success"));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task if_branching_runs_identically(bool takeThen)
    {
        List<string> baseline =
            await ParityRunner.AssertFsmParityAsync(() => IfBranch(takeThen), ParityDrives.OneRunAsync);
        Assert.That(baseline, Does.Contain(takeThen ? "entered yes" : "entered no"));
    }

    [Test]
    public async Task switch_routing_runs_identically()
    {
        List<string> baseline =
            await ParityRunner.AssertFsmParityAsync(SwitchRouting, ParityDrives.OneRunAsync);
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("transition switch->#2"),
                "The 'beta' case node (index 2) is selected by the director.");
            Assert.That(baseline, Does.Contain("run-result Success"));
        });
    }

    [Test]
    public async Task outcome_codes_and_last_outcome_run_identically()
    {
        List<string> baseline =
            await ParityRunner.AssertFsmParityAsync(OutcomeCodes, ParityDrives.OneRunAsync);
        Assert.That(baseline, Does.Contain("last-outcome 42 name=Done"),
            "LastOutcome/LastOutcomeName must report the terminal node's declared outcome.");
    }

    [Test]
    public async Task enter_exit_action_counts_run_identically()
    {
        List<string> baseline =
            await ParityRunner.AssertFsmParityAsync(EnterExitActions, ParityDrives.OneRunAsync);
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("probe flaky-entered=1"),
                "Enter actions fire once per visit — in-place retries do not re-fire them.");
            Assert.That(baseline, Does.Contain("probe flaky-exited=1"));
            Assert.That(baseline, Does.Contain("probe flaky-executions=3"));
            Assert.That(baseline, Does.Contain("probe after-entered=1"));
            Assert.That(baseline, Does.Contain("probe after-exited=1"));
        });
    }

    [Test]
    public async Task state_log_delivery_runs_identically()
    {
        // Pins the State.Log report bridge (spec 017): the sync machines wire the sync
        // callback, the async machines wire the async slot and Log falls back to it —
        // delivery and attribution must be identical either way.
        List<string> baseline =
            await ParityRunner.AssertFsmParityAsync(StateLogChain, ParityDrives.OneRunAsync);
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("log a hello-from-a"));
            Assert.That(baseline, Does.Contain("log b hello-from-b"));
        });
    }

    [Test]
    public async Task observer_throw_at_run_start_recovers_identically()
    {
        // Pins the run-start gate-repair hardening (spec 016) on all four surfaces: the
        // observer throw escapes to the caller, the machine repairs to Ready with the gate
        // released, and the next run completes with a full clean trace.
        List<string> baseline = await ParityRunner.AssertFsmParityAsync(
            LinearChain,
            static async (machine, trace) =>
            {
                try
                {
                    await machine.RunToEndAsync();
                    trace.Add("no-throw");
                }
                catch (Exception ex)
                {
                    trace.Add($"threw {ex.GetType().Name}");
                }

                trace.Add($"status-after-throw {machine.Status}");
                Result result = await machine.RunToEndAsync();
                ParityDrives.AppendSurface(machine, trace, result);
            },
            observerThrowOnceAt: "machine-started");

        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("threw InvalidOperationException"));
            Assert.That(baseline, Does.Contain("status-after-throw Ready"),
                "Both runtimes must repair the transient status and release the gate.");
            Assert.That(baseline, Does.Contain("run-result Success"));
        });
    }

    [Test]
    public async Task suspend_mid_run_resume_and_complete_runs_identically()
    {
        // The stepped adapters step once, suspend, resume onto a fresh machine over the same
        // graph, and finish; the full-run adapters provide the uninterrupted baseline.
        // Resume replays nothing, so the stitched trace must equal the uninterrupted one.
        List<string> baseline = await ParityRunner.AssertSuspendResumeParityAsync(LinearChain);
        Assert.That(baseline, Does.Contain("run-result Success"));
    }

    // ── Restart policies ────────────────────────────────────────────────

    [Test]
    public async Task restart_policy_auto_reruns_identically()
    {
        List<string> baseline = await ParityRunner.AssertFsmParityAsync(
            LinearChain,
            static async (machine, trace) =>
            {
                await ParityDrives.OneRunAsync(machine, trace);
                await ParityDrives.OneRunAsync(machine, trace);
            });

        Assert.That(baseline.Count(line => line == "machine-completed Success"), Is.EqualTo(2),
            "Auto restart re-runs the machine from scratch on the second call.");
    }

    [Test]
    public async Task restart_policy_manual_throws_identically()
    {
        List<string> baseline = await ParityRunner.AssertFsmParityAsync(
            LinearChain,
            static async (machine, trace) =>
            {
                machine.SetRestartPolicy(RestartPolicy.Manual);
                await ParityDrives.OneRunAsync(machine, trace);
                await ParityDrives.RunExpectingThrowAsync(machine, trace);
                trace.Add($"status-after-throw {machine.Status}");
            });

        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("final-status Completed"),
                "Manual restart keeps the terminal status until Reset().");
            Assert.That(baseline, Does.Contain("threw InvalidOperationException"));
            Assert.That(baseline, Does.Contain("status-after-throw Completed"));
        });
    }

    [Test]
    public async Task restart_policy_ignore_returns_cached_result_identically()
    {
        List<string> baseline = await ParityRunner.AssertFsmParityAsync(
            TerminalFailure,
            static async (machine, trace) =>
            {
                machine.SetRestartPolicy(RestartPolicy.Ignore);
                await ParityDrives.OneRunAsync(machine, trace);
                // The second attempt must return the cached terminal result while
                // producing zero observer events on every adapter.
                await ParityDrives.OneRunAsync(machine, trace);
            });

        Assert.Multiple(() =>
        {
            Assert.That(baseline.Count(line => line == "run-result Failure"), Is.EqualTo(2),
                "The ignored attempt reports the cached terminal result.");
            Assert.That(baseline.Count(line => line == "machine-completed Failure"), Is.EqualTo(1),
                "The ignored attempt fires no lifecycle events.");
            Assert.That(baseline, Does.Contain("final-status Failed"));
        });
    }

    // ── Composites (one shallow scenario; deep composite parity is covered
    //    by the dedicated suspend/history fixtures) ───────────────────────

    private static ParityScenario SubGraphComposite()
    {
        Graph child = GraphBuilder
            .StartWith(() => Result.Success).SetName("child-a")
            .To(() => Result.Success).SetName("child-b")
            .Build();
        Graph parent = GraphBuilder
            .StartWith(() => Result.Success).SetName("before")
            .SubGraph(ParallelStepMode.RunToJoin, child).SetName("nested")
            .To(() => Result.Success).SetName("after")
            .Build();
        return new ParityScenario { Graph = parent };
    }

    [Test]
    public async Task shallow_subgraph_composite_runs_identically()
    {
        // A sync RunToJoin child machine is executable from both runtimes (via the
        // sync-logic adapter under the async machine), so one recipe serves all adapters.
        List<string> baseline =
            await ParityRunner.AssertFsmParityAsync(SubGraphComposite, ParityDrives.OneRunAsync);
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("entered nested"));
            Assert.That(baseline, Does.Contain("exited nested"));
            Assert.That(baseline, Does.Contain("run-result Success"));
        });
    }
}
