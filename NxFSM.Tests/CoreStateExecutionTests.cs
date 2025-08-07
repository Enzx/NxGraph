using NxFSM.Authoring;
using NxFSM.Fsm;

namespace NxFSM.Tests;

[TestFixture]
[Category("core_state_execution")]
public class CoreStateExecutionTests
{
    [Test]
    public async Task should_return_success_when_single_state_succeeds()
    {
        StateMachine fsm = GraphBuilder.StartWith(_ => ResultHelpers.Success).ToStateMachine();
        Result result = await fsm.ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public async Task should_return_failure_when_single_state_fails()
    {
        StateMachine fsm = GraphBuilder.StartWith(_ => ResultHelpers.Failure).Build().ToStateMachine();
        Result result = await fsm.ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Failure));
    }
}