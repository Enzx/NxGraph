# NxGraph Unity Package

## Overview

This Unity package wraps the core `NxGraph` runtime as a source-first UPM package.

It is intended to let Unity users:

- inspect the implementation directly
- step through the source in their own projects
- avoid relying on decompiled DLLs for runtime understanding

## Package contents

- `Runtime/NxGraph` - staged runtime source
- `Runtime/NxGraph.Runtime.asmdef` - runtime assembly definition
- `Samples~/QuickStart` - starter sample
- `Documentation~` - package documentation

## Recommended workflow

1. Run `scripts/build-upm.ps1 -Mode Source` or `scripts/build-upm.ps1 -Mode Binary`
2. Reference `upm/com.enzx.nxgraph` from Unity through a local path or Git URL

## Important

Use one staging mode at a time:

- source mode via `build-upm.ps1 -Mode Source`, or
- binary fallback via `build-upm.ps1 -Mode Binary`

The legacy `build-upm-binary.ps1` script is still available as a compatibility shortcut.

Do not keep both staged source and runtime DLLs in the package simultaneously.

Because this package stages the live NxGraph source, supported Unity editor versions depend on the C# language features present in the current runtime files.

