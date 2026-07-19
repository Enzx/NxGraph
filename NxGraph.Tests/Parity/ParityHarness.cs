using System.Text.RegularExpressions;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxGraph.Tests.Parity;

// ─────────────────────────────────────────────────────────────────────────────
// Cross-runtime parity conformance harness (spec: runtime dedup + parity).
//
// A scenario is a graph-builder recipe plus probes; per runtime an adapter runs
// it and produces a normalized ordered trace collected from a recording
// observer, with surface-level facts (terminal result, final status,
// LastOutcome, thrown exception types, probe counters) appended as ordinary
// trace lines. Traces are compared with order-exact equality; the only
// normalizations applied are the documented mechanical differences between the
// runtimes, each encoded exactly once in TraceNormalizer / the drive helpers
// with a comment naming its source.
//
// Timing is excluded from traces by construction: no line ever carries a
// timestamp or duration, because the time domain legitimately differs between
// the runtimes (CLAUDE.md: "Backoff applies to the async runtime only; the
// sync runtime retries next tick"). The matrix compares sequences, never
// durations, and retry scenarios use zero backoff.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Canonical event-line formatting shared by all recording observers.</summary>
internal static class TraceFormat
{
    public static string Node(NodeId id) => string.IsNullOrEmpty(id.Name) ? $"#{id.Index}" : id.Name;

    public static string Failed(NodeId id, Exception? ex) =>
        ex is null ? $"failed {Node(id)} result" : $"failed {Node(id)} ex:{ex.GetType().Name}";
}

/// <summary>A graph recipe instantiation plus named probe counters read after the drive.</summary>
internal sealed class ParityScenario
{
    public required Graph Graph { get; init; }

    /// <summary>Read after the drive completes, appended to the trace in declaration order.</summary>
    public List<(string Name, Func<int> Read)> Probes { get; } = [];
}

// ── Recording observers ─────────────────────────────────────────────────────

/// <summary>
/// Sync FSM recorder. <paramref name="throwOnceAt"/> makes the observer throw a single
/// <see cref="InvalidOperationException"/> right after recording the first line starting
/// with that prefix — used to pin the run-start gate-repair behavior identically across
/// runtimes (the event is recorded first, so all adapters share the same trace prefix).
/// Note the sync observer surface has no OnStateMachineCancelled — see
/// <see cref="ParityAssert"/> normalization N3.
/// </summary>
internal sealed class SyncFsmRecorder(List<string> trace, string? throwOnceAt = null) : IStateMachineObserver
{
    private bool _thrown;

    private void Record(string line)
    {
        trace.Add(line);
        if (throwOnceAt is not null && !_thrown && line.StartsWith(throwOnceAt, StringComparison.Ordinal))
        {
            _thrown = true;
            throw new InvalidOperationException("parity observer boom");
        }
    }

    void IStateMachineObserver.OnStateEntered(NodeId id) => Record($"entered {TraceFormat.Node(id)}");

    void IStateMachineObserver.OnStateExited(NodeId id) => Record($"exited {TraceFormat.Node(id)}");

    void IStateMachineObserver.OnTransition(NodeId from, NodeId to) =>
        Record($"transition {TraceFormat.Node(from)}->{TraceFormat.Node(to)}");

    void IStateMachineObserver.OnStateFailed(NodeId id, Exception? ex) => Record(TraceFormat.Failed(id, ex));

    void IStateMachineObserver.OnStateMachineReset(NodeId graphId) => Record("machine-reset");

    void IStateMachineObserver.OnStateMachineStarted(NodeId graphId) => Record("machine-started");

    void IStateMachineObserver.OnStateMachineCompleted(NodeId graphId, Result result) =>
        Record($"machine-completed {result.Code}");

    void IStateMachineObserver.StateMachineStatusChanged(NodeId graphId, ExecutionStatus prev,
        ExecutionStatus next) => Record($"status {prev}->{next}");

    void IStateMachineObserver.OnLogReport(NodeId nodeId, string message) =>
        Record($"log {TraceFormat.Node(nodeId)} {message}");
}

