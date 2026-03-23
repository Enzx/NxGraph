using NxGraph.Authoring;
using NxGraph.Fsm;

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
            .Case(Mode.A, new AsyncRelayState(_ => ResultHelpers.Failure))
            .Case(Mode.B, new AsyncRelayState(_ => ResultHelpers.Success))
            .Case(Mode.C, new AsyncRelayState(_ => ResultHelpers.Failure))
            .Default(new AsyncRelayState(_ => ResultHelpers.Failure))
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
            .Case(0, _ => ResultHelpers.Failure)
            .Case(1, _ => ResultHelpers.Failure)
            .Default(_ => ResultHelpers.Success)
            .End()
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }
}