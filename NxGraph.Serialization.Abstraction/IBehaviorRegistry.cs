namespace NxGraph.Serialization.Abstraction;

/// <summary>
/// Resolves behavior payload identities (payload version 8). The read side maps a behavior's
/// runtime-stable type name plus its fields back to a live instance; the write side covers
/// behaviors that carry no <see cref="ISerializableBehavior"/> implementation of their own —
/// the shipped default registry (<c>NxGraph.Serialization.BehaviorRegistry</c>) handles the
/// standard set (<c>Log</c>, closed <c>SetValue&lt;T&gt;</c>) built in, so standard-set
/// graphs round-trip with zero options configured. Same posture as
/// <see cref="IRegionSelectorRegistry"/>: the registry restores <i>a</i> behavior for the
/// name; whether it behaves like the authored one is the user's contract.
/// </summary>
public interface IBehaviorRegistry
{
    /// <summary>
    /// Reconstructs a behavior from its runtime-stable type name and serialized fields.
    /// Returns <see langword="false"/> when the name is not known to this registry.
    /// </summary>
    bool TryRead(string behaviorTypeName, BehaviorFieldReader fields, out object? behavior);

    /// <summary>
    /// Writes the fields of a behavior that does not implement
    /// <see cref="ISerializableBehavior"/> itself (the standard set). Returns
    /// <see langword="false"/> when the instance is not recognized.
    /// </summary>
    bool TryWrite(object behavior, BehaviorFieldWriter fields);
}
