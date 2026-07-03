# NxGraph Unity Package

## Overview

This Unity package wraps the core `NxGraph` runtime as a UPM package. It can be staged in two modes:

- **Source mode** — the runtime source is staged into `Runtime/NxGraph`, so Unity users can inspect and step through the implementation directly.
- **Binary mode** — the netstandard2.1 `NxGraph.dll` (with PDB and XML docs) is staged into `Runtime/Plugins`.

## Package contents

- `Runtime/NxGraph` — staged runtime source (source mode), or `Runtime/Plugins/NxGraph.dll` (binary mode)
- `Runtime/NxGraph.Unity.Runtime.asmdef` — runtime assembly definition
- `Samples~/QuickStart` — starter sample
- `Documentation~` — package documentation

## Recommended workflow

1. From the repo root, run `dotnet run --project NxGraph.Build -- stage-source` (or `stage-binary`)
2. Reference `upm/com.enzx.nxgraph` from Unity through a local path, or consume a published release from the `upm` branch / release tarball

See `build-and-release.md` for the full staging and release pipeline.

## Important

Use one staging mode at a time — do not keep both staged source and runtime DLLs in the package simultaneously, or Unity may see duplicate types.

Because source mode stages the live NxGraph source, supported Unity editor versions depend on the C# language features present in the current runtime files.
