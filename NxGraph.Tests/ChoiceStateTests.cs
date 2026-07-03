using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

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
            .ThenAsync(_ => ResultHelpers.Success)
            .ElseAsync(_ => ResultHelpers.Failure)
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
            .ThenAsync(_ => ResultHelpers.Failure)
            .ElseAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    [Timeout(5000)]
    public void start_if_graph_is_executable_by_the_sync_runtime()
    {
        // Regression: Start().If(predicate) used to wrap the sync predicate in an
        // AsyncChoiceState, making the start node unexecutable by the sync StateMachine
        // while every sibling If overload produced a sync ChoiceState.
        const bool flag = true;

        StateMachine fsm = GraphBuilder.Start()
            .If(() => flag)
            .Then(new RelayState(() => Result.Success))
            .Else(new RelayState(() => Result.Failure))
            .ToStateMachine();

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = fsm.Execute();
        }

        Assert.That(result, Is.EqualTo(Result.Success));
    }
}