/// <summary>Async FSM recorder — the <see cref="SyncFsmRecorder"/> twin over the async surface.</summary>
internal sealed class AsyncFsmRecorder(List<string> trace, string? throwOnceAt = null) : IAsyncStateMachineObserver
{
    private bool _thrown;

    private void Record(string line)
    {
        trace.Add(line);
        if (throwOnceAt is not null && !_thrown && line.StartsWith(throwOnceAt, StringComparison.Ordinal))
        {
            _thrown = true;
            throw new InvalidOperationException("parity observer boom");
        }
    }

    public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
    {
        Record($"entered {TraceFormat.Node(id)}");
        return default;
    }

    public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
    {
        Record($"exited {TraceFormat.Node(id)}");
        return default;
    }

    public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
    {
        Record($"transition {TraceFormat.Node(from)}->{TraceFormat.Node(to)}");
        return default;
    }

    public ValueTask OnStateFailed(NodeId id, Exception? ex, CancellationToken ct = default)
    {
        Record(TraceFormat.Failed(id, ex));
        return default;
    }

    public ValueTask OnStateMachineReset(NodeId graphId, CancellationToken ct = default)
    {
        Record("machine-reset");
        return default;
    }

    public ValueTask OnStateMachineStarted(NodeId graphId, CancellationToken ct = default)
    {
        Record("machine-started");
        return default;
    }

    public ValueTask OnStateMachineCompleted(NodeId graphId, Result result, CancellationToken ct = default)
    {
        Record($"machine-completed {result.Code}");
        return default;
    }

    public ValueTask OnStateMachineCancelled(NodeId graphId, CancellationToken ct = default)
    {
        Record("machine-cancelled");
        return default;
    }

    public ValueTask StateMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next,
        CancellationToken ct = default)
    {
        Record($"status {prev}->{next}");
        return default;
    }

    public ValueTask OnLogReport(NodeId nodeId, string message, CancellationToken ct = default)
    {
        Record($"log {TraceFormat.Node(nodeId)} {message}");
        return default;
    }
}

/// <summary>Sync token recorder — node events carry the token-id dimension (spec 007).</summary>
internal sealed class SyncTokenRecorder(List<string> trace) : ITokenMachineObserver
{
    void ITokenMachineObserver.OnTokenSpawned(int tokenId, int parentTokenId, NodeId at) =>
        trace.Add($"spawned t{tokenId} parent {parentTokenId} at {TraceFormat.Node(at)}");

    void ITokenMachineObserver.OnTokenRetired(int tokenId, NodeId at, TokenRetireReason reason) =>
        trace.Add($"retired t{tokenId} at {TraceFormat.Node(at)} {reason}");

    void ITokenMachineObserver.OnJoinFired(NodeId joinNode, int survivingTokenId) =>
        trace.Add($"join-fired {TraceFormat.Node(joinNode)} survivor t{survivingTokenId}");

    void ITokenMachineObserver.OnStateEntered(int tokenId, NodeId id) =>
        trace.Add($"entered t{tokenId} {TraceFormat.Node(id)}");

    void ITokenMachineObserver.OnStateExited(int tokenId, NodeId id) =>
        trace.Add($"exited t{tokenId} {TraceFormat.Node(id)}");

    void ITokenMachineObserver.OnTransition(int tokenId, NodeId from, NodeId to) =>
        trace.Add($"transition t{tokenId} {TraceFormat.Node(from)}->{TraceFormat.Node(to)}");

    void ITokenMachineObserver.OnStateFailed(int tokenId, NodeId id, Exception? ex) =>
        trace.Add(ex is null
            ? $"failed t{tokenId} {TraceFormat.Node(id)} result"
            : $"failed t{tokenId} {TraceFormat.Node(id)} ex:{ex.GetType().Name}");

    void ITokenMachineObserver.OnLogReport(int tokenId, NodeId nodeId, string message) =>
        trace.Add($"log t{tokenId} {TraceFormat.Node(nodeId)} {message}");

