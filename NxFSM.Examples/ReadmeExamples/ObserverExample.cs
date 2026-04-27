using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxFSM.Examples.ReadmeExamples;

public sealed class LoggingState : State
{
    protected override Result OnRun()
    {
        Log("starting computation");
        Log("computation complete");
        return Result.Success;
    }
}

public sealed class DiagnosticObserver : IStateMachineObserver
{
    public void OnStateEntered(NodeId id) => Console.WriteLine($"  >> {id.Name}");
    public void OnStateExited(NodeId id)  => Console.WriteLine($"  << {id.Name}");
    public void OnTransition(NodeId from, NodeId to) =>
        Console.WriteLine($"     {from.Name} -> {to.Name}");
    public void OnStateFailed(NodeId id, Exception ex) =>
        Console.WriteLine($"  FAIL {id.Name}: {ex.Message}");
    public void OnStateMachineCompleted(NodeId graphId, Result result) =>
        Console.WriteLine($"  FSM done: {result}");
    public void OnLogReport(NodeId nodeId, string message) =>
        Console.WriteLine($"  [{nodeId.Name}] {message}");
}

public static class ObserverExample
{
    public static void Run()
    {
        Console.WriteLine("=== Sync Observer + State Logging ===");

        StateMachine sm = GraphBuilder
            .StartWith(new LoggingState()).SetName("Work")
            .To(() => Result.Success).SetName("Finish")
            .ToStateMachine(new DiagnosticObserver());

        Result result = Result.Continue;
        while (result == Result.Continue)
            result = sm.Execute();

        Console.WriteLine($"Result: {result}");
    }
}
