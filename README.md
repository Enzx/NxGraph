# NxGraph – A High-Performance Finite State Machine for .NET 8+

NxGraph is a **zero-allocation runtime, high-performance finite state machine (FSM) framework** for .NET 8+, designed for scenarios where execution speed, memory efficiency, and runtime safety are critical.

It’s built around:

* **Array-backed graph representation** for cache-friendly lookups
* **Single-edge transition model** with explicit director nodes for branching
* **Allocation-free hot paths** (`ValueTask`-based)
* **Extensible lifecycle hooks** via `IAsyncStateObserver`
* **Async-first design** to handle both CPU-bound and IO-bound workflows
* **Domain-Specific Language (DSL) & Fluent API** for expressive graph building

---

## Why NxGraph?

Many FSM frameworks are flexible but trade away performance for convenience. In production workloads, especially in **games**, **simulation engines**, **workflow orchestration**, and **real-time systems**, GC pressure and runtime overhead can be deal-breakers.

NxGraph is designed with **predictable performance** in mind:

* **0 runtime allocations** in the steady state
* **Fast transitions** (direct array indexing, no dictionary lookups)
* **Separation of control and branching logic** for clearer reasoning
* **Timeouts, cancellation, and observation** without breaking perf
* Native support for **NativeAOT** publishing

---

## Core Concepts

### Graph

The FSM is a directed graph of nodes (`INode`) connected by single outgoing edges. Multiple edges are modeled using *director* nodes (e.g., `ChoiceState`, `SwitchState`), which decide the next node at runtime.

### Node

A unit of execution:

* Implements `INode.ExecuteAsync(CancellationToken)`
* Returns `Result.Success` to move to the next node
* Returns `Result.Failure` to stop with failure

### Director Nodes

Special nodes that decide *which* next node to run:

* `ChoiceState` — binary branching based on a predicate
* `SwitchState` — multi-way branching based on a selector

### StateMachine

Executes a graph, one node at a time, until a terminal node is reached or cancellation/timeout occurs.

---

## DSL & Fluent API

NxGraph offers a fluent DSL for building graphs without manually wiring transitions. This API focuses on readability and discoverability:

```csharp

//inline states definition with delegates
//also, you can pass the logging observer described later
//ConsoleStateObserver observer = new();
StateMachine fsm = GraphBuilder
    .StartWith(_ =>
    {
        Console.WriteLine("Initializing workflow...");
        return ResultHelpers.Success;
    }).SetName("Initial")
    .To(_ =>
    {
        Console.WriteLine("Running first step...");
        return ResultHelpers.Success;
    }).SetName("Second")
    .To(_ =>
    {
        Console.WriteLine("Running second step...");
        return ResultHelpers.Success;
    }).SetName("End")
    .ToStateMachine();
// .ToStateMachine(observer);

Result result = await fsm.ExecuteAsync();
```
Or you can inherit from `State` or `State<TAgent>` and implement explicit states:
```csharp
IdleState idleState = new();
AttackState attackState = new();
PatrolState patrolState = new();
StateMachine = GraphBuilder
                .StartWith(idleState)
                .If(() => Target.IsTargetInRange)
                .Then(attackState).WaitFor(1.Seconds()).To(idleState)
                .Else(patrolState)
                .ToStateMachine<AiEnemy>()
                .WithAgent(this);
```

**DSL benefits:**

* **Readable:** Matches the logical flow of your process.
* **Type-safe:** Prevents invalid transitions at compile-time.
* **Compositional:** You can create subgraphs and reuse them.

Even though the DSL is optional, it can greatly reduce boilerplate for complex graphs.


## Observers

Implement `IAsyncStateObserver` to listen for state enter/exit events:

```csharp
 public class ConsoleStateObserver : IAsyncStateObserver
 {
     public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
     {
         Console.WriteLine($"{id.Name}::Enter");
         return ValueTask.CompletedTask;
     }
     public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
     {
         Console.WriteLine($"{id.Name}::Exit");
         return ValueTask.CompletedTask;
     }
     public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
     {
         Console.WriteLine($"{from.Name}->{to.Name}");
         return ValueTask.CompletedTask;
     }
 }
```

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

## Design Trade-offs

* **Single outgoing edge per node** — simplifies graph traversal and speeds up lookups.
* **Explicit node naming** — recommended for debugging/observability.
* **Async-first** — ensures a consistent execution model.

---

## Installation
Coming soon!!!
```bash
dotnet add package NxGraph
```

---

## Roadmap

* [ ] Expand DSL & fluent API features
* [ ] Source generator for enum-based graphs
* [ ] Graph validation & analyzers
* [ ] Deterministic replay for debugging
* [ ] Virtual-time testing
* [ ] Graph visualizer with real-time view
* [x] Persistence: Binary and Text Serialization
* [ ] Visual Graph Editor?
* [ ] Unity Support?
* [ ] Synchronous API?

---

## License

[MIT](/LICENSE)
