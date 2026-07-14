# NxGraph
A lean, high‑performance finite state machine (FSM) / stateflow library for .NET with a clean authoring DSL, strong validation, first‑class observability, and export tools (Mermaid, tracing, replay). Designed for correctness on hot paths (allocation‑free), production diagnostics, and pleasant authoring.

---

## Why NxGraph

- **Simple, fast core**: array‑backed `Graph` of `Node[]` and `Transition[]`, single edge per node. Cache‑friendly and easy to reason about.
- **Explicit branching**: choices/switches are modeled by *director* nodes, keeping the graph sparse and predictable.
- **Production diagnostics**: validators, Mermaid exporter, tracing observers, and replay tooling.
- **Authoring ergonomics**: fluent DSL with `StartWith`, `.To(...)`, `.If(...)`, `.Switch(...)`, `.WaitFor(...)`, `.Timeout(...)`.

---
## Features

- ✅ Allocation‑aware execution (`ValueTask<Result>` hot paths)
- ✅ Clear DSL for building graphs
- ✅ Directors for branching (`If`, `Switch`, choice predicates)
- ✅ Time primitives: `WaitFor(TimeSpan)`, `Timeout(TimeSpan)` wrappers
- ✅ Strong validation: broken edges, self‑loops, reachability, terminal paths
- ✅ Observers: lifecycle + node/transition hooks; exceptions bubble by default
- ✅ Mermaid exporter for architecture/ops visuals
- ✅ Replay trace capture and deterministic playback
- ✅ (Optional) OpenTelemetry‑style tracing via `Activity`
- ✅ Serialization (JSON/MessagePack) for graphs and replays

---

## Quick start

```csharp
using System.Threading;
using System.Threading.Tasks;
using NxGraph;
using NxGraph.Authoring;

// 1) Define state logic (no allocations on hot path)
static ValueTask<Result> Acquire(CancellationToken ct) => ResultHelpers.Success;
static ValueTask<Result> Process(CancellationToken ct) => ResultHelpers.Success;
static ValueTask<Result> Release(CancellationToken ct) => ResultHelpers.Success;

// 2) Build the graph with the DSL
var graph = GraphBuilder
    .StartWith(Acquire)
    .To(Process)
    .To(Release)
    .Build();

// 3) Execute
var sm = graph.ToAsyncStateMachine();
await sm.ExecuteAsync(CancellationToken.None);
```

**What you get**
- Deterministic, single‑edge execution
- Easy branching via directors (see below)
- Hooks for tracing/visualization

---

## Core concepts

- **Graph**: immutable structure of nodes and single outgoing transitions.
- **Node**: wraps `ILogic` — work that returns a `Result` (`Success`, `Failure`, etc.).
- **Director**: a special node that chooses the next node (e.g., `If`, `Switch`).
- **Transition**: an index from node *i → j*.
- **State machine**: a runtime over a `Graph` that executes from a start node until terminal.

---

## Authoring DSL

### Linear flows

```csharp
static ValueTask<Result> Start(CancellationToken _) => ResultHelpers.Success;
static ValueTask<Result> End(CancellationToken _) => ResultHelpers.Success;

var graph = GraphBuilder
    .StartWith(Start)
    .To(End)
    .Build();
```

### Branching with directors

```csharp
static ValueTask<Result> Start(CancellationToken _) => ResultHelpers.Success;
static bool IsPremium() => true; // your predicate
static ValueTask<Result> Premium(CancellationToken _) => ResultHelpers.Success;
static ValueTask<Result> Standard(CancellationToken _) => ResultHelpers.Success;

var graph = GraphBuilder
    .StartWith(Start)
    .If(IsPremium)
        .Then(Premium)
        .Else(Standard)
    .Build();
```

`Switch` example:

