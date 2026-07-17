# NxGraph.Serialization

This project provides serialization and deserialization functionalities for [NxGraph](https://github.com/Enzx/NxGraph) objects, allowing you to easily save and load graph structures.

## Blackboard payloads

A durable flow is shipped as separate artifacts, each with its own lifecycle:

1. **Graph structure** — `GraphSerializer.ToJsonAsync`/`ToBinaryAsync` (node logic encoded via your `ILogicCodec`).
2. **Machine position** — a shallow `StateMachineSnapshot` or, when the flow's suspension points live inside composites, a deep `StateMachineDeepSnapshot` from `SuspendDeep()` (which additionally captures composite-internal position — nested machines, history children, sync parallel regions). Both are primitives-only records you serialize with any serializer; the library never serializes them itself.
3. **One payload per blackboard** — `BlackboardSerializer.ToJsonAsync`/`ToBinaryAsync`; a global board saves once per world, graph boards per entity.

Restore is *restore-into*: schemas are code and cannot be reconstructed from a payload, so you create a live `Blackboard` over the current schema and apply the payload over its defaults (`RestoreFromJsonAsync`/`RestoreFromBinaryAsync`). Post-state is always defaults + payload — keys added to the schema after the save keep their defaults, and stale pre-restore values never survive.

Schema drift is handled by `BlackboardMismatchPolicy`:

| Payload vs live schema | `Strict` (default) | `Skip` |
| --- | --- | --- |
| Schema name or scope differs | throws | whole restore is a no-op |
| Payload key unknown to the schema | throws | entry ignored |
| Key's value type changed | throws | entry ignored (slot keeps its default) |
| Key missing from the payload | default | default |
| Corrupt/undeserializable value | throws | throws (Skip is for schema evolution, not corruption) |
| Payload version newer than the serializer | throws | throws |

Custom value types plug in through the constructor options — supply `JsonSerializerOptions` converters and/or a `MessagePackSerializerOptions` resolver (e.g. `ContractlessStandardResolver`). Payload type names are verification data only and are never used to resolve a type.

Note: graph payloads do **not** carry blackboard schema declarations (`GraphDto` is unchanged) — a deserialized `Graph` has `Schema == null` and binds boards permissively; the per-key schema check in `Blackboard` remains the runtime safety net.
