using NxGraph.Blackboards;

namespace NxGraph.Serialization.Abstraction;

/// <summary>
/// How restore treats payload entries that no longer line up with the live schema.
/// Independent of direction: schema keys absent from the payload always keep their
/// registered defaults (forward compatibility for schemas that added keys).
/// </summary>
public enum BlackboardMismatchPolicy
{
    /// <summary>
    /// Fail loudly: an unknown payload key, a changed value type, or a schema-name/scope
    /// mismatch throws.
    /// </summary>
    Strict = 0,

    /// <summary>
    /// Skip entries that don't fit the schema (schema evolution). Corrupt values still throw.
    /// </summary>
    Skip = 1,
}

/// <summary>
/// Serializes one <see cref="Blackboard"/> to/from JSON as an independent durability
/// artifact (alongside the graph payload and the machine snapshot). Restore writes into an
/// existing board — the schema is code and cannot be reconstructed from a payload.
/// </summary>
public interface IBlackboardJsonSerializer
{
    ValueTask ToJsonAsync(Blackboard blackboard, Stream destination, CancellationToken ct = default);

    ValueTask RestoreFromJsonAsync(Blackboard target, Stream source,
        BlackboardMismatchPolicy policy = BlackboardMismatchPolicy.Strict, CancellationToken ct = default);
}

/// <summary>
/// Binary counterpart of <see cref="IBlackboardJsonSerializer"/>.
/// </summary>
public interface IBlackboardBinarySerializer
{
    ValueTask ToBinaryAsync(Blackboard blackboard, Stream destination, CancellationToken ct = default);

    ValueTask RestoreFromBinaryAsync(Blackboard target, Stream source,
        BlackboardMismatchPolicy policy = BlackboardMismatchPolicy.Strict, CancellationToken ct = default);
}
