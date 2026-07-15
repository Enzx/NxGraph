namespace NxGraph.Serialization;

public static class SerializationVersion
{
    // v2: failure edges, retry policies, outcome codes/names.
    // v3: sync nested-machine marker ("SyncStateMachine") — older readers reject v3 payloads
    //     cleanly instead of tripping over the unknown marker string.
    public const int Version = 3;
}