```csharp
static ValueTask<Result> Start(CancellationToken _) => ResultHelpers.Success;

static int RouteKey() => 2;
static ValueTask<Result> One(CancellationToken _) => ResultHelpers.Success;
static ValueTask<Result> Two(CancellationToken _) => ResultHelpers.Success;
static ValueTask<Result> Other(CancellationToken _) => ResultHelpers.Success;

var graph = GraphBuilder
    .StartWith(Start)
    .Switch(RouteKey)
        .Case(1, One)
        .Case(2, Two)
        .Default(Other)
    .End()
    .Build();
```

### Delays & timeouts

Both runtimes have wait and timeout constructs; all timeout overloads take the timeout first.

```csharp
// Sync (frame-stepped): WaitFor returns InProgress across ticks; ToWithTimeout checks the
// deadline between ticks and feeds the unified fault model on overrun.
var graph = GraphBuilder
    .StartWith(Start)
    .WaitFor(250.Milliseconds())
    .ToWithTimeout(500.Milliseconds(), () => Result.Success)
    .To(End)
    .Build();

// Async: WaitForAsync awaits a delay; ToWithTimeoutAsync cancels the wrapped logic on overrun.
var asyncGraph = GraphBuilder
    .StartWithAsync(StartAsync)
    .WaitForAsync(250.Milliseconds())
    .ToWithTimeoutAsync(500.Milliseconds(), async ct => await WorkAsync(ct))
    .Build();
```

> The async timeout cancels the wrapped logic via `CancellationToken` — honor the token inside your logic for graceful stops. The sync timeout cannot interrupt a node mid-execution (no cancellation in the sync runtime); it detects the deadline between ticks. Under `TimeoutBehavior.Fail` (default) a timeout is an ordinary node failure routed through failure edges and retries.

### Agents (dependency injection)

Provide an *agent* (context/service) to all nodes that opt‑in via `IAgentSettable<T>`.

```csharp
public sealed class AppAgent { public required ILogger Log { get; init; } }

public sealed class WorkState : ILogic, IAgentSettable<AppAgent>
{
    private AppAgent _agent = default!;
    public void SetAgent(AppAgent agent) => _agent = agent;
    public ValueTask<Result> ExecuteAsync(CancellationToken ct)
    {
        _agent.Log.LogInformation("working");
        return ResultHelpers.Success;
    }
}

var g = GraphBuilder.StartWith(new WorkState()).Build();
var sm = g.ToAsyncStateMachine<AppAgent>();
sm.SetAgent(new AppAgent { Log = logger });
```

### Blackboards (scoped shared memory)

Orthogonal to the agent: typed-key working memory with **Global** (one board shared across machines), **Graph** (one board per machine), and **Node** (transient per-visit scratch) scopes. Keys live on a `BlackboardSchema`; nodes access everything through the routed `Bb` context on the state bases — zero-boxing, zero-allocation reads/writes. Avoid `Dictionary<string, object>` contexts (string hashing + boxing per access).

Node-scoped boards are machine-owned: declare the schema on the graph via `.WithSchema(...)` and every machine auto-creates its own board — they can never be bound with `WithBlackboard`. Values reset to their registered defaults at every transition boundary (new visit, failure reroute, run start, reset, resume); in-place retries of the same visit keep the scratch. They are not durable: resuming a snapshot restores Node keys to defaults.

```csharp
static class Keys
{
    public static readonly BlackboardSchema World = new("world", BlackboardScope.Global);
    public static readonly BlackboardKey<bool> Alarm = World.Register<bool>("Alarm");

    public static readonly BlackboardSchema Enemy = new("enemy"); // Graph scope
    public static readonly BlackboardKey<int> Distance = Enemy.Register<int>("Distance", 10);
}

sealed class ChaseState : AsyncState
{
    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        if (Bb.Get(Keys.Alarm)) Bb.GetRef(Keys.Distance)--; // routed by schema scope
        return ResultHelpers.Success;
    }
}

Graph graph = GraphBuilder
    .StartWithAsync(new ChaseState())
    .If(bb => bb.Get(Keys.Alarm)).ThenAsync((bb, ct) => ResultHelpers.Success)
                                 .ElseAsync((bb, ct) => ResultHelpers.Success)
    .WithSchema(Keys.Enemy).WithSchema(Keys.World)   // opt-in bind-time validation
    .Build();

Blackboard world = new(Keys.World);                  // shared by every machine
var sm = graph.ToAsyncStateMachine()
    .WithBlackboard(world)
    .WithBlackboard(new Blackboard(Keys.Enemy));     // this machine's own memory
```

