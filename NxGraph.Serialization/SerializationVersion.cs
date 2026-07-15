namespace NxGraph.Serialization;

public static class SerializationVersion
{
    // v2: failure edges, retry policies, outcome codes/names.
    // v3: sync nested-machine marker ("SyncStateMachine") — older readers reject v3 payloads
    //     cleanly instead of tripping over the unknown marker string.
    // v4: history/parallel composites (CompositeDto section + "HistoryState"/"SyncHistoryState"/
    //     "ParallelState"/"SyncParallelState" markers) — older readers reject v4 payloads via
    //     the version gate instead of misreading the composite section.
    public const int Version = 4;
}

