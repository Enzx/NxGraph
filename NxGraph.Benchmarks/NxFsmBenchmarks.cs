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
public class NxFsmBenchmarks
{
    // ReSharper disable NullableWarningSuppressionIsUsed
    private StateMachine _singleSuccess = null!;
    private StateMachine _chain10 = null!;
    private StateMachine _chain50 = null!;
    private StateMachine _withObserver = null!;
    private StateMachine _withTimeoutWrapper = null!;
    private StateMachine _directorLinear10 = null!;
    // ReSharper restore NullableWarningSuppressionIsUsed

    private sealed class NoopObserver : IAsyncStateObserver
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
            .StartWith(_ => ResultHelpers.Success)
            .ToStateMachine();

        StateToken gb10 = GraphBuilder.StartWith(_ => ResultHelpers.Success);
        for (int i = 0; i < 9; i++)
        {
            gb10 = gb10.To(_ => ResultHelpers.Success);
        }

        _chain10 = gb10.ToStateMachine();

        StateToken gb50 = GraphBuilder.StartWith(_ => ResultHelpers.Success);
        for (int i = 0; i < 49; i++)
        {
            gb50 = gb50.To(_ => ResultHelpers.Success);
        }

        _chain50 = gb50.ToStateMachine();

        _withObserver = GraphBuilder
            .StartWith(_ => ResultHelpers.Success)
            .ToStateMachine(new NoopObserver());

        _withTimeoutWrapper = GraphBuilder
            .Start()
            .ToWithTimeout(_ => ValueTask.FromResult(Result.Success), 1.Seconds(), TimeoutBehavior.Fail)
            .ToStateMachine();

        // Director that sequences through 10 nodes
        StateToken builder = GraphBuilder.StartWith(_ => ResultHelpers.Success);
        for (int i = 0; i < 9; i++)
        {
            builder = builder.To(_ => ResultHelpers.Success);
        }

        _directorLinear10 = builder.To(new LinearDirector(10)).ToStateMachine();
    }

    // ---- Benchmarks ----

    [BenchmarkCategory("SingleNode")]
    [Benchmark(Baseline = true, Description = "Single node (RelayState.Success)")]
    public async Task<Result> SingleNode()
    {
        return await _singleSuccess.ExecuteAsync();
    }

    [BenchmarkCategory("Chain10")]
    [Benchmark(Description = "Chain of 10 nodes (RelayState.Success)")]
    public async Task<Result> Chain10()
    {
        return await _chain10.ExecuteAsync();
    }

    [BenchmarkCategory("Chain50")]
    [Benchmark(Description = "Chain of 50 nodes (RelayState.Success)")]
    public async Task<Result> Chain50()
    {
        return await _chain50.ExecuteAsync();
    }

    [BenchmarkCategory("WithObserver")]
    [Benchmark(Description = "Single node + NoopObserver")]
    public async Task<Result> WithObserver()
    {
        return await _withObserver.ExecuteAsync();
    }

    [BenchmarkCategory("WithTimeoutWrapper")]
    [Benchmark(Description = "Timeout wrapper around immediate success")]
    public async Task<Result> WithTimeoutWrapper()
    {
        return await _withTimeoutWrapper.ExecuteAsync();
    }

    [BenchmarkCategory("DirectorLinear10")]
    [Benchmark(Description = "Director-driven 10-step flow")]
    public async Task<Result> DirectorLinear10()
    {
        return await _directorLinear10.ExecuteAsync();
    }
}

file sealed class LinearDirector(int max) : State, IDirector
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