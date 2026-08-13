namespace NxGraph.Serialization;

/// <summary>
/// Payload entry for a plain nested machine. <paramref name="OutcomeCodeKeyName"/> /
/// <paramref name="OutcomeNameKeyName"/> (payload version 11) name the blackboard keys the
/// machine publishes its terminal outcome through — names only (keys never ride; the read
/// side rebuilds name-bound machines resolved against the bound boards per publish); the
/// value types are fixed (<c>int</c> / <c>string</c>), so no type names ride. Pre-v11
/// payloads read outcome-key-free.
/// </summary>
internal sealed record SubGraphDto(int OwnerIndex, GraphDto Graph,
    string? OutcomeCodeKeyName = null, string? OutcomeNameKeyName = null);
