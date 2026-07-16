using NxGraph;
using NxGraph.Authoring;
using NxGraph.Diagnostics.Export;
using NxGraph.Fsm;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxFSM.Examples.ReadmeExamples;

/// <summary>
/// Runnable version of the README "Token runtime: fork, join, and mid-graph merge" section:
/// the fork/join diamond on both token machines, an Any-join used as a mid-graph merge, and
/// an M-of-N quorum whose late leftover absorbs benignly.
/// </summary>
public static class TokenRunnerExamples
{
    public static async ValueTask RunAsync()
    {
        ForkJoinDiamond();
        await ForkJoinDiamondAsync();
        AnyJoinMerge();
        QuorumJoin();
        MermaidExport();
    }

    // ── The README snippet: fork(2) → All-join → shared tail ─────────────

    static void ForkJoinDiamond()
    {
        Console.WriteLine("=== Token runtime: fork/join diamond (sync) ===");

        JoinState join = new(JoinPolicy.All(2)); // or JoinPolicy.Any (merge), JoinPolicy.Quorum(m)

        Graph graph = GraphBuilder
            .StartWith(() => Step("Load", "read the save file")).SetName("Load")
            .ForkTo(
                b => b.To(() => Step("Audio", "warm the mixer"))   // branch 0 continues the arriving token
                    .To(join)                                      // converge by routing chains to the same JoinState
                    .To(() => Step("Ready", "enter the scene")),   // the surviving token carries on past the join
                b => b.To(() => Step("Terrain", "stream chunks"))  // every other branch spawns a new token
                    .To(join))
            .Build();

        TokenMachine machine = graph.ToTokenMachine();   // sync twin; frame-stepped like StateMachine
        machine.SetStepMode(ParallelStepMode.RunToJoin); // or RoundPerTick (default): one round per Execute()
        Result result = machine.Execute();
        Console.WriteLine($"  Joined: {result}");
    }

    static async ValueTask ForkJoinDiamondAsync()
    {
        Console.WriteLine("=== Token runtime: fork/join diamond (async) ===");

        JoinState join = new(JoinPolicy.All(2));
        Graph graph = GraphBuilder
            .StartWithAsync(_ => StepAsync("Load", "read the save file"))
            .ForkTo(
                b => b.ToAsync(_ => StepAsync("Audio", "warm the mixer"))
                    .To(join)
                    .ToAsync(_ => StepAsync("Ready", "enter the scene")),
                b => b.ToAsync(_ => StepAsync("Terrain", "stream chunks"))
                    .To(join))
            .Build();

        AsyncTokenMachine asyncMachine = graph.ToAsyncTokenMachine(); // async twin: ExecuteAsync / StepAsync
        Result result = await asyncMachine.ExecuteAsync();
        Console.WriteLine($"  Joined: {result}");
    }

    // ── Any-join: every token passes through — a mid-graph merge point ───

    static void AnyJoinMerge()
    {
        Console.WriteLine("=== Token runtime: Any-join as a mid-graph merge ===");

        JoinState merge = new(JoinPolicy.Any);
        Graph graph = GraphBuilder
            .StartWith(() => Step("Intake", "accept two work items"))
            .ForkTo(
                b => b.To(() => Step("Lane A", "process")).To(merge)
                    .To(() => Step("Ship", "one shared tail, entered per token")),
                b => b.To(() => Step("Lane B", "process")).To(merge))
            .Build();

        TokenMachine machine = graph.ToTokenMachine();
        machine.SetStepMode(ParallelStepMode.RunToJoin);
        Console.WriteLine($"  Merged: {machine.Execute()}");
    }

    // ── Quorum: fire at 2-of-3; the straggler absorbs benignly at run end ─

    static void QuorumJoin()
    {
        Console.WriteLine("=== Token runtime: 2-of-3 quorum join ===");

        JoinState quorum = new(JoinPolicy.Quorum(2));
        Graph graph = GraphBuilder
            .StartWith(() => Step("Scatter", "ask three replicas"))
            .ForkTo(
                b => b.To(() => Step("Replica 1", "answer fast")).To(quorum)
                    .To(() => Step("Commit", "two answers are enough")),
                b => b.To(() => Step("Replica 2", "answer fast")).To(quorum),
                b => b.To(() => Step("Replica 3", "answer slowly"))
                    .To(() => Step("Replica 3", "still working..."))
                    .To(quorum))
            .Build();

        TokenMachine machine = graph.ToTokenMachine();
        machine.SetStepMode(ParallelStepMode.RunToJoin);
        Console.WriteLine($"  Quorum run: {machine.Execute()} (the late replica's token absorbs)");
    }

    // ── Mermaid: fork/join render first-class (bars, labeled AND-split edges) ─

    static void MermaidExport()
    {
        Console.WriteLine("=== Token runtime: Mermaid export ===");

        JoinState join = new(JoinPolicy.All(2));
        StateToken start = GraphBuilder.StartWith(() => Result.Success);
        start.Builder.SetName(start.Id, "Load");
        ForkToken fork = start.ForkTo(
            b =>
            {
                StateToken audio = b.To(() => Result.Success);
                audio.Builder.SetName(audio.Id, "Audio");
                StateToken joined = audio.To(join);
                joined.Builder.SetName(joined.Id, "Join");
                StateToken ready = joined.To(() => Result.Success);
                ready.Builder.SetName(ready.Id, "Ready");
                return ready;
            },
            b =>
            {
                StateToken terrain = b.To(() => Result.Success);
                terrain.Builder.SetName(terrain.Id, "Terrain");
                return terrain.To(join);
            });
        Graph graph = fork.SetName("Fork").Build();

        Console.WriteLine(graph.ToMermaid());
    }

    static Result Step(string who, string what)
    {
        Console.WriteLine($"  {who,-9} {what}");
        return Result.Success;
    }

    static ValueTask<Result> StepAsync(string who, string what)
    {
        Console.WriteLine($"  {who,-9} {what}");
        return ResultHelpers.Success;
    }
}
