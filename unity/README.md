# Unity development project

`NxGraphDev` is a harness for developing the NxGraph graph editor. It is **not** shipped and **not** a sample: it exists so the editor code — which lives in the UPM package, not here — can be run, debugged and screenshotted against a real Unity install.

- Unity **6000.4.8f1**, API Compatibility Level **.NET Standard 2.1**.
- Both packages are referenced by relative `file:` path in `Packages/manifest.json`, so Unity uses the working tree directly. Edits to the package are live; there is no copy to keep in sync.

## Opening it

Unity Hub → **Add** → **Add project from disk** → pick `NxGraph_Code/unity/NxGraphDev`. Hub matches the version from `ProjectSettings/ProjectVersion.txt` (6000.4.8f1); if you later open it with a different Unity, expect an upgrade prompt.

Do the staging step below **first**, or Unity opens to a package with no code in it.

## Before opening it

The packages ship prebuilt assemblies, and those are gitignored — a fresh clone has `.meta` files in `Runtime/Plugins` and nothing beside them. Stage them first:

```bash
dotnet run --project NxGraph.Build -- stage-binary
```

Without this Unity opens to a package with no code in it. Re-run it after any change to the core or serialization libraries; Unity picks the new assemblies up on focus.

Use `stage-binary`, not `stage-source`. Source mode compiles the core inside Unity as the `NxGraph.Unity.Runtime` assembly, which the prebuilt `NxGraph.Serialization.dll` cannot bind to.

## Writing code in it

This is an ordinary Unity project — nothing about NxGraph changes the workflow.

The packages ship their assemblies as auto-referenced plugins, so a plain script in `Assets/` with no assembly definition can `using NxGraph.Authoring;` and build a machine. `Assets/Scratch/RuntimeUsageProbe.cs` is exactly that: a `MonoBehaviour`, no asmdef, compiled into `Assembly-CSharp`. If you add your own asmdef, leave `overrideReferences` off (the default) and the plugins stay visible.

`com.unity.ide.rider` is installed, so Unity generates the C# projects and `NxGraphDev.sln`. Four projects show up: `Assembly-CSharp`, `Assembly-CSharp-Editor`, `NxGraph.Unity.Runtime`, and `NxGraph.Unity.Editor` — the last one is the package's editor code, and because the packages are referenced by `file:` path rather than copied, its sources point straight at the working tree. **Editing the graph editor from the IDE edits the package**; save, focus Unity, and it recompiles. There is no copy to keep in sync and no separate build step.

The one thing that is *not* automatic: changes to the C# libraries under `NxGraph_Code/` (`NxGraph`, `NxGraph.Serialization`) are compiled by `dotnet`, not Unity. Re-run `stage-binary` after those, then focus Unity.

## Where the code lives

| What | Where |
| --- | --- |
| Editor code | `upm/com.enzx.nxgraph/Editor/` |
| Scratch scenes and test assets | `unity/NxGraphDev/Assets/Scratch/` |

Editor code lives in the package on purpose: it ships to consumers with the package, the `Editor` platform constraint in its asmdef keeps it out of player builds, and developing it in place means there is never a migration from "project code" to "package code".

`Assets/Scratch/` is for throwaway work. Nothing there is referenced by the package.

## Using it

`Window → NxGraph → Graph Editor`, or double-click an `NxGraphAsset` (`Assets → Create → NxGraph → Graph`).

Right-click the canvas to add a step; drag from `success` or `failure` to wire it; right-click a node to make it the start. Every structural edit recompiles the asset into a real `Graph` and runs `graph.Validate()` — the panel at the bottom shows the library's own verdict, not a second opinion.

## What this is not, yet

The asset model covers plain steps and the two transition channels. Branch nodes, composites, fork/join, behaviors, retry policies and blackboard schemas are all authorable through the C# DSL and are **not** in the editor's model yet. `Copy Mermaid` runs the library's exporter over the compiled graph, so it renders more than the canvas draws.
