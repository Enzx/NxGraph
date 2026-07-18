using NxGraph.Blackboards;

namespace NxGraph.Behaviors;

/// <summary>
/// Standard composite behavior: runs an inner behavior list a <b>bounded, fixed</b> number of
/// times inside a single node execution — the one control-flow behavior, a leaf-level For.
/// The trip count is a <see cref="BlackboardValue{T}"/> resolved through the context
/// <b>once at entry</b>, fixing the iteration count for that execution — the body cannot
/// extend its own loop by writing the bound key mid-run. A <b>literal</b> negative count is
/// rejected at construction (an authoring error, caught at wiring time); a <b>key-bound</b>
/// count resolving to zero or less runs nothing and returns <c>Success</c> (vacuous — runtime
/// data must not throw). Anything condition-driven (<c>While</c>, guards, branches) belongs
/// in director nodes, where validators, Mermaid export, and snapshots can see it.
/// <para>
/// Each iteration writes the optional 0-based index key (any scope binds; Node scratch is the
/// natural home) and walks the body in order, fail-fast: the first non-<c>Success</c> entry
/// stops the body <b>and</b> the remaining iterations, and the behavior returns
/// <c>Failure</c>. The composite's context passes through unchanged, so nested
/// <see cref="BehaviorContext.Report"/> calls route to the owning node's report channel. The
/// node keeps the whole fault model: <c>.Retry(...)</c> re-runs the node's whole entry list,
/// meaning <b>all</b> iterations re-execute — the idempotency note on <see cref="IBehavior"/>
/// compounds with the trip count. One class implements both behavior interfaces, so a single
/// instance authors either runtime: the async path observes cancellation between iterations,
/// while the sync path cannot (the sync runtime has no token — existing contract); the fixed
/// entry-resolved trip count is the deliberate mitigation.
/// </para>
/// </summary>
public sealed class Repeat : IBehavior, IAsyncBehavior
{
    private readonly IBehavior[] _body;
    private readonly BlackboardKey<int> _indexKey;
    private readonly string? _indexKeyName;

    /// <summary>Creates a repeat of <paramref name="body"/>, <paramref name="count"/> (literal or key-bound) times.</summary>
    public Repeat(BlackboardValue<int> count, params IBehavior[] body)
        : this(count, default, indexKeyName: null, body)
    {
    }

    /// <summary>
    /// Creates a repeat that writes the 0-based iteration to <paramref name="indexKey"/>
    /// before each iteration. Any key scope binds — Node scratch is the natural home, since
    /// the index is meaningful within one visit only.
    /// </summary>
    public Repeat(BlackboardValue<int> count, BlackboardKey<int> indexKey, params IBehavior[] body)
        : this(count, RepeatComposition.ValidateIndexKey(in indexKey), indexKey.Name, body)
    {
    }

    private Repeat(BlackboardValue<int> count, BlackboardKey<int> indexKey, string? indexKeyName, IBehavior[] body)
    {
        Count = RepeatComposition.ValidateCount(in count);
        _indexKey = indexKey;
        _indexKeyName = indexKeyName;
        _body = BehaviorComposition.ValidateEntries(body);
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] is IAgentBehavior)
            {
                throw BehaviorComposition.AgentEntryInUntyped(body[i], i, "Repeat<TAgent>");
            }
        }
    }

    /// <summary>
    /// Creates a name-bound repeat — the deserialization rebind form. The optional index key
    /// resolves per execution against the machine's bound boards' schemas (Graph, then
    /// Global, then Node), with targeted miss/type-mismatch errors — exactly
    /// <see cref="SetValue{T}.Unbound"/>; <see langword="null"/> means no index key.
    /// </summary>
    public static Repeat Unbound(BlackboardValue<int> count, string? indexKeyName, params IBehavior[] body) =>
        new(count, default, RepeatComposition.ValidateIndexKeyName(indexKeyName), body);

    /// <summary>The trip-count binding — literal or key; resolved once at entry.</summary>
    public BlackboardValue<int> Count { get; }

    /// <summary>The index key's registered name, or <see langword="null"/> when no index key — the serialization identity.</summary>
    public string? IndexKeyName => _indexKeyName;

    /// <summary>The body entries, walked in order each iteration — inspectable, editor-facing.</summary>
    public IReadOnlyList<IBehavior> Body => _body;

    /// <inheritdoc />
    public Result Execute(in BehaviorContext ctx)
    {
        int count = ctx.Resolve(Count); // Resolved once at entry — the trip count is fixed.
        IBehavior[] body = _body;
        for (int iteration = 0; iteration < count; iteration++)
        {
            RepeatComposition.WriteIndex(ctx.Bb, _indexKey, _indexKeyName, iteration);
            for (int i = 0; i < body.Length; i++)
            {
                if (body[i].Execute(in ctx) != Result.Success)
                {
                    return Result.Failure;
                }
            }
        }

        return Result.Success;
    }

    /// <inheritdoc />
    public ValueTask<Result> ExecuteAsync(BehaviorContext ctx, CancellationToken ct)
    {
        int count = ctx.Resolve(Count);
        IBehavior[] body = _body;
        for (int iteration = 0; iteration < count; iteration++)
        {
            // Between iterations — a large key-bound count cannot pin a cancelled machine.
            ct.ThrowIfCancellationRequested();
            RepeatComposition.WriteIndex(ctx.Bb, _indexKey, _indexKeyName, iteration);
            for (int i = 0; i < body.Length; i++)
            {
                if (body[i].Execute(in ctx) != Result.Success)
                {
                    return ResultHelpers.Failure;
                }
            }
        }

        return ResultHelpers.Success;
    }
}

