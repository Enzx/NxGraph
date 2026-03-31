# NxGraph for Unity

NxGraph is a lean finite state machine / stateflow library with:

- a fluent authoring DSL
- sync and async runtimes
- graph validation
- observers, replay, and Mermaid export

## Install

### Local package

Add this package to your Unity project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.enzx.nxgraph": "file:../NxGraph/upm/com.enzx.nxgraph"
  }
}
```

### Git package

Once the package folder is committed, it can also be consumed through Git:

```json
{
  "dependencies": {
    "com.enzx.nxgraph": "https://github.com/Enzx/NxGraph.git?path=/upm/com.enzx.nxgraph"
  }
}
```

## Source-first package layout

This package is designed to be staged from source so consumers can navigate the implementation directly inside Unity projects.

Runtime source is staged into `Runtime/NxGraph` by `scripts/build-upm.ps1`.

## Quick start

```csharp
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;

StateMachine fsm = GraphBuilder
    .StartWith(() => Result.Success).SetName("Start")
    .To(() => Result.Success).SetName("Finish")
    .ToStateMachine();

Result result = fsm.Execute();
```

## Notes

- Use `scripts/build-upm.ps1 -Mode Source` for source staging.
- Use `scripts/build-upm.ps1 -Mode Binary` for binary staging.
- `scripts/build-upm-binary.ps1` remains available as a backward-compatible shortcut for binary staging.
- Do not stage both source and runtime DLLs at the same time.
- Source staging mirrors the current runtime source exactly, so actual Unity editor compatibility depends on the C# features used by the staged files.

