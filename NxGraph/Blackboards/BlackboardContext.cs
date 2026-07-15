using System.Runtime.CompilerServices;
using NxGraph.Compatibility;

namespace NxGraph.Blackboards;

/// <summary>
/// The per-machine set of scope-bound boards, stamped onto nodes as one unit.
/// <see cref="Get{T}"/>/<see cref="Set{T}"/> route by the key's schema scope in O(1) —
/// no fall-through chain, no allocation.
/// <para>
/// A default-constructed context is valid and empty: any key access throws a precise
/// "no board bound for scope" error, so consumers never need their own null guards.
/// </para>
/// </summary>
public readonly struct BlackboardContext
{
    private readonly Blackboard? _global;
    private readonly Blackboard? _graph;
    private readonly Blackboard? _node;

    /// <summary>
    /// Creates a context from explicit per-scope boards. Each board's schema scope must
    /// match the slot it is passed for. The <paramref name="node"/> slot is machine-owned
    /// transient scratch — user code composes contexts through the machines, never directly.
    /// </summary>
    public BlackboardContext(Blackboard? global, Blackboard? graph, Blackboard? node = null)
    {
        if (global is not null && global.Schema.Scope != BlackboardScope.Global)
        {
            throw new ArgumentException(
                $"Board over schema '{global.Schema.Name ?? "<unnamed>"}' is {global.Schema.Scope}-scoped " +
                "and cannot occupy the Global slot.", nameof(global));
        }

        if (graph is not null && graph.Schema.Scope != BlackboardScope.Graph)
        {
            throw new ArgumentException(
                $"Board over schema '{graph.Schema.Name ?? "<unnamed>"}' is {graph.Schema.Scope}-scoped " +
                "and cannot occupy the Graph slot.", nameof(graph));
        }

        if (node is not null && node.Schema.Scope != BlackboardScope.Node)
        {
            throw new ArgumentException(
                $"Board over schema '{node.Schema.Name ?? "<unnamed>"}' is {node.Schema.Scope}-scoped " +
                "and cannot occupy the Node slot.", nameof(node));
        }

        _global = global;
        _graph = graph;
        _node = node;
    }

    /// <summary><see langword="true"/> when no board is bound for any scope.</summary>
    public bool IsEmpty => _global is null && _graph is null && _node is null;

    /// <summary>The Global-scoped board, or <see langword="null"/> when unbound. Escape hatch for bulk ops and serialization.</summary>
    public Blackboard? Global => _global;

    /// <summary>The Graph-scoped board, or <see langword="null"/> when unbound. Escape hatch for bulk ops and serialization.</summary>
    public Blackboard? Graph => _graph;

    /// <summary>
    /// The machine-owned Node-scoped board, or <see langword="null"/> when the graph declares
    /// no Node schema. Transient per-visit scratch — not durable, never user-bound.
    /// </summary>
    public Blackboard? Node => _node;

    /// <summary>The board bound for <paramref name="scope"/>, or <see langword="null"/> when unbound.</summary>
    public Blackboard? Board(BlackboardScope scope) => scope switch
    {
        BlackboardScope.Global => _global,
        BlackboardScope.Graph => _graph,
        BlackboardScope.Node => _node,
        _ => null,
    };

    /// <summary><see langword="true"/> when a board is bound for <paramref name="scope"/>.</summary>
    public bool HasBoard(BlackboardScope scope) => Board(scope) is not null;

    /// <summary>
    /// Returns a copy with <paramref name="board"/> occupying its schema's scope slot
    /// (replace semantics — rebinding a scope swaps the board).
    /// </summary>
    public BlackboardContext With(Blackboard board)
    {
        Guard.NotNull(board, nameof(board));
        return board.Schema.Scope switch
        {
            BlackboardScope.Global => new BlackboardContext(board, _graph, _node),
            BlackboardScope.Graph => new BlackboardContext(_global, board, _node),
            _ => new BlackboardContext(_global, _graph, board),
        };
    }

    /// <summary>
    /// Returns a copy with the machine-owned Node slot cleared. Machines drop a parent's Node
    /// board when receiving a context through the stamping walk — each machine composes its own.
    /// </summary>
    public BlackboardContext WithoutNode() => _node is null ? this : new BlackboardContext(_global, _graph);

    /// <summary>Reads <paramref name="key"/> from the board its schema scope routes to.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Get<T>(BlackboardKey<T> key) => Route(key).Get(key);

    /// <summary>Writes <paramref name="key"/> on the board its schema scope routes to.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set<T>(BlackboardKey<T> key, T value) => Route(key).Set(key, value);

    /// <summary>Returns a reference into the routed board's slot array for in-place struct mutation.</summary>
    public ref T GetRef<T>(BlackboardKey<T> key) => ref Route(key).GetRef(key);

    /// <summary>
    /// Reads <paramref name="key"/> from its routed board; returns <see langword="false"/>
    /// (instead of throwing) when the scope is unbound or the key is invalid/foreign.
    /// </summary>
    public bool TryGet<T>(BlackboardKey<T> key, out T value)
    {
        if (key.Schema is not null &&
            Board(key.Schema.Scope) is { } board)
        {
            return board.TryGet(key, out value);
        }

        value = default!;
        return false;
    }

    // ── Routing ─────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Blackboard Route<T>(in BlackboardKey<T> key)
    {
        if (key.Schema is null)
        {
            ThrowUninitializedKey();
        }

        BlackboardScope scope = key.Schema!.Scope;
        Blackboard? board = scope switch
        {
            BlackboardScope.Global => _global,
            BlackboardScope.Graph => _graph,
            _ => _node,
        };
        if (board is null)
        {
            ThrowUnboundScope(scope, key.Name);
        }

        return board!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowUninitializedKey() =>
        throw new InvalidOperationException(
            "Uninitialized blackboard key — obtain keys via BlackboardSchema.Register<T>(...).");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowUnboundScope(BlackboardScope scope, string keyName)
    {
        if (scope == BlackboardScope.Node)
        {
            throw new InvalidOperationException(
                $"node key '{keyName}' used but no node blackboard — declare a Node-scoped schema " +
                "via WithSchema(...) on the graph; the machine creates its own board from it.");
        }

        string scopeWord = scope == BlackboardScope.Global ? "global" : "graph";
        throw new InvalidOperationException(
            $"{scopeWord} key '{keyName}' used but no {scopeWord} blackboard bound — " +
            "bind one via WithBlackboard(...).");
    }
}
