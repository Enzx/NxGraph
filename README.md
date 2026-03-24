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
  - [Waits and timeouts](#waits-and-timeouts)
  - [Naming nodes](#naming-nodes)
  - [Agents / context injection](#agents--context-injection)
- [Execution](#execution)
- [Validation](#validation)
- [Observability](#observability)
  - [Observers](#observers)
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

- **Simple runtime model**: graphs are backed by dense node/transition arrays and each node has at most one direct outgoing edge.
- **Predictable branching**: fan-out happens through director nodes such as `ChoiceState` and `SwitchState<TKey>`.
- **Authoring ergonomics**: build flows with `StartWith`, `.To(...)`, `.If(...)`, `.Switch(...)`, `.WaitFor(...)`, and `.ToWithTimeout(...)`.
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

static ValueTask<Result> Acquire(CancellationToken _) => ResultHelpers.Success;
static ValueTask<Result> Process(CancellationToken _) => ResultHelpers.Success;
static ValueTask<Result> Release(CancellationToken _) => ResultHelpers.Success;

AsyncStateMachine fsm = GraphBuilder
    .StartWith(Acquire).SetName("Acquire")
    .To(Process).SetName("Process")
    .To(Release).SetName("Release")
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

Result result = fsm.Execute();
```

---

## Authoring DSL

### Linear flows

```csharp
var graph = GraphBuilder
    .StartWith(_ => ResultHelpers.Success).SetName("Start")
    .To(_ => ResultHelpers.Success).SetName("Step1")
    .To(_ => ResultHelpers.Success).SetName("Step2")
    .Build();
```

### Branching with `If`

```csharp
bool IsPremium() => true;

var graph = GraphBuilder
    .StartWith(_ => ResultHelpers.Success).SetName("Entry")
    .If(IsPremium)
        .Then(_ => ResultHelpers.Success).SetName("Premium")
        .Else(_ => ResultHelpers.Success).SetName("Standard")
    .Build();
```

### Branching with `Switch`

```csharp
int RouteKey() => 2;

var graph = GraphBuilder
    .StartWith(_ => ResultHelpers.Success).SetName("Entry")
    .Switch(RouteKey)
        .Case(1, _ => ResultHelpers.Success)
        .Case(2, _ => ResultHelpers.Success)
        .Default(_ => ResultHelpers.Failure)
    .End().SetName("Router")
    .Build();
```

### Waits and timeouts

```csharp
var delayed = GraphBuilder
    .StartWith(_ => ResultHelpers.Success).SetName("Start")
    .WaitFor(250.Milliseconds()).SetName("Cooldown")
    .To(_ => ResultHelpers.Success).SetName("Finish")
    .Build();

var timed = GraphBuilder
    .StartWith(_ => ResultHelpers.Success).SetName("Start")
    .ToWithTimeout(2.Seconds(), _ => ResultHelpers.Success, TimeoutBehavior.Fail)
        .SetName("TimedWork")
    .To(_ => ResultHelpers.Success).SetName("AfterTimeout")
    .Build();
```

### Naming nodes

Names are optional but strongly recommended for diagnostics, Mermaid export, replay, and observer output.

```csharp
var graph = GraphBuilder
    .StartWith(_ => ResultHelpers.Success).SetName("Initial")
    .To(_ => ResultHelpers.Success).SetName("Second")
    .Build()
    .SetName("SampleGraph");
```

### Agents / context injection

Use typed state machines when your states need shared mutable context or services.

```csharp
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;

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
    .StartWith(new WorkState()).SetName("Work")
    .ToAsyncStateMachine<AppAgent>()
    .WithAgent(new AppAgent());

await fsm.ExecuteAsync();
```

---

## Execution

For async flows:

```csharp
AsyncStateMachine sm = graph.ToAsyncStateMachine(observer: null);
Result result = await sm.ExecuteAsync();
```

For sync flows:

```csharp
StateMachine sm = graph.ToStateMachine(observer: null);
Result result = sm.Execute();
```

Notes:

- execution is reentrancy-guarded per machine instance
- async execution accepts cancellation tokens
- observer exceptions bubble by default
- graphs are immutable after build and can be shared across machine instances

---

## Validation

`Build()` already validates the graph. In `DEBUG`, invalid graphs throw immediately.

You can also validate a graph explicitly:

```csharp
using NxGraph.Diagnostics.Validations;

Graph graph = GraphBuilder
    .StartWith(_ => ResultHelpers.Success)
    .To(_ => ResultHelpers.Success)
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

Async observer example:

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

Synchronous flows use `IStateMachineObserver` with the same event names but `void` return types.

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
    .StartWith(_ => ResultHelpers.Success).SetName("Start")
    .To(_ => ResultHelpers.Success).SetName("Process")
    .To(_ => ResultHelpers.Success).SetName("End")
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
    .StartWith(new ExampleState { Data = "start" }).SetName("Start")
    .To(new ExampleState { Data = "end" }).SetName("End")
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
- an AI enemy example
- Mermaid export example
- a sync Dungeon Crawler example using the DSL, observers, director nodes, loops, and named states

Run it with:

```bash
dotnet run --project NxFSM.Examples
```

---

## Benchmarks

Benchmarks live in `NxGraph.Benchmarks` and use BenchmarkDotNet.

Run them with:

```bash
dotnet run --project NxGraph.Benchmarks -c Release
```

The repository benchmark suite currently compares scenarios such as:

- single-node execution
- chains of 10 and 50 nodes
- timeout wrappers
- observer overhead
- director-driven flows

---

## Testing

Run the full test suite:

```bash
dotnet test -c Release
```

The tests cover:

- sync and async execution
- reentrancy and cancellation
- observers
- replay
- validation
- Mermaid export
- serialization round-trips

---

## FAQ

**Why is there only one direct outgoing transition per node?**  
Branching is modeled explicitly through directors such as `ChoiceState` and `SwitchState<TKey>`, which keeps execution simple and predictable.

**Can I share a graph across machines?**  
Yes. `Graph` is immutable after build and can be reused across multiple state machine instances.

**Do observer exceptions get swallowed?**  
No. They bubble by default.

**When should I name nodes?**  
Almost always. Names improve logs, observer output, replay traces, and Mermaid diagrams.

**Does the core package include Mermaid export and replay?**  
Yes. Those features are part of `NxGraph` itself; graph serialization is the optional extra package.

---

## Roadmap

- richer package docs and example coverage
- additional validation/reporting improvements
- more visualization tooling
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
