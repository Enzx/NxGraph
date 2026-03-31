using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

[TestFixture]
[Category("core_state_execution")]
public class CoreStateExecutionTests
{
    [Test]
    public async Task should_return_success_when_single_state_succeeds()
    {
        AsyncStateMachine fsm = GraphBuilder.StartWith(_ => ResultHelpers.Success).ToAsyncStateMachine();
        Result result = await fsm.ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public async Task should_return_failure_when_single_state_fails()
    {
        AsyncStateMachine fsm = GraphBuilder.StartWith(_ => ResultHelpers.Failure).Build().ToAsyncStateMachine();
        Result result = await fsm.ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Failure));
    }
}