    void ITokenMachineObserver.OnTokenMachineReset(NodeId graphId) => trace.Add("machine-reset");

    void ITokenMachineObserver.OnTokenMachineStarted(NodeId graphId) => trace.Add("machine-started");

    void ITokenMachineObserver.OnTokenMachineCompleted(NodeId graphId, Result result) =>
        trace.Add($"machine-completed {result.Code}");

    void ITokenMachineObserver.TokenMachineStatusChanged(NodeId graphId, ExecutionStatus prev,
        ExecutionStatus next) => trace.Add($"status {prev}->{next}");
}

/// <summary>Async token recorder — the <see cref="SyncTokenRecorder"/> twin.</summary>
internal sealed class AsyncTokenRecorder(List<string> trace) : IAsyncTokenMachineObserver
{
    public ValueTask OnTokenSpawned(int tokenId, int parentTokenId, NodeId at, CancellationToken ct = default)
    {
        trace.Add($"spawned t{tokenId} parent {parentTokenId} at {TraceFormat.Node(at)}");
        return default;
    }

    public ValueTask OnTokenRetired(int tokenId, NodeId at, TokenRetireReason reason,
        CancellationToken ct = default)
    {
        trace.Add($"retired t{tokenId} at {TraceFormat.Node(at)} {reason}");
        return default;
    }

    public ValueTask OnJoinFired(NodeId joinNode, int survivingTokenId, CancellationToken ct = default)
    {
        trace.Add($"join-fired {TraceFormat.Node(joinNode)} survivor t{survivingTokenId}");
        return default;
    }

    public ValueTask OnStateEntered(int tokenId, NodeId id, CancellationToken ct = default)
    {
        trace.Add($"entered t{tokenId} {TraceFormat.Node(id)}");
        return default;
    }

    public ValueTask OnStateExited(int tokenId, NodeId id, CancellationToken ct = default)
    {
        trace.Add($"exited t{tokenId} {TraceFormat.Node(id)}");
        return default;
    }

    public ValueTask OnTransition(int tokenId, NodeId from, NodeId to, CancellationToken ct = default)
    {
        trace.Add($"transition t{tokenId} {TraceFormat.Node(from)}->{TraceFormat.Node(to)}");
        return default;
    }

    public ValueTask OnStateFailed(int tokenId, NodeId id, Exception? ex, CancellationToken ct = default)
    {
        trace.Add(ex is null
            ? $"failed t{tokenId} {TraceFormat.Node(id)} result"
            : $"failed t{tokenId} {TraceFormat.Node(id)} ex:{ex.GetType().Name}");
        return default;
    }

    public ValueTask OnLogReport(int tokenId, NodeId nodeId, string message, CancellationToken ct = default)
    {
        trace.Add($"log t{tokenId} {TraceFormat.Node(nodeId)} {message}");
        return default;
    }

    public ValueTask OnTokenMachineReset(NodeId graphId, CancellationToken ct = default)
    {
        trace.Add("machine-reset");
        return default;
    }

    public ValueTask OnTokenMachineStarted(NodeId graphId, CancellationToken ct = default)
    {
        trace.Add("machine-started");
        return default;
    }

    public ValueTask OnTokenMachineCompleted(NodeId graphId, Result result, CancellationToken ct = default)
    {
        trace.Add($"machine-completed {result.Code}");
        return default;
    }

    public ValueTask OnTokenMachineCancelled(NodeId graphId, CancellationToken ct = default)
    {
        trace.Add("machine-cancelled");
        return default;
    }

    public ValueTask TokenMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next,
        CancellationToken ct = default)
    {
        trace.Add($"status {prev}->{next}");
        return default;
    }
}

// ── Runtime adapters ────────────────────────────────────────────────────────

/// <summary>
/// Uniform driving surface over the four FSM adapters. All members are surface forwarding
/// only — the adapters add no behavior of their own, so any trace divergence is the
/// runtimes', not the harness's.
/// </summary>
internal interface IParityMachine
{
    /// <summary>One whole run through this adapter's surface (a stepped adapter loops).</summary>
    ValueTask<Result> RunToEndAsync();

