namespace NxGraph.Conditions;

/// <summary>
/// How a <c>ChoiceState</c> combines its condition list. Evaluation short-circuits in both
/// modes — legal because conditions are side-effect free by contract
/// (see <see cref="ICondition"/>).
/// </summary>
public enum ConditionMatch
{
    /// <summary>Logical AND: walks until the first <see langword="false"/>.</summary>
    All = 0,

    /// <summary>Logical OR: walks until the first <see langword="true"/>.</summary>
    Any = 1,
}
