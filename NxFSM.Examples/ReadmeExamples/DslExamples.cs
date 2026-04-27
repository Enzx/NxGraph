using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxFSM.Examples.ReadmeExamples;

/// <summary>
/// Runnable versions of the Authoring DSL snippets from the README.
/// Each method builds a graph, converts it to a state machine, and runs it.
/// </summary>
public static class DslExamples
{
    public static async ValueTask RunAsync()
    {
        await LinearFlowAsync();
        await BranchingWithIfAsync();
        await BranchingWithSwitchAsync();
        await WaitsAndTimeoutsAsync();
        await NamingNodesAsync();
    }

    // ── Linear flows ──────────────────────────────────────────────────────

    static async ValueTask LinearFlowAsync()
    {
        Console.WriteLine("=== DSL: Linear Flow ===");

        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success).SetName("Start")
            .ToAsync(_ => ResultHelpers.Success).SetName("Step1")
            .ToAsync(_ => ResultHelpers.Success).SetName("Step2")
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();
        Console.WriteLine($"Result: {result}");
    }

    // ── Branching with If ─────────────────────────────────────────────────

    static async ValueTask BranchingWithIfAsync()
    {
        Console.WriteLine("=== DSL: Branching with If ===");

        bool IsPremium() => true;

        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success).SetName("Entry")
            .If(IsPremium)
                .ThenAsync(_ =>
                {
                    Console.WriteLine("  Taking Premium branch");
                    return ResultHelpers.Success;
                }).SetName("Premium")
                .ElseAsync(_ =>
                {
                    Console.WriteLine("  Taking Standard branch");
                    return ResultHelpers.Success;
                }).SetName("Standard")
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();
        Console.WriteLine($"Result: {result}");
    }

    // ── Branching with Switch ─────────────────────────────────────────────

    static async ValueTask BranchingWithSwitchAsync()
    {
        Console.WriteLine("=== DSL: Branching with Switch ===");

        int RouteKey() => 2;

        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success).SetName("Entry")
            .Switch(RouteKey)
                .CaseAsync(1, _ =>
                {
                    Console.WriteLine("  Route 1");
                    return ResultHelpers.Success;
                })
                .CaseAsync(2, _ =>
                {
                    Console.WriteLine("  Route 2");
                    return ResultHelpers.Success;
                })
                .DefaultAsync(_ => ResultHelpers.Failure)
            .End().SetName("Router")
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();
        Console.WriteLine($"Result: {result}");
    }

    // ── Waits and timeouts ────────────────────────────────────────────────

    static async ValueTask WaitsAndTimeoutsAsync()
    {
        Console.WriteLine("=== DSL: Wait ===");

        Graph delayed = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success).SetName("Start")
            .WaitForAsync(10.Milliseconds()).SetName("Cooldown")
            .ToAsync(_ => ResultHelpers.Success).SetName("Finish")
            .Build();

        Result r1 = await delayed.ToAsyncStateMachine().ExecuteAsync();
        Console.WriteLine($"Wait result: {r1}");

        Console.WriteLine("=== DSL: Timeout Wrapper ===");

        Graph timed = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success).SetName("Start")
            .ToWithTimeoutAsync(2.Seconds(), _ => ResultHelpers.Success, TimeoutBehavior.Fail)
                .SetName("TimedWork")
            .ToAsync(_ => ResultHelpers.Success).SetName("AfterTimeout")
            .Build();

        Result r2 = await timed.ToAsyncStateMachine().ExecuteAsync();
        Console.WriteLine($"Timeout result: {r2}");
    }

    // ── Naming nodes ──────────────────────────────────────────────────────

    static async ValueTask NamingNodesAsync()
    {
        Console.WriteLine("=== DSL: Named Nodes ===");

        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success).SetName("Initial")
            .ToAsync(_ => ResultHelpers.Success).SetName("Second")
            .Build()
            .SetName("SampleGraph");

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();
        Console.WriteLine($"Graph '{graph.Id.Name}' result: {result}");
    }
}
