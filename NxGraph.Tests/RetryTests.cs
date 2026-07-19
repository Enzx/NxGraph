using System.Diagnostics;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
public class RetryTests
{
    [Test]
    public async Task async_node_failing_twice_then_succeeding_completes()
    {
        int executions = 0;
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ++executions < 3 ? ResultHelpers.Failure : ResultHelpers.Success)
            .Retry(maxAttempts: 3)
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(executions, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task async_retries_are_exhausted_and_machine_fails()
    {
        int executions = 0;
        Graph graph = GraphBuilder
            .StartWithAsync(_ =>
            {
                executions++;
                return ResultHelpers.Failure;
            })
            .Retry(maxAttempts: 2)
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(executions, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task exhausted_retries_route_through_the_failure_edge()
    {
        int executions = 0;
        bool cleanupRan = false;
        Graph graph = GraphBuilder
            .StartWithAsync(_ =>
            {
                executions++;
                return ResultHelpers.Failure;
            })
            .Retry(maxAttempts: 2)
            .OnErrorAsync(_ =>
            {
                cleanupRan = true;
                return ResultHelpers.Success;
            })
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(executions, Is.EqualTo(2));
            Assert.That(cleanupRan, Is.True);
        });
    }

    [Test]
    public async Task attempt_counter_resets_between_runs()
    {
        int executions = 0;
        Graph graph = GraphBuilder
            .StartWithAsync(_ =>
            {
                executions++;
                return ResultHelpers.Failure;
            })
            .Retry(maxAttempts: 2)
            .Build();

        var machine = graph.ToAsyncStateMachine();
        await machine.ExecuteAsync();
        await machine.ExecuteAsync();

        Assert.That(executions, Is.EqualTo(4), "Each run should get a fresh attempt budget.");
    }

    [Test]
    public async Task backoff_delay_is_applied_between_async_attempts()
    {
        int executions = 0;
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ++executions < 2 ? ResultHelpers.Failure : ResultHelpers.Success)
            .Retry(maxAttempts: 2, backoff: TimeSpan.FromMilliseconds(80))
            .Build();

        Stopwatch sw = Stopwatch.StartNew();
        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();
        sw.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(60),
                "The retry should have waited for the configured backoff.");
        });
    }

    // The end-to-end backoff tests below stay real-time by decision: a fake-clock seam for
    // the async retry path was considered and rejected for now — the repo keeps no
    // InternalsVisibleTo by policy (spec 004), and a public delay-scheduler hook is API
    // surface growth a test convenience does not justify. Elapsed-time assertions are
    // therefore lower-bound-only (Task.Delay never completes early; overshoot on a loaded
    // runner is unbounded, so upper bounds would be pure flake).

    [Test]
    [CancelAfter(10_000)]
    public async Task linear_backoff_is_applied_between_async_attempts(CancellationToken ct)
    {
        int executions = 0;
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ++executions < 3 ? ResultHelpers.Failure : ResultHelpers.Success)
            .Retry(maxAttempts: 3, backoff: TimeSpan.FromMilliseconds(40), backoffKind: BackoffKind.Linear)
            .Build();

        Stopwatch sw = Stopwatch.StartNew();
        Result result = await graph.ToAsyncStateMachine().ExecuteAsync(ct);
        sw.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(executions, Is.EqualTo(3));
            // Two failed attempts → linear delays of 1×40 ms and 2×40 ms = 120 ms total;
            // assert with the same 25% timer-skew margin the fixed-backoff test uses.
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(90),
                "Linear backoff must scale the machine-level delay with the failed-attempt count.");
        });
    }

    [Test]
    [CancelAfter(10_000)]
    public async Task exponential_backoff_is_applied_between_async_attempts(CancellationToken ct)
    {
        int executions = 0;
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ++executions < 4 ? ResultHelpers.Failure : ResultHelpers.Success)
            .Retry(maxAttempts: 4, backoff: TimeSpan.FromMilliseconds(20), backoffKind: BackoffKind.Exponential)
            .Build();

        Stopwatch sw = Stopwatch.StartNew();
        Result result = await graph.ToAsyncStateMachine().ExecuteAsync(ct);
        sw.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(executions, Is.EqualTo(4));
            // Three failed attempts → exponential delays of 20, 40, 80 ms = 140 ms total.
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(105),
                "Exponential backoff must double the machine-level delay per failed attempt.");
        });
    }

    [Test]
    public void sync_node_failing_twice_then_succeeding_completes()
    {
        int executions = 0;
        Graph graph = GraphBuilder
            .StartWith(() => ++executions < 3 ? Result.Failure : Result.Success)
            .Retry(maxAttempts: 3)
            .Build();

        StateMachine machine = graph.ToStateMachine();
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(executions, Is.EqualTo(3));
        });
    }

    [Test]
    public void sync_retries_are_exhausted_and_machine_fails()
    {
        int executions = 0;
        Graph graph = GraphBuilder
            .StartWith(() =>
            {
                executions++;
                return Result.Failure;
            })
            .Retry(maxAttempts: 2)
            .Build();

        StateMachine machine = graph.ToStateMachine();
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(executions, Is.EqualTo(2));
        });
    }

    [Test]
    public void retry_policy_delays_scale_by_backoff_kind()
    {
        RetryPolicy fixedPolicy = new(4, TimeSpan.FromSeconds(1));
        RetryPolicy linear = new(4, TimeSpan.FromSeconds(1), BackoffKind.Linear);
        RetryPolicy exponential = new(4, TimeSpan.FromSeconds(1), BackoffKind.Exponential);

        Assert.Multiple(() =>
        {
            Assert.That(fixedPolicy.DelayForAttempt(3), Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(linear.DelayForAttempt(3), Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(exponential.DelayForAttempt(3), Is.EqualTo(TimeSpan.FromSeconds(4)));
        });
    }

    [Test]
    public void zero_max_attempts_policy_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new RetryPolicy(0));
    }

    [Test]
    public void retry_on_unknown_node_throws()
    {
        GraphBuilder builder = new();
        builder.AddNode(new RelayState(() => Result.Success), isStart: true);

        Assert.Throws<InvalidOperationException>(() =>
            builder.SetRetryPolicy(new NodeId(7), new RetryPolicy(2)));
    }
}
