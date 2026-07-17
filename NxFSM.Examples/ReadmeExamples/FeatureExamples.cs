using System.Text.Json;
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxFSM.Examples.ReadmeExamples;

/// <summary>
/// Runnable versions of the README sections added with the 2.x feature wave:
/// error handling (retries + failure edges), Goto loops, named outcomes,
/// subgraph composites, and durable suspend/resume (shallow and deep).
/// </summary>
public static class FeatureExamples
{
    public static async ValueTask RunAsync()
    {
        await RetryAndFailureEdgeAsync();
        GotoLoop();
        await NamedOutcomesAsync();
        await SubGraphCompositeAsync();
        await DurableSuspendResumeAsync();
        await DeepSuspendResumeAsync();
    }

    // ── Error handling: retries and failure edges ─────────────────────────

    static async ValueTask RetryAndFailureEdgeAsync()
    {
        Console.WriteLine("=== Feature: Retry + OnError ===");

        int attempts = 0;

        ValueTask<Result> CallFlakyService(CancellationToken _)
        {
            attempts++;
            Console.WriteLine($"  Call attempt {attempts}");
            return ResultHelpers.Failure; // always fails — exhausts retries, takes the failure edge
        }

        ValueTask<Result> Cleanup()
        {
            Console.WriteLine("  Cleanup handler ran");
            return ResultHelpers.Success;
        }

        Graph graph = GraphBuilder
            .StartWithAsync(CallFlakyService).SetName("Call")
                .Retry(maxAttempts: 3, backoff: 1.Milliseconds(), BackoffKind.Exponential)
                .OnErrorAsync(_ => Cleanup()).SetName("Cleanup")
            .Build();

        Result result = await graph.ToAsyncStateMachine().ExecuteAsync();
        Console.WriteLine($"Result after {attempts} attempts: {result}");
    }

    // ── Loops with Goto ───────────────────────────────────────────────────

    static void GotoLoop()
    {
        Console.WriteLine("=== Feature: Goto loop ===");

        int laps = 0;

        Graph loop = GraphBuilder
            .StartWith(() => Result.Success).SetName("Gather")
            .To(() => ++laps < 3 ? Result.Success : Result.Failure).SetName("Craft")
            .Goto("Gather") // Craft's success edge loops back to Gather
            .Build();

        StateMachine fsm = loop.ToStateMachine();
        Result result = Result.InProgress;
        while (result == Result.InProgress)
            result = fsm.Execute();

        Console.WriteLine($"Looped {laps} laps, exit: {result}");
    }

    // ── Named outcomes ────────────────────────────────────────────────────

    static async ValueTask NamedOutcomesAsync()
    {
        Console.WriteLine("=== Feature: Named outcomes ===");

        const int delivered = 1;

        static ValueTask<Result> ProcessOrder(CancellationToken _) => ResultHelpers.Success;

        Graph graph = GraphBuilder
            .StartWithAsync(ProcessOrder).SetName("Process")
            .ToAsync(_ => ResultHelpers.Success).SetName("Deliver").WithOutcome(delivered, "Delivered")
            .Build();

        AsyncStateMachine fsm = graph.ToAsyncStateMachine();
        await fsm.ExecuteAsync();
        Console.WriteLine($"Outcome {fsm.LastOutcome}: {fsm.LastOutcomeName}");
    }

    // ── Composites: subgraph nesting ──────────────────────────────────────

    static async ValueTask SubGraphCompositeAsync()
    {
        Console.WriteLine("=== Feature: SubGraph composite ===");

        Graph child = GraphBuilder
            .StartWithAsync(_ =>
            {
                Console.WriteLine("  child step 1");
                return ResultHelpers.Success;
            })
            .ToAsync(_ =>
            {
                Console.WriteLine("  child step 2");
                return ResultHelpers.Success;
            })
            .Build();

        Graph flow = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success).SetName("Prepare")
            .SubGraph(child, history: true).SetName("Work")
            .ToAsync(_ => ResultHelpers.Success).SetName("Finish")
            .Build();

        Result result = await flow.ToAsyncStateMachine().ExecuteAsync();
        Console.WriteLine($"Result: {result}");
    }

    // ── Durable suspend / resume ──────────────────────────────────────────

    static async ValueTask DurableSuspendResumeAsync()
    {
        Console.WriteLine("=== Feature: Durable suspend/resume (cross-runtime) ===");

        Graph graph = GraphBuilder
            .StartWith(() =>
            {
                Console.WriteLine("  node 0 (async machine)");
                return Result.Success;
            })
            .To(() =>
            {
                Console.WriteLine("  node 1 (sync machine)");
                return Result.Success;
            })
            .To(() =>
            {
                Console.WriteLine("  node 2 (sync machine)");
                return Result.Success;
            })
            .Build();

        AsyncStateMachine first = graph.ToAsyncStateMachine();
        await first.StepAsync();                          // advance one node
        StateMachineSnapshot snapshot = first.Suspend();  // primitives-only record

        string json = JsonSerializer.Serialize(snapshot); // any serializer works

        // Continue on the sync runtime — snapshots are interchangeable.
        StateMachine second = graph.ToStateMachine();
        second.Resume(JsonSerializer.Deserialize<StateMachineSnapshot>(json)!);

        Result result = Result.InProgress;
        while (result == Result.InProgress)
            result = second.Execute();

        Console.WriteLine($"Resumed run finished: {result}");
    }

    // ── Deep suspend / resume: composite internals survive the boundary ───

    static async ValueTask DeepSuspendResumeAsync()
    {
        Console.WriteLine("=== Feature: Deep suspend/resume (composite internals) ===");

        bool repaired = false;

        Graph BuildFlow()
        {
            Graph child = GraphBuilder
                .StartWithAsync(_ =>
                {
                    Console.WriteLine("  child: step 1");
                    return ResultHelpers.Success;
                })
                .ToAsync(_ =>
                {
                    Console.WriteLine("  child: step 2");
                    return repaired ? ResultHelpers.Success : ResultHelpers.Failure;
                })
                .Build();

            StateToken sub = GraphBuilder.Start().SubGraph(child, history: true).SetName("Work");
            StateToken repair = sub.Builder.TokenFor(sub.Builder.AddNode(new AsyncRelayState(_ =>
            {
                repaired = true;
                Console.WriteLine("  repair ran");
                return ResultHelpers.Success;
            })));
            repair.Goto("Work");
            return sub.OnError(repair).Build();
        }

        AsyncStateMachine first = BuildFlow().ToAsyncStateMachine();
        await first.StepAsync();                              // child failed at step 2; parent at repair
        StateMachineDeepSnapshot deep = first.SuspendDeep();  // position + composite internals

        string json = JsonSerializer.Serialize(deep);         // still plain records — any serializer works

        // Later — fresh machine over an equivalent (rebuilt) graph:
        AsyncStateMachine second = BuildFlow().ToAsyncStateMachine();
        second.ResumeDeep(JsonSerializer.Deserialize<StateMachineDeepSnapshot>(json)!);

        Result result = Result.InProgress;
        while (result == Result.InProgress)
            result = await second.StepAsync();

        Console.WriteLine($"Deep-resumed run finished: {result} (child resumed at step 2 — step 1 did not re-run)");
    }
}
