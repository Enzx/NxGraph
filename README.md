[![NuGet NxGraph](https://img.shields.io/nuget/v/NxGraph.svg?label=NuGet%20NxGraph)](https://www.nuget.org/packages/NxGraph/)
[![NuGet NxGraph.Serialization](https://img.shields.io/nuget/v/NxGraph.Serialization.svg?label=Serialization)](https://www.nuget.org/packages/NxGraph.Serialization/)
[![NuGet NxGraph.Serialization.Abstraction](https://img.shields.io/nuget/v/NxGraph.Serialization.Abstraction.svg?label=Abstraction)](https://www.nuget.org/packages/NxGraph.Serialization.Abstraction/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
[![Build](https://github.com/Enzx/NxGraph/actions/workflows/dotnet.yml/badge.svg?branch=main)](https://github.com/Enzx/NxGraph/actions/workflows/dotnet.yml)
[![Publish](https://github.com/Enzx/NxGraph/actions/workflows/publish-nuget.yml/badge.svg)](https://github.com/Enzx/NxGraph/actions/workflows/publish-nuget.yml)

# NxGraph

A lean, high‑performance finite state machine (FSM) / stateflow library for .NET with a clean authoring DSL, strong validation, first‑class observability, and export tools (Mermaid, tracing, replay). Designed for correctness on hot paths (allocation‑free), production diagnostics, and pleasant authoring.

> **Status**: production‑ready core; APIs around serialization/visualization may evolve.

---

## Table of contents

- [Why NxGraph](#why-nxgraph)
- [Features](#features)
- [Benchmarks](#benchmarks)
- [Install](#install)
- [Quick start](#quick-start)
- [Core concepts](#core-concepts)
- [Authoring DSL](#authoring-dsl)
  - [Linear flows](#linear-flows)
  - [Branching with directors](#branching-with-directors)
  - [Delays & timeouts](#delays--timeouts)
  - [Agents (dependency injection)](#agents-dependency-injection)
- [Execution](#execution)
- [Validation](#validation)
- [Observability](#observability)
  - [State machine observers](#state-machine-observers)
  - [Tracing (OpenTelemetry-friendly)](#tracing-opentelemetryfriendly)
  - [Replay](#replay)
- [Visualization](#visualization)
  - [Mermaid export](#mermaid-export)
  - [Realtime/offline visualizer (C# only)](#realtimeoffline-visualizer-c-only)
- [Serialization](#serialization)
- [Performance notes](#performance-notes)
- [Testing](#testing)
- [FAQ](#faq)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License](#license)

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

## Benchmarks

<img width="1234" height="582" alt="image" src="https://github.com/user-attachments/assets/34e90c0c-753e-4cde-91b3-5c36e06282b1" />

### Execution Time (ms)


| Scenario           | NxFSM  | Stateless |
| ------------------ | ------ | --------- |
| Chain10            | 0.4293 | 47.06     |
| Chain50            | 1.6384 | 142.75    |
| DirectorLinear10   | 0.4372 | 42.76     |
| SingleNode         | 0.1182 | 14.53     |
| WithObserver       | 0.1206 | 42.96     |
| WithTimeoutWrapper | 0.2952 | 14.23     |

### Memory Allocation (KB)

| Scenario           | NxFSM | Stateless |
| ------------------ | ----- | --------- |
| Chain10            | 0     | 15.07     |
| Chain50            | 0     | 73.51     |
| DirectorLinear10   | 0     | 15.07     |
| SingleNode         | 0     | 1.85      |
| WithObserver       | 0     | 15.42     |
| WithTimeoutWrapper | 0     | 1.85      |

---

## Install
```bash
dotnet add package NxGraph
```

>Additionally, you can clone the repository and reference projects directly, or build a local package.


```bash
# build
dotnet build -c Release

# (optional) create local package
dotnet pack -c Release
```

Add a project reference to `NxGraph` (and `NxGraph.Exporters` / `NxGraph.Serialization` if needed).

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
var sm = graph.ToStateMachine();
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

```csharp
var graph = GraphBuilder
    .StartWith(Start)
    .WaitFor(250.Milliseconds())
    .To(End)
    .Build();

// Timeout wrapper for a long-running state
var timeoutGraph = GraphBuilder
    .StartWith(Start).ToWithTimeout(500.Milliseconds(), _=> ResultHelpers.Failure)
    .To(Release)
    .Build();
```

> `Timeout` cancels the wrapped logic if it exceeds the specified duration and routes to the next node. Prefer passing a linked `CancellationToken` inside your logic for graceful stops.

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
var sm = g.ToStateMachine<AppAgent>();
sm.SetAgent(new AppAgent { Log = logger });
```

---

## Execution

```csharp
var sm = graph.ToStateMachine(observer: myObserver);
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

var sm = graph.ToStateMachine(observer: new ConsoleObserver());
await sm.ExecuteAsync(CancellationToken.None);
```

> Observer exceptions bubble by default; wrap if you want best‑effort.

### Tracing (OpenTelemetry‑friendly)

A built‑in tracing observer maps machine/node lifecycles to `Activity` spans/events so you can export to Jaeger/Tempo/Zipkin.

```csharp
using var observer = new TracingObserver(activitySource);
var sm = graph.ToStateMachine(observer);
await sm.ExecuteAsync(ct);
```

### Replay

Record execution for offline visualization or debugging and play it back deterministically.

```csharp
var recorder = new ReplayRecorder();
var sm = graph.ToStateMachine(observer: recorder);
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

### Realtime/offline visualizer (C# only)

Coming Soon!
---

## Serialization

Serialize graphs (and replays) to JSON or MessagePack.

```csharp

//Serialize
 GraphSerializer.SetLogicCodec(new ExampleLogicSerializer());
ExampleState start = new() { Data = "start" };
ExampleState end = new() { Data = "end" };
Graph graph = GraphBuilder.StartWith(start).SetName("Start").To(end).SetName("End").Build().SetName("FSM");
MemoryStream stream = new();
await GraphSerializer.ToJsonAsync(graph, stream);

//Deserialize
Graph deserializedGraph = await GraphSerializer.FromJsonAsync(stream);
StateMachine fsm = deserializedGraph.ToStateMachine();
await fsm.ExecuteAsync();
```

> **Note**: Current serializer uses a configurable logic codec. Future versions may expose an instance‑based `GraphSerializer` to avoid global state and improve concurrency.

---

## Performance notes

- Keep logic static (avoid captures) for zero‑alloc authoring:
  ```csharp
  static ValueTask<Result> Ok(CancellationToken _) => ResultHelpers.Success;
  ```

---

## Testing

- Unit tests cover reentrancy, cancellation, observers, validation, exporters, replay.
- Add property tests (FsCheck) if you extend the DSL, especially around director cycles and exporter escaping.

Run:

```bash
dotnet test -c Release
```

---

## QA

**Q: Why a single outgoing edge per node?**  
A: It makes the graph compact and the runtime simple. Branching happens in directors, not by fanning out edges.

**Q: Can I run multiple machines on the same graph?**  
A: Yes. Graphs are immutable and thread-safe for sharing.

**Q: Do observer exceptions crash execution?**  
A: They bubble by default. Wrap if you need best‑effort telemetry.

**Q: How do I draw my graph in Docs?**  
A: Use the Mermaid exporter to produce `.mmd` files that GitHub/GitLab render natively.

---

## Roadmap

- Instance‑based serializers (no global codec)
- Additional validators (cycle analysis across directors)
- Realtime Visualizer
- NuGet packaging & SourceLink

---

## Contributing

PRs welcome. Please run `dotnet format` and ensure tests pass. For non‑trivial changes, open an issue first.

---

## License

MIT (see `LICENSE`).


