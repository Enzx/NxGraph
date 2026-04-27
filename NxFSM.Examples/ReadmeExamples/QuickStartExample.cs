using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxFSM.Examples.ReadmeExamples;

/// <summary>
/// Runnable versions of the Quick Start snippets from the README.
/// </summary>
public static class QuickStartExample
{
    // ── Async quick start ─────────────────────────────────────────────────

    static ValueTask<Result> Acquire(CancellationToken _) => ResultHelpers.Success;
    static ValueTask<Result> Process(CancellationToken _) => ResultHelpers.Success;
    static ValueTask<Result> Release(CancellationToken _) => ResultHelpers.Success;

    public static async ValueTask RunAsync()
    {
        Console.WriteLine("=== Async Quick Start ===");

        AsyncStateMachine fsm = GraphBuilder
            .StartWithAsync(Acquire).SetName("Acquire")
            .ToAsync(Process).SetName("Process")
            .ToAsync(Release).SetName("Release")
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Console.WriteLine($"Result: {result}");
    }

    // ── Sync quick start ──────────────────────────────────────────────────

    public static void RunSync()
    {
        Console.WriteLine("=== Sync Quick Start ===");

        StateMachine fsm = GraphBuilder
            .StartWith(() => Result.Success).SetName("Start")
            .To(() => Result.Success).SetName("End")
            .ToStateMachine();

        // Execute() advances one node per call; loop to run to completion.
        Result result = Result.Continue;
        while (result == Result.Continue)
            result = fsm.Execute();

        Console.WriteLine($"Result: {result}");
    }
}
