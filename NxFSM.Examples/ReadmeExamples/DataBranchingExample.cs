using NxGraph;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Conditions;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxFSM.Examples.ReadmeExamples;

/// <summary>
/// Data-built branching (README "Data-built branching (serializable)"): the decision is a list
/// of <see cref="ICondition"/> objects or a blackboard key plus literal cases, not a closure —
/// so the branch rides the graph payload, survives suspend/resume, and renders labelled arms in
/// the Mermaid export. A condition that is false is a decision, never a node failure.
/// </summary>
public static class DataBranchingExample
{
    public static async ValueTask RunAsync()
    {
        Console.WriteLine("=== Data-built branching (serializable) ===");

        BlackboardSchema world = new("world");
        BlackboardKey<bool> alarmRaised = world.Register("alarmRaised", false);
        BlackboardKey<int> tier = world.Register("tier", 0);

        Graph guarded = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success).SetName("Entry")
            .If(new IsTrue(alarmRaised))
                .ThenAsync(_ =>
                {
                    Console.WriteLine("  Taking Evacuate branch");
                    return ResultHelpers.Success;
                }).SetName("Evacuate")
                .ElseAsync(_ =>
                {
                    Console.WriteLine("  Taking Patrol branch");
                    return ResultHelpers.Success;
                }).SetName("Patrol")
            .WithSchema(world)
            .Build();

        Blackboard board = new(world);
        board.Set(alarmRaised, true);
        Result guardedResult = await guarded.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();
        Console.WriteLine($"Result: {guardedResult}");

        Graph routed = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success).SetName("Entry")
            .Switch(tier)
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
                .DefaultAsync(_ =>
                {
                    Console.WriteLine("  Route default");
                    return ResultHelpers.Success;
                })
            .End().SetName("Router")
            .WithSchema(world)
            .Build();

        Blackboard routing = new(world);
        routing.Set(tier, 2);
        Result routedResult = await routed.ToAsyncStateMachine().WithBlackboard(routing).ExecuteAsync();
        Console.WriteLine($"Result: {routedResult}");
    }
}
