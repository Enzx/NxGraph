namespace NxGraph.Serialization;

/// <summary>
/// Sparse per-node retry policy entry: only nodes that declare a policy are serialized.
/// </summary>
internal sealed record RetryPolicyDto(int Index, byte MaxAttempts, long BackoffTicks, byte BackoffKind);
