using NxGraph.Behaviors;
using NxGraph.Blackboards;
using NxGraph.Graphs;

namespace NxGraph.Fsm;

/// <summary>
/// One arm of a data-built <see cref="SwitchState{T}"/>: a <b>literal</b> value and the node
/// it routes to. Case values are deliberately never bindings — a key-bound case value would
/// make distinctness undecidable at construction, and the switch's whole contract is that at
/// most one case can match.
/// </summary>
public readonly record struct SwitchCase<T>(T Value, NodeId Target);

/// <summary>
/// <b>Data-built</b> multi-way branch (spec 023): reads one blackboard key and routes to the
/// case whose literal value it equals, else to <see cref="DefaultTarget"/>. Nothing about the
/// decision is code, so a switching graph rides the serialization payload and survives
/// suspend/resume. For a selector that is genuinely code, use the delegate-backed
/// <see cref="RelaySwitchState{TKey}"/>.
/// <para>
/// <b>Exactly one case can match, and the data enforces it</b>: case values are literals and
/// the constructor rejects duplicates by <see cref="EqualityComparer{T}.Default"/>, naming the
/// offending value. Deserialization reconstructs through the public constructor, so the same
/// guard covers rebuilt graphs — there is no second code path to keep in step.
/// </para>
/// <para>
/// <b>A switch is a lookup and carries no order.</b> Ordered, first-match-wins semantics —
/// where an earlier arm may shadow a later one, or where different arms test different keys —
/// are a <b>chain of <see cref="ChoiceState"/>s</b>, which is what if/else-if is. Lower to that
/// in the host; the library ships the two shapes every language has and does not grow a third
/// branching primitive to hold an ordered rule table.
/// </para>
/// <para>
/// One class implements both logic slots and both director interfaces, so a single instance
/// authors either runtime. <see cref="ILogic.Execute"/> returns <see cref="Result.Success"/> —
/// a decision never faults. Selection is one typed <c>Get</c> plus a linear scan over one
/// stack-allocated context — 0 B.
/// </para>
/// </summary>
/// <typeparam name="T">The tested key's value type.</typeparam>
public sealed class SwitchState<T> : ILogic, IAsyncLogic, IDirector, IAsyncDirector, IBlackboardSettable, ISwitchNode
{
    private readonly BlackboardKey<T> _key;
    private readonly string _keyName;
    private readonly SwitchCase<T>[] _cases;
    private readonly NodeId _defaultTarget;
    private readonly NodeId[] _staticTargets;
    private BlackboardContext _blackboards;

    /// <param name="key">The blackboard key whose value the switch tests.</param>
    /// <param name="cases">The arms, in authoring order (order is presentation only — at most
    /// one can match). At least one is required; duplicate values are rejected.</param>
    /// <param name="defaultTarget">The arm taken when no case matches; pass
    /// <see cref="NodeId.Default"/> for a terminal exit (the validator warns about it).</param>
    public SwitchState(BlackboardKey<T> key, IReadOnlyList<SwitchCase<T>> cases, NodeId defaultTarget)
        : this(ValidatedName(key), key, cases, defaultTarget)
    {
    }

    private SwitchState(string keyName, BlackboardKey<T> key, IReadOnlyList<SwitchCase<T>> cases,
        NodeId defaultTarget)
    {
        _key = key;
        _keyName = keyName;
        _cases = ValidateCases(cases);
        _defaultTarget = defaultTarget;

        NodeId[] targets = new NodeId[_cases.Length + 1];
        for (int i = 0; i < _cases.Length; i++)
        {
            targets[i] = _cases[i].Target;
        }

        targets[_cases.Length] = defaultTarget;
        _staticTargets = targets;
    }

    /// <summary>
    /// Creates a name-bound switch — the deserialization rebind form. The tested key resolves
    /// per selection against the machine's bound boards' schemas (Graph, then Global, then
    /// Node), with targeted miss/type-mismatch errors.
    /// </summary>
    public static SwitchState<T> Unbound(string keyName, IReadOnlyList<SwitchCase<T>> cases, NodeId defaultTarget)
    {
        if (string.IsNullOrEmpty(keyName))
        {
            throw new ArgumentException("Key name cannot be null or empty.", nameof(keyName));
        }

        return new SwitchState<T>(keyName, default, cases, defaultTarget);
    }

    /// <summary>The live tested key; default (invalid) for name-bound instances.</summary>
    public BlackboardKey<T> Key => _key;

    /// <inheritdoc />
    public string KeyName => _keyName;

    /// <summary>The arms, in authoring order.</summary>
    public IReadOnlyList<SwitchCase<T>> Cases => _cases;

    /// <inheritdoc />
    public NodeId DefaultTarget => _defaultTarget;

    Type ISwitchNode.ValueType => typeof(T);

    int ISwitchNode.CaseCount => _cases.Length;

    object? ISwitchNode.CaseValueAt(int index) => _cases[index].Value;

    NodeId ISwitchNode.CaseTargetAt(int index) => _cases[index].Target;

    void IBlackboardSettable.SetBlackboards(in BlackboardContext context) => _blackboards = context;

    private static string ValidatedName(in BlackboardKey<T> key)
    {
        if (!key.IsValid)
        {
            throw new ArgumentException(
                "Invalid blackboard key — obtain keys via BlackboardSchema.Register<T>(...).", nameof(key));
        }

        return key.Name;
    }

    private static SwitchCase<T>[] ValidateCases(IReadOnlyList<SwitchCase<T>> cases)
    {
        if (cases is null || cases.Count == 0)
        {
            throw new ArgumentException(
                "A switch needs at least one case — route unmatched values through the default target.",
                nameof(cases));
        }

        SwitchCase<T>[] copy = new SwitchCase<T>[cases.Count];
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = cases[i];
            for (int j = 0; j < i; j++)
            {
                if (comparer.Equals(copy[j].Value, copy[i].Value))
                {
                    throw new ArgumentException(
                        $"Case value '{copy[i].Value?.ToString() ?? "<null>"}' is declared twice (arms {j} and " +
                        $"{i}) — a switch is a lookup, so at most one case may match. Ordered, " +
                        "first-match-wins rules are a chain of ChoiceStates.", nameof(cases));
                }
            }
        }

        return copy;
    }

    private NodeId SelectNextCore()
    {
        BlackboardContext bb = _blackboards;
        T value = _key.IsValid ? bb.Get(_key) : bb.Get(BehaviorKeyResolver.Resolve<T>(in bb, _keyName));

        SwitchCase<T>[] cases = _cases;
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < cases.Length; i++)
        {
            if (comparer.Equals(cases[i].Value, value))
            {
                return cases[i].Target;
            }
        }

        return _defaultTarget;
    }

    /// <inheritdoc cref="IDirector.SelectNext" />
    public NodeId SelectNext() => SelectNextCore();

    Result ILogic.Execute() => Result.Success;

    ValueTask<Result> IAsyncLogic.ExecuteAsync(CancellationToken ct) => ResultHelpers.Success;

    ValueTask<NodeId> IAsyncDirector.SelectNextAsync(CancellationToken ct) => new(SelectNextCore());

    IEnumerable<NodeId> IDirector.EnumerateStaticTargets() => _staticTargets;

    IEnumerable<NodeId> IAsyncDirector.EnumerateStaticTargets() => _staticTargets;
}
