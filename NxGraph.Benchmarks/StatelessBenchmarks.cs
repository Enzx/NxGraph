using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Stateless;

namespace NxGraph.Benchmarks;

/// <summary>
/// Comparison baselines against the Stateless library, harness-matched to the NxGraph
/// suites: no <c>[IterationSetup]</c> (BenchmarkDotNet documents it as ruining ns-scale
/// precision, and the NxGraph classes measure setup-free thanks to
/// <c>RestartPolicy.Auto</c> re-execution), so each benchmark resets its state variable
/// inside the measured body — the same place NxGraph pays its per-run reset.
/// </summary>
[MemoryDiagnoser]
[HideColumns("Job", "Median", "StdDev", "StdErr", "Error", "RatioSD")]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[CsvExporter]
public class StatelessBenchmarks
{
    private const int TriggerNext = 1;

    private int _stateSingle;
    private int _stateChain10;
    private int _stateChain50;
    private int _stateWithObserver;
    private int _stateTimeoutWrapper;
    private int _stateDirector;

    // ReSharper disable NullableWarningSuppressionIsUsed
    private StateMachine<int, int> _single = null!;
    private StateMachine<int, int> _chain10 = null!;
    private StateMachine<int, int> _chain50 = null!;
    private StateMachine<int, int> _withObserver = null!;
    private StateMachine<int, int> _timeoutWrapper = null!;
    private StateMachine<int, int> _directorLinear10 = null!;
    // ReSharper restore NullableWarningSuppressionIsUsed

    [GlobalSetup]
    public void Setup()
    {
        _single = new StateMachine<int, int>(() => _stateSingle, s => _stateSingle = s);
        _single.Configure(0).Permit(TriggerNext, 1);

        _chain10 = new StateMachine<int, int>(() => _stateChain10, s => _stateChain10 = s);
        for (int i = 0; i < 10; i++)
        {
            _chain10.Configure(i).Permit(TriggerNext, i + 1);
        }

        _chain50 = new StateMachine<int, int>(() => _stateChain50, s => _stateChain50 = s);
        for (int i = 0; i < 50; i++)
        {
            _chain50.Configure(i).Permit(TriggerNext, i + 1);
        }

        // WithObserver: mirror NxGraph's no-op observer using async entry hooks
        // We attach a trivial async OnEntry to each state to simulate observer overhead.
        _withObserver = new StateMachine<int, int>(() => _stateWithObserver, s => _stateWithObserver = s);
        for (int i = 0; i < 10; i++)
        {
            _withObserver.Configure(i)
                .OnEntryAsync(async _ => await Task.CompletedTask)
                .Permit(TriggerNext, i + 1);
        }

        // Timeout analog matched to NxGraph's AsyncTimeoutState: arm a cancellation timer
        // for the deadline, run the (immediately completing) work under its token, and
        // reclaim the timer when the work finishes. The previous version scheduled a real
        // uncancelled Task.Delay(1000) per invocation — an orphaned live timer NxGraph
        // never pays, which made the comparison row measure a different operation.
        _timeoutWrapper = new StateMachine<int, int>(() => _stateTimeoutWrapper, s => _stateTimeoutWrapper = s);
        _timeoutWrapper.Configure(0)
            .OnEntryAsync(async _ =>
            {
                using CancellationTokenSource cts = new();
                cts.CancelAfter(TimeSpan.FromSeconds(1));
                await WorkUnderToken(cts.Token); // completes immediately; timer reclaimed by Dispose
            })
            .Permit(TriggerNext, 1);

        // DirectorLinear10: emulate a director-driven 10-step flow
        _directorLinear10 = new StateMachine<int, int>(() => _stateDirector, s => _stateDirector = s);
        _directorLinear10.Configure(0).Permit(TriggerNext, 1);
        for (int i = 1; i < 10; i++)
        {
            _directorLinear10.Configure(i).Permit(TriggerNext, i + 1);
        }
    }

    private static Task WorkUnderToken(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    // ---- Benchmarks ----

    [BenchmarkCategory("SingleNode")]
    [Benchmark(Baseline = true, Description = "Single transition 0 -> 1 (async)")]
    public async Task<int> SingleTransition()
    {
        _stateSingle = 0;
        await _single.FireAsync(TriggerNext);
        return _stateSingle;
    }

    [BenchmarkCategory("Chain10")]
    [Benchmark(Description = "Chain of 10 transitions (async)")]
    public async Task<int> Chain10()
    {
        _stateChain10 = 0;
        for (int i = 0; i < 10; i++)
        {
            await _chain10.FireAsync(TriggerNext);
        }

        return _stateChain10;
    }

    [BenchmarkCategory("Chain50")]
    [Benchmark(Description = "Chain of 50 transitions (async)")]
    public async Task<int> Chain50()
    {
        _stateChain50 = 0;
        for (int i = 0; i < 50; i++)
        {
            await _chain50.FireAsync(TriggerNext);
        }

        return _stateChain50;
    }

    [BenchmarkCategory("WithObserver")]
    [Benchmark(Description = "Chain10 + async no-op OnEntry (observer-like)")]
    public async Task<int> Chain10WithObserver()
    {
        _stateWithObserver = 0;
        for (int i = 0; i < 10; i++)
        {
            await _withObserver.FireAsync(TriggerNext);
        }

        return _stateWithObserver;
    }

    [BenchmarkCategory("WithTimeoutWrapper")]
    [Benchmark(Description = "Timeout-style wrapper around immediate success (async)")]
    public async Task<int> WithTimeoutWrapper()
    {
        _stateTimeoutWrapper = 0;
        await _timeoutWrapper.FireAsync(TriggerNext);
        return _stateTimeoutWrapper;
    }

    [BenchmarkCategory("DirectorLinear10")]
    [Benchmark(Description = "Director-driven 10-step flow (async-fired)")]
    public async Task<int> DirectorLinear10()
    {
        _stateDirector = 0;
        //emulate a director sequencing nodes
        for (int i = 0; i < 10; i++)
        {
            await _directorLinear10.FireAsync(TriggerNext);
        }

        return _stateDirector;
    }
}
