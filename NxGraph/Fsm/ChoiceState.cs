using NxGraph.Behaviors;
using NxGraph.Blackboards;
using NxGraph.Conditions;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// <b>Data-built</b> two-way branch (spec 023): the decision is a list of
/// <see cref="ICondition"/> data objects combined by a <see cref="ConditionMatch"/> mode, not
/// a closure — so a branching graph rides the serialization payload with zero options and
/// therefore survives suspend/resume. For a decision that is genuinely code, the delegate-backed
/// <see cref="RelayChoiceState"/> stays fully supported.
/// <para>
/// <see cref="ILogic.Execute"/> returns <see cref="Result.Success"/> — <b>a decision never
/// faults</b>. Selection evaluates the list against the machine-stamped blackboard context:
/// <see cref="ConditionMatch.All"/> walks until the first <see langword="false"/>,
/// <see cref="ConditionMatch.Any"/> until the first <see langword="true"/>; the outcome picks
/// <see cref="TrueTarget"/> or <see cref="FalseTarget"/>. Either arm may be
/// <see cref="NodeId.Default"/> (terminal exit).
/// </para>
/// <para>
/// One class implements both logic slots and both director interfaces (the <c>ForkState</c> /
/// <c>EventEntryState</c> shape), so a single instance authors either runtime and one wire
/// marker covers both. <see cref="IDirector.EnumerateStaticTargets"/> yields the true arm then
/// the false arm, so reachability validation and Mermaid export need no special casing.
/// </para>
/// <para>
/// The condition list is evaluated through a <see cref="BehaviorContext"/> whose report channel
/// is inert: a branch node's decision is side-effect free by contract, so nothing is routed to
/// the observer from here (<see cref="BehaviorContext.HasReporter"/> is
/// <see langword="false"/>). Selection is an array walk over one stack-allocated context — 0 B.
/// </para>
/// </summary>
public sealed class ChoiceState : ILogic, IAsyncLogic, IDirector, IAsyncDirector, IBlackboardSettable, IChoiceNode
{
    private readonly ICondition[] _conditions;
    private readonly ConditionMatch _match;
    private readonly NodeId _trueTarget;
    private readonly NodeId _falseTarget;
    private readonly NodeId[] _staticTargets;
    private BlackboardContext _blackboards;

    /// <param name="conditions">The conditions to evaluate, in order. At least one is
    /// required; null entries are rejected.</param>
    /// <param name="match">How the conditions combine.</param>
    /// <param name="trueTarget">The arm taken when the combined decision is true.</param>
    /// <param name="falseTarget">The arm taken when it is false.</param>
    public ChoiceState(IReadOnlyList<ICondition> conditions, ConditionMatch match, NodeId trueTarget,
        NodeId falseTarget)
    {
        _conditions = ConditionComposition.ValidateEntries(conditions, nameof(conditions));
        _match = match;
        _trueTarget = trueTarget;
        _falseTarget = falseTarget;
        _staticTargets = [trueTarget, falseTarget];
    }

    /// <summary>Creates a single-condition choice (<see cref="ConditionMatch.All"/> of one).</summary>
    public ChoiceState(ICondition condition, NodeId trueTarget, NodeId falseTarget)
        : this(new[] { condition }, ConditionMatch.All, trueTarget, falseTarget)
    {
    }

    /// <inheritdoc />
    public ConditionMatch Match => _match;

    /// <inheritdoc />
    public IReadOnlyList<ICondition> Conditions => _conditions;

    /// <inheritdoc />
    public NodeId TrueTarget => _trueTarget;

    /// <inheritdoc />
    public NodeId FalseTarget => _falseTarget;

    void IBlackboardSettable.SetBlackboards(in BlackboardContext context) => _blackboards = context;

    private NodeId SelectNextCore()
    {
        BehaviorContext ctx = new(in _blackboards, null);
        ICondition[] conditions = _conditions;
        if (_match == ConditionMatch.All)
        {
            for (int i = 0; i < conditions.Length; i++)
            {
                if (!conditions[i].Evaluate(in ctx))
                {
                    return _falseTarget;
                }
            }

            return _trueTarget;
        }

        for (int i = 0; i < conditions.Length; i++)
        {
            if (conditions[i].Evaluate(in ctx))
            {
                return _trueTarget;
            }
        }

        return _falseTarget;
    }

    /// <inheritdoc cref="IDirector.SelectNext" />
    public NodeId SelectNext() => SelectNextCore();

    Result ILogic.Execute() => Result.Success;

    ValueTask<Result> IAsyncLogic.ExecuteAsync(CancellationToken ct) => ResultHelpers.Success;

    ValueTask<NodeId> IAsyncDirector.SelectNextAsync(CancellationToken ct) => new(SelectNextCore());

    IEnumerable<NodeId> IDirector.EnumerateStaticTargets() => _staticTargets;

    IEnumerable<NodeId> IAsyncDirector.EnumerateStaticTargets() => _staticTargets;
}
