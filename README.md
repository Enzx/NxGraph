[![NuGet NxGraph](https://img.shields.io/nuget/v/NxGraph.svg?label=NuGet%20NxGraph)](https://www.nuget.org/packages/NxGraph/)
[![NuGet NxGraph.Serialization](https://img.shields.io/nuget/v/NxGraph.Serialization.svg?label=Serialization)](https://www.nuget.org/packages/NxGraph.Serialization/)
[![NuGet NxGraph.Serialization.Abstraction](https://img.shields.io/nuget/v/NxGraph.Serialization.Abstraction.svg?label=Abstraction)](https://www.nuget.org/packages/NxGraph.Serialization.Abstraction/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
[![Build](https://github.com/Enzx/NxGraph/actions/workflows/dotnet.yml/badge.svg?branch=main)](https://github.com/Enzx/NxGraph/actions/workflows/dotnet.yml)
[![Publish](https://github.com/Enzx/NxGraph/actions/workflows/publish-nuget.yml/badge.svg)](https://github.com/Enzx/NxGraph/actions/workflows/publish-nuget.yml)

# NxGraph

NxGraph is a lean, high-performance finite state machine / stateflow library for .NET with:

- a fluent authoring DSL
- explicit branching through director nodes
- sync and async runtimes
- stepped execution for Unity (`Update()`-loop friendly)
- a unified fault model: per-node retries, failure edges (`.OnError`), and timeouts
- composites: subgraphs (with history), parallel regions, and blackboard-selected dynamic regions
- durable suspend/resume via serializable snapshots
- scoped blackboards (typed, zero-boxing shared memory)
- graph validation
- observers, tracing, replay, and Mermaid export
- optional graph serialization via a codec-based serializer

The core package targets `net8.0` and `netstandard2.1`.

---

## Table of contents

- [Why NxGraph](#why-nxgraph)
- [Packages](#packages)
- [Install](#install)
- [Quick start](#quick-start)
  - [Async quick start](#async-quick-start)
  - [Sync quick start](#sync-quick-start)
- [Authoring DSL](#authoring-dsl)
  - [Linear flows](#linear-flows)
  - [Branching with `If`](#branching-with-if)
  - [Branching with `Switch`](#branching-with-switch)
  - [Custom directors](#custom-directors)
  - [Fan-out at a glance](#fan-out-at-a-glance)
  - [Waits and timeouts](#waits-and-timeouts)
  - [Error handling: retries and failure edges](#error-handling-retries-and-failure-edges)
  - [Loops with `Goto`](#loops-with-goto)
  - [Named outcomes](#named-outcomes)
  - [Naming nodes](#naming-nodes)
  - [Agents / context injection](#agents--context-injection)
  - [Blackboards (scoped shared memory)](#blackboards-scoped-shared-memory)
- [Execution](#execution)
  - [Async execution](#async-execution)
  - [Sync execution, stepped model](#sync-execution-stepped-model)
  - [Unity integration](#unity-integration)
  - [Nested machines](#nested-machines)
  - [Composites: subgraphs, history, and parallel regions](#composites-subgraphs-history-and-parallel-regions)
  - [Durable suspend / resume](#durable-suspend--resume)
  - [Restart policy](#restart-policy)
- [Validation](#validation)
- [Observability](#observability)
  - [Observers](#observers)
  - [State logging](#state-logging)
  - [Tracing](#tracing)
  - [Replay](#replay)
- [Visualization](#visualization)
- [Serialization](#serialization)
- [Examples](#examples)
- [Benchmarks](#benchmarks)
- [Testing](#testing)
- [FAQ](#faq)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License](#license)

---

## Why NxGraph

- **Simple runtime model**: graphs are backed by dense node/transition arrays and each node has at most one success edge plus an optional failure edge.
- **Predictable branching**: run-one fan-out happens through director nodes such as `ChoiceState` and `SwitchState<TKey>`; run-many fan-out through parallel composites — see [Fan-out at a glance](#fan-out-at-a-glance).
- **Authoring ergonomics**: build flows with `StartWithAsync`, `.ToAsync(...)`, `.If(...)`, `.Switch(...)`, `.WaitForAsync(...)`/`.WaitFor(...)`, and `.ToWithTimeoutAsync(...)`/`.ToWithTimeout(...)` — every construct has twins in both runtimes.
- **Unity-ready sync runtime**: `StateMachine.Execute()` advances exactly one node per call, drop it into `MonoBehaviour.Update()`.
- **Diagnostics built in**: validate graphs, inspect Mermaid output, attach observers, capture replay logs, or emit `Activity` traces.
- **Both async and sync**: use `AsyncStateMachine` for async logic and `StateMachine` for sync-only flows.

---

## Packages

### `NxGraph`
The core package. Includes:

- graph model and FSM runtimes
- fluent DSL
- validation
- Mermaid export
- replay recording / playback
- tracing observer

### `NxGraph.Serialization`
Optional serializer package for persisting graphs to JSON or MessagePack using your own logic codec.

### `NxGraph.Serialization.Abstraction`
Optional interfaces for consumers who only need serialization contracts.

---

## Install

Core package:

```bash
dotnet add package NxGraph
```

Optional graph serialization:

```bash
dotnet add package NxGraph.Serialization
```

Optional serialization abstractions only:

```bash
dotnet add package NxGraph.Serialization.Abstraction
```

Build from source:

```bash
dotnet build -c Release
dotnet test -c Release
```

---

## Quick start

### Async quick start

```csharp
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

static ValueTask<Result> Acquire(CancellationToken _) => ResultHelpers.Success;
static ValueTask<Result> Process(CancellationToken _) => ResultHelpers.Success;
static ValueTask<Result> Release(CancellationToken _) => ResultHelpers.Success;

AsyncStateMachine fsm = GraphBuilder
    .StartWithAsync(Acquire).SetName("Acquire")
    .ToAsync(Process).SetName("Process")
    .ToAsync(Release).SetName("Release")
    .ToAsyncStateMachine();

Result result = await fsm.ExecuteAsync();
```

### Sync quick start

```csharp
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;

StateMachine fsm = GraphBuilder
    .StartWith(() => Result.Success).SetName("Start")
    .To(() => Result.Success).SetName("End")
    .ToStateMachine();

// Execute() advances one node per call; loop to run to completion:
Result result = Result.InProgress;
while (result == Result.InProgress)
    result = fsm.Execute();
```

For a single-node graph `Execute()` returns `Result.Success` (or `Result.Failure`) immediately. For multi-node graphs it returns `Result.InProgress` after each intermediate node, signalling that more nodes remain. See [Sync execution, stepped model](#sync-execution-stepped-model) for the Unity pattern.

---

## Authoring DSL

### Linear flows

```csharp
var graph = GraphBuilder
    .StartWithAsync(_ => ResultHelpers.Success).SetName("Start")
    .ToAsync(_ => ResultHelpers.Success).SetName("Step1")
    .ToAsync(_ => ResultHelpers.Success).SetName("Step2")
    .Build();
```

### Branching with `If`

```csharp
bool IsPremium() => true;

var graph = GraphBuilder
    .StartWithAsync(_ => ResultHelpers.Success).SetName("Entry")
    .If(IsPremium)
        .ThenAsync(_ => ResultHelpers.Success).SetName("Premium")
        .ElseAsync(_ => ResultHelpers.Success).SetName("Standard")
    .Build();
```

### Branching with `Switch`

```csharp
int RouteKey() => 2;

var graph = GraphBuilder
    .StartWithAsync(_ => ResultHelpers.Success).SetName("Entry")
    .Switch(RouteKey)
        .CaseAsync(1, _ => ResultHelpers.Success)
        .CaseAsync(2, _ => ResultHelpers.Success)
        .DefaultAsync(_ => ResultHelpers.Failure)
    .End().SetName("Router")
    .Build();
```

### Custom directors

`.If(...)` and `.Switch(...)` compile down to the built-in director nodes `ChoiceState` and `SwitchState<TKey>`. A **director** is a node implementing `IDirector` (`IAsyncDirector` for the async runtime) whose `SelectNext()` picks the next node at runtime — implement it yourself when the routing decision doesn't fit a predicate or a key/case map. Override `EnumerateStaticTargets()` to surface the nodes you can route to: the validator and the Mermaid exporter walk it, and the validator warns when a custom director exposes none (its branches would be invisible to reachability analysis and diagrams).

### Fan-out at a glance

Every fan-out construct answers two questions: **how many successors run**, and **when the set is chosen**. The four quadrants:

| How many run | Chosen statically (declared in the graph) | Chosen dynamically (at runtime) |
|---|---|---|
| **One of many** | Conditional — [`.If(...)`](#branching-with-if) / [`.Switch(...)`](#branching-with-switch) declare the branches and the routing rule | Director — [`IDirector`](#custom-directors) selects any node in code; `ChoiceState`/`SwitchState<TKey>` are the built-ins |
| **Many at once** | Parallel — [`.Parallel(regions...)`](#parallel-regions-and-states) runs **all** region graphs | Dynamic parallel — [`.Parallel(selector, ...)`](#dynamic-some-of-many-regions) runs the **subset** a blackboard selector picks |

There is deliberately no free-form fan-out inside one flat graph (several nodes of the same graph active at once): the runtimes keep exactly one active node, and many-at-once execution lives inside the parallel composites, which join back into the single-active flow at the composite boundary. A token-based (Petri-net-style) runner that would lift this — mid-graph rejoin, the same node active k times — is a recorded, deliberately deferred design; if it ever lands it will be a third runtime beside the sync/async machines, not a change to them.

### Waits and timeouts

```csharp
var delayed = GraphBuilder
    .StartWithAsync(_ => ResultHelpers.Success).SetName("Start")
    .WaitForAsync(250.Milliseconds()).SetName("Cooldown")
    .ToAsync(_ => ResultHelpers.Success).SetName("Finish")
    .Build();

var timed = GraphBuilder
    .StartWithAsync(_ => ResultHelpers.Success).SetName("Start")
    .ToWithTimeoutAsync(2.Seconds(), _ => ResultHelpers.Success, TimeoutBehavior.Fail)
        .SetName("TimedWork")
    .ToAsync(_ => ResultHelpers.Success).SetName("AfterTimeout")
    .Build();
```

With `TimeoutBehavior.Fail` an expired timeout is an ordinary node failure — it consumes the node's retry budget and follows its failure edge like any other `Result.Failure` (see the next section). `TimeoutBehavior.Throw` raises a `TimeoutException` instead.

The sync runtime has frame-stepped twins. `.WaitFor(TimeSpan)` returns `Result.InProgress` across ticks until the duration elapses (a `Stopwatch` timestamp comparison — no timers, no allocation); `.ToWithTimeout(timeout, ...)` runs the wrapped logic once per tick and produces the timeout outcome when it overstays the deadline. Because the sync runtime has no cancellation, the deadline is detected *between* ticks — a node cannot be interrupted mid-execution. Both are sync-only (`WaitFor`'s node-level `InProgress` is rejected by the async loop); all timeout overloads, sync and async, take the timeout first.

```csharp
var syncFlow = GraphBuilder
    .StartWith(() => Result.Success)
    .WaitFor(250.Milliseconds())
    .ToWithTimeout(2.Seconds(), () => Result.Success)
    .Build();
```

### Error handling: retries and failure edges

A node returning `Result.Failure` flows through one unified fault model: first its per-node `RetryPolicy` re-runs it in place, then its **failure edge** (if any) routes to a handler, and only when neither applies does the machine terminate with `Failure`.

```csharp
var graph = GraphBuilder
    .StartWithAsync(CallFlakyService).SetName("Call")
        .Retry(maxAttempts: 3, backoff: 100.Milliseconds(), BackoffKind.Exponential)
        .OnErrorAsync(_ => Cleanup()).SetName("Cleanup")
    .Build();
```

- `.Retry(maxAttempts, backoff, kind)` re-runs the node in place; `BackoffKind` is `Fixed`, `Linear`, or `Exponential`. Backoff delays apply to the async runtime; the sync runtime retries on the next tick.
- `.OnError(...)` / `.OnErrorAsync(...)` set the failure destination. The success chain continues from the original node, so failure handlers branch off without disturbing the happy path; `.OnError(StateToken)` wires an already-built detached chain as the handler.
- Retries fire **before** the failure edge: with the graph above, `Call` runs up to 3 times before `Cleanup` is entered.

### Loops with `Goto`

`.Goto("name")` wires a back-edge to a named node, resolved at `Build()` — unknown or ambiguous names fail the build:

```csharp
int laps = 0;

var loop = GraphBuilder
    .StartWith(() => Result.Success).SetName("Gather")
    .To(() => ++laps < 3 ? Result.Success : Result.Failure).SetName("Craft")
    .Goto("Gather") // Craft's success edge loops back to Gather
    .Build();
```

A `Goto` consumes the node's one success edge and closes the chain, so the loop needs an exit: a node that eventually returns `Failure` (routed by `.OnError` or terminating the run), or a director (`If`/`Switch`) placed inside the loop.

### Named outcomes

Terminal nodes can report *which* outcome ended the run, beyond `Success`/`Failure`:

```csharp
const int Delivered = 1;

var graph = GraphBuilder
    .StartWithAsync(ProcessOrder).SetName("Process")
    .ToAsync(_ => ResultHelpers.Success).SetName("Deliver").WithOutcome(Delivered, "Delivered")
    .Build();

AsyncStateMachine fsm = graph.ToAsyncStateMachine();
await fsm.ExecuteAsync();
Console.WriteLine($"{fsm.LastOutcome}: {fsm.LastOutcomeName}"); // "1: Delivered"
```

`.WithOutcome(code, name)` tags a node; when a run terminates at that node, the machine exposes the code via `LastOutcome` and the registered name via `LastOutcomeName` (0 / `null` when the terminal node has no outcome). Branch graphs give each terminal branch its own code, so callers can tell *how* the flow ended. `LastOutcome` resets at every run start and survives suspend/resume (it is part of the snapshot).

### Naming nodes

Names are optional but strongly recommended for diagnostics, Mermaid export, replay, and observer output.

```csharp
var graph = GraphBuilder
    .StartWithAsync(_ => ResultHelpers.Success).SetName("Initial")
    .ToAsync(_ => ResultHelpers.Success).SetName("Second")
    .Build()
    .SetName("SampleGraph");
```

### Agents / context injection

Use typed state machines when your states need shared mutable context or services.

```csharp
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

public sealed class AppAgent
{
    public int Counter { get; set; }
}

public sealed class WorkState : AsyncState<AppAgent>
{
    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        Agent.Counter++;
        return ResultHelpers.Success;
    }
}

AsyncStateMachine<AppAgent> fsm = GraphBuilder
    .StartWithAsync(new WorkState()).SetName("Work")
    .ToAsyncStateMachine<AppAgent>()
    .WithAgent(new AppAgent());

await fsm.ExecuteAsync();
```

### Blackboards (scoped shared memory)

The agent is *who* is acting (an `Enemy`, a workflow context); a **blackboard** is the graph's *working memory*. They are orthogonal channels — both reach nodes simultaneously. Blackboards are typed-key slot stores with three scopes: **Global** (one user-owned board shared by every machine — world state), **Graph** (one board per machine/entity), and **Node** (transient per-visit scratch). Reads and writes are zero-boxing, zero-allocation array accesses.

```csharp
using NxGraph.Blackboards;

// Schemas are code: declare keys once, share the schema across N boards.
static class WorldKeys
{
    public static readonly BlackboardSchema Schema = new("world", BlackboardScope.Global);
    public static readonly BlackboardKey<bool> AlarmRaised = Schema.Register<bool>("AlarmRaised");
}

static class EnemyKeys
{
    public static readonly BlackboardSchema Schema = new("enemy"); // Graph scope (default)
    public static readonly BlackboardKey<int> TargetDistance = Schema.Register<int>("TargetDistance", 10);
}

sealed class ChaseState : AsyncState<Enemy>
{
    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        // One call site — the key's schema scope picks the board:
        if (Bb.Get(WorldKeys.AlarmRaised))          // routed → shared global board
            Bb.GetRef(EnemyKeys.TargetDistance)--;   // routed → this machine's board
        return ResultHelpers.Success;
    }
}

// Declare schemas on the graph (opt-in bind-time validation), bind boards per machine:
Graph graph = GraphBuilder
    .StartWithAsync(new ChaseState())
    .If(bb => bb.Get(WorldKeys.AlarmRaised))         // blackboard-driven branching
        .ThenAsync((bb, ct) => ResultHelpers.Success)
        .ElseAsync((bb, ct) => ResultHelpers.Success)
    .WithSchema(EnemyKeys.Schema)
    .WithSchema(WorldKeys.Schema)
    .Build();

Blackboard world = new(WorldKeys.Schema);            // one world board for everyone

AsyncStateMachine<Enemy> fsm = graph.ToAsyncStateMachine<Enemy>()
    .WithBlackboard(world)                           // routed by schema scope
    .WithBlackboard(new Blackboard(EnemyKeys.Schema)) // this enemy's own memory
    .WithAgent(enemy);
```

**Node scope (transient per-visit scratch).** A `BlackboardSchema` with `BlackboardScope.Node` declares keys whose values live for one node *visit*: they reset to their registered defaults at every transition boundary (success transition, failure-edge reroute, run start, reset, resume), while in-place retries of the same visit keep the scratch — partial progress across attempts is the point. Node boards are **machine-owned**: declare the schema on the graph with `.WithSchema(...)` and every machine auto-creates its own board (two machines over one shared `Graph` never see each other's scratch); binding one with `WithBlackboard` throws. They are deliberately **not durable** — resuming a `StateMachineSnapshot` restores Node keys to defaults.

```csharp
static class ScratchKeys
{
    public static readonly BlackboardSchema Schema = new("scratch", BlackboardScope.Node);
    public static readonly BlackboardKey<int> BytesSent = Schema.Register<int>("BytesSent");
}

// bb.GetRef(ScratchKeys.BytesSent) accumulates across retries of one visit,
// and is back to 0 when the machine moves to the next node.
Graph graph = GraphBuilder.StartWith(new UploadState()).Retry(3)
    .WithSchema(ScratchKeys.Schema)
    .Build();
```

> **Anti-pattern:** don't use `Dictionary<string, object>` as a context — every access pays string hashing plus boxing. A `BlackboardKey<T>` slot is a schema-checked array read: type-safe, allocation-free, and serializable (see `BlackboardSerializer` in `NxGraph.Serialization` — each board is its own durability artifact alongside the graph payload and machine snapshot; Node boards are transient and never serialize).

Like the state machines, a blackboard is owned by a single runner at a time (not thread-safe).

---

## Execution

### Async execution

```csharp
AsyncStateMachine sm = graph.ToAsyncStateMachine(observer: null);
Result result = await sm.ExecuteAsync();
```

### Sync execution, stepped model

`StateMachine.Execute()` is the stepped entry point. Each call advances the machine by **exactly one node** and returns:

| Return value | Meaning |
|---|---|
| `Result.InProgress` | Node completed; there are more nodes to run. Call `Execute()` again. |
| `Result.Success` | Machine finished successfully, no more nodes. |
| `Result.Failure` | A node failed or threw. Machine is now in `Failed` status. |

**Blocking / non-Unity loop:**

```csharp
StateMachine sm = graph.ToStateMachine();
Result result = Result.InProgress;
while (result == Result.InProgress)
    result = sm.Execute();
```

**Multi-frame nodes:** A node can return `Result.InProgress` from its own `OnRun()` to signal it needs another frame (e.g. a countdown timer or a wait-for-input node). The machine stays on that node and invokes it again on the next `Execute()` call.

### Unity integration

Call `Execute()` from `MonoBehaviour.Update()`. The machine advances one node per frame and the main thread is never blocked:

```csharp
public class FsmRunner : MonoBehaviour
{
    private StateMachine _fsm;

    void Start()
    {
        _fsm = GraphBuilder
            .StartWith(new PatrolState()).SetName("Patrol")
            .To(new AlertState()).SetName("Alert")
            .To(new AttackState()).SetName("Attack")
            .ToStateMachine();
        _fsm.SetRestartPolicy(RestartPolicy.Ignore);
    }

    void Update()
    {
        Result r = _fsm.Execute();
    }
}
```

### Nested machines

Both `StateMachine` and `AsyncStateMachine` implement the node interface directly, so a machine can be passed as a node inside another machine with no wrapper state required.

**Sync, stepped:**

```csharp
StateMachine childFsm = GraphBuilder
    .StartWith(() => Result.Success).SetName("Init")
    .To(new RelayState(
            run: () => Result.Success,
            onExit: () => Console.WriteLine("child done")))
    .ToStateMachine();

StateMachine parentFsm = GraphBuilder
    .StartWith(childFsm).SetName("Child")
    .To(new RelayState(
            run: () => Result.Success,
            onExit: () => Console.WriteLine("parent done")))
    .SetName("Cleanup")
    .ToStateMachine();

// Each Execute() advances exactly one node — even one inside the child.
// 3 ticks: child node 1 → child node 2 (child done) → parent Cleanup
Result r = Result.InProgress;
while (r == Result.InProgress)
    r = parentFsm.Execute();
```

From Unity's `Update()` each call advances exactly one node across the whole hierarchy — no frame blocking.

**Async:**

```csharp
AsyncStateMachine childFsm = GraphBuilder
    .StartWithAsync(_ => ResultHelpers.Success)
    .ToAsync(_ => ResultHelpers.Success)
    .ToAsyncStateMachine();

AsyncStateMachine parentFsm = GraphBuilder
    .StartWithAsync(childFsm)
    .ToAsync(_ => ResultHelpers.Success)
    .ToAsyncStateMachine();

Result result = await parentFsm.ExecuteAsync();
```

Nesting can be arbitrarily deep. Each level is stepped independently; the parent treats a running child as `Result.InProgress` and a completed child as `Result.Success`.

### Composites: subgraphs, history, and parallel regions

#### Subgraphs

`.SubGraph(child)` nests a whole child graph as a single node of the parent — the DSL shorthand for the nested-machine pattern above. With `history: true`, a child that *failed* resumes at its last-active node when the parent re-enters the composite (e.g. after a failure edge and a `Goto` back), instead of restarting from its start node; a child that *completed* restarts from the top:

```csharp
Graph child = GraphBuilder
    .StartWithAsync(_ => ResultHelpers.Success)
    .ToAsync(_ => ResultHelpers.Success)
    .Build();

Graph flow = GraphBuilder
    .StartWithAsync(_ => ResultHelpers.Success).SetName("Prepare")
    .SubGraph(child, history: true).SetName("Work")
    .ToAsync(_ => ResultHelpers.Success).SetName("Finish")
    .Build();
```

The sync twin takes a `ParallelStepMode` (the same enum the parallel composites use): `.SubGraph(mode, child)` nests the child as a sync `StateMachine` node, `.SubGraph(mode, child, history: true)` builds a sync `HistoryState`. `RunToJoin` completes the child within one tick (and is therefore also runnable under the async machine via the sync-logic adapter); `RoundPerTick` advances one child node per parent tick, sync runtime only:

```csharp
Graph flow = GraphBuilder
    .StartWith(() => Result.Success)
    .SubGraph(ParallelStepMode.RoundPerTick, child, history: true)
    .To(() => Result.Success)
    .Build();
```

#### Parallel regions (AND-states)

`.Parallel(regions...)` runs N region graphs via **cooperative interleaving**: each round advances every still-running region by one node, and the composite joins when all regions reach a terminal result — `Success` only if every region succeeded, otherwise `Failure` through the parent's unified fault model (failure edges, retries). This is deliberately not thread-concurrent, which keeps the hot path allocation-free:

```csharp
AsyncStateMachine fsm = GraphBuilder
    .Start()
    .Parallel(tents, fire, scouts) // three region graphs, one node each per round
    .ToAsyncStateMachine();

Result joined = await fsm.ExecuteAsync();
```

The sync twin takes a `ParallelStepMode`: `RunToJoin` completes the whole join inside one `Execute()` call, while `RoundPerTick` advances one round per call and returns `Result.InProgress` in between — so region progress aligns 1:1 with game-loop frames:

```csharp
StateMachine fsm = GraphBuilder
    .Start()
    .Parallel(ParallelStepMode.RoundPerTick, watchtower, gateCrew)
    .ToStateMachine();

// From Update(): each call advances every still-running region by one node.
Result r = fsm.Execute();
```

`RoundPerTick` is sync-only (the async loop rejects node-level `InProgress`); `RunToJoin` composites also run under the async machine via the sync-logic adapter. Validating a graph destined for the async runtime with `new GraphValidationOptions { StrictAsyncCompatible = true }` flags reachable `RoundPerTick` composites (including nested sync machines left in their default per-tick mode) as errors instead of a mid-run surprise.

#### Dynamic (some-of-many) regions

The selector overloads pick which regions run at every composite entry, from the machine-bound [blackboard](#blackboards-scoped-shared-memory):

```csharp
RegionMask SelectDefenses(BlackboardContext bb)
{
    RegionMask mask = RegionMask.Bit(0);                    // archers always
    if (bb.Get(Threat) >= 2) mask |= RegionMask.Bit(1);     // cauldrons
    if (bb.Get(Threat) >= 3) mask |= RegionMask.Bit(2);     // catapults
    return mask;
}

StateMachine fsm = GraphBuilder
    .Start()
    .Parallel(ParallelStepMode.RunToJoin, SelectDefenses, archers, cauldrons, catapults)
    .ToStateMachine()
    .WithBlackboard(board);
```

The selector runs once per composite execution and fixes the selected set for that run; unselected regions are never stepped. An empty mask is a vacuous join (immediate `Success`); mask bits at or above the region count throw; up to 64 regions per composite. Selectors execute on the measured zero-allocation path, so compose masks with `RegionMask.Bit(i) | ...` as above — allocation-free — while `RegionMask.Of(params)` allocates its array and belongs at setup time only. Both variants exist in both runtimes (`.Parallel(selector, ...)` for async, `.Parallel(mode, selector, ...)` for sync). See `NxFSM.Examples/ParallelDemo` for runnable sync and async demos.

### Durable suspend / resume

Both runtimes can pause a run at a step boundary, persist it, and continue later — on the same machine, a fresh machine, another process, or even the **other runtime** (snapshots are interchangeable):

```csharp
using System.Text.Json;

AsyncStateMachine first = graph.ToAsyncStateMachine();
await first.StepAsync();                              // advance one node
StateMachineSnapshot snapshot = first.Suspend();      // primitives-only record

string json = JsonSerializer.Serialize(snapshot);     // any serializer works

// Later — possibly after a process restart, on the sync runtime:
StateMachine second = graph.ToStateMachine();
second.Resume(JsonSerializer.Deserialize<StateMachineSnapshot>(json)!);

Result result = Result.InProgress;
while (result == Result.InProgress)
    result = second.Execute();                        // continues at the next node
```

- `Suspend()` is legal between `StepAsync()`/`Execute()` calls of a stepped run, or on an idle/terminal machine; it captures the current node, status, retry attempts, and `LastOutcome`.
- `Resume(snapshot)` requires a graph that is structurally equivalent (same node indices); re-attach agents/blackboards before continuing.
- A fully durable flow persists three artifacts: the graph payload (`GraphSerializer`), the snapshot, and one `BlackboardSerializer` payload per bound board — see [Serialization](#serialization).
- Node-scoped blackboards are transient by definition and are **not** part of the durable flow: `Resume(snapshot)` restores Node keys to their registered defaults — a node suspended mid-visit loses its scratch.
- Composite-internal progress (positions inside parallel regions or history children) is not part of the flat snapshot; a resumed composite starts its visit fresh.

### Restart policy

Control what happens after the machine reaches a terminal status (`Completed`, `Failed`, or `Cancelled`):

| Policy | Behaviour |
|---|---|
| `RestartPolicy.Auto` *(default)* | Automatically resets to `Ready`, ideal for Unity `Update()` loops |
| `RestartPolicy.Manual` | Stays terminal; re-execution throws until `Reset()` is called explicitly |
| `RestartPolicy.Ignore` | Stays terminal; further `Execute()` calls are no-ops that return the cached result |

```csharp
fsm.SetRestartPolicy(RestartPolicy.Auto);

// Backwards-compatible alias:
fsm.SetAutoReset(true);  // maps to RestartPolicy.Auto
fsm.SetAutoReset(false); // maps to RestartPolicy.Manual
```

Additional notes on execution:

- reentrancy is guarded per machine instance, calling `Execute()` from inside a node throws
- async execution accepts cancellation tokens
- observer exceptions bubble to the caller by default
- graphs are immutable after `Build()` and can be shared across machine instances

---

## Validation

`Build()` already validates the graph. In `DEBUG`, invalid graphs throw immediately.

You can also validate a graph explicitly:

```csharp
using NxGraph.Diagnostics.Validations;

Graph graph = GraphBuilder
    .StartWithAsync(_ => ResultHelpers.Success)
    .ToAsync(_ => ResultHelpers.Success)
    .Build();

GraphValidationResult validation = graph.Validate();
if (validation.HasErrors)
{
    foreach (GraphDiagnostic diagnostic in validation.Diagnostics)
    {
        Console.WriteLine(diagnostic);
    }
}

graph.ValidateAndThrowIfErrorsDebug();
```

Validation checks include:

- broken transitions
- reachability from the start node
- self-loops (configurable)
- terminal path analysis for director-driven graphs

---

## Observability

### Observers

**Async observer:**

```csharp
using NxGraph.Fsm;
using NxGraph.Graphs;

public sealed class ConsoleObserver : IAsyncStateMachineObserver
{
    public ValueTask OnStateMachineStarted(NodeId graphId, CancellationToken ct = default)
    {
        Console.WriteLine($"FSM started: {graphId}");
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
    {
        Console.WriteLine($"Entered: {id.Name}");
        return ValueTask.CompletedTask;
    }

    public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
    {
        Console.WriteLine($"Transition: {from.Name} -> {to.Name}");
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
    {
        Console.WriteLine($"Exited: {id.Name}");
        return ValueTask.CompletedTask;
    }
}
```

**Sync observer** (`IStateMachineObserver`), all callbacks are `void` with default no-op implementations; override only what you need:

```csharp
using NxGraph.Fsm;
using NxGraph.Graphs;

public sealed class DiagnosticObserver : IStateMachineObserver
{
    // Node lifecycle
    public void OnStateEntered(NodeId id) => Console.WriteLine($">> {id.Name}");
    public void OnStateExited(NodeId id)  => Console.WriteLine($"<< {id.Name}");
    public void OnTransition(NodeId from, NodeId to) =>
        Console.WriteLine($"   {from.Name} -> {to.Name}");
    public void OnStateFailed(NodeId id, Exception ex) =>
        Console.WriteLine($"FAIL {id.Name}: {ex.Message}");

    // Machine lifecycle
    public void OnStateMachineStarted(NodeId graphId) =>
        Console.WriteLine($"FSM started: {graphId.Name}");
    public void OnStateMachineCompleted(NodeId graphId, Result result) =>
        Console.WriteLine($"FSM done: {result}");
    public void OnStateMachineReset(NodeId graphId) { }

    // Status changes (e.g. Created → Starting → Running → Completed)
    public void StateMachineStatusChanged(NodeId graphId, ExecutionStatus prev, ExecutionStatus next) { }

    // Log messages emitted by State.Log()
    public void OnLogReport(NodeId nodeId, string message) =>
        Console.WriteLine($"[{nodeId.Name}] {message}");
}
```

### State logging

Custom sync states can emit structured log messages through the observer without taking a direct dependency on a logger:

```csharp
using NxGraph.Fsm;

public sealed class WorkState : State
{
    protected override Result OnRun()
    {
        Log("starting heavy computation");
        // ... do work ...
        Log("computation complete");
        return Result.Success;
    }
}
```

`Log(message)` routes to `IStateMachineObserver.OnLogReport` when an observer is attached, and is a no-op otherwise.

### Tracing

On .NET 8+, `TracingObserver` emits `Activity` spans/tags for state machine and node execution.

```csharp
using NxGraph.Fsm;

IAsyncStateMachineObserver observer = new TracingObserver();
AsyncStateMachine fsm = graph.ToAsyncStateMachine(observer);
await fsm.ExecuteAsync();
```

This integrates naturally with OpenTelemetry pipelines listening to the `ActivitySource` named `"NxGraph"`.

### Replay

Capture a machine run and replay the event stream later:

```csharp
using NxGraph.Diagnostics.Replay;
using NxGraph.Fsm;

ReplayRecorder recorder = new();
AsyncStateMachine fsm = graph.ToAsyncStateMachine(recorder);
await fsm.ExecuteAsync();

StateMachineReplay replay = new(recorder.GetEvents().Span);
replay.ReplayAll(evt =>
{
    Console.WriteLine($"{evt.Type}: {evt.SourceId} -> {evt.TargetId} | {evt.Message}");
});

byte[] bytes = replay.Serialize();
ReplayEvent[] roundTripped = StateMachineReplay.Deserialize(bytes);
```

Replay persistence is its own binary event format; it is separate from graph serialization.

---

## Visualization

Export graphs to Mermaid for docs, PRs, or operations runbooks.

```csharp
using NxGraph.Diagnostics.Export;

string mermaid = GraphBuilder
    .StartWithAsync(_ => ResultHelpers.Success).SetName("Start")
    .ToAsync(_ => ResultHelpers.Success).SetName("Process")
    .ToAsync(_ => ResultHelpers.Success).SetName("End")
    .Build()
    .ToMermaid();

Console.WriteLine(mermaid);
```

---

## Serialization

`NxGraph.Serialization` serializes graphs using an application-provided logic codec.

Text codec example:

```csharp
using System.Text.Json;
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Graphs;
using NxGraph.Serialization;

public sealed class ExampleState : IAsyncLogic
{
    public string Data { get; set; } = string.Empty;

    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
        => ResultHelpers.Success;
}

public sealed class ExampleLogicCodec : ILogicTextCodec
{
    public string Serialize(IAsyncLogic data)
        => JsonSerializer.Serialize((ExampleState)data);

    public IAsyncLogic Deserialize(string payload)
        => JsonSerializer.Deserialize<ExampleState>(payload)
           ?? throw new InvalidOperationException("Failed to deserialize ExampleState.");
}

Graph graph = GraphBuilder
    .StartWithAsync(new ExampleState { Data = "start" }).SetName("Start")
    .ToAsync(new ExampleState { Data = "end" }).SetName("End")
    .Build()
    .SetName("ExampleGraph");

GraphSerializer serializer = new(new ExampleLogicCodec());

await using MemoryStream stream = new();
await serializer.ToJsonAsync(graph, stream);
stream.Position = 0;

Graph roundTripped = await serializer.FromJsonAsync(stream);
```

Notes:

- graph serialization is optional and lives in a separate package
- serializer usage is instance-based
- JSON and MessagePack are both supported through `GraphSerializer`
- your codec controls how node logic is persisted and restored

---

## Examples

The solution includes a runnable examples project with:

- a simple async FSM
- an AI enemy example (typed agent injection)
- Mermaid export example
- a serialization round-trip example
- a sync Dungeon Crawler example using the DSL, observers, director nodes, loops, and named states
- a blackboard demo (scoped shared memory, schema declarations, per-entity boards)
- **parallel-region demos**: the sync Stronghold Siege (`ParallelStepMode.RoundPerTick` frame ticking, `RunToJoin` waves, blackboard-selected dynamic regions) and the async Expedition Camp (cooperative round-robin interleaving under `AsyncStateMachine`, dynamic region selection)

Run it with:

```bash
dotnet run --project NxFSM.Examples
```

---

## Benchmarks

Benchmarks live in `NxGraph.Benchmarks` and use BenchmarkDotNet. The suite covers both `AsyncStateMachine` and `StateMachine` (sync), and also measures equivalent Stateless scenarios for comparison.

Run them with:

```bash
dotnet run --project NxGraph.Benchmarks -c Release
```

### Results

> Runtime: .NET 8.0.26 (RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI) · BenchmarkDotNet v0.13.12 · ShortRun job · 2026-04-27

![Benchmark chart](docs/benchmarks.svg)

**Async `AsyncStateMachine`:**

| Scenario | Mean | Alloc |
|---|---:|---:|
| Single node (`RelayState.Success`) ★ | 199 ns | 0 B |
| Single node + `NoopObserver` | 239 ns | 0 B |
| Timeout wrapper (immediate success) | 330 ns | 0 B |
| Chain × 10 nodes | 802 ns | 0 B |
| Director-driven × 10 nodes | 856 ns | 0 B |
| Chain × 50 nodes | 3,344 ns | 0 B |

**Sync `StateMachine`:**

| Scenario | Mean | Alloc |
|---|---:|---:|
| Single node ★ | 24 ns | 0 B |
| Single node + `SyncNoopObserver` | 27 ns | 0 B |
| Chain × 10 nodes | 194 ns | 0 B |
| Chain × 50 nodes | 979 ns | 0 B |

★ baseline

Key observations:

- **Zero allocations**: both runtimes are fully alloc-free after graph construction.
- **Sync is ~8× faster on a single node**: 24 ns vs 199 ns, reflecting the absence of async machinery and `Interlocked` operations.
- **Observer overhead is constant** and runtime-dependent: +3 ns for sync, +40 ns for async, independent of chain length.
- **Per-node cost falls with chain length**: async 199 ns for 1 node → 80 ns/node for 10 → 67 ns/node for 50.
- **Sync per-node cost is consistent**: ~19 ns/node for both chain × 10 and chain × 50.
- **Director nodes** add ~54 ns over a plain 10-node async chain.

---

## Testing

Run the full test suite:

```bash
dotnet test -c Release
```

The tests cover:

- sync and async execution
- stepped execution (`SteppedExecutionTests`), one-node-per-tick semantics, multi-frame nodes, restart policies
- reentrancy and cancellation
- observers and log reports
- replay
- validation
- Mermaid export
- serialization round-trips

---

## FAQ

**Why is there only one direct success transition per node?**  
Branching is modeled explicitly through directors such as `ChoiceState` and `SwitchState<TKey>`, which keeps execution simple and predictable. A node can additionally carry one failure edge (`.OnError`) for the fault path. When several paths must run at once, use the parallel composites instead of extra edges — see [Fan-out at a glance](#fan-out-at-a-glance); a token runner with free-form fan-out in one flat graph is a recorded, deliberately deferred design.

**Can I share a graph across machines?**  
Yes. `Graph` is immutable after build and can be reused across multiple state machine instances.

**Do observer exceptions get swallowed?**  
No. They bubble by default.

**When should I name nodes?**  
Almost always. Names improve logs, observer output, replay traces, and Mermaid diagrams.

**Does the core package include Mermaid export and replay?**  
Yes. Those features are part of `NxGraph` itself; graph serialization is the optional extra package.

**Can I use NxGraph in Unity?**  
Yes. Use `StateMachine` (the sync runtime) and call `Execute()` from `MonoBehaviour.Update()`. `Execute()` advances exactly one node per call so the main thread is never blocked. Set `RestartPolicy.Auto` for automatic reset between runs, or `RestartPolicy.Ignore` to freeze the machine in its terminal state until you explicitly call `Reset()`. See [Unity integration](#unity-integration) for a full example.

**What does `Result.InProgress` mean?**  
The machine has more nodes to process but is returning control to the caller (e.g. to avoid blocking a frame in Unity). Call `Execute()` again on the next frame. A node can also return `Result.InProgress` from its own `OnRun()` to signal it needs multiple frames (e.g. a frame-based timer).

---

## Roadmap

- sync twins for the remaining async-only constructs (history subgraphs, waits, timeouts)
- hierarchical snapshots so composite-internal progress survives durable suspend/resume
- validator and Mermaid-export awareness of composite interiors
- continued ergonomics improvements around DSL authoring and serialization

---

## Contributing

PRs are welcome. Please run formatting and tests before submitting:

```bash
dotnet test
```

---

## License

MIT. See [LICENSE](LICENSE) for details.
