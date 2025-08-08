using NxGraph.Authoring;
using NxGraph.Fsm;

namespace NxGraph.Tests;

[TestFixture]
[Category("transition_flow")]
public class TransitionFlowTests
{
    [Test]
    public async Task should_traverse_two_states_and_succeed()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .To(_ => ResultHelpers.Success)
            .ToStateMachine();
        Result result = await fsm.ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public async Task should_stop_on_failure_of_second_state()
    {
        StateMachine fsm = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .To(_ => ResultHelpers.Failure)
            .ToStateMachine();
        Result result = await fsm.ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Failure));
    }
}