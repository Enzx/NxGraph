using System.Diagnostics;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// Sync twins of the async wait/timeout constructs: <see cref="WaitState"/> (multi-tick wait)
/// and <see cref="TimeoutState"/> (between-ticks deadline feeding the unified fault model).
/// Deterministic via an injected clock — no wall-clock assertions.
/// </summary>
[TestFixture]
public class SyncWaitTimeoutTests
{
    private sealed class FakeClock
    {
        public long Now;
        public void Advance(TimeSpan by) => Now += (long)(by.TotalSeconds * Stopwatch.Frequency);
        public Func<long> Func => () => Now;
    }

    // ── WaitState ─────────────────────────────────────────────────────────

    [Test]
    public void wait_returns_in_progress_until_the_duration_elapses()
    {
        FakeClock clock = new();
        WaitState wait = new(TimeSpan.FromSeconds(1), clock.Func);

        Assert.That(wait.Execute(), Is.EqualTo(Result.InProgress), "Tick 1 records the start.");

        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.That(wait.Execute(), Is.EqualTo(Result.InProgress), "Halfway — still waiting.");

        clock.Advance(TimeSpan.FromMilliseconds(600));
        Assert.That(wait.Execute(), Is.EqualTo(Result.Success), "Past the duration — done.");
    }

    [Test]
    public void zero_and_negative_durations_complete_immediately()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new WaitState(TimeSpan.Zero).Execute(), Is.EqualTo(Result.Success));
            Assert.That(new WaitState(TimeSpan.FromSeconds(-1)).Execute(), Is.EqualTo(Result.Success));
        });
    }

    [Test]
    public void reentering_a_completed_wait_starts_a_fresh_wait()
    {
        FakeClock clock = new();
        WaitState wait = new(TimeSpan.FromSeconds(1), clock.Func);

        wait.Execute();
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.That(wait.Execute(), Is.EqualTo(Result.Success));

        // Same instance revisited: the recorded timestamp was cleared on completion.
        Assert.That(wait.Execute(), Is.EqualTo(Result.InProgress),
            "Re-entry records a fresh start instead of reusing the elapsed one.");
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.That(wait.Execute(), Is.EqualTo(Result.Success));
    }

    [Test]
    public void wait_for_dsl_ticks_under_the_sync_machine()
    {
        int after = 0;
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success)
            .WaitFor(TimeSpan.FromMilliseconds(30))
            .To(() =>
            {
                after++;
                return Result.Success;
            })
            .Build();

        StateMachine machine = graph.ToStateMachine();
        Result first = machine.Execute(); // start node
        Result second = machine.Execute(); // wait's first tick — cannot have elapsed yet

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(Result.InProgress));
            Assert.That(second, Is.EqualTo(Result.InProgress), "The wait spans ticks.");
            Assert.That(after, Is.Zero);
        });

        Result result = Result.InProgress;
        Stopwatch guard = Stopwatch.StartNew();
        while (result == Result.InProgress && guard.ElapsedMilliseconds < 5_000)
        {
            result = machine.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(after, Is.EqualTo(1));
        });
    }

    [Test]
    public void wait_for_is_rejected_by_the_async_runtime()
    {
        Graph graph = GraphBuilder
            .Start()
            .WaitFor(TimeSpan.FromSeconds(1))
            .Build();

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await graph.ToAsyncStateMachine().ExecuteAsync(),
            "Node-level InProgress is reserved in the async runtime — WaitFor is sync-only.");
    }

    // ── TimeoutState ──────────────────────────────────────────────────────

    private sealed class CountingInProgressLogic : ILogic
    {
        public int Executions;
        public Result Next = Result.InProgress;

        public Result Execute()
        {
            Executions++;
            return Next;
        }
    }

    [Test]
    public void timeout_produces_failure_when_the_inner_logic_overstays()
    {
        FakeClock clock = new();
        CountingInProgressLogic inner = new();
        TimeoutState state = new(inner, TimeSpan.FromSeconds(1), TimeoutBehavior.Fail, clock.Func);

        Assert.That(state.Execute(), Is.EqualTo(Result.InProgress));

        clock.Advance(TimeSpan.FromSeconds(2));
        Result result = state.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(result.Message, Does.Contain("timed out"));
            Assert.That(inner.Executions, Is.EqualTo(2),
                "The deadline is detected between ticks — the inner logic ran once more on the " +
                "tick that noticed the overrun; the sync runtime cannot interrupt mid-execution.");
        });
    }

    [Test]
    public void timeout_throws_when_behavior_is_throw()
    {
        FakeClock clock = new();
        TimeoutState state = new(new CountingInProgressLogic(), TimeSpan.FromSeconds(1),
            TimeoutBehavior.Throw, clock.Func);

        Assert.That(state.Execute(), Is.EqualTo(Result.InProgress));

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Throws<TimeoutException>(() => state.Execute());
    }

    [Test]
    public void inner_completion_before_the_deadline_passes_through()
    {
        FakeClock clock = new();
        CountingInProgressLogic inner = new() { Next = Result.Success };
        TimeoutState state = new(inner, TimeSpan.FromSeconds(1), TimeoutBehavior.Fail, clock.Func);

        Assert.That(state.Execute(), Is.EqualTo(Result.Success));

        inner.Next = Result.Failure;
        Assert.That(state.Execute(), Is.EqualTo(Result.Failure),
            "Terminal inner results pass through untouched — only overstaying InProgress times out.");
    }

    [Test]
    public void a_retried_timeout_gets_a_fresh_deadline()
    {
        FakeClock clock = new();
        CountingInProgressLogic inner = new();
        TimeoutState state = new(inner, TimeSpan.FromSeconds(1), TimeoutBehavior.Fail, clock.Func);

        state.Execute();
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.That(state.Execute(), Is.EqualTo(Result.Failure), "First visit times out.");

        // In-place retry: the wrapper re-arms; without advancing the clock there is no overrun.
        inner.Next = Result.Success;
        Assert.That(state.Execute(), Is.EqualTo(Result.Success), "The retry starts a fresh deadline.");
    }

    [Test]
    public void non_positive_timeouts_are_rejected()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _ = new TimeoutState(new CountingInProgressLogic(), TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _ = new TimeoutState(new CountingInProgressLogic(), TimeSpan.FromMilliseconds(-1)));
        });
    }

    [Test]
    public void timeout_routes_through_the_failure_edge_under_the_sync_machine()
    {
        bool cleanupRan = false;
        Graph graph = GraphBuilder
            .Start()
            .ToWithTimeout(TimeSpan.FromMilliseconds(30), new CountingInProgressLogic())
            .OnError(new RelayState(() =>
            {
                cleanupRan = true;
                return Result.Success;
            }))
            .Build();

        StateMachine machine = graph.ToStateMachine();
        Result result = Result.InProgress;
        Stopwatch guard = Stopwatch.StartNew();
        while (result == Result.InProgress && guard.ElapsedMilliseconds < 5_000)
        {
            result = machine.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(cleanupRan, Is.True, "The timeout flowed through the unified fault model.");
        });
    }

    [Test]
    public void to_with_timeout_lambda_runs_the_happy_path()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success)
            .ToWithTimeout(TimeSpan.FromSeconds(5), () => Result.Success)
            .Build();

        StateMachine machine = graph.ToStateMachine();
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        Assert.That(result, Is.EqualTo(Result.Success));
    }
}
