using NxGraph.Authoring;
using NxGraph.Fsm;

namespace NxGraph.Tests;

[TestFixture]
[Category("switch_default")]
public class SwitchDefaultCaseTests
{
    [Test]
    public async Task switch_should_follow_default_when_no_case_matches()
    {
        const string key = "nope";

        StateMachine? fsm = GraphBuilder
            .Start()
            .Switch(() => key)
            .Case("a", _ => ResultHelpers.Failure)
            .Case("b", _ => ResultHelpers.Failure)
            .Default(_ => ResultHelpers.Success)
            .End()
            .ToStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }
}