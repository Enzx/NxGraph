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
}