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
    public const int Version = 9;
}

