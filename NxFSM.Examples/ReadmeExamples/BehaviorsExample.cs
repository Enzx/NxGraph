using NxGraph;
using NxGraph.Authoring;
using NxGraph.Behaviors;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxFSM.Examples.ReadmeExamples;

/// <summary>
/// Behaviors (README "Behaviors (declarative state composition)"): a node authored as a
/// sequence of small data-shaped behaviors — in order, fail-fast — with fields that are
/// literals or <see cref="BlackboardValue{T}"/> key bindings. <see cref="Log"/> goes through
/// the report channel (observer OnLogReport, never the console directly), and the same
/// dual-interface instances author either runtime.
/// </summary>
public static class BehaviorsExample
{
    private sealed class ConsoleReportObserver : IStateMachineObserver
    {
        void IStateMachineObserver.OnLogReport(NodeId nodeId, string message)
        {
            Console.WriteLine($"  report: {message}");
        }
    }

    public static void Run()
    {
        Console.WriteLine("=== Behaviors (declarative state composition) ===");

        ConsoleReportObserver observer = new();

        var stats = new BlackboardSchema("stats"); // Graph scope (default)
        BlackboardKey<string> playerName = stats.Register("playerName", "Hero");
        BlackboardKey<int> score = stats.Register("score", 0);

        Graph graph = GraphBuilder.Start()
            .ToBehaviors(
                new Log(LogSeverity.Info, playerName), // message bound to a key, resolved per run
                new SetValue<int>(score, 100),         // literal write — the typed copy/constant primitive
                new Log("checkpoint saved"))           // literal message, Info severity by default
            .WithSchema(stats)
            .Build();

        Blackboard board = new(stats);
        Result result = graph.ToStateMachine(observer)      // Log lands in the observer's OnLogReport
            .WithBlackboard(board)
            .Execute();

        Console.WriteLine($"  Sync behaviors: {result}, score = {board.Get(score)}");
    }
}
