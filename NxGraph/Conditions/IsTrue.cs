using NxGraph.Behaviors;

namespace NxGraph.Conditions;

/// <summary>
/// Standard condition: the plain guard — <see langword="true"/> when the resolved
/// <see cref="Value"/> is <see langword="true"/>. Takes a <c>BlackboardValue&lt;bool&gt;</c>,
/// so it reads either a bool slot (<c>new IsTrue(doorOpenKey)</c>) or a literal
/// (<c>new IsTrue(true)</c> — a constant arm, occasionally useful as a default).
/// This is the shape <c>KeyEquals&lt;bool&gt;</c> would otherwise be spelled awkwardly in.
/// </summary>
public sealed class IsTrue : ICondition
{
    /// <summary>Creates a truth test of <paramref name="value"/> (literal or key-bound).</summary>
    public IsTrue(BlackboardValue<bool> value)
    {
        Value = value;
    }

    /// <summary>The tested value — literal or key-bound.</summary>
    public BlackboardValue<bool> Value { get; }

    /// <inheritdoc />
    public bool Evaluate(in BehaviorContext ctx) => ctx.Resolve(Value);
}
