# NxGraph Serialization

Optional serialization for NxGraph in Unity. See the package `README.md` for installation requirements and the duplicate-assembly caveat that comes with bundled dependencies.

## What a durable flow consists of

Three kinds of artifact, serialized independently:

| Artifact | Produced by | Notes |
| --- | --- | --- |
| Graph payload | `GraphSerializer` | Structure: nodes, transitions, composites, fork/join, event entries, behaviors. |
| Machine snapshot | `StateMachine.Suspend()` / `SuspendDeep()` | Plain records (`StateMachineSnapshot`, `StateMachineDeepSnapshot`). Serialize with any serializer. |
| Blackboard payload | `BlackboardSerializer` | One per bound board. Node-scoped boards are transient and never serialize. |

Machine-level configuration (step mode, restart policy) is not structure and does not ride the payload.

## Node logic

Node logic is serialized through pluggable `ILogicCodec` implementations, which live in `NxGraph.Serialization.Abstraction` — shipped in the **core** package, so a codec assembly can reference the abstraction without depending on this package.

Delegate-carrying relay nodes (`.To(bb => ...)`, port relays, `Relay*` branch states) are not serializable by construction; a codec decides how the graphs you author are represented on the wire.

## Payload version

The wire format is versioned (`SerializationVersion`). Older payloads stay readable; a payload from a newer version than the running assembly is rejected. Because this package is pinned to an exact core version, the two always agree.

## Build and release

This package is staged by the repository's C# build system and only in binary mode:

```bash
dotnet run --project NxGraph.Build -- stage-binary
```

Staging copies `NxGraph.Serialization.dll` and its dependency closure out of `NxGraph.Serialization/bin/Release/netstandard2.1/` into `Runtime/Plugins/`. The set of files allowed there is defined by the committed `.meta` sidecars: if a dependency appears or disappears, staging fails until the sidecars are updated, so nothing is silently bundled into or dropped from a shipped package.

See the core package's `Documentation~/build-and-release.md` for the full pipeline.
