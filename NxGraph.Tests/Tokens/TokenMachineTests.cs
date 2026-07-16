using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxGraph.Tests.Tokens;

/// <summary>
/// Behavior spec for the sync <see cref="TokenMachine"/> (spec 007): fork fan-out, join
/// merge/quorum semantics, per-token failure/retry, starvation rules, stepping modes, and
/// the fail-loud contract of fork/join nodes under the FSM runtimes.
/// </summary>
[TestFixture]
public class TokenMachineTests
{
    private static Func<Result> Log(List<string> log, string message) => () =>
    {
        log.Add(message);
        return Result.Success;
    };

    /// <summary>load → fork(a, b) → join(All 2) → finish.</summary>
    private static Graph Diamond(List<string> log)
    {
        JoinState join = new(JoinPolicy.All(2));
        return GraphBuilder.StartWith(Log(log, "load"))
            .ForkTo(
                b => b.To(Log(log, "a")).To(join).To(Log(log, "finish")),
                b => b.To(Log(log, "b")).To(join))
            .Build();
    }

    [Test]
    public void fork_join_all_runs_both_branches_and_joins()
    {
        List<string> log = [];
        TokenMachine machine = Diamond(log).ToTokenMachine();
        machine.SetStepMode(ParallelStepMode.RunToJoin);

        Result result = machine.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "load", "a", "b", "finish" }),
                "Round scheduling: both branches advance one node per round; the join fires when both arrive.");
        });
    }

    [Test]
    public void round_per_tick_returns_in_progress_between_rounds()
    {
        List<string> log = [];
        TokenMachine machine = Diamond(log).ToTokenMachine();

        Assert.Multiple(() =>
        {
            Assert.That(machine.Execute(), Is.EqualTo(Result.InProgress), "Round 1: load runs.");
            Assert.That(machine.Execute(), Is.EqualTo(Result.InProgress), "Round 2: branches run, join fires.");
            Assert.That(machine.Execute(), Is.EqualTo(Result.Success), "Round 3: finish runs; run drains.");
            Assert.That(log, Is.EqualTo(new[] { "load", "a", "b", "finish" }));
        });
    }

    [Test]
    public void any_join_is_a_merge_point_every_token_passes()
    {
        List<string> log = [];
        JoinState merge = new(JoinPolicy.Any);
        Graph graph = GraphBuilder.StartWith(Log(log, "load"))
            .ForkTo(
                b => b.To(Log(log, "a")).To(merge).To(Log(log, "after")),
                b => b.To(Log(log, "b")).To(merge))
            .Build();

        TokenMachine machine = graph.ToTokenMachine();
        machine.SetStepMode(ParallelStepMode.RunToJoin);

        Result result = machine.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log.Count(entry => entry == "after"), Is.EqualTo(2),
                "Any-join merges: both tokens pass through and re-enter the shared tail mid-graph.");
        });
    }

    [Test]
    public void quorum_join_fires_at_m_and_absorbs_leftovers()
    {
        List<string> log = [];
        List<(int Token, TokenRetireReason Reason)> retired = [];
        JoinState quorum = new(JoinPolicy.Quorum(2));
        Graph graph = GraphBuilder.StartWith(Log(log, "load"))
            .ForkTo(
                b => b.To(Log(log, "fast1")).To(quorum).To(Log(log, "after")),
                b => b.To(Log(log, "fast2")).To(quorum),
                b => b.To(Log(log, "slow1")).To(Log(log, "slow2")).To(quorum))
            .Build();

        TokenMachine machine = graph.ToTokenMachine(new RetireRecorder(retired));
        machine.SetStepMode(ParallelStepMode.RunToJoin);

        Result result = machine.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success),
                "The 2-of-3 quorum fires on the two fast branches; the slow leftover absorbs benignly.");
            Assert.That(log.Count(entry => entry == "after"), Is.EqualTo(1), "The quorum fires exactly once.");
            Assert.That(retired.Count(r => r.Reason == TokenRetireReason.Absorbed), Is.EqualTo(1),
                "The late third token retires as a benign quorum leftover.");
        });
    }

    [Test]
    public void all_join_starves_when_a_supplier_dies()
    {
        List<(int Token, TokenRetireReason Reason)> retired = [];
        JoinState join = new(JoinPolicy.All(2));
        Graph graph = GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => Result.Success).To(join).To(() => Result.Success),
                b => b.To(() => Result.Failure))
            .Build();

        TokenMachine machine = graph.ToTokenMachine(new RetireRecorder(retired));
        machine.SetStepMode(ParallelStepMode.RunToJoin);

        Result result = machine.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure), "A dead supplier starves the All-join.");
            Assert.That(retired.Count(r => r.Reason == TokenRetireReason.Failed), Is.EqualTo(1));
            Assert.That(retired.Count(r => r.Reason == TokenRetireReason.Starved), Is.EqualTo(1),
                "The waiting token starves at the join that never fired.");
        });
    }

    [Test]
    public void machine_fails_when_any_token_dies_but_others_run_to_completion()
    {
        List<string> log = [];
        Graph graph = GraphBuilder.StartWith(Log(log, "load"))
            .ForkTo(
                b => b.To(Log(log, "ok1")).To(Log(log, "ok2")),
                b => b.To(() => Result.Failure))
            .Build();

        TokenMachine machine = graph.ToTokenMachine();
        machine.SetStepMode(ParallelStepMode.RunToJoin);

        Result result = machine.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure), "A token died unjoined.");
            Assert.That(log, Is.EqualTo(new[] { "load", "ok1", "ok2" }),
                "The surviving branch runs to its natural end before the machine reports failure.");
        });
    }

    [Test]
    public void per_token_retry_consumes_attempts_in_place()
    {
        int attempts = 0;
        JoinState join = new(JoinPolicy.All(2));
        StateToken flakyChain = default;
        Graph graph = GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b =>
                {
                    flakyChain = b.To(() => ++attempts < 3 ? Result.Failure : Result.Success);
                    flakyChain.Retry(3);
                    return flakyChain.To(join);
                },
                b => b.To(() => Result.Success).To(join))
            .Build();

        TokenMachine machine = graph.ToTokenMachine();
        machine.SetStepMode(ParallelStepMode.RunToJoin);

        Result result = machine.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success), "The flaky node succeeds within its retry budget.");
            Assert.That(attempts, Is.EqualTo(3), "Two in-place retries after the first failure.");
        });
    }

    [Test]
    public void failure_edge_reroutes_the_failing_token_only()
    {
        List<string> log = [];
        JoinState join = new(JoinPolicy.All(2));
        Graph graph = GraphBuilder.StartWith(Log(log, "load"))
            .ForkTo(
                b => b.To(() =>
                    {
                        log.Add("broken");
                        return Result.Failure;
                    })
                    .OnError(new RelayState(Log(log, "recovered")))
                    .To(join),
                b => b.To(Log(log, "steady")).To(join).To(Log(log, "finish")))
            .Build();

        TokenMachine machine = graph.ToTokenMachine();
        machine.SetStepMode(ParallelStepMode.RunToJoin);

        Result result = machine.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure),
                "The recovered handler is terminal (not wired onward), so its token completes there — " +
                "but the steady token starves at the All-join, failing the run.");
            Assert.That(log, Does.Contain("recovered"), "The failing token rerouted to its failure handler.");
        });
    }

    [Test]
    public void failure_edge_rejoining_the_flow_keeps_the_run_green()
    {
        List<string> log = [];
        JoinState join = new(JoinPolicy.All(2));
        RelayState recovered = new(Log(log, "recovered"));
        GraphBuilder? builderCapture = null;
        Graph graph = GraphBuilder.StartWith(Log(log, "load"))
            .ForkTo(
                b =>
                {
                    StateToken broken = b.To(() =>
                    {
                        log.Add("broken");
                        return Result.Failure;
                    });
                    builderCapture = broken.Builder;
                    broken.OnError(recovered);
                    // Wire the handler onward into the join so the rerouted token reconverges.
                    NodeId handlerId = builderCapture.AddNode(recovered);
                    return builderCapture.TokenFor(handlerId).To(join);
                },
                b => b.To(Log(log, "steady")).To(join).To(Log(log, "finish")))
            .Build();

        TokenMachine machine = graph.ToTokenMachine();
        machine.SetStepMode(ParallelStepMode.RunToJoin);

        Result result = machine.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success),
                "The rerouted token reaches the join; both tokens join and the run completes.");
            Assert.That(log, Is.EqualTo(new[] { "load", "broken", "steady", "recovered", "finish" }));
        });
    }

    [Test]
    public void token_multiplicity_same_node_active_for_each_token()
    {
        int sharedRuns = 0;
        JoinState merge = new(JoinPolicy.Any);
        RelayState shared = new(() =>
        {
            sharedRuns++;
            return Result.Success;
        });

        Graph graph = GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(shared).To(merge),
                // Same head instance dedupes to the same node — the chain past it is already
                // wired by the first branch.
                b => b.To(shared))
            .Build();

        TokenMachine machine = graph.ToTokenMachine();
        machine.SetStepMode(ParallelStepMode.RunToJoin);

        Result result = machine.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(sharedRuns, Is.EqualTo(2),
                "Both fork branches dedupe to the same node; two tokens each execute it once.");
        });
    }

    [Test]
    public void fsm_runtimes_throw_loud_on_fork_nodes()
    {
        Graph graph = GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => Result.Success),
                b => b.To(() => Result.Success))
            .Build();

        StateMachine syncFsm = graph.ToStateMachine();
        AsyncStateMachine asyncFsm = graph.ToAsyncStateMachine();

        Assert.Multiple(() =>
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                Result r = Result.InProgress;
                while (r == Result.InProgress)
                {
                    r = syncFsm.Execute();
                }
            }, "The sync FSM cannot express multiple active nodes — fork must fail loud.");
            Assert.ThrowsAsync<NotSupportedException>(async () => await asyncFsm.ExecuteAsync(),
                "The async FSM must fail loud on fork nodes too.");
        });
    }

    [Test]
    public void pool_exhaustion_throws_and_fails_the_machine()
    {
        Graph graph = GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => Result.Success),
                b => b.To(() => Result.Success),
                b => b.To(() => Result.Success))
            .Build();

        TokenMachine machine = graph.ToTokenMachine(maxTokens: 2);
        machine.SetStepMode(ParallelStepMode.RunToJoin);
        machine.SetRestartPolicy(RestartPolicy.Manual);

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => machine.Execute());
            Assert.That(machine.Status, Is.EqualTo(ExecutionStatus.Failed));
        });
    }

    [Test]
    public void auto_restart_policy_allows_back_to_back_runs()
    {
        List<string> log = [];
        TokenMachine machine = Diamond(log).ToTokenMachine();
        machine.SetStepMode(ParallelStepMode.RunToJoin);

        Result first = machine.Execute();
        Result second = machine.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(Result.Success));
            Assert.That(second, Is.EqualTo(Result.Success));
            Assert.That(log.Count(entry => entry == "finish"), Is.EqualTo(2));
        });
    }

    [Test]
    public void token_machine_nests_as_a_node_of_a_sync_fsm()
    {
        List<string> log = [];
        TokenMachine child = Diamond(log).ToTokenMachine();
        child.SetStepMode(ParallelStepMode.RunToJoin);
        child.SetRestartPolicy(RestartPolicy.Manual);

        Graph parent = GraphBuilder.StartWith(Log(log, "before"))
            .To(child)
            .To(Log(log, "after"))
            .Build();

        StateMachine fsm = parent.ToStateMachine();
        fsm.SetStepMode(ParallelStepMode.RunToJoin);

        Result result = fsm.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "before", "load", "a", "b", "finish", "after" }),
                "The token subnet runs as one node of the parent FSM (RunToJoin).");
        });
    }

    [Test]
    public void ctor_fails_fast_on_async_only_nodes()
    {
        Graph graph = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success).Build();

        Assert.Throws<ArgumentException>(() => graph.ToTokenMachine(),
            "The sync token machine mirrors StateMachine's fail-fast on async-only logic.");
    }

    [Test]
    public void manual_restart_policy_requires_reset()
    {
        List<string> log = [];
        TokenMachine machine = Diamond(log).ToTokenMachine();
        machine.SetStepMode(ParallelStepMode.RunToJoin);
        machine.SetRestartPolicy(RestartPolicy.Manual);

        Assert.That(machine.Execute(), Is.EqualTo(Result.Success));
        Assert.Throws<InvalidOperationException>(() => machine.Execute());
        machine.Reset();
        Assert.That(machine.Execute(), Is.EqualTo(Result.Success));
    }

    private sealed class RetireRecorder(List<(int Token, TokenRetireReason Reason)> sink) : ITokenMachineObserver
    {
        public void OnTokenRetired(int tokenId, NodeId at, TokenRetireReason reason)
        {
            sink.Add((tokenId, reason));
        }
    }
}
