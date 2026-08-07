# NxGraph Serialization

Optional serialization for [NxGraph](https://github.com/Enzx/NxGraph) in Unity: durable graph payloads, state-machine snapshots, and blackboard payloads, in JSON or MessagePack.

This package is separate from `com.enzx.nxgraph` because it is the only part of the library with third-party dependencies. The core package has none and stays that way.

## Requirements

- Unity 2021.3 or newer, with **API Compatibility Level set to .NET Standard 2.1** (Project Settings → Player → Other Settings).
- `com.enzx.nxgraph` at the same version. The dependency is pinned exactly; the two packages are built and released together.

## What ships here

`Runtime/Plugins/` carries `NxGraph.Serialization.dll` (netstandard2.1) together with its full dependency closure — MessagePack and System.Text.Json plus the BCL facades those need on netstandard2.1. Nothing else has to be installed.

Bundling has a cost worth knowing about before you install: several of those facades (`System.Memory`, `System.Buffers`, `System.Runtime.CompilerServices.Unsafe`, `System.Text.Encodings.Web`, …) are commonly shipped by *other* Unity packages too. If your project already gets them from somewhere else — NuGetForUnity, another asset, a different SDK — Unity will report duplicate assemblies, and you must keep exactly one copy. Deleting the duplicate from this package's `Runtime/Plugins` folder is a valid fix as long as the surviving version is compatible.

`NxGraph.Serialization.Abstraction.dll` is **not** here. It has no dependencies of its own, so it ships in the core package where custom codecs can reference it without pulling any of this in.

## Usage

```csharp
using NxGraph.Serialization;

GraphSerializer serializer = new(new MyLogicCodec());

// JSON
await serializer.ToJsonAsync(graph, stream);
Graph restored = await serializer.FromJsonAsync(stream);

// MessagePack
await serializer.ToMessagePackAsync(graph, stream);
```

A durable flow is more than one artifact: the graph payload, the machine snapshot (`StateMachineSnapshot` or `StateMachineDeepSnapshot` — plain records, serialize them with anything), and one `BlackboardSerializer` payload per bound board. Node-scoped boards are transient and never serialize.

See the core package's documentation for the full model.

## Source mode is not supported here

The core package can be staged in *source* mode, where its C# compiles inside Unity as the `NxGraph.Unity.Runtime` assembly. A prebuilt `NxGraph.Serialization.dll` references the assembly named `NxGraph` and cannot bind to that, so this package requires the core package in **binary** mode. The release pipeline enforces it: source-mode releases do not publish this package at all.
