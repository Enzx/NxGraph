using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxFSM.Examples.ReadmeExamples;

public sealed class AppAgent
{
    public int Counter { get; set; }
}

public sealed class WorkState : AsyncState<AppAgent>
{
    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        Agent.Counter++;
        Console.WriteLine($"  Counter incremented to {Agent.Counter}");
        return ResultHelpers.Success;
    }
}

public static class AgentExample
{
    public static async ValueTask RunAsync()
    {
        Console.WriteLine("=== Agent / Context Injection ===");

        AppAgent agent = new();
        AsyncStateMachine<AppAgent> fsm = GraphBuilder
            .StartWithAsync(new WorkState()).SetName("Work")
            .ToAsyncStateMachine<AppAgent>()
            .WithAgent(agent);

        await fsm.ExecuteAsync();
        Console.WriteLine($"Final counter: {agent.Counter}");
    }
}
