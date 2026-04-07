using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

[TestFixture]
[Category("transition_flow")]
public class TransitionFlowTests
{
    [Test]
    public async Task should_traverse_two_states_and_succeed()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();
        Result result = await fsm.ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public async Task should_stop_on_failure_of_second_state()
    {
        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Failure)
            .ToAsyncStateMachine();
        Result result = await fsm.ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Failure));
    }
}
