using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tokens;

/// <summary>
/// Structural fan-out node for the token runtimes: a token passing through a fork continues
/// to <see cref="Branches"/>[0] and one new token is spawned per remaining branch. Forks carry
/// no user logic and their <see cref="Transition"/> slot must stay empty — the branches replace
/// the success edge (the DSL enforces this; the validator lints it).
/// <para>
/// Fork nodes are interpreted <b>only</b> by <see cref="TokenMachine"/> /
/// <see cref="AsyncTokenMachine"/>. The FSM runtimes cannot express multiple active nodes, so
/// executing a fork under them throws instead of silently misrouting. The
/// <see cref="IDirector"/>/<see cref="IAsyncDirector"/> implementations exist solely to surface
/// the branches to reachability validation and Mermaid export via
/// <c>EnumerateStaticTargets()</c> — selection likewise throws.
/// </para>
/// </summary>
public sealed class ForkState : ILogic, IAsyncLogic, IDirector, IAsyncDirector
{
    private readonly NodeId[] _branches;

    public ForkState(params NodeId[] branches)
    {
        if (branches is null || branches.Length == 0)
        {
            throw new ArgumentException("A fork must declare at least one branch.", nameof(branches));
        }

        foreach (NodeId branch in branches)
        {
            if (branch == NodeId.Default)
            {
                throw new ArgumentException("Fork branches cannot be NodeId.Default.", nameof(branches));
            }
        }

        _branches = (NodeId[])branches.Clone();
    }

    /// <summary>The branch heads, in declaration order. Branch 0 continues the arriving token.</summary>
    public IReadOnlyList<NodeId> Branches => _branches;

    internal int BranchCount => _branches.Length;

    internal NodeId BranchAt(int index) => _branches[index];

    Result ILogic.Execute() => throw TokenNodeMisuse.ForFork();

    ValueTask<Result> IAsyncLogic.ExecuteAsync(CancellationToken ct) => throw TokenNodeMisuse.ForFork();

    NodeId IDirector.SelectNext() => throw TokenNodeMisuse.ForFork();

    ValueTask<NodeId> IAsyncDirector.SelectNextAsync(CancellationToken ct) => throw TokenNodeMisuse.ForFork();

    IEnumerable<NodeId> IDirector.EnumerateStaticTargets() => _branches;

    IEnumerable<NodeId> IAsyncDirector.EnumerateStaticTargets() => _branches;
}

/// <summary>Shared throw helpers for token-only structural nodes run under the wrong runtime.</summary>
internal static class TokenNodeMisuse
{
    internal static NotSupportedException ForFork() => new(
        "ForkState is a token-runtime structural node and cannot be executed by the FSM runtimes. " +
        "Run this graph with TokenMachine or AsyncTokenMachine (NxGraph.Tokens).");

    internal static NotSupportedException ForJoin() => new(
        "JoinState is a token-runtime structural node and cannot be executed by the FSM runtimes. " +
        "Run this graph with TokenMachine or AsyncTokenMachine (NxGraph.Tokens).");
}
