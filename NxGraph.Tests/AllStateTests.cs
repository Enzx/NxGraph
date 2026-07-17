using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// In-node concurrency (spec 012): <c>.ToAllAsync(...)</c> runs N works truly concurrently
/// inside one node and joins on all of them (Success iff every work succeeded); failures never
/// cancel siblings — all works settle before the combine. The sync twin <c>.ToAll(...)</c>
/// runs the works sequentially in one tick with identical join semantics and also runs under
/// the async machine via the adapter.
/// </summary>
[TestFixture]
public class AllStateTests
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

    // ── Concurrency is real: deterministic handshake, no timing asserts ───

    [Test]
    public async Task async_works_genuinely_overlap_in_time()
    {
        // Each work signals its own start, then awaits the other's signal. The node completes
        // only if both works were started before either finished — i.e. under true overlap.
        // Sequential execution (await one work to completion before starting the next) would
        // never finish; no timing assertions are involved.
        TaskCompletionSource first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource second = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Graph graph = GraphBuilder
            .Start()
            .ToAllAsync(
                async (_, _) =>
                {
                    first.SetResult();
                    await second.Task;
                    return Result.Success;
                },
                async (_, _) =>
                {
                    second.SetResult();
                    await first.Task;
                    return Result.Success;
                })
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Success));
    }

    // ── Join semantics: all-success / one-failure, all works always run ───

    [Test]
    public async Task async_all_success_joins_to_success()
    {
        Graph graph = GraphBuilder
            .Start()
            .ToAllAsync(
                (_, _) => ResultHelpers.Success,
                (_, _) => ResultHelpers.Success,
                (_, _) => ResultHelpers.Success)
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public async Task async_one_failure_joins_to_failure_after_all_works_ran()
    {
        bool[] ran = new bool[3];

        Graph graph = GraphBuilder
            .Start()
            .ToAllAsync(
                (_, _) =>
                {
                    ran[0] = true;
                    return ResultHelpers.Success;
                },
                (_, _) =>
                {
                    ran[1] = true;
                    return ResultHelpers.Failure;
                },
                (_, _) =>
                {
                    ran[2] = true;
                    return ResultHelpers.Success;
                })
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(ran, Is.All.True, "A failing work must not abort or cancel its siblings.");
        });
    }

    // ── One node failure feeding the unified fault model ──────────────────

    [Test]
    public async Task async_failure_consumes_retry_and_rerun_covers_all_works()
    {
        int flakyCalls = 0;
        int steadyCalls = 0;

        Graph graph = GraphBuilder
            .Start()
            .ToAllAsync(
                (_, _) => ++flakyCalls < 2 ? ResultHelpers.Failure : ResultHelpers.Success,
                (_, _) =>
                {
                    steadyCalls++;
                    return ResultHelpers.Success;
                })
            .Retry(maxAttempts: 2)
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(flakyCalls, Is.EqualTo(2), "The retry re-runs the whole node.");
            Assert.That(steadyCalls, Is.EqualTo(2), "All works re-run on retry, succeeded ones included.");
        });
    }

    [Test]
    public async Task async_failure_follows_the_failure_edge()
    {
        bool handled = false;

        Graph graph = GraphBuilder
            .Start()
            .ToAllAsync(
                (_, _) => ResultHelpers.Success,
                (_, _) => ResultHelpers.Failure)
            .OnErrorAsync(_ =>
            {
                handled = true;
                return ResultHelpers.Success;
            })
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(handled, Is.True, "The join failure is one ordinary node failure.");
        });
    }

    // ── Exceptions and cancellation ───────────────────────────────────────

    [Test]
    public void async_exception_propagates_after_siblings_settle()
    {
        bool siblingCompleted = false;

        Graph graph = GraphBuilder
            .Start()
            .ToAllAsync(
                (_, _) => throw new InvalidOperationException("boom"),
                async (_, _) =>
                {
                    await Task.Yield();
                    siblingCompleted = true;
                    return Result.Success;
                })
            .Build();

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await graph.ToAsyncStateMachine().ExecuteAsync());
        Assert.That(siblingCompleted, Is.True,
            "WhenAll semantics: the exception surfaces only after every work settled.");
    }

    [Test]
    public void async_machine_cancellation_cancels_all_works()
    {
        bool[] cancelled = new bool[2];
        CancellationTokenSource cts = new(10);

        Graph graph = GraphBuilder
            .Start()
            .ToAllAsync(
                async (_, ct) =>
                {
                    try
                    {
                        await Task.Delay(System.Threading.Timeout.Infinite, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled[0] = true;
                        throw;
                    }

                    return Result.Success;
                },
                async (_, ct) =>
                {
                    try
                    {
                        await Task.Delay(System.Threading.Timeout.Infinite, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled[1] = true;
                        throw;
                    }

                    return Result.Success;
                })
            .Build();

        Assert.That(async () => await graph.ToAsyncStateMachine().ExecuteAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(cancelled, Is.All.True, "The node's token reaches and cancels every work.");
    }

    // ── Disjoint keys: concurrent writes to distinct slots land correctly ─

    [Test]
    public async Task async_disjoint_key_writes_from_overlapping_works_land_correctly()
    {
        BlackboardSchema io = new("all-io");
        BlackboardKey<int> left = io.Register<int>("left");
        BlackboardKey<int> right = io.Register<int>("right");
        Blackboard board = new(io);

        // The handshake forces both works to be mid-flight while writing, so the writes
        // genuinely overlap — distinct keys mean distinct slots, which is the contract.
        TaskCompletionSource leftStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource rightStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Graph graph = GraphBuilder
            .Start()
            .ToAllAsync(
                async (bb, _) =>
                {
                    leftStarted.SetResult();
                    await rightStarted.Task;
                    bb.Set(left, 21);
                    return Result.Success;
                },
                async (bb, _) =>
                {
                    rightStarted.SetResult();
                    await leftStarted.Task;
                    bb.Set(right, 42);
                    return Result.Success;
                })
            .ToAsync((bb, _) =>
                bb.Get(left) == 21 && bb.Get(right) == 42 ? ResultHelpers.Success : ResultHelpers.Failure)
            .WithSchema(io)
            .Build();

        Result result = await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success), "The next node reads both port values.");
            Assert.That(board.Get(left), Is.EqualTo(21));
            Assert.That(board.Get(right), Is.EqualTo(42));
        });
    }

    // ── Sync twin: sequential order, identical join, both machines ────────

    [Test]
    public void sync_works_run_sequentially_in_declaration_order()
    {
        List<string> order = [];

        Graph graph = GraphBuilder
            .Start()
            .ToAll(
                _ =>
                {
                    order.Add("a");
                    return Result.Success;
                },
                _ =>
                {
                    order.Add("b");
                    return Result.Success;
                },
                _ =>
                {
                    order.Add("c");
                    return Result.Success;
                })
            .Build();

        Result result = RunToCompletion(graph.ToStateMachine());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(order, Is.EqualTo(new[] { "a", "b", "c" }), "Sequential, in declaration order.");
        });
    }

    [Test]
    public void sync_one_failure_joins_to_failure_after_all_works_ran()
    {
        bool[] ran = new bool[3];

        Graph graph = GraphBuilder
            .Start()
            .ToAll(
                _ =>
                {
                    ran[0] = true;
                    return Result.Success;
                },
                _ =>
                {
                    ran[1] = true;
                    return Result.Failure;
                },
                _ =>
                {
                    ran[2] = true;
                    return Result.Success;
                })
            .Build();

        Result result = RunToCompletion(graph.ToStateMachine());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(ran, Is.All.True, "No early abort: every work runs even after a Failure.");
        });
    }

    [Test]
    public async Task sync_all_node_runs_under_both_machines()
    {
        int calls = 0;

        Graph graph = GraphBuilder
            .Start()
            .ToAll(
                _ =>
                {
                    calls++;
                    return Result.Success;
                },
                _ =>
                {
                    calls++;
                    return Result.Success;
                })
            .Build();

        Result syncResult = RunToCompletion(graph.ToStateMachine());
        Result asyncResult = await graph.ToAsyncStateMachine().ExecuteAsync(); // adapter path

        Assert.Multiple(() =>
        {
            Assert.That(syncResult, Is.EqualTo(Result.Success));
            Assert.That(asyncResult, Is.EqualTo(Result.Success));
            Assert.That(calls, Is.EqualTo(4), "Both machines ran both works once.");
        });
    }

    // ── Wiring-time validation ────────────────────────────────────────────

    [Test]
    public void empty_works_throw_at_wiring_time()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => GraphBuilder.Start().ToAllAsync(),
                Throws.ArgumentException, "async, empty");
            Assert.That(() => GraphBuilder.Start().ToAll(),
                Throws.ArgumentException, "sync, empty");
        });
    }

    [Test]
    public void null_work_entries_throw_at_wiring_time()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => GraphBuilder.Start().ToAllAsync((_, _) => ResultHelpers.Success, null!),
                Throws.ArgumentException, "async, null entry");
            Assert.That(() => GraphBuilder.Start().ToAll(_ => Result.Success, null!),
                Throws.ArgumentException, "sync, null entry");
        });
    }

    [Test]
    public async Task single_work_is_legal()
    {
        Graph asyncGraph = GraphBuilder
            .Start()
            .ToAllAsync((_, _) => ResultHelpers.Success)
            .Build();
        Graph syncGraph = GraphBuilder
            .Start()
            .ToAll(_ => Result.Success)
            .Build();

        Result asyncResult = await asyncGraph.ToAsyncStateMachine().ExecuteAsync();
        Result syncResult = RunToCompletion(syncGraph.ToStateMachine());

        Assert.Multiple(() =>
        {
            Assert.That(asyncResult, Is.EqualTo(Result.Success));
            Assert.That(syncResult, Is.EqualTo(Result.Success));
        });
    }
}
