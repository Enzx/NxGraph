# NxGraph for Unity

NxGraph is a lean finite state machine / stateflow library with:

- a fluent authoring DSL
- sync and async runtimes
- failure edges, retries, and timeouts (unified fault model)
- composites: subgraphs, history, parallel regions (static and dynamic selection)
- a token runtime with fork/join for many-active-tokens flows (all / any / quorum merges)
- durable suspend/resume via snapshots, including deep suspend of composite trees
- scoped blackboards and typed step I/O ports
- in-node wall-clock concurrency (`.ToAllAsync(...)`)
- graph validation, observers, replay, and Mermaid export

## Install

### Git package (recommended)

Releases are published to the `upm` branch. Add to your Unity project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.enzx.nxgraph": "https://github.com/Enzx/NxGraph.git#upm"
  }
}
```

Or pin a specific release tag:

```json
{
  "dependencies": {
    "com.enzx.nxgraph": "https://github.com/Enzx/NxGraph.git#upm/v2.1.0-alpha"
  }
}
```

Do **not** consume the `upm/com.enzx.nxgraph` folder from the `main` branch — its staged artifacts are not refreshed on every commit and may lag the released version.

### Tarball

Each GitHub release under the `upm/v*` tags carries a `.tgz` asset. Install via Unity Package Manager → "Add package from tarball".

### Local package

For local development against a checkout:

```json
{
  "dependencies": {
    "com.enzx.nxgraph": "file:../NxGraph/upm/com.enzx.nxgraph"
  }
}
```

Stage fresh artifacts into the folder first (see Staging below).

## Staging

Staging is driven by the C# build system (`NxGraph.Build`), run from the repo root:

- Source mode (runtime source staged into `Runtime/NxGraph`): `dotnet run --project NxGraph.Build -- stage-source`
- Binary mode (netstandard2.1 DLL staged into `Runtime/Plugins`): `dotnet run --project NxGraph.Build -- stage-binary`

Do not stage both source and runtime DLLs at the same time. Source staging mirrors the current runtime source exactly, so actual Unity editor compatibility depends on the C# features used by the staged files.

## Quick start

```csharp
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;

StateMachine fsm = GraphBuilder
    .StartWith(() => Result.Success).SetName("Start")
    .To(() => Result.Success).SetName("Finish")
    .ToStateMachine();

// Execute() is stepped: it advances one node per call and returns
// Result.InProgress until the machine reaches a terminal result.
Result result;
do
{
    result = fsm.Execute();
} while (result == Result.InProgress);
```
