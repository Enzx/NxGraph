using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

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
            .StartWithAsync(new AsyncRelayState(
                run: _ => ResultHelpers.Success,
                onEnter: _ => { entered = true; return ResultHelpers.InProgress; },
                onExit: _ => { exited = true; return default; }))
            .ToAsyncStateMachine();

        await fsm.ExecuteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entered, Is.True);
            Assert.That(exited, Is.True);
        }
    }

    [Test]
    public async Task relay_state_on_exit_should_run_even_on_failure()
    {
        bool exited = false;
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(
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
            .StartWithAsync(new AsyncRelayState(
                run: _ => { log.Add("run"); return ResultHelpers.Success; },
                onEnter: _ => { log.Add("enter"); return ResultHelpers.InProgress; },
                onExit: _ => { log.Add("exit"); return default; }))
            .ToAsyncStateMachine();

        await fsm.ExecuteAsync();

        Assert.That(log, Is.EqualTo(["enter", "run", "exit"]));
    }

    [Test]
    public async Task on_enter_returning_a_completed_result_short_circuits_run_and_exit()
    {
        // Pins the documented enter-gating contract (current behavior, not a fix): a relay
        // onEnter must return InProgress for the body to run — a completed result becomes
        // the node's result and both run and onExit are skipped.
        bool ran = false, exited = false;
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(
                run: _ => { ran = true; return ResultHelpers.Success; },
                onEnter: _ => new ValueTask<Result>(Result.Success),
                onExit: _ => { exited = true; return default; }))
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success), "The enter hook's result is the node's result.");
            Assert.That(ran, Is.False, "run is skipped when onEnter returns a completed result.");
            Assert.That(exited, Is.False, "onExit is skipped when onEnter short-circuits.");
        });
    }

    [Test]
    public async Task typed_relay_on_enter_returning_a_completed_result_short_circuits_run_and_exit()
    {
        bool ran = false, exited = false;
        AsyncStateMachine<object> fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState<object>(
                run: (_, _) => { ran = true; return ResultHelpers.Success; },
                onEnter: (_, _) => new ValueTask<Result>(Result.Success),
                onExit: (_, _) => { exited = true; return default; }))
            .ToAsyncStateMachine<object>()
            .WithAgent(new object());

        Result result = await fsm.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(ran, Is.False, "run is skipped when onEnter returns a completed result.");
            Assert.That(exited, Is.False, "onExit is skipped when onEnter short-circuits.");
        });
    }

    [Test]
    public void relay_state_on_exit_should_run_even_on_exception()
    {
        bool exited = false;
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(new AsyncRelayState(
                run: _ => throw new ApplicationException("boom"),
                onExit: _ => { exited = true; return default; }))
            .ToAsyncStateMachine();

        Assert.ThrowsAsync<ApplicationException>(async () => await fsm.ExecuteAsync());

        Assert.That(exited, Is.True);
    }
}


