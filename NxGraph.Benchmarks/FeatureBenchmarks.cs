using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxGraph.Benchmarks;

/// <summary>
/// Benchmarks for the headline features that shipped after the original suite: parallel
/// composites (static and dynamic selection), scoped blackboards (typed keys vs the
/// documented <c>Dictionary&lt;string, object&gt;</c> anti-pattern), the token runtime's
/// diamond (fork → join), step I/O port relays, durable suspend + resume, and Switch
/// dispatch. No Stateless comparison rows — these have no counterpart there.
/// </summary>
[MemoryDiagnoser]
[HideColumns("Job", "Median", "StdDev", "StdErr", "Error", "RatioSD")]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[CsvExporter]
public class FeatureBenchmarks
{
    private const int BlackboardOpsPerRun = 10;

    private static readonly BlackboardSchema BoardSchema = new("bench", BlackboardScope.Graph);
    private static readonly BlackboardKey<int> CounterKey = BoardSchema.Register("counter", 0);
    private static readonly BlackboardKey<int> PortKey = BoardSchema.Register("port", 0);

    // ReSharper disable NullableWarningSuppressionIsUsed
    private AsyncStateMachine _parallelAsync = null!;
    private StateMachine _parallelSync = null!;
    private AsyncStateMachine _dynamicParallelAsync = null!;
    private StateMachine _dynamicParallelSync = null!;
    private StateMachine _blackboardTyped = null!;
    private StateMachine _blackboardDictionary = null!;
    private Dictionary<string, object> _dictionaryBoard = null!;
    private AsyncTokenMachine _tokenDiamondAsync = null!;
    private TokenMachine _tokenDiamondSync = null!;
    private AsyncStateMachine _portRelayAsync = null!;
    private StateMachine _portRelaySync = null!;
    private AsyncStateMachine _suspendResumeAsync = null!;
    private StateMachine _suspendResumeSync = null!;
    private AsyncStateMachine _switchAsync = null!;
    private StateMachine _switchSync = null!;
    // ReSharper restore NullableWarningSuppressionIsUsed

    private static Graph AsyncRegion()
    {
        return GraphBuilder
            .StartWithAsync(static _ => ResultHelpers.Success)
            .ToAsync(static _ => ResultHelpers.Success)
            .Build();
    }

    private static Graph SyncRegion()
    {
        return GraphBuilder
            .StartWith(static () => Result.Success)
            .To(static () => Result.Success)
            .Build();
    }

    private static Result Run(StateMachine sm)
    {
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = sm.Execute();
        }