    /// <summary>Exactly one step/tick. Stepped adapters only.</summary>
    ValueTask<Result> StepOnceAsync();

    void SetRestartPolicy(RestartPolicy policy);

    ExecutionStatus Status { get; }

    int LastOutcome { get; }

    string? LastOutcomeName { get; }

    StateMachineSnapshot Suspend();

    void Resume(StateMachineSnapshot snapshot);
}

internal sealed record FsmAdapter(string Name, bool Stepped,
    Func<Graph, List<string>, string?, IParityMachine> Create);

/// <summary>Sync StateMachine, RunToJoin: one Execute() completes the run.</summary>
internal sealed class SyncFullMachine : IParityMachine
{
    private readonly StateMachine _machine;

    public SyncFullMachine(Graph graph, List<string> trace, string? throwOnceAt)
    {
        _machine = graph.ToStateMachine(new SyncFsmRecorder(trace, throwOnceAt));
        _machine.SetStepMode(ParallelStepMode.RunToJoin);
    }

    public ValueTask<Result> RunToEndAsync() => new(_machine.Execute());

    public ValueTask<Result> StepOnceAsync() =>
        throw new NotSupportedException("RunToJoin completes a run per Execute(); use the stepped adapter.");

    public void SetRestartPolicy(RestartPolicy policy) => _machine.SetRestartPolicy(policy);

    public ExecutionStatus Status => _machine.Status;

    public int LastOutcome => _machine.LastOutcome;

    public string? LastOutcomeName => _machine.LastOutcomeName;

    public StateMachineSnapshot Suspend() => _machine.Suspend();

    public void Resume(StateMachineSnapshot snapshot) => _machine.Resume(snapshot);
}

/// <summary>Sync StateMachine, default RoundPerTick: Execute() advances one node per call.</summary>
internal sealed class SyncSteppedMachine(Graph graph, List<string> trace, string? throwOnceAt) : IParityMachine
{
    private readonly StateMachine _machine = graph.ToStateMachine(new SyncFsmRecorder(trace, throwOnceAt));

    public ValueTask<Result> RunToEndAsync()
    {
        Result result = _machine.Execute();
        while (result == Result.InProgress)
        {
            result = _machine.Execute();
        }

        return new ValueTask<Result>(result);
    }

    public ValueTask<Result> StepOnceAsync() => new(_machine.Execute());

    public void SetRestartPolicy(RestartPolicy policy) => _machine.SetRestartPolicy(policy);

    public ExecutionStatus Status => _machine.Status;

    public int LastOutcome => _machine.LastOutcome;

    public string? LastOutcomeName => _machine.LastOutcomeName;

    public StateMachineSnapshot Suspend() => _machine.Suspend();

    public void Resume(StateMachineSnapshot snapshot) => _machine.Resume(snapshot);
}

/// <summary>AsyncStateMachine, full-run ExecuteAsync.</summary>
internal sealed class AsyncFullMachine(Graph graph, List<string> trace, string? throwOnceAt) : IParityMachine
{
    private readonly AsyncStateMachine _machine =
        graph.ToAsyncStateMachine(new AsyncFsmRecorder(trace, throwOnceAt));

    public ValueTask<Result> RunToEndAsync() => _machine.ExecuteAsync();

    public ValueTask<Result> StepOnceAsync() =>
        throw new NotSupportedException("ExecuteAsync completes a run per call; use the stepped adapter.");

    public void SetRestartPolicy(RestartPolicy policy) => _machine.SetRestartPolicy(policy);

    public ExecutionStatus Status => _machine.Status;

    public int LastOutcome => _machine.LastOutcome;

    public string? LastOutcomeName => _machine.LastOutcomeName;

    public StateMachineSnapshot Suspend() => _machine.Suspend();

    public void Resume(StateMachineSnapshot snapshot) => _machine.Resume(snapshot);
}

