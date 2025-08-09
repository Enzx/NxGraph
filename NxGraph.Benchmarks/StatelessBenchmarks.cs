using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Stateless;

namespace NxGraph.Benchmarks;

[MemoryDiagnoser]
[HideColumns("Job", "Median", "StdDev", "StdErr", "Error", "RatioSD")]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
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
            _chain10.Configure(i).Permit(TriggerNext, i + 1);

        _chain50 = new StateMachine<int, int>(() => _stateChain50, s => _stateChain50 = s);
        for (int i = 0; i < 50; i++)
            _chain50.Configure(i).Permit(TriggerNext, i + 1);

        // WithObserver: mirror NxGraph's no-op observer using async entry hooks
        // We attach a trivial async OnEntry to each state to simulate observer overhead.
        _withObserver = new StateMachine<int, int>(() => _stateWithObserver, s => _stateWithObserver = s);
        for (int i = 0; i < 10; i++)
        {
            _withObserver.Configure(i)
                .OnEntryAsync(async _ => await Task.CompletedTask)
                .Permit(TriggerNext, i + 1);
        }

        _timeoutWrapper = new StateMachine<int, int>(() => _stateTimeoutWrapper, s => _stateTimeoutWrapper = s);
        _timeoutWrapper.Configure(0)
            .OnEntryAsync(async _ =>
            {
                Task work = Task.CompletedTask;
                Task timeout = Task.Delay(1000); // 1 second timeout
                await Task.WhenAny(work, timeout); // work completes first; just measuring wrapper cost
            })
            .Permit(TriggerNext, 1);

        // DirectorLinear10: emulate a director-driven 10-step flow
        _directorLinear10 = new StateMachine<int, int>(() => _stateDirector, s => _stateDirector = s);
        _directorLinear10.Configure(0).Permit(TriggerNext, 1);
        for (int i = 1; i < 10; i++)
            _directorLinear10.Configure(i).Permit(TriggerNext, i + 1);
    }

    [IterationSetup(Target = nameof(SingleTransition))]
    public void ResetSingle() => _stateSingle = 0;

    [IterationSetup(Target = nameof(Chain10))]
    public void Reset10() => _stateChain10 = 0;

    [IterationSetup(Target = nameof(Chain50))]
    public void Reset50() => _stateChain50 = 0;

    [IterationSetup(Target = nameof(Chain10_WithObserver))]
    public void ResetObserver() => _stateWithObserver = 0;

    [IterationSetup(Target = nameof(WithTimeoutWrapper))]
    public void ResetTimeout() => _stateTimeoutWrapper = 0;

    [IterationSetup(Target = nameof(DirectorLinear10))]
    public void ResetDirector() => _stateDirector = 0;

    // ---- Benchmarks ----

    [BenchmarkCategory("SingleNode")]
    [Benchmark(Baseline = true, Description = "Single transition 0 -> 1 (async)")]
    public async Task<int> SingleTransition()
    {
        await _single.FireAsync(TriggerNext);
        return _stateSingle;
    }

    [BenchmarkCategory("Chain10")]
    [Benchmark(Description = "Chain of 10 transitions (async)")]
    public async Task<int> Chain10()
    {
        for (int i = 0; i < 10; i++)
            await _chain10.FireAsync(TriggerNext);
        return _stateChain10;
    }

    [BenchmarkCategory("Chain50")]
    [Benchmark(Description = "Chain of 50 transitions (async)")]
    public async Task<int> Chain50()
    {
        for (int i = 0; i < 50; i++)
            await _chain50.FireAsync(TriggerNext);
        return _stateChain50;
    }

    [BenchmarkCategory("WithObserver")]
    [Benchmark(Description = "Chain10 + async no-op OnEntry (observer-like)")]
    public async Task<int> Chain10_WithObserver()
    {
        for (int i = 0; i < 10; i++)
            await _withObserver.FireAsync(TriggerNext);
        return _stateWithObserver;
    }

    [BenchmarkCategory("WithTimeoutWrapper")]
    [Benchmark(Description = "Timeout-style wrapper around immediate success (async)")]
    public async Task<int> WithTimeoutWrapper()
    {
        await _timeoutWrapper.FireAsync(TriggerNext);
        return _stateTimeoutWrapper;
    }

    [BenchmarkCategory("DirectorLinear10")]
    [Benchmark(Description = "Director-driven 10-step flow (async-fired)")]
    public async Task<int> DirectorLinear10()
    {
        //emulate a director sequencing nodes
        for (int i = 0; i < 10; i++)
            await _directorLinear10.FireAsync(TriggerNext);
        return _stateDirector;
    }
}
