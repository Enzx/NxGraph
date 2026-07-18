using NxGraph.Blackboards;

namespace NxGraph.Behaviors;

/// <summary>
/// Async twin of <see cref="Repeat"/>: runs an <see cref="IAsyncBehavior"/> body a
/// <b>bounded, fixed</b> number of times inside a single node execution, async runtime only
/// (a sync <c>Execute</c> cannot await a body entry — a sync <see cref="Repeat"/> authors
/// either runtime). Same contract as the sync form: the count is resolved through the context
/// <b>once at entry</b> (a literal negative count is rejected at construction; a key-bound
/// count resolving to zero or less is vacuous <c>Success</c>), the optional 0-based index key
/// is written before each iteration, iterations are fail-fast, the context passes through
/// unchanged, and a node <c>.Retry(...)</c> re-runs <b>all</b> iterations — the idempotency
/// note on <see cref="IAsyncBehavior"/> compounds with the trip count. Cancellation is
/// observed between iterations, so a large key-bound count cannot pin a cancelled machine.
/// Condition-driven loops belong in director nodes, where validators, Mermaid export, and
/// snapshots can see them.
/// </summary>
public sealed class AsyncRepeat : IAsyncBehavior
{
    private readonly IAsyncBehavior[] _body;
    private readonly BlackboardKey<int> _indexKey;
    private readonly string? _indexKeyName;

    /// <summary>Creates a repeat of <paramref name="body"/>, <paramref name="count"/> (literal or key-bound) times.</summary>
    public AsyncRepeat(BlackboardValue<int> count, params IAsyncBehavior[] body)
        : this(count, default, indexKeyName: null, body)
    {
    }

    /// <summary>
    /// Creates a repeat that writes the 0-based iteration to <paramref name="indexKey"/>
    /// before each iteration. Any key scope binds — Node scratch is the natural home.
    /// </summary>
    public AsyncRepeat(BlackboardValue<int> count, BlackboardKey<int> indexKey, params IAsyncBehavior[] body)
        : this(count, RepeatComposition.ValidateIndexKey(in indexKey), indexKey.Name, body)
    {
    }

    private AsyncRepeat(BlackboardValue<int> count, BlackboardKey<int> indexKey, string? indexKeyName,
        IAsyncBehavior[] body)
    {
        Count = RepeatComposition.ValidateCount(in count);
        _indexKey = indexKey;
        _indexKeyName = indexKeyName;
        _body = BehaviorComposition.ValidateEntries(body);
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] is IAsyncAgentBehavior)
            {
                throw BehaviorComposition.AgentEntryInUntyped(body[i], i, "AsyncRepeat<TAgent>");
            }
        }
    }

    /// <summary>
    /// Creates a name-bound repeat — the deserialization rebind form (see
    /// <see cref="Repeat.Unbound"/>); <see langword="null"/> means no index key.
    /// </summary>
    public static AsyncRepeat Unbound(BlackboardValue<int> count, string? indexKeyName,
        params IAsyncBehavior[] body) =>
        new(count, default, RepeatComposition.ValidateIndexKeyName(indexKeyName), body);

    /// <summary>The trip-count binding — literal or key; resolved once at entry.</summary>
    public BlackboardValue<int> Count { get; }

    /// <summary>The index key's registered name, or <see langword="null"/> when no index key — the serialization identity.</summary>
    public string? IndexKeyName => _indexKeyName;

    /// <summary>The body entries, walked in order each iteration — inspectable, editor-facing.</summary>
    public IReadOnlyList<IAsyncBehavior> Body => _body;

    /// <inheritdoc />
    public async ValueTask<Result> ExecuteAsync(BehaviorContext ctx, CancellationToken ct)
    {
        int count = ctx.Resolve(Count); // Resolved once at entry — the trip count is fixed.
        IAsyncBehavior[] body = _body;
        for (int iteration = 0; iteration < count; iteration++)
        {
            // Between iterations — a large key-bound count cannot pin a cancelled machine.
            ct.ThrowIfCancellationRequested();
            RepeatComposition.WriteIndex(ctx.Bb, _indexKey, _indexKeyName, iteration);
            for (int i = 0; i < body.Length; i++)
            {
                Result result = await body[i].ExecuteAsync(ctx, ct).ConfigureAwait(false);
                if (result != Result.Success)
                {
                    return Result.Failure;
                }
            }
        }

        return Result.Success;
    }
}