/// <summary>AsyncStateMachine, StepAsync loop: one node per call.</summary>
internal sealed class AsyncSteppedMachine(Graph graph, List<string> trace, string? throwOnceAt) : IParityMachine
{
    private readonly AsyncStateMachine _machine =
        graph.ToAsyncStateMachine(new AsyncFsmRecorder(trace, throwOnceAt));

    public async ValueTask<Result> RunToEndAsync()
    {
        Result result = await _machine.StepAsync();
        while (result == Result.InProgress)
        {
            result = await _machine.StepAsync();
        }

        return result;
    }

    public ValueTask<Result> StepOnceAsync() => _machine.StepAsync();

    public void SetRestartPolicy(RestartPolicy policy) => _machine.SetRestartPolicy(policy);

    public ExecutionStatus Status => _machine.Status;

    public int LastOutcome => _machine.LastOutcome;

    public string? LastOutcomeName => _machine.LastOutcomeName;

    public StateMachineSnapshot Suspend() => _machine.Suspend();

    public void Resume(StateMachineSnapshot snapshot) => _machine.Resume(snapshot);
}

/// <summary>Uniform driving surface over the four token-machine adapters.</summary>
internal interface ITokenParityMachine
{
    ValueTask<Result> RunToEndAsync();

    void SetRestartPolicy(RestartPolicy policy);

    ExecutionStatus Status { get; }
}

internal sealed record TokenAdapter(string Name, Func<Graph, List<string>, ITokenParityMachine> Create);

/// <summary>Sync TokenMachine, RunToJoin: one Execute() drains the whole flow.</summary>
internal sealed class SyncTokenFullMachine : ITokenParityMachine
{
    private readonly TokenMachine _machine;

    public SyncTokenFullMachine(Graph graph, List<string> trace)
    {
        _machine = graph.ToTokenMachine(new SyncTokenRecorder(trace));
        _machine.SetStepMode(ParallelStepMode.RunToJoin);
    }

    public ValueTask<Result> RunToEndAsync() => new(_machine.Execute());

    public void SetRestartPolicy(RestartPolicy policy) => _machine.SetRestartPolicy(policy);

    public ExecutionStatus Status => _machine.Status;
}

/// <summary>Sync TokenMachine, default RoundPerTick: Execute() advances one round per call.</summary>
internal sealed class SyncTokenSteppedMachine(Graph graph, List<string> trace) : ITokenParityMachine
{
    private readonly TokenMachine _machine = graph.ToTokenMachine(new SyncTokenRecorder(trace));

    public ValueTask<Result> RunToEndAsync()
    {
        Result result = _machine.Execute();
        while (result == Result.InProgress)
        {
            result = _machine.Execute();
        }

        return new ValueTask<Result>(result);
    }

    public void SetRestartPolicy(RestartPolicy policy) => _machine.SetRestartPolicy(policy);

    public ExecutionStatus Status => _machine.Status;
}

/// <summary>AsyncTokenMachine, full-run ExecuteAsync.</summary>
internal sealed class AsyncTokenFullMachine(Graph graph, List<string> trace) : ITokenParityMachine
{
    private readonly AsyncTokenMachine _machine = graph.ToAsyncTokenMachine(new AsyncTokenRecorder(trace));

    public ValueTask<Result> RunToEndAsync() => _machine.ExecuteAsync();

    public void SetRestartPolicy(RestartPolicy policy) => _machine.SetRestartPolicy(policy);

    public ExecutionStatus Status => _machine.Status;
}

/// <summary>AsyncTokenMachine, StepAsync loop: one scheduling round per call.</summary>
internal sealed class AsyncTokenSteppedMachine(Graph graph, List<string> trace) : ITokenParityMachine
{
    private readonly AsyncTokenMachine _machine = graph.ToAsyncTokenMachine(new AsyncTokenRecorder(trace));

    public async ValueTask<Result> RunToEndAsync()
    {
        Result result = await _machine.StepAsync();
        while (result == Result.InProgress)
        {
            result = await _machine.StepAsync();
        }

        return result;
    }

