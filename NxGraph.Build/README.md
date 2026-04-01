# NxGraph.Build

A C# build-orchestration project that consolidates all CI/CD logic into testable, locally-runnable
[Bullseye](https://github.com/adamralph/bullseye) targets. The GitHub Actions workflows become thin
wrappers that delegate to this project, keeping YAML minimal and all real logic in C#.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or newer)

No other tools are required — all dependencies (Bullseye, SimpleExec) are restored automatically.

## Quick start

```bash
# From the repository root
dotnet run --project NxGraph.Build -- <target> [target2 ...]
```

Bullseye supports passing **multiple targets** in a single invocation. Shared dependencies
(e.g. `restore`, `build`) are automatically de-duplicated and only executed once.

## Available targets

| Target | Depends on | Description |
|---|---|---|
| `clean` | — | Remove all staged UPM files (source & binary) |
| `restore` | — | `dotnet restore` the solution |
| `build` | `restore` | `dotnet build` the solution |
| `test` | `build` | `dotnet test` with code-coverage collection |
| **`ci`** | `test` | **Full CI pipeline** (restore → build → test) |
| `pack` | `build` | Pack one or more NuGet packages |
| `push` | `pack` | Push `.nupkg` + `.snupkg` to nuget.org |
| **`publish`** | `ci`, `push` | **Full release pipeline** (ci + pack + push) |
| `stage-source` | — | Copy NxGraph source files into the UPM package |
| `stage-binary` | — | Build NxGraph DLL and copy into the UPM package |
| `upm-patch-version` | — | Patch `version` field in UPM `package.json` |
| `upm-tarball` | `upm-patch-version` | Create a `.tgz` archive of the UPM package |

### Dependency tree

```
publish
├── ci
│   └── test
│       └── build
│           └── restore
└── push
    └── pack
        └── build
            └── restore
```

## Examples

### Run the full CI pipeline locally

```bash
dotnet run --project NxGraph.Build -- ci
```

This runs **restore → build → test** with code-coverage. Equivalent to what the
`dotnet.yml` workflow does on every push/PR.

### Build & test with a custom coverage threshold

```bash
COVERAGE_THRESHOLD=80 dotnet run --project NxGraph.Build -- ci
```

On Windows (PowerShell):

```powershell
$env:COVERAGE_THRESHOLD = "80"
dotnet run --project NxGraph.Build -- ci
```

### Pack NuGet packages locally

```bash
# Pack all packages at version 1.2.3
TARGET=all VERSION=1.2.3 dotnet run --project NxGraph.Build -- pack
```

```bash
# Pack only the Serialization package
TARGET=serialization VERSION=2.0.0-beta.1 dotnet run --project NxGraph.Build -- pack
```

On Windows (PowerShell):

```powershell
$env:TARGET = "all"
$env:VERSION = "1.2.3"
dotnet run --project NxGraph.Build -- pack
```

### Full NuGet publish (CI + pack + push)

```bash
TARGET=all \
VERSION=1.2.3 \
NUGET_API_KEY=your-api-key \
  dotnet run --project NxGraph.Build -- publish
```

This is the single command the `publish-nuget.yml` workflow runs.

### Stage UPM source package

```bash
dotnet run --project NxGraph.Build -- stage-source
```

### Stage UPM binary package

```bash
dotnet run --project NxGraph.Build -- stage-binary
```

### Full UPM release flow (CI + stage + patch version + tarball)

```bash
VERSION=1.0.0 dotnet run --project NxGraph.Build -- ci stage-binary upm-patch-version upm-tarball
```

Bullseye runs all four targets (and their transitive dependencies) in a single process.
This is what the `upm-release.yml` workflow runs — the remaining git-push and GitHub Release
steps stay in YAML because they need authenticated git operations.

### Clean staged UPM files

```bash
dotnet run --project NxGraph.Build -- clean
```

### List all targets

```bash
dotnet run --project NxGraph.Build -- --list-targets
```

### Show the dependency tree for a target

```bash
dotnet run --project NxGraph.Build -- --list-tree publish
```

## Environment variables

All configuration is passed via environment variables. Values that are not set fall back to
sensible defaults for local development.

| Variable | Used by | Default | Description |
|---|---|---|---|
| `CONFIGURATION` | `build`, `test` | `Release` | Build configuration |
| `COVERAGE_THRESHOLD` | `test` | `0` | Minimum coverage % (Coverlet) |
| `TARGET` | `pack` | _(from git tag)_ | Which packages to pack: `all`, `nxgraph`, `serialization`, `serialization-abstraction` |
| `VERSION` | `pack`, `upm-patch-version`, `upm-tarball` | _(from git tag)_ | SemVer version string (e.g. `1.2.3` or `1.0.0-beta.1`) |
| `NUGET_API_KEY` | `push` | _(required)_ | NuGet.org API key |
| `NUGET_SOURCE` | `push` | `https://api.nuget.org/v3/index.json` | NuGet feed URL |
| `ARTIFACTS_DIR` | `pack`, `push` | `artifacts` | Directory for `.nupkg` / `.snupkg` output |
| `REPO_URL` | `pack` | _(optional)_ | Repository URL embedded in NuGet package (SourceLink) |
| `REPO_BRANCH` | `pack` | _(optional)_ | Branch name embedded in NuGet package |
| `REPO_COMMIT` | `pack` | _(optional)_ | Commit SHA embedded in NuGet package |
| `UPM_PACKAGE_DIR` | `upm-patch-version`, `upm-tarball` | `upm/com.enzx.nxgraph` | Relative path to the UPM package directory |

In CI, these are set automatically by the GitHub Actions workflows. For local use, only
`TARGET` and `VERSION` are needed for pack/UPM commands; everything else has defaults.

## How it maps to GitHub Actions workflows

| Workflow | YAML does | Build project does |
|---|---|---|
| **`dotnet.yml`** | checkout, setup .NET, upload coverage artifact | `ci` (restore → build → test) |
| **`publish-nuget.yml`** | checkout, setup .NET, preflight API key mask, validate `.nupkg` contents, upload artifact | `publish` (ci + pack + push) |
| **`upm-build.yml`** | checkout, setup .NET, upload artifact | `stage-source` or `stage-binary` |
| **`upm-release.yml`** | checkout, setup .NET, resolve version, git push to `upm` branch, create GitHub Release | `ci` + `stage-{mode}` + `upm-patch-version` + `upm-tarball` |

The YAML files only contain what **must** stay in GitHub Actions: triggers, permissions,
concurrency groups, checkout, SDK setup, secret masking, artifact upload, git push, and
GitHub Release creation.

All real logic (restore, build, test, pack, push, staging, version patching, tarball creation)
lives in the build project, making it easy to run and debug locally.

## Project structure

```
NxGraph.Build/
├── NxGraph.Build.csproj   # Project file (Bullseye + SimpleExec references)
├── Program.cs             # Bullseye target definitions + UPM staging logic
├── BuildHelpers.cs        # Shared utilities (SemVer, paths, pack args, tarball, etc.)
└── README.md              # This file
```

### Build-tool output isolation

NxGraph.Build outputs its binaries to `.tools/` at the repository root instead of the
conventional `bin/` and `obj/` directories. This keeps build-tool artifacts
(`NxGraph.Build.exe`, `Bullseye.dll`, `SimpleExec.dll`, etc.) completely separated from
the actual project outputs.

```
.tools/                ← git-ignored, build-tool output only
├── bin/
│   ├── Debug/net8.0/  ← local dev (dotnet run)
│   └── Release/net8.0/← CI
└── obj/               ← intermediate files
```

Additionally, NxGraph.Build is **excluded from the solution-level Release build**. When CI
runs `dotnet build -c Release` on the solution, it only builds the library, test, and example
projects — not the build tool itself. The tool is built on-demand by
`dotnet run --project NxGraph.Build`.

> **Note:** The Debug configuration still includes NxGraph.Build in the solution build
> for IDE convenience (IntelliSense, error highlighting, etc.).