Boards serialize independently via `BlackboardSerializer` (`NxGraph.Serialization`) — one payload per board, restored into a live board over the same schema.

---

## Execution

```csharp
var sm = graph.ToAsyncStateMachine(observer: myObserver);
var status = await sm.ExecuteAsync(ct);
```

- **Threading**: execution is reentrancy‑guarded; call `ExecuteAsync` once per instance.
- **Cancellation**: all logic receives a `CancellationToken`.
- **Errors**: exceptions propagate unless you wrap logic/observer.

---

## Validation

Validate a graph before running it.

```csharp
GraphValidationResult results = GraphBuilder
    .StartWith(_ => ResultHelpers.Success).To(_ => ResultHelpers.Success)
    .Build().Validate();
if (results.HasErrors)
{
    foreach (GraphDiagnostic res in results.Diagnostics)
    {
        Console.WriteLine(res);
    }
}

//or throw exceptions in case of invalid graphs
GraphBuilder
    .StartWith(_ => ResultHelpers.Success).To(_ => ResultHelpers.Success)
    .Build().ValidateAndThrowIfErrorsDebug();
```

Checks include:
- Broken edges (out of range)
- Self‑loops (optional severity)
- Reachability from the start node
- Terminal path exists (no infinite director cycles)

---

## Observability

### State machine observers

Subscribe to lifecycle, node, and transition events.

```csharp
public sealed class ConsoleObserver : IAsyncStateMachineObserver
{
    public ValueTask OnStartedAsync(int id, CancellationToken ct) => Write("started");
    public ValueTask OnNodeEnteredAsync(int id, int idx, CancellationToken ct) => Write($"enter {idx}");
    public ValueTask OnTransitionAsync(int id, int from, int to, string? label, CancellationToken ct) => Write($"{from}->{to} {label}");
    public ValueTask OnNodeExitedAsync(int id, int idx, CancellationToken ct) => Write($"exit {idx}");
    public ValueTask OnStoppedAsync(int id, CancellationToken ct) => Write("stopped");
    static ValueTask Write(string s) { Console.WriteLine(s); return ValueTask.CompletedTask; }
}

var sm = graph.ToAsyncStateMachine(observer: new ConsoleObserver());
await sm.ExecuteAsync(CancellationToken.None);
```

> Observer exceptions bubble by default; wrap if you want best‑effort.

### Tracing (OpenTelemetry‑friendly)

A built‑in tracing observer maps machine/node lifecycles to `Activity` spans/events so you can export to Jaeger/Tempo/Zipkin.

```csharp
using var observer = new TracingObserver(activitySource);
var sm = graph.ToAsyncStateMachine(observer);
await sm.ExecuteAsync(ct);
```

### Replay

Record execution for offline visualization or debugging and play it back deterministically.

```csharp
var recorder = new ReplayRecorder();
var sm = graph.ToAsyncStateMachine(observer: recorder);
await sm.ExecuteAsync(ct);

var replay = new StateMachineReplay(recorder.GetEvents().Span);
replay.ReplayAll(
    evt => Console.WriteLine($"{evt.Timestamp:O} {evt.Type} {evt.Message}")
);

```

---

## Visualization

### Mermaid export

Export a static diagram for docs/PRs.

```csharp
string mermaid = GraphBuilder.StartWith(_ => ResultHelpers.Success).SetName("Start")
            .To(_ => ResultHelpers.Success).SetName("Process" )
            .To(_ => ResultHelpers.Success).SetName("Release")
            .Build()
            .ToMermaid();
Console.WriteLine("Mermaid:");
Console.WriteLine(mermaid);
```

Example output:

````mermaid
flowchart LR
    0([Start]) --> 1([Process])
    1 --> 2([Release])
````
