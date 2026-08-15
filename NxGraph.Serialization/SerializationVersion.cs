namespace NxGraph.Serialization;

public static class SerializationVersion
{
    // v2: failure edges, retry policies, outcome codes/names.
    // v3: sync nested-machine marker ("SyncStateMachine") — older readers reject v3 payloads
    //     cleanly instead of tripping over the unknown marker string.
    // v4: history/parallel composites (CompositeDto section + "HistoryState"/"SyncHistoryState"/
    //     "ParallelState"/"SyncParallelState" markers) — older readers reject v4 payloads via
    //     the version gate instead of misreading the composite section.
    // v5: per-node stable UIDs (sparse UidDto section) — editor-tooling identity metadata.
    // v6: token fork/join sections ("ForkState"/"JoinState" markers), dynamic-parallel
    //     composite kinds + SelectorKey ("DynamicParallelState"/"SyncDynamicParallelState"
    //     markers, selector resolved via IRegionSelectorRegistry), container section
    //     (markerless claims routed to the configured IContainerCodec).
    // v7: event entry section ("EventEntryState" marker, sparse EventEntryDto beside the
    //     other sections) — dispatch table as (KeyName, EventTypeName, TargetIndex) entries
    //     plus the Otherwise target; keys never ride, so the read side rebuilds unbound
    //     registrations resolved by name against the machine's bound board at raise time.
    // v8: behavior composite section ("BehaviorState"/"AsyncBehaviorState" markers, sparse
    //     BehaviorDto beside the other sections) — entries as (BehaviorTypeName, Fields[])
    //     in the neutral field model, bindings by key name (rebound against the machine's
    //     bound boards at execution), AgentTypeName closing BehaviorState<TAgent> on read;
    //     the standard set (Log, SetValue<T>) rides with zero options via the default
    //     BehaviorRegistry.
    // v9: nested behavior entries (BehaviorFieldKind.Behaviors) — field values gain an
    //     Entries slot carrying BehaviorEntry lists, encoded recursively through the
    //     serializer's entry codec, so Repeat/AsyncRepeat bodies (and user behaviors nested
    //     inside them) ride under the top-level entry rules; read-side nesting is capped at
    //     32. No new section — the change lives entirely inside the field model, and pre-v9
    //     payloads never contain the new kind, so they read unchanged.
    // v10: data-built branching sections (sparse ChoiceDto/SwitchDto beside the other
    //      sections, markers "ChoiceState"/"SwitchState", one per state for both runtimes) —
    //      a choice rides its ConditionMatch mode plus its condition list in the neutral field
    //      model (nested Not bodies via the new BehaviorFieldKind.Conditions, read-side
    //      recursion capped at 32); a switch rides its key name, runtime-stable value type
    //      name, literal cases and default target, rebuilding unbound so the key resolves by
    //      name against the machine's bound schemas. The standard condition set (IsTrue, Not,
    //      KeyEquals<T>) rides with zero options via the default ConditionRegistry, closing
    //      the last relay-lambda hole in graph payloads. Pre-v10 payloads read branch-free.
    // v11: hierarchical outcome keys — the SubGraphs entries and the history kinds of
    //      CompositeDto gain optional OutcomeCodeKeyName/OutcomeNameKeyName (names only;
    //      value types are fixed int/string, and the names are exclusive to the history
    //      kinds on CompositeDto), so a composite that publishes its child's terminal
    //      outcome onto declared blackboard keys survives the trip. Rebuilds name-bound,
    //      resolved per publish against the machine's bound schemas (the EventEntryDto
    //      recipe). Pre-v11 payloads read outcome-key-free.
    // v12: sub-graph ports — the SubGraphs entries gain OwnsBoard + a ports list, and
    //      CompositeDto gains sparse per-region ports (entry presence = owns-board; history
    //      kinds may only claim region 0), so a composite that runs its child on the child's
    //      own Graph board with declared inputs/outputs survives the trip. Each port rides as
    //      (Direction, source key name or field-model literal, target name, runtime-stable
    //      value type name); everything rebuilds unbound and resolves per application against
    //      the boards bound at that moment, with targeted miss/type-mismatch errors. Pre-v12
    //      payloads read shared-board and port-free.
    public const int Version = 12;
}

