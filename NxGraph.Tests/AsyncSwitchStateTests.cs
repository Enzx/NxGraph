using NxGraph.Authoring;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

[TestFixture]
[Category("branching_switch")]
public class AsyncSwitchStateTests
{
    private enum Mode
    {
        A,
        B,
        C
    }

    [Test]
    public async Task async_switch_state_should_follow_matching_case()
    {
        const Mode mode = Mode.B;

        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .SwitchAsync(() => new ValueTask<Mode>(mode))
            .CaseAsync(Mode.A, new AsyncRelayState(_ => ResultHelpers.Failure))
            .CaseAsync(Mode.B, new AsyncRelayState(_ => ResultHelpers.Success))
            .CaseAsync(Mode.C, new AsyncRelayState(_ => ResultHelpers.Failure))
            .DefaultAsync(new AsyncRelayState(_ => ResultHelpers.Failure))
            .End()
            .Build().ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public async Task async_switch_state_should_use_default_when_no_match()
    {
        const int selector = 99; // no matching case

        AsyncStateMachine fsm = GraphBuilder.Start()
            .SwitchAsync(() => new ValueTask<int>(selector))
            .CaseAsync(0, _ => ResultHelpers.Failure)
            .CaseAsync(1, _ => ResultHelpers.Failure)
            .DefaultAsync(_ => ResultHelpers.Success)
            .End()
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public async Task async_switch_state_should_work_after_previous_state()
    {
        const Mode mode = Mode.A;

        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .ToAsync(new AsyncRelayState(_ => ResultHelpers.Success))
            .SwitchAsync(() => new ValueTask<Mode>(mode))
            .CaseAsync(Mode.A, new AsyncRelayState(_ => ResultHelpers.Success))
            .CaseAsync(Mode.B, new AsyncRelayState(_ => ResultHelpers.Failure))
            .End()
            .Build().ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public async Task async_switch_state_with_sync_cases()
    {
        const Mode mode = Mode.C;

        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .SwitchAsync(() => new ValueTask<Mode>(mode))
            .Case(Mode.A, () => Result.Failure)
            .Case(Mode.B, () => Result.Failure)
            .Case(Mode.C, () => Result.Success)
            .Default(() => Result.Failure)
            .End()
            .Build().ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }
}