/// <summary>
/// Agent-typed twin of <see cref="AsyncRepeat"/> — see <see cref="Repeat{TAgent}"/> for the
/// full agent contract: a mixed body (plain and agent-typed async entries), typed dispatch
/// pre-split at construction, the agent received per execution passed to typed body entries
/// per iteration as a call parameter (never stamped), plain entries agent-blind, wrong-agent
/// entries rejected at wiring time naming both types. Count resolved once at entry (literal
/// negative rejected, key-bound ≤ 0 vacuous), fail-fast iterations, compounded retry
/// idempotency, cancellation observed between iterations.
/// </summary>
/// <typeparam name="TAgent">The agent type delivered to typed body entries.</typeparam>
public sealed class AsyncRepeat<TAgent> : IAsyncBehavior<TAgent>
{
    private readonly IAsyncBehavior[] _body;
    private readonly IAsyncBehavior<TAgent>?[] _typed;
    private readonly BlackboardKey<int> _indexKey;
    private readonly string? _indexKeyName;

    /// <summary>Creates a repeat of <paramref name="body"/>, <paramref name="count"/> (literal or key-bound) times.</summary>
    public AsyncRepeat(BlackboardValue<int> count, params IAsyncBehavior[] body)
        : this(count, default, indexKeyName: null, body)
    {
    }

    /// <summary>
    /// Creates a repeat that writes the 0-based iteration to <paramref name="indexKey"/>
    /// before each iteration. Any key scope binds — Node scratch is the natural home.
    /// </summary>
    public AsyncRepeat(BlackboardValue<int> count, BlackboardKey<int> indexKey, params IAsyncBehavior[] body)
        : this(count, RepeatComposition.ValidateIndexKey(in indexKey), indexKey.Name, body)
    {
    }

    private AsyncRepeat(BlackboardValue<int> count, BlackboardKey<int> indexKey, string? indexKeyName,
        IAsyncBehavior[] body)
    {
        Count = RepeatComposition.ValidateCount(in count);
        _indexKey = indexKey;
        _indexKeyName = indexKeyName;
        _body = BehaviorComposition.ValidateEntries(body);
        _typed = new IAsyncBehavior<TAgent>?[body.Length];
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] is not IAsyncAgentBehavior)
            {
                continue;
            }

            _typed[i] = body[i] as IAsyncBehavior<TAgent> ?? throw BehaviorComposition.AgentTypeMismatch(
                body[i], i, typeof(TAgent), typeof(IAsyncBehavior<>));
        }
    }

    /// <summary>
    /// Creates a name-bound repeat — the deserialization rebind form (see
    /// <see cref="Repeat.Unbound"/>); <see langword="null"/> means no index key.
    /// </summary>
    public static AsyncRepeat<TAgent> Unbound(BlackboardValue<int> count, string? indexKeyName,
        params IAsyncBehavior[] body) =>
        new(count, default, RepeatComposition.ValidateIndexKeyName(indexKeyName), body);

    /// <summary>The trip-count binding — literal or key; resolved once at entry.</summary>
    public BlackboardValue<int> Count { get; }

    /// <summary>The index key's registered name, or <see langword="null"/> when no index key — the serialization identity.</summary>
    public string? IndexKeyName => _indexKeyName;

    /// <summary>The body entries, walked in order each iteration — inspectable, editor-facing.</summary>
    public IReadOnlyList<IAsyncBehavior> Body => _body;

    /// <inheritdoc />
    public async ValueTask<Result> ExecuteAsync(TAgent agent, BehaviorContext ctx, CancellationToken ct)
    {
        int count = ctx.Resolve(Count); // Resolved once at entry — the trip count is fixed.
        IAsyncBehavior[] body = _body;
        IAsyncBehavior<TAgent>?[] typed = _typed;
        for (int iteration = 0; iteration < count; iteration++)
        {
            // Between iterations — a large key-bound count cannot pin a cancelled machine.
            ct.ThrowIfCancellationRequested();
            RepeatComposition.WriteIndex(ctx.Bb, _indexKey, _indexKeyName, iteration);
            for (int i = 0; i < body.Length; i++)
            {
                Result result = typed[i] is { } agentBehavior
                    ? await agentBehavior.ExecuteAsync(agent, ctx, ct).ConfigureAwait(false)
                    : await body[i].ExecuteAsync(ctx, ct).ConfigureAwait(false);
                if (result != Result.Success)
                {
                    return Result.Failure;
                }
            }
        }

        return Result.Success;
    }
}
