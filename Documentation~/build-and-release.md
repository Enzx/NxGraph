# Build and release

Staging is driven by the C# build system (`NxGraph.Build`, Bullseye targets). Run all commands from the repository root (`NxGraph_Code`).

The repository produces **two** UPM packages:

| Package | Contents | Staging modes |
| --- | --- | --- |
| `com.enzx.nxgraph` | The core library, plus the dependency-free `NxGraph.Serialization.Abstraction`. | source or binary |
| `com.enzx.nxgraph.serialization` | `NxGraph.Serialization` and its bundled third-party dependencies. | binary only |

They share a version and are released together; the serialization package pins its dependency on the core to that exact version.

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

Source mode does not stage the serialization package, and clears it if it was staged before. Source-compiled core code becomes the Unity assembly `NxGraph.Unity.Runtime`, while a prebuilt `NxGraph.Serialization.dll` carries an assembly reference to `NxGraph` — the two cannot bind, so the combination is refused rather than shipped broken.

## Binary staging

If a binary package is needed instead:

```bash
dotnet run --project NxGraph.Build -- stage-binary
```

This builds `NxGraph.Serialization` for netstandard2.1 — which also builds the core and the abstraction it references — and distributes the output across both packages:

- `com.enzx.nxgraph/Runtime/Plugins`: `NxGraph` and `NxGraph.Serialization.Abstraction` (each with PDB and XML docs).
- `com.enzx.nxgraph.serialization/Runtime/Plugins`: `NxGraph.Serialization` plus its whole dependency closure. The serialization project sets `CopyLocalLockFileAssemblies` on the netstandard2.1 leg precisely so that closure lands next to the assembly.

### The `.meta` files are the allowlist

Staged binaries are gitignored; the `.meta` sidecars beside them are committed. That is deliberate, and two rules follow from it:

- Staging never deletes a `.meta`. Those files carry the plugin GUIDs Unity uses as reference identity — regenerating one hands every consumer project a new GUID for the same assembly.
- Staging copies only files that already have a `.meta`, and fails if the built set and the sidecar set disagree in either direction. A newly resolved transitive dependency cannot be silently bundled into a shipped package, and a dropped one cannot silently become a `TypeLoadException` at the consumer. The error names the files; the fix is to review the change and add or delete sidecars.

## Release

The `upm-release.yml` workflow (triggered by `upm/v*` tags or manually) runs `ci`, stages the chosen mode, patches both `package.json` files via `upm-patch-version` (pinning the serialization package's dependency on the core), creates the tarballs via `upm-tarball`, publishes each package layout at the root of its own orphan branch — `upm` and `upm-serialization` — and attaches the tarballs to a GitHub release.

Source-mode releases publish the core alone: one tarball, one branch.

## Important

Do not keep both staged source and `NxGraph.dll` in the same package layout, or Unity may see duplicate types.

If a target Unity version cannot compile the staged runtime source as-is, prefer the binary package for that environment.

The serialization package bundles assemblies that other Unity packages commonly ship too (`System.Memory`, `System.Buffers`, `System.Runtime.CompilerServices.Unsafe`, `System.Text.Encodings.Web`, …). That is the accepted cost of a zero-setup install; consumers who hit a duplicate-assembly error resolve it by keeping one copy.
