namespace NxGraph.Serialization.Abstraction;

/// <summary>
/// Opt-in serialization contract for conditions (payload version 10) — the branch twin of
/// <see cref="ISerializableBehavior"/>, reusing the same neutral field model
/// (<see cref="BehaviorFieldWriter"/>) so a decision rides the wire under exactly the rules a
/// behavior does. Reconstruction is registry-based: register a factory under the condition's
/// runtime-stable type name on the <c>GraphSerializerOptions.ConditionRegistry</c>, and it
/// rebuilds the instance from a <see cref="BehaviorFieldReader"/> on read. The standard set
/// (<c>IsTrue</c>, <c>Not</c>, <c>KeyEquals&lt;T&gt;</c>) needs neither — the default registry
/// carries it built in, so a branching graph round-trips with zero options.
/// </summary>
public interface ISerializableCondition
{
    /// <summary>Writes this condition's fields to the payload.</summary>
    void Write(BehaviorFieldWriter writer);
}
