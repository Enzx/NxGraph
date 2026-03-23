using NxGraph.Fsm;
using NxGraph.Graphs;
#if NETSTANDARD2_1
using ArgumentNullException = System.ArgumentNullExceptionShim;
#endif

namespace NxGraph.Authoring;

public static partial class Dsl
{
    // ── StateToken overloads accepting Func<Result> ──────────────────────

    /// <summary>
    /// Chains a synchronous state (wrapping <paramref name="run"/> in a <see cref="SyncRelayState"/>).
    /// </summary>
    public static StateToken To(this StateToken prev, Func<Result> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return prev.To(new SyncRelayState(run));
    }

    // ── StartToken overloads accepting Func<Result> ─────────────────────

    /// <summary>
    /// Adds the first (start) node that executes <paramref name="run"/> synchronously.
    /// </summary>
    public static StateToken To(this StartToken token, Func<Result> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return token.To(new SyncRelayState(run));
    }

    // ── IfBuilder / BranchBuilder / BranchEnd overloads ─────────────────

    /// <summary>
    /// Creates a "then" branch with a synchronous lambda.
    /// </summary>
    public static BranchBuilder Then(this IfBuilder ifBuilder, Func<Result> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return ifBuilder.Then(new SyncRelayState(run));
    }

    /// <summary>
    /// Creates an "else" branch with a synchronous lambda.
    /// </summary>
    public static BranchEnd Else(this BranchBuilder branch, Func<Result> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return branch.Else(new SyncRelayState(run));
    }

    /// <summary>
    /// Chains a synchronous state onto the "then" branch.
    /// </summary>
    public static BranchBuilder To(this BranchBuilder branch, Func<Result> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return branch.To(new SyncRelayState(run));
    }

    /// <summary>
    /// Chains a synchronous state after the "else" branch.
    /// </summary>
    public static StateToken To(this BranchEnd branchEnd, Func<Result> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return branchEnd.To(new SyncRelayState(run));
    }

    // ── SwitchBuilder overloads accepting Func<Result> ──────────────────

    /// <summary>
    /// Adds a case with a synchronous lambda.
    /// </summary>
    public static SwitchBuilder<TKey> Case<TKey>(this SwitchBuilder<TKey> switchBuilder, TKey key,
        Func<Result> run) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(run);
        return switchBuilder.Case(key, new SyncRelayState(run));
    }

    /// <summary>
    /// Adds a default case with a synchronous lambda.
    /// </summary>
    public static SwitchBuilder<TKey> Default<TKey>(this SwitchBuilder<TKey> switchBuilder,
        Func<Result> run) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(run);
        return switchBuilder.Default(new SyncRelayState(run));
    }

    // ── GraphBuilder.StartWith overload for Func<Result> ────────────────

    /// <summary>
    /// Creates a new graph whose first (start) node executes <paramref name="run"/> synchronously.
    /// </summary>
    public static StateToken StartWithSync(Func<Result> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return GraphBuilder.StartWith(new SyncRelayState(run));
    }
}

