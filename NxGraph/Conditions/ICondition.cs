using NxGraph.Behaviors;

namespace NxGraph.Conditions;

/// <summary>
/// A <b>data-shaped decision</b> (spec 023): the branching counterpart of
/// <see cref="IBehavior"/>. A condition reads the machine-bound blackboards through the
/// behavior model's <see cref="BehaviorContext"/> — routed <c>Bb</c>, typed
/// <see cref="BehaviorContext.Resolve{T}"/> for literal-or-key operands — and answers
/// <see langword="true"/> or <see langword="false"/>.
/// <para>
/// It deliberately reuses <b>none</b> of the fault model. <see cref="IBehavior"/> returns a
/// <see cref="Result"/> whose <c>Failure</c> makes the owning node fault into retry and the
/// failure edge, and whose <c>InProgress</c> is meaningless to a decision; a condition that is
/// false is <i>not</i> a fault, and conflating the two spends the node fault model on ordinary
/// branching. A condition returns <see langword="bool"/>.
/// </para>
/// <para>
/// <b>Contract — conditions are side-effect free.</b> They read the boards and write nothing,
/// so the <see cref="ConditionMatch.All"/> / <see cref="ConditionMatch.Any"/> short-circuit
/// walk is always safe and re-evaluation is always equivalent. A genuine wiring fault (an
/// unbound key, a key declared with a different value type) <b>throws</b> and propagates like
/// any node throw; it is never reported as <see langword="false"/>.
/// </para>
/// <para>
/// Conditions are sync-only by design: they read boards, and a sync condition runs under both
/// runtimes exactly as sync <c>Repeat</c> bodies do. Implementations are shareable data
/// objects — never stamped with per-machine state — so one instance may appear in several
/// graphs.
/// </para>
/// </summary>
public interface ICondition
{
    /// <summary>
    /// Evaluates this condition against the machine-bound context. Must not write to the
    /// boards; must throw (not return <see langword="false"/>) on a wiring fault.
    /// </summary>
    bool Evaluate(in BehaviorContext ctx);
}

/// <summary>
/// Shared wiring-time validation for condition lists — the condition twin of
/// <c>BehaviorComposition</c>.
/// </summary>
internal static class ConditionComposition
{
    internal const string ParamName = "conditions";

    /// <summary>
    /// Copies the list into a dense array, rejecting a null/empty list and null entries. The
    /// copy matters: the caller's list must not be able to mutate a built graph's decision.
    /// </summary>
    internal static ICondition[] ValidateEntries(IReadOnlyList<ICondition> conditions, string paramName)
    {
        if (conditions is null || conditions.Count == 0)
        {
            throw new ArgumentException(
                "At least one condition is required — an empty condition list has no defensible reading.",
                paramName);
        }

        ICondition[] copy = new ICondition[conditions.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = conditions[i] ?? throw new ArgumentException(
                $"Condition at index {i} is null — conditions must not contain null entries.", paramName);
        }

        return copy;
    }
}
