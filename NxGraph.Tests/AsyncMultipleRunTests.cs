using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

[TestFixture]
[Category("async_multiple_runs")]
public class AsyncMultipleRunTests
{
    [Test]
    public async Task should_support_multiple_sequential_runs_with_auto_reset()
    {
        int counter = 0;
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(_ =>
            {
                counter++;
                return ResultHelpers.Success;
            }))
            .ToAsyncStateMachine();
        fsm.SetAutoReset(true);

        for (int i = 0; i < 100; i++)
        {
            Result r = await fsm.ExecuteAsync();
            Assert.That(r, Is.EqualTo(Result.Success));
        }

        Assert.That(counter, Is.EqualTo(100));
    }

    [Test]
    public async Task should_support_multiple_runs_with_manual_reset()
    {
        int counter = 0;
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(_ =>
            {
                counter++;
                return ResultHelpers.Success;
            }))
            .ToAsyncStateMachine();
        fsm.SetAutoReset(false);

        for (int i = 0; i < 10; i++)
        {
            Result r = await fsm.ExecuteAsync();
            Assert.That(r, Is.EqualTo(Result.Success));
            await fsm.Reset();
        }

        Assert.That(counter, Is.EqualTo(10));
    }

    [Test]
    public async Task should_traverse_three_states_async()
    {
        int counter = 0;
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(_ => { counter++; return ResultHelpers.Success; }))
            .ToAsync(new AsyncRelayState(_ => { counter++; return ResultHelpers.Success; }))
            .ToAsync(new AsyncRelayState(_ => { counter++; return ResultHelpers.Success; }))
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(counter, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task alternating_success_and_failure_runs_should_work()
    {
        int runCount = 0;
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(_ =>
            {
                runCount++;
                return runCount % 2 == 0
                    ? ResultHelpers.Failure
                    : ResultHelpers.Success;
            }))
            .ToAsyncStateMachine();
        fsm.SetAutoReset(true);

        Result r1 = await fsm.ExecuteAsync(); // runCount=1 -> Success
        Result r2 = await fsm.ExecuteAsync(); // runCount=2 -> Failure
        Result r3 = await fsm.ExecuteAsync(); // runCount=3 -> Success

        Assert.Multiple(() =>
        {
            Assert.That(r1, Is.EqualTo(Result.Success));
            Assert.That(r2, Is.EqualTo(Result.Failure));
            Assert.That(r3, Is.EqualTo(Result.Success));
            Assert.That(runCount, Is.EqualTo(3));
        });
    }
}


