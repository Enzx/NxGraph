namespace NxGraph.Serialization;

/// <summary>
/// Payload entry for a plain nested machine. <paramref name="OutcomeCodeKeyName"/> /
/// <paramref name="OutcomeNameKeyName"/> (payload version 11) name the blackboard keys the
/// machine publishes its terminal outcome through — names only (keys never ride; the read
/// side rebuilds name-bound machines resolved against the bound boards per publish); the
/// value types are fixed (<c>int</c> / <c>string</c>), so no type names ride. Pre-v11
/// payloads read outcome-key-free. <paramref name="OwnsBoard"/> / <paramref name="Ports"/>
/// (payload version 12) carry the sub-graph ports declaration: the machine runs its child
/// graph on its own board, with the declared ports (see <see cref="SubGraphPortDto"/>).
/// <paramref name="Ports"/> may be an empty array (board-only declaration) and must be
/// null/empty when <paramref name="OwnsBoard"/> is false. Pre-v12 payloads read shared-board
/// and port-free.
/// </summary>
internal sealed record SubGraphDto(int OwnerIndex, GraphDto Graph,
    string? OutcomeCodeKeyName = null, string? OutcomeNameKeyName = null,
    bool OwnsBoard = false, SubGraphPortDto[]? Ports = null);
