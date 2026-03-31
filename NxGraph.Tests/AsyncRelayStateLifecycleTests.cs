using NxGraph.Authoring;
using NxGraph.Fsm;

namespace NxGraph.Tests;

[TestFixture]
[Category("async_relay_lifecycle")]
public class AsyncRelayStateLifecycleTests
{
    [Test]
    public async Task relay_state_should_call_on_enter_and_on_exit()
    {
        bool entered = false, exited = false;
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(new AsyncRelayState(
                run: _ => ResultHelpers.Success,
                onEnter: _ => { entered = true; return default; },
                onExit: _ => { exited = true; return default; }))
            .ToAsyncStateMachine();

        await fsm.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(entered, Is.True);
            Assert.That(exited, Is.True);
        });
    }

    [Test]
    public async Task relay_state_on_exit_should_run_even_on_failure()
    {
        bool exited = false;
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(new AsyncRelayState(
                run: _ => ResultHelpers.Failure,
                onExit: _ => { exited = true; return default; }))
            .ToAsyncStateMachine();

        await fsm.ExecuteAsync();

        Assert.That(exited, Is.True);
    }

    [Test]
    public async Task relay_state_lifecycle_order_should_be_enter_run_exit()
    {
        List<string> log = [];
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(new AsyncRelayState(
                run: _ => { log.Add("run"); return ResultHelpers.Success; },
                onEnter: _ => { log.Add("enter"); return default; },
                onExit: _ => { log.Add("exit"); return default; }))
            .ToAsyncStateMachine();

        await fsm.ExecuteAsync();

        Assert.That(log, Is.EqualTo(["enter", "run", "exit"]));
    }

    [Test]
    public async Task relay_state_on_exit_should_run_even_on_exception()
    {
        bool exited = false;
        AsyncStateMachine fsm = GraphBuilder
            .StartWith(new AsyncRelayState(
                run: _ => throw new ApplicationException("boom"),
                onExit: _ => { exited = true; return default; }))
            .ToAsyncStateMachine();

        Assert.ThrowsAsync<ApplicationException>(async () => await fsm.ExecuteAsync());

        Assert.That(exited, Is.True);
    }
}

