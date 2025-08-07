using NxFSM.Authoring;
using NxFSM.Fsm;

namespace NxFSM.Tests;

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

        StateMachine fsm = GraphBuilder
            .Start()
            .Switch(() => mode)
            .Case(Mode.A, new RelayState(_ => ResultHelpers.Failure))
            .Case(Mode.B, new RelayState(_ => ResultHelpers.Success))
            .Case(Mode.C, new RelayState(_ => ResultHelpers.Failure))
            .Default(new RelayState(_ => ResultHelpers.Failure))
            .End()
            .Build().ToStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public async Task switch_state_should_use_default_when_no_match()
    {
        const int selector = 99; // no matching case

        StateMachine fsm = GraphBuilder.Start()
            .Switch(() => selector)
            .Case(0, _ => ResultHelpers.Failure)
            .Case(1, _ => ResultHelpers.Failure)
            .Default(_ => ResultHelpers.Success)
            .End()
            .ToStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }
}