using NxGraph.Blackboards;

namespace NxGraph.Tokens;

/// <summary>
/// Pool-slot lifecycle of a token. <see cref="Free"/> slots are available for spawning;
/// only <see cref="Runnable"/>/<see cref="Parked"/> tokens exist logically.
/// </summary>
internal enum TokenSlotPhase : byte
{
    Free = 0,
    Runnable = 1,
    Parked = 2,
}

/// <summary>
/// A unit of execution state in the token runtimes: its position, per-visit attempt counter,
/// lifecycle phase, and (when the graph declares a Node-scope schema) its own transient
/// scratch board. Tokens are pooled — every slot (board included) is preallocated at machine
/// construction so spawning and retiring are free-list index operations at 0 B.
/// </summary>
internal sealed class Token
{
    /// <summary>The pool slot this token permanently occupies (set once at pool creation).</summary>
    public int SlotIndex;

    /// <summary>Stable identity within a run (dense, ascending from 0).</summary>
    public int Id;

    /// <summary>Index of the node this token currently occupies (or is parked at).</summary>
    public int NodeIndex;

    /// <summary>Executions of the current node by this token in this visit.</summary>
    public int Attempts;

    /// <summary>Whether the current node's EnterAction has fired for this token's visit.</summary>
    public bool NodeEntered;

    public TokenSlotPhase Phase;

    /// <summary>
    /// The scheduling round this token was spawned in. A token never runs in its spawn round —
    /// it first executes in the following round, keeping interleaving deterministic.
    /// </summary>
    public int SpawnRound;

    /// <summary>
    /// Per-token Node-scope scratch, created from the graph's declared Node schema; null when
    /// none is declared. Resets exactly when this token's attempt counter resets.
    /// </summary>
    public Blackboard? NodeBoard;
}
