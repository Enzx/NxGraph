using NxGraph;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxFSM.Examples.DungeonCrawler;

/// <summary>
/// Synchronous observer that prints colour-coded lifecycle events to the console.
/// Demonstrates every callback in <see cref="IStateMachineObserver"/>.
/// </summary>
public sealed class DungeonObserver : IStateMachineObserver
{
    private const string Indent = "  ";

    // ── State lifecycle ─────────────────────────────────────────────────

    public void OnStateEntered(NodeId id)
    {
        WriteColour(ConsoleColor.DarkCyan, $"{Indent}▶ Entered  [{id.Name}]");
    }

    public void OnStateExited(NodeId id)
    {
        WriteColour(ConsoleColor.DarkGray, $"{Indent}◀ Exited   [{id.Name}]");
    }

    public void OnTransition(NodeId from, NodeId to)
    {
        WriteColour(ConsoleColor.DarkYellow, $"{Indent}→ Transition: [{from.Name}] ──► [{to.Name}]");
    }

    public void OnStateFailed(NodeId id, Exception ex)
    {
        WriteColour(ConsoleColor.Red, $"{Indent}✖ FAILED [{id.Name}]: {ex.Message}");
    }

    // ── Machine lifecycle ───────────────────────────────────────────────

    public void OnStateMachineStarted(NodeId graphId)
    {
        WriteColour(ConsoleColor.Green, $"═══ StateMachine Started ({graphId}) ═══");
    }

    public void OnStateMachineCompleted(NodeId graphId, Result result)
    {
        ConsoleColor c = result == Result.Success ? ConsoleColor.Green : ConsoleColor.Red;
        WriteColour(c, $"═══ StateMachine Completed ({graphId}) ► {result} ═══");
    }

    public void OnStateMachineReset(NodeId graphId)
    {
        WriteColour(ConsoleColor.Magenta, $"═══ StateMachine Reset ({graphId}) ═══");
    }

    public void StateMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next)
    {
        WriteColour(ConsoleColor.DarkGray, $"{Indent}   Status: {prev} → {next}");
    }

    // ── Log reports from states ─────────────────────────────────────────

    public void OnLogReport(NodeId nodeId, string message)
    {
        WriteColour(ConsoleColor.White, $"{Indent}   📜 [{nodeId.Name}] {message}");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static void WriteColour(ConsoleColor colour, string text)
    {
        ConsoleColor prev = Console.ForegroundColor;
        Console.ForegroundColor = colour;
        Console.WriteLine(text);
        Console.ForegroundColor = prev;
    }
}

