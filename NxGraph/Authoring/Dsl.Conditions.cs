using NxGraph.Blackboards;
using NxGraph.Conditions;
using NxGraph.Fsm;

namespace NxGraph.Authoring;

/// <summary>
/// Data-built branching overloads (spec 023) — the serializable twins of the delegate
/// <c>.If(predicate)</c> / <c>.Switch(selector)</c> paths. The decision is a list of
/// <see cref="ICondition"/> objects or a blackboard key plus literal cases, so a graph built
/// with these round-trips through <c>GraphSerializer</c> with zero options and survives
/// suspend/resume — and its arms carry labels into the Mermaid export.
/// <para>
/// The builders returned here are the same <see cref="Dsl.IfBuilder"/> /
/// <see cref="Dsl.SwitchBuilder{TKey}"/> the delegate paths return, so
/// <c>.Then(...)/.Else(...)</c> and <c>.Case(...)/.Default(...)/.End()</c> are unchanged.
/// </para>
/// </summary>
public static partial class Dsl
{
    /// <summary>
    /// Branches on a single condition. Equivalent to <c>.If(ConditionMatch.All, condition)</c>.
    /// </summary>
    public static IfBuilder If(this StateToken prev, ICondition condition)
    {
        return new IfBuilder(prev, Single(condition), ConditionMatch.All);
    }

    /// <inheritdoc cref="If(StateToken,ICondition)" />
    public static IfBuilder If(this StartToken root, ICondition condition)
    {
        return new IfBuilder(root, Single(condition), ConditionMatch.All);
    }

    /// <summary>
    /// Branches on a condition list combined by <paramref name="match"/>
    /// (<see cref="ConditionMatch.All"/> = AND, <see cref="ConditionMatch.Any"/> = OR).
    /// Evaluation short-circuits; conditions are side-effect free by contract.
    /// </summary>
    public static IfBuilder If(this StateToken prev, ConditionMatch match, params ICondition[] conditions)
    {
        return new IfBuilder(prev, conditions, match);
    }

    /// <inheritdoc cref="If(StateToken,ConditionMatch,ICondition[])" />
    public static IfBuilder If(this StartToken root, ConditionMatch match, params ICondition[] conditions)
    {
        return new IfBuilder(root, conditions, match);
    }

    /// <summary>
    /// Switches on the value of a blackboard key, with literal cases — the serializable twin of
    /// <c>.Switch(selector)</c>. Chain <c>.Case(value, logic)</c> arms and an optional
    /// <c>.Default(logic)</c>, then <c>.End()</c>. A value cased twice is rejected at
    /// <c>.End()</c>: a switch is a lookup, so at most one case may match. Ordered,
    /// first-match-wins rules are a chain of <c>.If(condition)</c> branches.
    /// </summary>
    public static SwitchBuilder<TKey> Switch<TKey>(this StateToken prev, BlackboardKey<TKey> key)
        where TKey : notnull
    {
        return new SwitchBuilder<TKey>(prev, key);
    }

    /// <inheritdoc cref="Switch{TKey}(StateToken,BlackboardKey{TKey})" />
    public static SwitchBuilder<TKey> Switch<TKey>(this StartToken root, BlackboardKey<TKey> key)
        where TKey : notnull
    {
        return new SwitchBuilder<TKey>(root, key);
    }

    private static ICondition[] Single(ICondition condition)
    {
        // Null is rejected by the ChoiceState constructor with the "conditions" parameter name,
        // so a single-condition call reports the same way as the list overload.
        return [condition];
    }
}
