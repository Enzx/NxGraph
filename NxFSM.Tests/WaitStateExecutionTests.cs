using System.Diagnostics;
using NxFSM.Authoring;
using NxFSM.Fsm;

namespace NxFSM.Tests;

[TestFixture]
[Category("wait_state_execution")]
public class WaitStateExecutionTests
{
    [Test]
    public async Task wait_state_should_complete_after_delay()
    {
        const int delay = 1;
        const float errorMargin = 0.1f;
        StateMachine fsm = GraphBuilder
            .Start()
            .WaitFor(delay.Seconds())
            .To(_ => ResultHelpers.Success)
            .ToStateMachine();

        Stopwatch stopwatch = Stopwatch.StartNew();
        Result result = await fsm.ExecuteAsync();
        stopwatch.Stop();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.GreaterThanOrEqualTo(delay));
            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(delay + errorMargin));
        });
    }

    [Test]
    public async Task wait_state_should_handle_zero_delay_immediately()
    {
        StateMachine fsm = GraphBuilder
            .Start()
            .WaitFor(0.Seconds())
            .To(_ => ResultHelpers.Success)
            .ToStateMachine();

        Stopwatch stopwatch = Stopwatch.StartNew();
        Result result = await fsm.ExecuteAsync();
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(stopwatch.Elapsed.TotalMilliseconds, Is.LessThan(10)); // should be nearly immediate
        });
    }

    [Test]
    public async Task wait_state_should_handle_negative_delay_immediately()
    {
        StateMachine fsm = GraphBuilder
            .Start()
            .WaitFor(-1.Seconds()) // negative delay should be treated as immediate
            .To(_ => ResultHelpers.Success)
            .ToStateMachine();

        Stopwatch stopwatch = Stopwatch.StartNew();
        Result result = await fsm.ExecuteAsync();
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(stopwatch.Elapsed.TotalMilliseconds, Is.LessThan(10)); // should be nearly immediate
        });
    }
    
    [Test]
    public async Task wait_state_should_fail_if_cancelled()
    {
        CancellationTokenSource cts = new ();
        StateMachine fsm = GraphBuilder
            .Start()
            .WaitFor(5.Seconds(), cts.Token)
            .To(_ => ResultHelpers.Success)
            .ToStateMachine();

        // Cancel immediately
        await cts.CancelAsync();

        // ReSharper disable once MethodSupportsCancellation
        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Failure));
    }
}