/// <summary>
/// Agent-typed twin of <see cref="Repeat"/>: takes a mixed body (plain and agent-typed sync
/// entries), pre-splits typed dispatch at construction (the
/// <see cref="BehaviorState{TAgent}"/> pattern), and passes the agent received per execution
/// to typed body entries <b>per iteration</b> as a call parameter — never stamped, so
/// instances stay shareable data objects. Plain entries run agent-blind in the same
/// sequence; an <see cref="IAgentBehavior"/> entry of a different agent type is rejected at
/// wiring time naming both types. All other semantics match the untyped form: the count is
/// resolved <b>once at entry</b> (literal negative rejected at construction, key-bound ≤ 0
/// vacuous <c>Success</c>), iterations are fail-fast, a node <c>.Retry(...)</c> re-runs all
/// iterations, and only the async path observes cancellation between iterations —
/// condition-driven loops belong in director nodes.
/// </summary>
/// <typeparam name="TAgent">The agent type delivered to typed body entries.</typeparam>
public sealed class Repeat<TAgent> : IBehavior<TAgent>, IAsyncBehavior<TAgent>
{
    private readonly IBehavior[] _body;
    private readonly IBehavior<TAgent>?[] _typed;
    private readonly BlackboardKey<int> _indexKey;
    private readonly string? _indexKeyName;

    /// <summary>Creates a repeat of <paramref name="body"/>, <paramref name="count"/> (literal or key-bound) times.</summary>
    public Repeat(BlackboardValue<int> count, params IBehavior[] body)
        : this(count, default, indexKeyName: null, body)
    {
    }

    /// <summary>
    /// Creates a repeat that writes the 0-based iteration to <paramref name="indexKey"/>
    /// before each iteration. Any key scope binds — Node scratch is the natural home.
    /// </summary>
    public Repeat(BlackboardValue<int> count, BlackboardKey<int> indexKey, params IBehavior[] body)
        : this(count, RepeatComposition.ValidateIndexKey(in indexKey), indexKey.Name, body)
    {
    }