    public void SetRestartPolicy(RestartPolicy policy) => _machine.SetRestartPolicy(policy);

    public ExecutionStatus Status => _machine.Status;
}

internal static class ParityAdapters
{
    /// <summary>The four FSM adapters: sync/async × full-run/stepped.</summary>
    public static readonly FsmAdapter[] Fsm =
    [
        new("sync/run-to-join", Stepped: false, (g, t, x) => new SyncFullMachine(g, t, x)),
        new("sync/stepped", Stepped: true, (g, t, x) => new SyncSteppedMachine(g, t, x)),
        new("async/execute", Stepped: false, (g, t, x) => new AsyncFullMachine(g, t, x)),
        new("async/stepped", Stepped: true, (g, t, x) => new AsyncSteppedMachine(g, t, x)),
    ];

    /// <summary>The four token-machine adapters: sync/async × full-run/stepped.</summary>
    public static readonly TokenAdapter[] Token =
    [
        new("token-sync/run-to-join", (g, t) => new SyncTokenFullMachine(g, t)),
        new("token-sync/stepped", (g, t) => new SyncTokenSteppedMachine(g, t)),
        new("token-async/execute", (g, t) => new AsyncTokenFullMachine(g, t)),
        new("token-async/stepped", (g, t) => new AsyncTokenSteppedMachine(g, t)),
    ];
}

// ── Normalizer ──────────────────────────────────────────────────────────────

/// <summary>
/// The documented mechanical differences between the runtime families, each encoded exactly
/// once. Within a family (FSM×FSM, token×token) traces are compared raw — no normalization —
/// so any drift in event structure fails the suite. Normalizations apply only to the
/// cross-family comparison of plain (fork/join-free) graphs.
/// </summary>
internal static class TraceNormalizer
{
    /// <summary>
    /// N1 — source: CLAUDE.md/AGENTS.md token-runtime bullet (spec 007): the token machines
    /// move tokens inside a scheduling round without the machine-wide
    /// Running → Transitioning → Running status hop the FSM machines make per node move, so
    /// they never report the Transitioning status. Dropping both lines of each hop from an
    /// FSM trace makes it comparable with a token-family trace.
    /// </summary>
    public static List<string> WithoutTransitioningStatus(List<string> trace) =>
        trace.Where(static line => !line.Contains("Transitioning", StringComparison.Ordinal)).ToList();

    /// <summary>
    /// N2 — source: spec 007 / CLAUDE.md ("Observers ... carry a token-id dimension"): the
    /// token observers add token-pool bookkeeping events (spawned/retired/join-fired) and a
    /// t{id} attribution on node events that the FSM observers do not have. A plain graph
    /// runs single-token, so stripping the dimension yields the FSM-shaped trace. Only
    /// single-token traces may be normalized this way.
    /// </summary>
    public static List<string> WithoutTokenDimension(List<string> trace) =>
        trace.Where(static line =>
                !line.StartsWith("spawned ", StringComparison.Ordinal) &&
                !line.StartsWith("retired ", StringComparison.Ordinal) &&
                !line.StartsWith("join-fired ", StringComparison.Ordinal))
            .Select(static line => Regex.Replace(line, @" t\d+ ", " "))
            .ToList();
}

// ── Assertion + drive helpers ───────────────────────────────────────────────

