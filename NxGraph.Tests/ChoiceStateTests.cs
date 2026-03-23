using NxGraph.Authoring;
using NxGraph.Fsm;

namespace NxGraph.Tests;

[TestFixture]
[Category("branching_choice")]
public class ChoiceStateTests
{
    [Test]
    [Timeout(5000)]
    public async Task choice_state_should_follow_true_branch()
    {
        const bool flag = true;

        AsyncStateMachine fsm = GraphBuilder.Start()
            .If(() => flag)
            .Then(_ => ResultHelpers.Success)
            .Else(_ => ResultHelpers.Failure)
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public async Task choice_state_should_follow_false_branch()
    {
        const bool flag = false;

        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .If(() => flag)
            .Then(_ => ResultHelpers.Failure)
            .Else(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }
}