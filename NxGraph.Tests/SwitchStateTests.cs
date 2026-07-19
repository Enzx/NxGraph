using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

[TestFixture]
[Category("branching_switch")]
public class SwitchStateTests
{
    private enum Mode
    {
        A,
        B,
        C
    }

    [Test]
    public async Task switch_state_should_follow_matching_case()
    {
        const Mode mode = Mode.B;

        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .Switch(() => mode)
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
    public async Task switch_state_should_use_default_when_no_match()
    {
        const int selector = 99; // no matching case

        AsyncStateMachine fsm = GraphBuilder.Start()
            .Switch(() => selector)
            .CaseAsync(0, _ => ResultHelpers.Failure)
            .CaseAsync(1, _ => ResultHelpers.Failure)
            .DefaultAsync(_ => ResultHelpers.Success)
            .End()
            .ToAsyncStateMachine();


        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }

    // ── Sync-runtime twins: dispatch driven by StateMachine.Execute() ────

    [Test]
    public void sync_switch_state_should_follow_matching_case()
    {
        const Mode mode = Mode.B;
        bool matchedCaseRan = false;

        StateMachine fsm = GraphBuilder
            .Start()
            .Switch(() => mode)
            .Case(Mode.A, () => Result.Failure)
            .Case(Mode.B, () =>
            {
                matchedCaseRan = true;
                return Result.Success;
            })
            .Case(Mode.C, () => Result.Failure)
            .Default(() => Result.Failure)
            .End()
            .ToStateMachine();

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = fsm.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(matchedCaseRan, Is.True, "The sync runtime must route through the matching case body.");
        });
    }

    [Test]
    public void sync_switch_state_should_use_default_when_no_match()
    {
        const int selector = 99; // no matching case
        bool defaultRan = false;

        StateMachine fsm = GraphBuilder
            .Start()
            .Switch(() => selector)
            .Case(0, () => Result.Failure)
            .Case(1, () => Result.Failure)
            .Default(() =>
            {
                defaultRan = true;
                return Result.Success;
            })
            .End()
            .ToStateMachine();

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = fsm.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(defaultRan, Is.True, "The sync runtime must route through the explicit default body.");
        });
    }
}