internal static class ParityAssert
{
    /// <summary>
    /// Order-exact equality of every trace against the first (the baseline adapter);
    /// returns the baseline so scenario tests can additionally pin driver-agnostic expected
    /// content (terminal result, outcome lines, probe values).
    /// Also enforces normalization N3 — source: CLAUDE.md ("sync has no Cancelled surface";
    /// the sync observer interface lacks OnStateMachineCancelled and the sync machine merely
    /// tolerates the status): cancellation is not comparable across the runtimes, so no
    /// scenario in this matrix may produce it. There is nothing to normalize away — the
    /// guard fails loudly if a scenario ever cancels.
    /// </summary>
    public static List<string> AllEqual(IReadOnlyList<(string Adapter, List<string> Trace)> runs)
    {
        Assert.That(runs, Has.Count.GreaterThanOrEqualTo(2), "A parity comparison needs at least two runs.");

        foreach ((string adapter, List<string> trace) in runs)
        {
            foreach (string line in trace)
            {
                if (line.Contains("Cancelled", StringComparison.Ordinal) ||
                    line.Contains("machine-cancelled", StringComparison.Ordinal))
                {
                    Assert.Fail(
                        $"Adapter '{adapter}' produced a cancellation event ('{line}'), but the sync runtime " +
                        "has no Cancelled surface — cancellation scenarios cannot be part of the " +
                        "cross-runtime matrix (normalization N3).");
                }
            }
        }

        (string baseline, List<string> expected) = runs[0];
        for (int i = 1; i < runs.Count; i++)
        {
            Assert.That(runs[i].Trace, Is.EqualTo(expected),
                $"Trace divergence: adapter '{runs[i].Adapter}' does not match baseline '{baseline}'.");
        }

        return expected;
    }
}

internal static class ParityDrives
{
    /// <summary>One full run; surface facts appended as trace lines.</summary>
    public static async ValueTask OneRunAsync(IParityMachine machine, List<string> trace)
    {
        Result result = await machine.RunToEndAsync();
        AppendSurface(machine, trace, result);
    }

    /// <summary>
    /// One run expected to throw out of the machine surface. Normalization N4 — source: the
    /// machines name their own concrete type in guidance messages ("StateMachine is in
    /// terminal state ..." vs "AsyncStateMachine is in terminal state ..."): parity is
    /// asserted on the exception type, never on message text, so only the type is recorded.
    /// </summary>
    public static async ValueTask RunExpectingThrowAsync(IParityMachine machine, List<string> trace)
    {
        try
        {
            await machine.RunToEndAsync();
            trace.Add("no-throw");
        }
        catch (Exception ex)
        {
            trace.Add($"threw {ex.GetType().Name}");
        }
    }

    public static void AppendSurface(IParityMachine machine, List<string> trace, Result result)
    {
        trace.Add($"run-result {result.Code}");
        trace.Add($"final-status {machine.Status}");
        trace.Add($"last-outcome {machine.LastOutcome} name={machine.LastOutcomeName ?? "-"}");
    }
}

// ── Runners ─────────────────────────────────────────────────────────────────

internal static class ParityRunner
{
    /// <summary>
    /// Runs the scenario through all four FSM adapters and asserts order-exact trace
    /// equality (raw — no normalization within the FSM family). The recipe is invoked per
    /// adapter so closure state (counters) starts fresh.
    /// </summary>
    public static async Task<List<string>> AssertFsmParityAsync(Func<ParityScenario> recipe,
        Func<IParityMachine, List<string>, ValueTask> drive, string? observerThrowOnceAt = null)
    {
        List<(string Adapter, List<string> Trace)> runs = [];
        foreach (FsmAdapter adapter in ParityAdapters.Fsm)
        {
            ParityScenario scenario = recipe();
            List<string> trace = [];
            IParityMachine machine = adapter.Create(scenario.Graph, trace, observerThrowOnceAt);
            await drive(machine, trace);
            AppendProbes(scenario, trace);
            runs.Add((adapter.Name, trace));
        }

        return ParityAssert.AllEqual(runs);
    }

