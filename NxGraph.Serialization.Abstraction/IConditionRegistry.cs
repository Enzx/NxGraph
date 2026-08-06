namespace NxGraph.Serialization.Abstraction;

/// <summary>
/// Resolves condition payload identities (payload version 10) — the branch twin of
/// <see cref="IBehaviorRegistry"/>. The read side maps a condition's runtime-stable type name
/// plus its fields back to a live instance; the write side covers conditions that carry no
/// <see cref="ISerializableCondition"/> implementation of their own — the shipped default
/// registry (<c>NxGraph.Serialization.ConditionRegistry</c>) handles the standard set
/// (<c>IsTrue</c>, <c>Not</c>, closed <c>KeyEquals&lt;T&gt;</c>) built in, so branching graphs
/// round-trip with zero options configured. Same posture as <see cref="IBehaviorRegistry"/>:
/// the registry restores <i>a</i> condition for the name; whether it decides like the authored
/// one is the user's contract.
/// </summary>
public interface IConditionRegistry
{
    /// <summary>
    /// Reconstructs a condition from its runtime-stable type name and serialized fields.
    /// Returns <see langword="false"/> when the name is not known to this registry.
    /// </summary>
    bool TryRead(string conditionTypeName, BehaviorFieldReader fields, out object? condition);

    /// <summary>
    /// Writes the fields of a condition that does not implement
    /// <see cref="ISerializableCondition"/> itself (the standard set). Returns
    /// <see langword="false"/> when the instance is not recognized.
    /// </summary>
    bool TryWrite(object condition, BehaviorFieldWriter fields);
}
