namespace NxGraph.Serialization.Abstraction;

/// <summary>
/// Internal recursion hook behind <see cref="BehaviorFieldWriter.WriteBehaviors"/> /
/// <see cref="BehaviorFieldReader.ReadBehaviors"/>: the graph serializer wires its per-entry
/// behavior dispatch (write: <see cref="ISerializableBehavior.Write"/> else
/// <see cref="IBehaviorRegistry.TryWrite"/>; read: registry-first
/// <see cref="IBehaviorRegistry.TryRead"/>) into every writer/reader it creates for a payload
/// session, so nested entry lists (<see cref="BehaviorFieldKind.Behaviors"/>) encode under
/// exactly the top-level rules. Standalone writers/readers carry no codec — the two methods
/// throw a targeted error there.
/// </summary>
internal interface IBehaviorEntryCodec
{
    /// <summary>Encodes one live behavior into a payload entry.</summary>
    BehaviorEntry WriteEntry(object behavior);

    /// <summary>Reconstructs one live behavior from a payload entry.</summary>
    object ReadEntry(BehaviorEntry entry);
}
