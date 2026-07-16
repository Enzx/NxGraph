using NxGraph.Graphs;

namespace NxGraph.Tokens;

/// <summary>
/// Structural merge node for the token runtimes: arriving tokens are parked until the
/// <see cref="Policy"/>'s requirement is met, then the join <b>fires</b> — the firing token
/// continues along the join's ordinary success edge, the other consumed tokens retire, and the
/// join re-arms for the next batch. <see cref="JoinPolicy.Any"/> makes the join a mid-graph
/// merge point (every arrival passes straight through).
/// <para>
/// Join nodes are interpreted <b>only</b> by <see cref="TokenMachine"/> /
/// <see cref="AsyncTokenMachine"/>; executing one under the FSM runtimes throws. Convergence
/// is authored by routing several chains to the <i>same</i> <see cref="JoinState"/> instance
/// (<c>.To(join)</c> dedupes by reference into one node).
/// </para>
/// </summary>
public sealed class JoinState : ILogic, IAsyncLogic
{
    public JoinState(JoinPolicy policy)
    {
        if (policy.RequiredCount < 1)
        {
            throw new ArgumentException(
                "Join policy is uninitialized — construct it via JoinPolicy.All/Any/Quorum.", nameof(policy));
        }

        Policy = policy;
    }

    /// <summary>The firing rule of this join.</summary>
    public JoinPolicy Policy { get; }

    Result ILogic.Execute() => throw TokenNodeMisuse.ForJoin();

    ValueTask<Result> IAsyncLogic.ExecuteAsync(CancellationToken ct) => throw TokenNodeMisuse.ForJoin();
}
