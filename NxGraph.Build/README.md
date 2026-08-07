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
| `info` | — | Diagnostic preflight: print repo root, resolved `dotnet` (and why), candidates, `dotnet --info` |
| `clean` | — | Remove staged UPM files (source & binary) from both packages |
| `restore` | — | `dotnet restore` the solution |
| `build` | `restore` | `dotnet build` the solution |
| `test` | `build` | `dotnet test` with code coverage + threshold gate (see [Coverage](#coverage)) |
| **`ci`** | `test` | **Full CI pipeline** (restore → build → test) |
| `pack` | `build` | Pack one or more NuGet packages |
| `push` | `pack` | Push `.nupkg` + `.snupkg` to nuget.org (API key never echoed — see below) |
| **`publish`** | `ci`, `push` | **Full release pipeline** (ci + pack + push) |
| `stage-source` | — | Copy NxGraph source files into the core UPM package (the serialization package is binary-only and is cleared) |
| `stage-binary` | — | Build the netstandard2.1 assemblies and distribute them across both UPM packages |
| `upm-patch-version` | — | Patch `version` in both UPM `package.json` files, pinning the serialization package's dependency on the core |
| `upm-tarball` | `upm-patch-version` | Create a `.tgz` archive per staged UPM package |

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

## Coverage

The `test` target runs coverage on **one** driver: **coverlet.msbuild**
(`/p:CollectCoverage=true /p:Threshold=<n> /p:ThresholdType=line /p:ThresholdStat=minimum`,
with `/p:CoverletOutputFormat=lcov` writing `coverage.info` next to each test csproj).
Each test project scopes its instrumentation via an `<Include>` property in its csproj,
and the threshold applies **per instrumented module**.

Historically the invocation also passed the VSTest collector
(`--collect:"XPlat Code Coverage"`, package `coverlet.collector`) at the same time.
Running both drivers at once is unsupported by coverlet and obscured which one enforced
the gate (the two packages had even drifted to different majors). The collector package
and flag were removed; the msbuild driver is the gate, and a deliberately impossible
`COVERAGE_THRESHOLD=99` run was used to verify the gate actually fails red.

The gate is enforced in `ci` via `COVERAGE_THRESHOLD` (default 70). Raise it as coverage
grows; never lower it without a recorded decision.

### Push secret handling

`push` hands the NuGet API key to `dotnet nuget push --api-key` with SimpleExec echo
suppressed, printing a redacted command line (`--api-key "***"`) instead — the key never
reaches the console or an exception message. The command-line route is deliberate:
`dotnet nuget push` reads the key from no environment variable, and a `NuGet.Config`
`apikeys` entry would persist the secret to disk.

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

**UPM release checklist:** both committed `package.json` files carry the **last released**
version by policy (CI's `upm-patch-version` stamps the same value at release time, and pins
the serialization package's `com.enzx.nxgraph` dependency to it). When cutting a UPM release,
bump both manifest `version` fields, the serialization manifest's dependency pin, both
CHANGELOG top entries, and the core package README's install pin — one version everywhere.

**Staged plugin allowlist:** `stage-binary` copies only assemblies that already have a
committed `.meta` sidecar in the target `Runtime/Plugins` folder, and fails when the built
set and the sidecar set disagree in either direction. The sidecars are the reviewed record of
what each package ships (the binaries themselves are gitignored), and they hold the plugin
GUIDs Unity treats as reference identity — which is why staging preserves them rather than
clearing the folder wholesale. If a transitive dependency appears or disappears, the target
fails with the file names; review the change, then add or delete sidecars.

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
| `COVERAGE_THRESHOLD` | `test` | `70` | Minimum line coverage % per instrumented module (Coverlet); raise as coverage grows, don't lower it |
| `TARGET` | `pack` | _(from git tag)_ | Which packages to pack: `all`, `nxgraph`, `serialization`, `serialization-abstraction` |
| `VERSION` | `pack`, `upm-patch-version`, `upm-tarball` | _(from git tag)_ | SemVer version string (e.g. `1.2.3` or `1.0.0-beta.1`) |
| `NUGET_API_KEY` | `push` | _(required)_ | NuGet.org API key |
| `NUGET_SOURCE` | `push` | `https://api.nuget.org/v3/index.json` | NuGet feed URL |
| `ARTIFACTS_DIR` | `pack`, `push` | `artifacts` | Directory for `.nupkg` / `.snupkg` output |
| `REPO_URL` | `pack` | _(optional)_ | Repository URL embedded in NuGet package (SourceLink) |
| `REPO_BRANCH` | `pack` | _(optional)_ | Branch name embedded in NuGet package |
| `REPO_COMMIT` | `pack` | _(optional)_ | Commit SHA embedded in NuGet package |
| `UPM_PACKAGE_DIR` | `upm-patch-version`, `upm-tarball` | `upm/com.enzx.nxgraph` | Relative path to the **core** UPM package directory. The serialization package is not relocatable — it is versioned and released with the core. |

In CI, these are set automatically by the GitHub Actions workflows. For local use, only
`TARGET` and `VERSION` are needed for pack/UPM commands; everything else has defaults.

## How it maps to GitHub Actions workflows

| Workflow | YAML does | Build project does |
|---|---|---|
| **`dotnet.yml`** | checkout, setup .NET, upload coverage artifact | `ci` (restore → build → test) |
| **`publish-nuget.yml`** | checkout, setup .NET, preflight API key mask, validate `.nupkg` contents, upload artifact | `publish` (ci + pack + push) |
| **`upm-build.yml`** | checkout, setup .NET, upload artifact | `stage-source` or `stage-binary` |
| **`upm-release.yml`** | checkout, setup .NET, resolve version, git push to the `upm` and `upm-serialization` branches, create GitHub Release | `ci` + `stage-{mode}` + `upm-patch-version` + `upm-tarball` |

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


