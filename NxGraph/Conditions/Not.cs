using NxGraph.Behaviors;

namespace NxGraph.Conditions;

/// <summary>
/// Standard condition: negates exactly one inner condition. Without negation,
/// <see cref="ConditionMatch.All"/> / <see cref="ConditionMatch.Any"/> cannot express "not
/// equal" — swapping the two arms only works for a single-condition choice.
/// <para>
/// The one nesting shape in the condition model; it rides the payload through the neutral
/// field model's nested-entry slot, under the same read-side depth cap as nested behaviors.
/// </para>
/// </summary>
public sealed class Not : ICondition
{
    /// <summary>Creates the negation of <paramref name="condition"/>.</summary>
    public Not(ICondition condition)
    {
        Inner = condition ?? throw new ArgumentNullException(nameof(condition));
    }

    /// <summary>The negated condition.</summary>
    public ICondition Inner { get; }

    /// <inheritdoc />
    public bool Evaluate(in BehaviorContext ctx) => !Inner.Evaluate(in ctx);
}
