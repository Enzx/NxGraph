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
    private StateMachine _withTimeoutWrapper = null!;
    private StateMachine _directorLinear10 = null!;
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

        // Sync twin of the async suite's timeout wrapper: TimeoutState checks the deadline
        // between ticks (no CTS), so this measures the timestamp bookkeeping around an
        // immediately succeeding node.
        _withTimeoutWrapper = GraphBuilder
            .Start()
            .ToWithTimeout(1.Seconds(), () => Result.Success, TimeoutBehavior.Fail)
            .ToStateMachine();

        // Sync twin of the async suite's director-driven flow (same shape, same steady-state
        // behavior: the director's counter is instance state and does not reset between runs).
        StateToken builder = GraphBuilder.StartWith(() => Result.Success);
        for (int i = 0; i < 9; i++)
            builder = builder.To(() => Result.Success);
        _directorLinear10 = builder.To(new SyncLinearDirector(10)).ToStateMachine();
    }

    private static Result Run(StateMachine sm)
    {
        Result result = Result.InProgress;
        while (result == Result.InProgress)
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

    [BenchmarkCategory("WithTimeoutWrapper")]
    [Benchmark(Description = "Sync timeout wrapper around immediate success")]
    public Result WithTimeoutWrapper() => Run(_withTimeoutWrapper);

    [BenchmarkCategory("DirectorLinear10")]
    [Benchmark(Description = "Sync director-driven 10-step flow")]
    public Result DirectorLinear10() => Run(_directorLinear10);
}

file sealed class SyncLinearDirector(int max) : State, IDirector
{
    private int _counter;

    public NodeId SelectNext()
    {
        _counter++;
        return _counter >= max ? NodeId.Default : new NodeId();
    }

    protected override Result OnRun()
    {
        return Result.Success;
    }
}
