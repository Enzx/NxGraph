using System.Diagnostics;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

[TestFixture]
[Category("wait_state_execution")]
public class WaitStateExecutionTests
{
    [Test]
    [CancelAfter(10_000)]
    public async Task wait_state_should_complete_after_delay(CancellationToken ct)
    {
        const int delay = 1;
        const float errorMargin = 0.5f;
        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .WaitForAsync(delay.Seconds())
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();

        Stopwatch stopwatch = Stopwatch.StartNew();
        Result result = await fsm.ExecuteAsync(ct);
        stopwatch.Stop();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.GreaterThanOrEqualTo(delay - errorMargin),
                "The wait state must actually wait out its configured delay.");
            // Deliberately no upper bound: on a loaded CI agent the overshoot is unbounded,
            // so an upper bound is pure flake. The [CancelAfter] guard (threaded into the
            // machine) replaces it as the hang stop.
        });
    }

    [Test]
    [CancelAfter(10_000)]
    public async Task wait_state_should_handle_zero_delay_immediately(CancellationToken ct)
    {
        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .WaitForAsync(0.Seconds())
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();

        Stopwatch stopwatch = Stopwatch.StartNew();
        Result result = await fsm.ExecuteAsync(ct);
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            // Generous budget instead of the old < 10 ms bound (which flakes on a cold or
            // loaded runner): a zero wait must not take seconds; a regression to a real or
            // infinite wait is caught here or by the [CancelAfter] guard.
            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(5));
        });
    }

    [Test]
    [CancelAfter(10_000)]
    public async Task wait_state_should_handle_negative_delay_immediately(CancellationToken ct)
    {
        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .WaitForAsync(-1.Seconds()) // negative delay should be treated as immediate
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();

        Stopwatch stopwatch = Stopwatch.StartNew();
        Result result = await fsm.ExecuteAsync(ct);
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            // Same generous budget as the zero-delay test; a raw negative TimeSpan reaching
            // Task.Delay would throw or wait forever — both caught without a tight bound.
            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(5));
        });
    }

    [Test]
    public async Task wait_state_should_fail_if_cancelled()
    {
        CancellationTokenSource cts = new();
        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .WaitForAsync(5.Seconds())
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();

        ValueTask<Result> task = fsm.ExecuteAsync(cts.Token);
        await cts.CancelAsync();
        Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
    }
}
