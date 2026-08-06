namespace NxGraph.Serialization.Abstraction;

/// <summary>
/// Internal recursion hook behind <see cref="BehaviorFieldWriter.WriteConditions"/> /
/// <see cref="BehaviorFieldReader.ReadConditions"/> — the exact twin of
/// <see cref="IBehaviorEntryCodec"/>: the graph serializer wires its per-entry condition
/// dispatch (write: <see cref="ISerializableCondition.Write"/> else
/// <see cref="IConditionRegistry.TryWrite"/>; read: <see cref="IConditionRegistry.TryRead"/>)
/// into every writer/reader it creates for a payload session, so nested entry lists
/// (<see cref="BehaviorFieldKind.Conditions"/>) encode under exactly the top-level rules.
/// Standalone writers/readers carry no codec — the two methods throw a targeted error there.
/// </summary>
internal interface IConditionEntryCodec
{
    /// <summary>Encodes one live condition into a payload entry.</summary>
    ConditionEntry WriteEntry(object condition);

    /// <summary>Reconstructs one live condition from a payload entry.</summary>
    object ReadEntry(ConditionEntry entry);
}
