using NxGraph.Conditions;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// Non-generic serialization/diagnostics surface of the data-built <see cref="ChoiceState"/>
/// — the branch twin of <c>IBehaviorComposite</c>. Keeps the serializer and the Mermaid
/// exporter reflection-free at the detection point.
/// </summary>
public interface IChoiceNode
{
    /// <summary>How the condition list combines.</summary>
    ConditionMatch Match { get; }

    /// <summary>The conditions, in evaluation order.</summary>
    IReadOnlyList<ICondition> Conditions { get; }

    /// <summary>The arm taken when the combined decision is <see langword="true"/>.</summary>
    NodeId TrueTarget { get; }

    /// <summary>The arm taken when the combined decision is <see langword="false"/>.</summary>
    NodeId FalseTarget { get; }
}

/// <summary>
/// Non-generic serialization/diagnostics surface of the data-built <see cref="SwitchState{T}"/>.
/// Case values are exposed boxed: the only consumers are cold paths (payload writing, Mermaid
/// labels).
/// </summary>
public interface ISwitchNode
{
    /// <summary>The tested key's registered name — the serialization identity.</summary>
    string KeyName { get; }

    /// <summary>The tested key's value type.</summary>
    Type ValueType { get; }

    /// <summary>The arm taken when no case matches (<see cref="NodeId.Default"/> = terminal).</summary>
    NodeId DefaultTarget { get; }

    /// <summary>The number of cases.</summary>
    int CaseCount { get; }

    /// <summary>The literal value of the case at <paramref name="index"/> (boxed).</summary>
    object? CaseValueAt(int index);

    /// <summary>The target of the case at <paramref name="index"/>.</summary>
    NodeId CaseTargetAt(int index);
}
