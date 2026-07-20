# Changelog

All notable changes to this package will be documented in this file.

## [2.1.0-alpha]

### Runtime (staged NxGraph core)
- Unified fault model: per-node retry policies with backoff, failure edges (`.OnError(...)`), timeouts as ordinary failures, and terminal outcome codes.
- Scoped blackboards (`Global`/`Graph`/`Node`) with typed keys, machine-bound boards, and blackboard-aware DSL overloads; graphs stay shareable templates.
- Step I/O ports: typed producer/consumer/pipe DSL overloads that pipe one step's output into the next through Graph-scoped blackboard keys.
- Token runtime: `TokenMachine`/`AsyncTokenMachine` run pooled tokens through one flat graph with `.ForkTo(...)` fan-out and `JoinState` merges (all / any / quorum).
- Durable suspend/resume: shallow `Suspend()`/`Resume(...)` plus deep `SuspendDeep()`/`ResumeDeep(...)` capturing composite trees; snapshots are interchangeable between runtimes.
- Sync/async parity across composites: nested machines, history, static and dynamic parallel regions, with one-tick and per-tick stepping modes.
- In-node wall-clock concurrency via `.ToAllAsync(...)` (sync twin `.ToAll(...)`).
- Stable per-node UIDs (`.WithUid(...)`) for editor tooling.

### Package
- Source staging now includes the `Tokens` folder (the source-mode package could not compile without it after the token runtime landed).
- Staging documentation now describes the actual mechanism (`dotnet run --project NxGraph.Build -- stage-source|stage-binary`); the previously referenced `scripts/build-upm.ps1` no longer exists.
- Source staging now includes the `Blackboards` and `Shims` folders (the source-mode package could not compile without them).
- Staged binaries are no longer committed to `main` (`Runtime/**` build outputs are git-ignored); each release rebuilds and stages `Runtime/Plugins/NxGraph.dll` in CI, retiring the previously committed stale 1.0.0 build that predated failure edges, suspend/resume, parallel composites, and blackboards.
- Versioning policy: the committed `package.json` always carries the **last released** version; the release workflow (`upm-patch-version`) stamps the same value at release time. Bumping the manifest is part of the release checklist (see `NxGraph.Build/README.md`).
- Aligned the assembly definition name (`NxGraph.Unity.Runtime`) with its file name.
- Install instructions now point at the released `upm` branch/tags instead of the main-branch package folder.

## [2.0.1-alpha]
- Alpha release published from the `upm/v2.0.1-alpha` tag (binary staging mode).

## [2.0.0.1-alpha]
- First alpha publish of the 2.x package pipeline (tag `upm/v2.0.0.1-alpha`; note: not valid SemVer — superseded by 2.0.1-alpha).
