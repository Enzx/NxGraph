# Build and release

Staging is driven by the C# build system (`NxGraph.Build`, Bullseye targets). Run all commands from the repository root (`NxGraph_Code`).

## Source-based staging

```bash
dotnet run --project NxGraph.Build -- stage-source
```

This stages the runtime source into `upm/com.enzx.nxgraph/Runtime/NxGraph`.

Source staging copies:

- `Authoring`
- `Blackboards`
- `Compatibility`
- `Diagnostics/Export`
- `Diagnostics/Replay`
- `Diagnostics/Validations`
- `Fsm`
- `Graphs`
- `Shims`
- `Tokens`
- `Result.cs`
- `ResultHelpers.cs`

It excludes `Fsm/TracingObserver.cs` from the staged Unity runtime (it is `NET8_0_OR_GREATER` only).

## Binary staging

If a binary package is needed instead:

```bash
dotnet run --project NxGraph.Build -- stage-binary
```

This builds Release and stages the netstandard2.1 `NxGraph.dll` (plus PDB and XML docs) into `Runtime/Plugins`.

## Release

The `upm-release.yml` workflow (triggered by `upm/v*` tags or manually) runs `ci`, stages the chosen mode, patches `package.json` via `upm-patch-version`, creates the tarball via `upm-tarball`, pushes the package layout to the `upm` branch, and attaches the tarball to a GitHub release.

## Important

Do not keep both staged source and `NxGraph.dll` in the same package layout, or Unity may see duplicate types.

If a target Unity version cannot compile the staged runtime source as-is, prefer the binary package for that environment.
