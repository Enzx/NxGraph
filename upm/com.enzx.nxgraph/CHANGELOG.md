# Changelog

All notable changes to this package will be documented in this file.

## [2.2.0-alpha]

### Breaking
- The delegate-backed director states were renamed: `ChoiceState` → `RelayChoiceState`, `SwitchState<TKey>` → `RelaySwitchState<TKey>`, `AsyncChoiceState` → `AsyncRelayChoiceState`, `AsyncSwitchState<TKey>` → `AsyncRelaySwitchState<TKey>`. Behavior and constructors are unchanged, and the `.If(predicate)` / `.Switch(selector)` DSL paths still build them. **No obsolete forwarding aliases exist**: the old names are reused in this same release for the new data-built states, so an alias would silently compile old code into a state that means something else. Update the type names; the compiler error is the migration instruction.

### Runtime (staged NxGraph core)
- **Data-built branching**: `ChoiceState(conditions, ConditionMatch.All|Any, trueTarget, falseTarget)` and `SwitchState<T>(key, literal cases, defaultTarget)` decide from data instead of a closure, so a branching graph rides the serialization payload with zero serializer options and survives suspend/resume. Conditions live in `NxGraph.Conditions` (`ICondition`, `KeyEquals<T>`, `IsTrue`, `Not`), reuse the behavior model's `BehaviorContext`, and reuse none of the fault model — a false condition is a decision, never a node failure. Author with `.If(condition)`, `.If(ConditionMatch.Any, …)`, and `.Switch(blackboardKey).Case(value, …).Default(…).End()`; a switch is a lookup (literal cases, duplicates rejected at build time) and ordered first-match-wins rules lower to a chain of choices.
- Typed event entry points: `GraphBuilder.StartWithEvents()` builds one graph with N externally-raised entry chains (`.On(key, e => chain)`, `.Otherwise(...)`); raise with `Execute<TEvent>(evt)` / `ExecuteAsync<TEvent>(evt, ct)` / `StepAsync<TEvent>(evt, ct)`. Event payloads deliver through Graph-scoped blackboard keys, so payload durability falls out of the existing blackboard artifacts.
- Declarative behaviors: author a node as an ordered, fail-fast list of small data-shaped behaviors (`.ToBehaviors(...)` / `.ToBehaviorsAsync(...)`, plus typed-agent variants). `BlackboardValue<T>` binds each field to a literal or a blackboard key; the standard set ships `Log` and `SetValue<T>`, and `Repeat` adds bounded sub-node iteration in both runtimes.
- Machine lifecycle hardening: run-start initialization is throw-hardened on all four machines (a throwing observer or context stamp repairs status back to Ready and releases the execute gate), sync `Reset()` is idempotent from every status, and every `Resume` rejects snapshots with undefined execution statuses.
- `State.Log` now delivers to the attached observer under both runtimes, and machines sharing a graph each attribute log reports to their own observer. The report channel is a capability rather than a base-class membership, so a node that is not a `State` subclass — a data-built branch, for instance — reaches the observer too, and a condition can report the decision it just made.
- Director-selected next nodes (choice/switch) are reported to observers under their built ids, and timeout wrappers forward machine wiring to the logic they wrap.
- Cross-runtime behavior is pinned by a parity conformance suite comparing sync/async × full-run/stepped runs as order-exact traces.
- Validation is self-sufficient (the node set derives from the graph itself); the Mermaid exporter escapes director labels correctly and labels data-built branch arms (`true` / `false`, the case literal, `otherwise`). The validator warns on a choice whose arms are the same node and on a switch with no default target.
- Serialization payload versions 7–10: event entry sections, behavior sections, nested behavior entries, and the choice/switch branch sections ride the graph payload — the last with an `ISerializableCondition` / `ConditionRegistry` pair mirroring the behavior registry, so branch graphs round-trip with zero serializer options. Read paths are hardened and the blackboard `Skip` restore policy never resets a board without restoring at least one value. Each version reads its predecessors unchanged.

### Package
- The netstandard2.1 (Unity-facing) public API surface now has its own approved baseline, so Unity-visible API changes are caught explicitly.
- Analyzer warnings are errors across the build; the staged runtime compiles warning-free under the stricter regime.

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
