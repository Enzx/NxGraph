using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Benchmarks;

[MemoryDiagnoser]
[HideColumns("Job", "Median", "StdDev", "StdErr", "Error", "RatioSD")]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[CsvExporter]
public class NxFsmBenchmarks
{
    // ReSharper disable NullableWarningSuppressionIsUsed
    private AsyncStateMachine _singleSuccess = null!;
    private AsyncStateMachine _chain10 = null!;
    private AsyncStateMachine _chain50 = null!;
    private AsyncStateMachine _withObserver = null!;
    private AsyncStateMachine _withTimeoutWrapper = null!;
    private AsyncStateMachine _directorLinear10 = null!;
    // ReSharper restore NullableWarningSuppressionIsUsed

    private sealed class NoopObserver : IAsyncStateMachineObserver
    {
        public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask OnStateFailed(NodeId id, Exception? ex, CancellationToken ct = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
        {
            return ValueTask.CompletedTask;
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        _singleSuccess = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine();

        StateToken gb10 = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success);
        for (int i = 0; i < 9; i++)
        {
            gb10 = gb10.ToAsync(_ => ResultHelpers.Success);
        }

        _chain10 = gb10.ToAsyncStateMachine();

        StateToken gb50 = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success);
        for (int i = 0; i < 49; i++)
        {
            gb50 = gb50.ToAsync(_ => ResultHelpers.Success);
        }

        _chain50 = gb50.ToAsyncStateMachine();

        _withObserver = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsyncStateMachine(new NoopObserver());

        _withTimeoutWrapper = GraphBuilder
            .Start()
            .ToWithTimeoutAsync(_ => ValueTask.FromResult(Result.Success), 1.Seconds(), TimeoutBehavior.Fail)
            .ToAsyncStateMachine();

        // Director that sequences through 10 nodes
        StateToken builder = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success);
        for (int i = 0; i < 9; i++)
        {
            builder = builder.ToAsync(_ => ResultHelpers.Success);
        }

        _directorLinear10 = builder.ToAsync(new LinearDirector(10)).ToAsyncStateMachine();
    }

    // ---- Benchmarks ----

    [BenchmarkCategory("SingleNode")]
    [Benchmark(Baseline = true, Description = "Single node (RelayState.Success)")]
    public ValueTask<Result> SingleNode() => _singleSuccess.ExecuteAsync();

    [BenchmarkCategory("Chain10")]
    [Benchmark(Description = "Chain of 10 nodes (RelayState.Success)")]
    public ValueTask<Result> Chain10() => _chain10.ExecuteAsync();

    [BenchmarkCategory("Chain50")]
    [Benchmark(Description = "Chain of 50 nodes (RelayState.Success)")]
    public ValueTask<Result> Chain50() => _chain50.ExecuteAsync();

    [BenchmarkCategory("WithObserver")]
    [Benchmark(Description = "Single node + NoopObserver")]
    public ValueTask<Result> WithObserver() => _withObserver.ExecuteAsync();

    [BenchmarkCategory("WithTimeoutWrapper")]
    [Benchmark(Description = "Timeout wrapper around immediate success")]
    public ValueTask<Result> WithTimeoutWrapper() => _withTimeoutWrapper.ExecuteAsync();

    [BenchmarkCategory("DirectorLinear10")]
    [Benchmark(Description = "Director-driven 10-step flow")]
    public ValueTask<Result> DirectorLinear10() => _directorLinear10.ExecuteAsync();
}

file sealed class LinearDirector(int max) : AsyncState, IDirector
{
    private int _counter;


    public NodeId SelectNext()
    {
        _counter++;
        return _counter >= max ? NodeId.Default : new NodeId();
    }

    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        return ResultHelpers.Success;
    }
}
