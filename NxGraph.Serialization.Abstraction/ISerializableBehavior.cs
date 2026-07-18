namespace NxGraph.Serialization.Abstraction;

/// <summary>
/// Opt-in serialization contract for behaviors (payload version 8): a behavior that wants to
/// ride graph payloads writes its fields through the deliberately small neutral field model
/// (<see cref="BehaviorFieldWriter"/>) — string, bool, integral/floating numerics, enum (as
/// string), and blackboard bindings. Reconstruction is registry-based: register a factory
/// under the behavior's runtime-stable type name on the
/// <c>GraphSerializerOptions.BehaviorRegistry</c>, and it rebuilds the instance from a
/// <see cref="BehaviorFieldReader"/> on read. The standard set (<c>Log</c>,
/// <c>SetValue&lt;T&gt;</c>) needs neither — the default registry carries it built in.
/// </summary>
public interface ISerializableBehavior
{
    /// <summary>Writes this behavior's fields to the payload.</summary>
    void Write(BehaviorFieldWriter writer);
}
