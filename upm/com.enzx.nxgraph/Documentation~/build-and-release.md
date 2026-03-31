# Build and release

## Source-based staging

From the repository root:

```powershell
.\scripts\build-upm.ps1 -Mode Source
```

This stages the runtime source into `upm/com.enzx.nxgraph/Runtime/NxGraph`.

The source staging script currently copies:

- `Authoring`
- `Compatibility`
- `Diagnostics/Export`
- `Diagnostics/Replay`
- `Diagnostics/Validations`
- `Fsm`
- `Graphs`
- `Result.cs`
- `ResultHelpers.cs`

It also excludes `Fsm/TracingObserver.cs` from the staged Unity runtime.

## Binary fallback

If a binary package is needed instead:

```powershell
.\scripts\build-upm.ps1 -Mode Binary
```

This stages `NxGraph.dll` into `Runtime/Plugins`.

For backward compatibility, `scripts/build-upm-binary.ps1` still delegates to binary mode.

## Important

Do not keep both staged source and `NxGraph.dll` in the same package layout, or Unity may see duplicate types.

If a target Unity version cannot compile the staged runtime source as-is, prefer the binary fallback package for that environment.