    private Repeat(BlackboardValue<int> count, BlackboardKey<int> indexKey, string? indexKeyName, IBehavior[] body)
    {
        Count = RepeatComposition.ValidateCount(in count);
        _indexKey = indexKey;
        _indexKeyName = indexKeyName;
        _body = BehaviorComposition.ValidateEntries(body);
        _typed = new IBehavior<TAgent>?[body.Length];
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] is not IAgentBehavior)
            {
                continue;
            }

            _typed[i] = body[i] as IBehavior<TAgent> ?? throw BehaviorComposition.AgentTypeMismatch(
                body[i], i, typeof(TAgent), typeof(IBehavior<>));
        }
    }

    /// <summary>
    /// Creates a name-bound repeat — the deserialization rebind form (see
    /// <see cref="Repeat.Unbound"/>); <see langword="null"/> means no index key.
    /// </summary>
    public static Repeat<TAgent> Unbound(BlackboardValue<int> count, string? indexKeyName,
        params IBehavior[] body) =>
        new(count, default, RepeatComposition.ValidateIndexKeyName(indexKeyName), body);

    /// <summary>The trip-count binding — literal or key; resolved once at entry.</summary>
    public BlackboardValue<int> Count { get; }

    /// <summary>The index key's registered name, or <see langword="null"/> when no index key — the serialization identity.</summary>
    public string? IndexKeyName => _indexKeyName;

    /// <summary>The body entries, walked in order each iteration — inspectable, editor-facing.</summary>
    public IReadOnlyList<IBehavior> Body => _body;

    /// <inheritdoc />
    public Result Execute(TAgent agent, in BehaviorContext ctx)
    {
        int count = ctx.Resolve(Count); // Resolved once at entry — the trip count is fixed.
        IBehavior[] body = _body;
        IBehavior<TAgent>?[] typed = _typed;
        for (int iteration = 0; iteration < count; iteration++)
        {
            RepeatComposition.WriteIndex(ctx.Bb, _indexKey, _indexKeyName, iteration);
            for (int i = 0; i < body.Length; i++)
            {
                Result result = typed[i] is { } agentBehavior
                    ? agentBehavior.Execute(agent, in ctx)
                    : body[i].Execute(in ctx);
                if (result != Result.Success)
                {
                    return Result.Failure;
                }
            }
        }

        return Result.Success;
    }

    /// <inheritdoc />
    public ValueTask<Result> ExecuteAsync(TAgent agent, BehaviorContext ctx, CancellationToken ct)
    {
        int count = ctx.Resolve(Count);
        IBehavior[] body = _body;
        IBehavior<TAgent>?[] typed = _typed;
        for (int iteration = 0; iteration < count; iteration++)
        {
            // Between iterations — a large key-bound count cannot pin a cancelled machine.
            ct.ThrowIfCancellationRequested();
            RepeatComposition.WriteIndex(ctx.Bb, _indexKey, _indexKeyName, iteration);
            for (int i = 0; i < body.Length; i++)
            {
                Result result = typed[i] is { } agentBehavior
                    ? agentBehavior.Execute(agent, in ctx)
                    : body[i].Execute(in ctx);
                if (result != Result.Success)
                {
                    return ResultHelpers.Failure;
                }
            }
        }

        return ResultHelpers.Success;
    }
}

/// <summary>
/// Shared count/index-key validation and the per-iteration index write for the four repeat
/// forms.
/// </summary>
internal static class RepeatComposition
{
    internal static BlackboardValue<int> ValidateCount(in BlackboardValue<int> count)
    {
        if (!count.IsBound && count.Literal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count.Literal,
                "A literal repeat count cannot be negative — an authoring error, caught at wiring time. " +
                "(A key-bound count resolving to zero or less runs nothing and succeeds.)");
        }

        return count;
    }

    internal static BlackboardKey<int> ValidateIndexKey(in BlackboardKey<int> indexKey)
    {
        if (!indexKey.IsValid)
        {
            throw new ArgumentException(
                "Invalid blackboard key — obtain keys via BlackboardSchema.Register<T>(...).", nameof(indexKey));
        }

        return indexKey;
    }

    internal static string? ValidateIndexKeyName(string? indexKeyName)
    {
        if (indexKeyName is { Length: 0 })
        {
            throw new ArgumentException("Index key name cannot be empty.", nameof(indexKeyName));
        }

        return indexKeyName;
    }

    /// <summary>
    /// Writes the 0-based iteration to the index key: the live key for authored instances,
    /// the name resolved per execution for deserialized ones (the <see cref="SetValue{T}"/>
    /// recipe); a no-op with no key — zero cost.
    /// </summary>
    internal static void WriteIndex(in BlackboardContext bb, in BlackboardKey<int> indexKey, string? indexKeyName,
        int iteration)
    {
        if (indexKeyName is null)
        {
            return;
        }

        if (indexKey.IsValid)
        {
            bb.Set(indexKey, iteration);
            return;
        }

        bb.Set(BehaviorKeyResolver.Resolve<int>(in bb, indexKeyName), iteration);
    }
}