        return result;
    }

    [GlobalSetup]
    public void Setup()
    {
        // ── Parallel composite: 2 regions × 2 nodes, cooperative rounds to the join ──
        _parallelAsync = GraphBuilder.Start()
            .Parallel(AsyncRegion(), AsyncRegion())
            .ToAsyncStateMachine();

        _parallelSync = GraphBuilder.Start()
            .Parallel(ParallelStepMode.RunToJoin, SyncRegion(), SyncRegion())
            .ToStateMachine();

        // ── Dynamic parallel: selector picks 1 of 2 regions once at entry ──
        _dynamicParallelAsync = GraphBuilder.Start()
            .Parallel(static _ => RegionMask.Bit(0), AsyncRegion(), AsyncRegion())
            .ToAsyncStateMachine();

        _dynamicParallelSync = GraphBuilder.Start()
            .Parallel(ParallelStepMode.RunToJoin, static _ => RegionMask.Bit(0), SyncRegion(), SyncRegion())
            .ToStateMachine();

        // ── Blackboard access: typed scoped key vs Dictionary<string, object> ──
        _blackboardTyped = GraphBuilder.Start()
            .WithSchema(BoardSchema)
            .To(static bb =>
            {
                for (int i = 0; i < BlackboardOpsPerRun; i++)
                {
                    int value = bb.Get(CounterKey);
                    bb.Set(CounterKey, value + 1);
                }

                return Result.Success;
            })
            .ToStateMachine()
            .WithBlackboard(new Blackboard(BoardSchema));

        Dictionary<string, object> dictionary = new() { ["counter"] = 0 };
        _dictionaryBoard = dictionary;
        _blackboardDictionary = GraphBuilder
            .StartWith(() =>
            {
                for (int i = 0; i < BlackboardOpsPerRun; i++)
                {
                    int value = (int)dictionary["counter"];
                    dictionary["counter"] = value + 1; // boxes on every write
                }

                return Result.Success;
            })
            .ToStateMachine();

        // ── Token runtime: diamond fork(a, b) → join(All 2) → finish ──
        _tokenDiamondAsync = BuildAsyncDiamond().ToAsyncTokenMachine();
        _tokenDiamondSync = BuildSyncDiamond().ToTokenMachine();
        _tokenDiamondSync.SetStepMode(ParallelStepMode.RunToJoin);

        // ── Step I/O ports: producer writes the port, consumer reads it ──
        _portRelayAsync = GraphBuilder.Start()
            .WithSchema(BoardSchema)
            .ToAsync(PortKey, static (_, _) => new ValueTask<int>(42))
            .ToAsync(PortKey, static (value, _, _) =>
                value == 42 ? ResultHelpers.Success : ResultHelpers.Failure)
            .ToAsyncStateMachine()
            .WithBlackboard(new Blackboard(BoardSchema));

        _portRelaySync = GraphBuilder.Start()
            .WithSchema(BoardSchema)
            .To(PortKey, static _ => 42)
            .To(PortKey, static (value, _) => value == 42 ? Result.Success : Result.Failure)
            .ToStateMachine()
            .WithBlackboard(new Blackboard(BoardSchema));

        // ── Suspend + resume round-trip on a 4-node chain, paused after the first step ──
        _suspendResumeAsync = GraphBuilder
            .StartWithAsync(static _ => ResultHelpers.Success)
            .ToAsync(static _ => ResultHelpers.Success)
            .ToAsync(static _ => ResultHelpers.Success)
            .ToAsync(static _ => ResultHelpers.Success)
            .ToAsyncStateMachine();

        _suspendResumeSync = GraphBuilder
            .StartWith(static () => Result.Success)
            .To(static () => Result.Success)
            .To(static () => Result.Success)
            .To(static () => Result.Success)
            .ToStateMachine();

        // ── Switch dispatch: 3 cases + default, selector picks the middle case ──
        _switchAsync = GraphBuilder
            .StartWithAsync(static _ => ResultHelpers.Success)
            .SwitchAsync(static () => new ValueTask<int>(2))
            .CaseAsync(1, static _ => ResultHelpers.Success)
            .CaseAsync(2, static _ => ResultHelpers.Success)
            .CaseAsync(3, static _ => ResultHelpers.Success)
            .DefaultAsync(static _ => ResultHelpers.Failure)
            .End()
            .ToAsyncStateMachine();

        _switchSync = GraphBuilder
            .StartWith(static () => Result.Success)
            .Switch(static () => 2)
            .Case(1, static () => Result.Success)
            .Case(2, static () => Result.Success)
            .Case(3, static () => Result.Success)
            .Default(static () => Result.Failure)
            .End()
            .ToStateMachine();
    }

    private static Graph BuildAsyncDiamond()
    {
        JoinState join = new(JoinPolicy.All(2));
        return GraphBuilder
            .StartWithAsync(static _ => ResultHelpers.Success)
            .ForkTo(
                b => b.ToAsync(static _ => ResultHelpers.Success).To(join)
                    .ToAsync(static _ => ResultHelpers.Success),
                b => b.ToAsync(static _ => ResultHelpers.Success).To(join))
            .Build();
    }

    private static Graph BuildSyncDiamond()
    {
        JoinState join = new(JoinPolicy.All(2));
        return GraphBuilder
            .StartWith(static () => Result.Success)
            .ForkTo(
                b => b.To(static () => Result.Success).To(join)
                    .To(static () => Result.Success),
                b => b.To(static () => Result.Success).To(join))
            .Build();
    }

    // ---- Benchmarks ----

    [BenchmarkCategory("ParallelComposite")]
    [Benchmark(Description = "Async parallel composite, 2 regions x 2 nodes")]
    public ValueTask<Result> ParallelAsync() => _parallelAsync.ExecuteAsync();

    [BenchmarkCategory("ParallelComposite")]
    [Benchmark(Description = "Sync parallel composite (RunToJoin), 2 regions x 2 nodes")]
    public Result ParallelSync() => Run(_parallelSync);

    [BenchmarkCategory("DynamicParallel")]
    [Benchmark(Description = "Async dynamic parallel, selector picks 1 of 2 regions")]
    public ValueTask<Result> DynamicParallelAsync() => _dynamicParallelAsync.ExecuteAsync();

    [BenchmarkCategory("DynamicParallel")]
    [Benchmark(Description = "Sync dynamic parallel (RunToJoin), selector picks 1 of 2 regions")]
    public Result DynamicParallelSync() => Run(_dynamicParallelSync);

    [BenchmarkCategory("BlackboardAccess")]
    [Benchmark(Baseline = true, Description = "Typed BlackboardKey<int> Get+Set x10 in one node")]
    public Result BlackboardTypedKey() => Run(_blackboardTyped);

    [BenchmarkCategory("BlackboardAccess")]
    [Benchmark(Description = "Dictionary<string, object> get+set x10 in one node (boxing anti-pattern)")]
    public Result BlackboardDictionary() => Run(_blackboardDictionary);

    [BenchmarkCategory("TokenDiamond")]
    [Benchmark(Description = "Async token diamond: fork(2) -> join(All 2) -> finish")]
    public ValueTask<Result> TokenDiamondAsync() => _tokenDiamondAsync.ExecuteAsync();

    [BenchmarkCategory("TokenDiamond")]
    [Benchmark(Description = "Sync token diamond (RunToJoin): fork(2) -> join(All 2) -> finish")]
    public Result TokenDiamondSync() => _tokenDiamondSync.Execute();

    [BenchmarkCategory("PortRelay")]
    [Benchmark(Description = "Async port relay hop: producer -> consumer via Graph-scoped key")]
    public ValueTask<Result> PortRelayAsync() => _portRelayAsync.ExecuteAsync();

    [BenchmarkCategory("PortRelay")]
    [Benchmark(Description = "Sync port relay hop: producer -> consumer via Graph-scoped key")]
    public Result PortRelaySync() => Run(_portRelaySync);

    [BenchmarkCategory("SuspendResume")]
    [Benchmark(Description = "Async suspend+resume round-trip mid-run (4-node chain)")]
    public async ValueTask<Result> SuspendResumeAsync()
    {
        Result result = await _suspendResumeAsync.StepAsync();
        StateMachineSnapshot snapshot = _suspendResumeAsync.Suspend();
        _suspendResumeAsync.Resume(snapshot);
        while (result == Result.InProgress)
        {
            result = await _suspendResumeAsync.StepAsync();
        }

        return result;
    }

    [BenchmarkCategory("SuspendResume")]
    [Benchmark(Description = "Sync suspend+resume round-trip mid-run (4-node chain)")]
    public Result SuspendResumeSync()
    {
        Result result = _suspendResumeSync.Execute();
        StateMachineSnapshot snapshot = _suspendResumeSync.Suspend();
        _suspendResumeSync.Resume(snapshot);
        while (result == Result.InProgress)
        {
            result = _suspendResumeSync.Execute();
        }

        return result;
    }

    [BenchmarkCategory("SwitchDispatch")]
    [Benchmark(Description = "Async Switch dispatch: 3 cases + default")]
    public ValueTask<Result> SwitchDispatchAsync() => _switchAsync.ExecuteAsync();

    [BenchmarkCategory("SwitchDispatch")]
    [Benchmark(Description = "Sync Switch dispatch: 3 cases + default")]
    public Result SwitchDispatchSync() => Run(_switchSync);
}
