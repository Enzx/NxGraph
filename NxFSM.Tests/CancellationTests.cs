using NxFSM.Authoring;
using NxFSM.Fsm;
using NxFSM.Graphs;

namespace NxFSM.Tests;

[TestFixture]
[Category("cancellation")]
public class CancellationTests
{
    [Test]
    public void should_cancel_execution_when_token_is_cancelled()
    {
        GraphBuilder builder = new();
        builder.AddNode(new RelayState(async ct =>
        {
            await Task.Delay(1000, ct);
            return Result.Success;
        }), isStart: true);
        Graph graph = builder.Build();

        StateMachine fsm = new(graph);

        using CancellationTokenSource cts = new(50);

        Assert.ThrowsAsync<TaskCanceledException>(async () => { await fsm.ExecuteAsync(cts.Token); });
    }
}