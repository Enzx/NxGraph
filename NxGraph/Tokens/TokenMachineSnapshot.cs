using NxGraph.Fsm;

namespace NxGraph.Tokens;

/// <summary>Lifecycle phase of a live token inside a <see cref="TokenMachineSnapshot"/>.</summary>
public enum TokenPhase : byte
{
    /// <summary>The token is advancing through the graph.</summary>
    Runnable = 0,

    /// <summary>The token is parked at a join, waiting for the join's policy to be met.</summary>
    Parked = 1,
}

/// <summary>Why a token left the run. Reported via the observers' <c>OnTokenRetired</c>.</summary>
public enum TokenRetireReason : byte
{
    /// <summary>The token reached a terminal node with Success.</summary>
    Completed = 0,

    /// <summary>The token failed terminally (retries and failure edge exhausted) — it died.</summary>
    Failed = 1,

    /// <summary>The token was consumed by a join firing.</summary>
    Joined = 2,

    /// <summary>
    /// The run ended while the token was parked at a join that never fired (join starvation —
    /// the machine fails).
    /// </summary>
    Starved = 3,

    /// <summary>
    /// The run ended while the token was parked at a join that had already fired — a benign
    /// quorum leftover (e.g. the late arrival of an M-of-N join). Does not fail the machine.
    /// </summary>
    Absorbed = 4,
}

/// <summary>A serialization-friendly capture of one live token inside a snapshot.</summary>
public sealed record TokenRecord(
    int Id,
    int NodeIndex,
    int Attempts,
    bool NodeEntered,
    TokenPhase Phase);

/// <summary>
/// A serialization-friendly capture of a token machine's runtime state: the multiset of live
/// tokens plus per-join arrival counts. Produced by <c>Suspend()</c> and consumed by
/// <c>Resume(...)</c> on a <see cref="TokenMachine"/> or <see cref="AsyncTokenMachine"/> built
/// over an equivalent graph (same node indices); snapshots are interchangeable between the two
/// token runtimes. This is a separate contract from <see cref="StateMachineSnapshot"/> — the
/// flat FSM snapshot shape is deliberately untouched.
/// <para>
/// The snapshot contains only primitives and plain records, so it serializes cleanly with any
/// serializer. Node-scope scratch is transient by definition and resumes as defaults; the user
/// context (agent/blackboards) is owned by the caller and must be re-attached after resuming.
/// </para>
/// </summary>
public sealed record TokenMachineSnapshot(
    ExecutionStatus Status,
    bool MidRun,
    int NextTokenId,
    TokenRecord[] Tokens,
    int[] JoinArrivals)
{
    /// <summary>
    /// Whether any token had already died (failed terminally) before the snapshot was taken —
    /// the resumed run must still report <see cref="Result.Failure"/> at its natural end.
    /// </summary>
    public bool AnyTokenDied { get; init; }

    /// <summary>
    /// Per-node "this join has fired at least once this run" flags (indexed by node index;
    /// empty when the graph has no joins). Distinguishes benign quorum leftovers from join
    /// starvation after a resume.
    /// </summary>
    public bool[] JoinsFired { get; init; } = [];
}