    /// <summary>
    /// Suspend/resume parity: the full-run adapters contribute the uninterrupted baseline;
    /// each stepped adapter steps once, suspends, resumes the snapshot onto a fresh machine
    /// over the same graph (recording into the same trace list), and completes the run.
    /// Resume replays no observer events, so the stitched trace must equal the
    /// uninterrupted one exactly — across the boundary and across the runtimes.
    /// </summary>
    public static async Task<List<string>> AssertSuspendResumeParityAsync(Func<ParityScenario> recipe)
    {
        List<(string Adapter, List<string> Trace)> runs = [];
        foreach (FsmAdapter adapter in ParityAdapters.Fsm)
        {
            ParityScenario scenario = recipe();
            List<string> trace = [];
            IParityMachine first = adapter.Create(scenario.Graph, trace, null);

            if (!adapter.Stepped)
            {
                Result result = await first.RunToEndAsync();
                ParityDrives.AppendSurface(first, trace, result);
            }
            else
            {
                Result step = await first.StepOnceAsync();
                Assert.That(step, Is.EqualTo(Result.InProgress),
                    $"[{adapter.Name}] the scenario must span more than one step to suspend mid-run.");

                StateMachineSnapshot snapshot = first.Suspend();
                IParityMachine second = adapter.Create(scenario.Graph, trace, null);
                second.Resume(snapshot);
                Result result = await second.RunToEndAsync();
                ParityDrives.AppendSurface(second, trace, result);
            }

            AppendProbes(scenario, trace);
            runs.Add((adapter.Name, trace));
        }

        return ParityAssert.AllEqual(runs);
    }

    /// <summary>
    /// Runs the scenario through all token-machine adapters and asserts order-exact trace
    /// equality (raw — no normalization within the token family).
    /// </summary>
    public static async Task<List<string>> AssertTokenParityAsync(Func<ParityScenario> recipe,
        Action<ITokenParityMachine>? configure = null)
    {
        List<(string Adapter, List<string> Trace)> runs = [];
        foreach (TokenAdapter adapter in ParityAdapters.Token)
        {
            ParityScenario scenario = recipe();
            List<string> trace = [];
            ITokenParityMachine machine = adapter.Create(scenario.Graph, trace);
            configure?.Invoke(machine);
            Result result = await machine.RunToEndAsync();
            trace.Add($"run-result {result.Code}");
            trace.Add($"final-status {machine.Status}");
            AppendProbes(scenario, trace);
            runs.Add((adapter.Name, trace));
        }

        return ParityAssert.AllEqual(runs);
    }

    /// <summary>
    /// Cross-family parity for plain (fork/join-free) graphs — pins the documented claim
    /// that such graphs "run identically under either family". FSM traces are normalized
    /// with N1 (<see cref="TraceNormalizer.WithoutTransitioningStatus"/>), token traces with
    /// N2 (<see cref="TraceNormalizer.WithoutTokenDimension"/>); the LastOutcome surface is
    /// FSM-only, so the shared trailer carries run-result and final-status only.
    /// </summary>
    public static async Task<List<string>> AssertCrossFamilyParityAsync(Func<ParityScenario> recipe)
    {
        List<(string Adapter, List<string> Trace)> runs = [];

        foreach (FsmAdapter adapter in ParityAdapters.Fsm)
        {
            ParityScenario scenario = recipe();
            List<string> trace = [];
            IParityMachine machine = adapter.Create(scenario.Graph, trace, null);
            Result result = await machine.RunToEndAsync();
            List<string> normalized = TraceNormalizer.WithoutTransitioningStatus(trace);
            normalized.Add($"run-result {result.Code}");
            normalized.Add($"final-status {machine.Status}");
            AppendProbes(scenario, normalized);
            runs.Add((adapter.Name, normalized));
        }

        foreach (TokenAdapter adapter in ParityAdapters.Token)
        {
            ParityScenario scenario = recipe();
            List<string> trace = [];
            ITokenParityMachine machine = adapter.Create(scenario.Graph, trace);
            Result result = await machine.RunToEndAsync();
            List<string> normalized = TraceNormalizer.WithoutTokenDimension(trace);
            normalized.Add($"run-result {result.Code}");
            normalized.Add($"final-status {machine.Status}");
            AppendProbes(scenario, normalized);
            runs.Add((adapter.Name, normalized));
        }

        return ParityAssert.AllEqual(runs);
    }

    private static void AppendProbes(ParityScenario scenario, List<string> trace)
    {
        foreach ((string name, Func<int> read) in scenario.Probes)
        {
            trace.Add($"probe {name}={read()}");
        }
    }
}
