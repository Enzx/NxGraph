using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Benchmarks;

[MemoryDiagnoser]
[HideColumns("Job", "Median", "StdDev", "StdErr", "Error", "RatioSD")]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[CsvExporter]
public class SyncFsmBenchmarks
{
    // ReSharper disable NullableWarningSuppressionIsUsed
    private StateMachine _singleSuccess = null!;
    private StateMachine _chain10 = null!;
    private StateMachine _chain50 = null!;
    private StateMachine _withObserver = null!;
    // ReSharper restore NullableWarningSuppressionIsUsed

    private sealed class SyncNoopObserver : IStateMachineObserver { }

    [GlobalSetup]
    public void Setup()
    {
        _singleSuccess = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();

        StateToken gb10 = GraphBuilder.StartWith(() => Result.Success);
        for (int i = 0; i < 9; i++)
            gb10 = gb10.To(() => Result.Success);
        _chain10 = gb10.ToStateMachine();

        StateToken gb50 = GraphBuilder.StartWith(() => Result.Success);
        for (int i = 0; i < 49; i++)
            gb50 = gb50.To(() => Result.Success);
        _chain50 = gb50.ToStateMachine();

        _withObserver = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine(new SyncNoopObserver());
    }

    private static Result Run(StateMachine sm)
    {
        Result result = Result.Continue;
        while (result == Result.Continue)
            result = sm.Execute();
        return result;
    }

    [BenchmarkCategory("SingleNode")]
    [Benchmark(Baseline = true, Description = "Sync single node")]
    public Result SingleNode() => Run(_singleSuccess);

    [BenchmarkCategory("Chain10")]
    [Benchmark(Description = "Sync chain of 10 nodes")]
    public Result Chain10() => Run(_chain10);

    [BenchmarkCategory("Chain50")]
    [Benchmark(Description = "Sync chain of 50 nodes")]
    public Result Chain50() => Run(_chain50);

    [BenchmarkCategory("WithObserver")]
    [Benchmark(Description = "Sync single node + SyncNoopObserver")]
    public Result WithObserver() => Run(_withObserver);
}
