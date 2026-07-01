using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

[TestFixture]
[Category("switch_default")]
public class SwitchDefaultCaseTests
{
    [Test]
    public async Task switch_should_follow_default_when_no_case_matches()
    {
        const string key = "nope";

        AsyncStateMachine? fsm = GraphBuilder
            .Start()
            .Switch(() => key)
            .CaseAsync("a", _ => ResultHelpers.Failure)
            .CaseAsync("b", _ => ResultHelpers.Failure)
            .DefaultAsync(_ => ResultHelpers.Success)
            .End()
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public async Task async_switch_without_default_should_terminate_when_no_case_matches()
    {
        // Regression: previously AsyncSwitchState defaulted _defaultNode to default(NodeId)
        // (index 0 = Start) so a no-match case silently looped to Start instead of
        // exiting cleanly. The fix routes the no-default case through NodeId.Default,
        // which the async runtime treats as terminal success.
        const string key = "nope";

        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .Switch(() => key)
            .CaseAsync("a", _ => ResultHelpers.Failure)
            .CaseAsync("b", _ => ResultHelpers.Failure)
            .End()
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public void sync_switch_without_default_should_terminate_when_no_case_matches()
    {
        // Regression mirror of the async test above for the sync runtime.
        const string key = "nope";

        StateMachine fsm = GraphBuilder
            .Start()
            .Switch(() => key)
            .Case("a", () => Result.Failure)
            .Case("b", () => Result.Failure)
            .End()
            .ToStateMachine();

        Result result;
        do
        {
            result = fsm.Execute();
        } while (result == Result.InProgress);

        Assert.That(result, Is.EqualTo(Result.Success));
    }
}