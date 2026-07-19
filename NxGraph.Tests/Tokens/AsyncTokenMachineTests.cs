using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxGraph.Tests.Tokens;

/// <summary>
/// Async twin of <see cref="TokenMachineTests"/> (runtime parity): same fork/join/quorum,
/// per-token failure, and starvation semantics, plus the async-specific mechanics —
/// <c>StepAsync</c> round stepping, cancellation, retry backoff, and the node-level
/// <see cref="Result.InProgress"/> rejection.
/// </summary>
[TestFixture]
public class AsyncTokenMachineTests
{
    private static Func<CancellationToken, ValueTask<Result>> Log(List<string> log, string message) => _ =>
    {
        log.Add(message);
        return ResultHelpers.Success;
    };

    /// <summary>load → fork(a, b) → join(All 2) → finish.</summary>
    private static Graph Diamond(List<string> log)
    {
        JoinState join = new(JoinPolicy.All(2));
        return GraphBuilder.StartWithAsync(Log(log, "load"))
            .ForkTo(
                b => b.ToAsync(Log(log, "a")).To(join).ToAsync(Log(log, "finish")),
                b => b.ToAsync(Log(log, "b")).To(join))
            .Build();
    }

    [Test]
    public async Task fork_join_all_runs_both_branches_and_joins()
    {
        List<string> log = [];
        AsyncTokenMachine machine = Diamond(log).ToAsyncTokenMachine();

        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "load", "a", "b", "finish" }),
                "Same interleaving as the sync twin: one node per token per round.");
        });
    }

    [Test]
    public async Task step_async_advances_one_round_per_call()
    {
        List<string> log = [];
        AsyncTokenMachine machine = Diamond(log).ToAsyncTokenMachine();

        Result first = await machine.StepAsync();
        Result second = await machine.StepAsync();
        Result third = await machine.StepAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(Result.InProgress), "Round 1: load runs.");
            Assert.That(second, Is.EqualTo(Result.InProgress), "Round 2: branches run, join fires.");
            Assert.That(third, Is.EqualTo(Result.Success), "Round 3: finish runs; run drains.");
            Assert.That(log, Is.EqualTo(new[] { "load", "a", "b", "finish" }));
        });
    }

    [Test]
    public async Task any_join_is_a_merge_point_every_token_passes()
    {
        List<string> log = [];
        JoinState merge = new(JoinPolicy.Any);
        Graph graph = GraphBuilder.StartWithAsync(Log(log, "load"))
            .ForkTo(
                b => b.ToAsync(Log(log, "a")).To(merge).ToAsync(Log(log, "after")),
                b => b.ToAsync(Log(log, "b")).To(merge))
            .Build();

        Result result = await graph.ToAsyncTokenMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log.Count(entry => entry == "after"), Is.EqualTo(2),
                "Any-join merges: both tokens rejoin the shared tail mid-graph.");
        });
    }

    [Test]
    public async Task quorum_join_fires_at_m_and_absorbs_leftovers()
    {
        List<string> log = [];
        List<TokenRetireReason> reasons = [];
        JoinState quorum = new(JoinPolicy.Quorum(2));
        Graph graph = GraphBuilder.StartWithAsync(Log(log, "load"))
            .ForkTo(
                b => b.ToAsync(Log(log, "fast1")).To(quorum).ToAsync(Log(log, "after")),
                b => b.ToAsync(Log(log, "fast2")).To(quorum),
                b => b.ToAsync(Log(log, "slow1")).ToAsync(Log(log, "slow2")).To(quorum))
            .Build();

        Result result = await graph.ToAsyncTokenMachine(new RetireRecorder(reasons)).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log.Count(entry => entry == "after"), Is.EqualTo(1));
            Assert.That(reasons.Count(r => r == TokenRetireReason.Absorbed), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task all_join_starves_when_a_supplier_dies()
    {
        List<TokenRetireReason> reasons = [];
        JoinState join = new(JoinPolicy.All(2));
        Graph graph = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success)
            .ForkTo(
                b => b.ToAsync(_ => ResultHelpers.Success).To(join).ToAsync(_ => ResultHelpers.Success),
                b => b.ToAsync(_ => new ValueTask<Result>(Result.Failure)))
            .Build();

        Result result = await graph.ToAsyncTokenMachine(new RetireRecorder(reasons)).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(reasons.Count(r => r == TokenRetireReason.Failed), Is.EqualTo(1));
            Assert.That(reasons.Count(r => r == TokenRetireReason.Starved), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task per_token_retry_honors_backoff_and_succeeds()
    {
        int attempts = 0;
        JoinState join = new(JoinPolicy.All(2));
        Graph graph = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success)
            .ForkTo(
                b => b.ToAsync(_ => new ValueTask<Result>(++attempts < 3 ? Result.Failure : Result.Success))
                    .Retry(3, TimeSpan.FromMilliseconds(1))
                    .To(join),
                b => b.ToAsync(_ => ResultHelpers.Success).To(join))
            .Build();

        Result result = await graph.ToAsyncTokenMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(attempts, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task failure_edge_reroutes_and_rejoins()
    {
        List<string> log = [];
        JoinState join = new(JoinPolicy.All(2));
        Fsm.Async.AsyncRelayState recovered = new(Log(log, "recovered"));
        Graph graph = GraphBuilder.StartWithAsync(Log(log, "load"))
            .ForkTo(
                b =>
                {
                    StateToken broken = b.ToAsync(_ =>
                    {
                        log.Add("broken");
                        return new ValueTask<Result>(Result.Failure);
                    });
                    broken.OnErrorAsync(recovered);
                    NodeId handlerId = broken.Builder.AddNode(recovered);
                    return broken.Builder.TokenFor(handlerId).To(join);
                },
                b => b.ToAsync(Log(log, "steady")).To(join).ToAsync(Log(log, "finish")))
            .Build();

        Result result = await graph.ToAsyncTokenMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "load", "broken", "steady", "recovered", "finish" }));
        });
    }

    [Test]
    public void node_level_in_progress_is_rejected()
    {
        Graph graph = GraphBuilder.StartWithAsync(_ => new ValueTask<Result>(Result.InProgress)).Build();
        AsyncTokenMachine machine = graph.ToAsyncTokenMachine();

        Assert.ThrowsAsync<InvalidOperationException>(async () => await machine.ExecuteAsync(),
            "Async node logic must never return InProgress — same contract as the async FSM.");
    }

    [Test]
    public async Task token_multiplicity_same_node_active_for_each_token()
    {
        int sharedRuns = 0;
        JoinState merge = new(JoinPolicy.Any);
        Fsm.Async.AsyncRelayState shared = new(_ =>
        {
            sharedRuns++;
            return ResultHelpers.Success;
        });

        Graph graph = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success)
            .ForkTo(
                b => b.ToAsync(shared).To(merge),
                // Same head instance dedupes to the same node — the chain past it is already
                // wired by the first branch.
                b => b.ToAsync(shared))
            .Build();

        Result result = await graph.ToAsyncTokenMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(sharedRuns, Is.EqualTo(2),
                "Both fork branches dedupe to the same node; two tokens each execute it once.");
        });
    }

    [Test]
    public void pool_exhaustion_throws_and_fails_the_machine()
    {
        Graph graph = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success)
            .ForkTo(
                b => b.ToAsync(_ => ResultHelpers.Success),
                b => b.ToAsync(_ => ResultHelpers.Success),
                b => b.ToAsync(_ => ResultHelpers.Success))
            .Build();

        AsyncTokenMachine machine = graph.ToAsyncTokenMachine(maxTokens: 2);
        machine.SetRestartPolicy(RestartPolicy.Manual);

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await machine.ExecuteAsync());
            Assert.That(machine.Status, Is.EqualTo(ExecutionStatus.Failed));
        });
    }

    [Test]
    public void cancellation_flows_out_and_cancels_the_machine()
    {
        using CancellationTokenSource cts = new();
        // Bounded wait (30 s, not Timeout.InfiniteTimeSpan) as the hang backstop: if token
        // plumbing regresses, the node completes and the assertions go red in bounded time
        // instead of hanging the suite.
        Graph graph = GraphBuilder.StartWithAsync(async ct =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return Result.Success;
            })
            .Build();

        AsyncTokenMachine machine = graph.ToAsyncTokenMachine();
        machine.SetRestartPolicy(RestartPolicy.Manual);

        cts.CancelAfter(TimeSpan.FromMilliseconds(50));
        Assert.CatchAsync<OperationCanceledException>(async () => await machine.ExecuteAsync(cts.Token));
        Assert.That(machine.Status, Is.EqualTo(ExecutionStatus.Cancelled));
    }

    [Test]
    public async Task snapshots_are_interchangeable_between_token_runtimes()
    {
        // Drive the sync machine two rounds in (one token parked at the join, one mid-branch),
        // suspend, and finish the flow on a fresh async machine — and vice versa.
        List<string> log = [];
        JoinState join = new(JoinPolicy.All(2));

        Graph BuildGraph() => GraphBuilder.StartWith(() =>
            {
                log.Add("load");
                return Result.Success;
            })
            .ForkTo(
                b => b.To(() =>
                {
                    log.Add("a");
                    return Result.Success;
                }).To(join).To(() =>
                {
                    log.Add("finish");
                    return Result.Success;
                }),
                b => b.To(() =>
                {
                    log.Add("b1");
                    return Result.Success;
                }).To(() =>
                {
                    log.Add("b2");
                    return Result.Success;
                }).To(join))
            .Build();

        TokenMachine sync = BuildGraph().ToTokenMachine();
        Assert.That(sync.Execute(), Is.EqualTo(Result.InProgress)); // round 1: load
        Assert.That(sync.Execute(), Is.EqualTo(Result.InProgress)); // round 2: a parks, b1 runs

        TokenMachineSnapshot snapshot = sync.Suspend();
        Assert.That(snapshot.MidRun, Is.True);
        Assert.That(snapshot.Tokens, Has.Length.EqualTo(2));

        AsyncTokenMachine resumed = BuildGraph().ToAsyncTokenMachine();
        resumed.Resume(snapshot);

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = await resumed.StepAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "load", "a", "b1", "b2", "finish" }),
                "The resumed machine continues exactly where the suspended one stopped.");
        });
    }

    private sealed class RetireRecorder(List<TokenRetireReason> sink) : IAsyncTokenMachineObserver
    {
        public ValueTask OnTokenRetired(int tokenId, NodeId at, TokenRetireReason reason,
            CancellationToken ct = default)
        {
            sink.Add(reason);
            return default;
        }
    }
}
