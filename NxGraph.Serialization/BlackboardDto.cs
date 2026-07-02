using System.Text.Json;

namespace NxGraph.Serialization;

/// <summary>One slot value: key name, value-type full name (verification only), and the value.</summary>
internal sealed record BlackboardEntryDto(string Key, string Type, JsonElement Value);

/// <summary>
/// JSON payload shape for one <see cref="NxGraph.Blackboards.Blackboard"/>. Independent of
/// <see cref="GraphDto"/> — the blackboard is its own durability artifact with its own
/// payload version.
/// </summary>
internal sealed record BlackboardDto(BlackboardEntryDto[] Values, string? Schema, int Scope)
{
    public int Version { get; init; } = BlackboardSerializer.PayloadVersion;
}
