using NxGraph.Compatibility;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Authoring;

public static partial class Dsl
{
    // ── StateToken overloads accepting Func<Result> ──────────────────────

    /// <summary>
    /// Chains a synchronous state (wrapping <paramref name="run"/> in a <see cref="RelayState"/>).
    /// </summary>
    public static StateToken To(this StateToken prev, Func<Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return prev.To(new RelayState(run));
    }

    // ── StartToken overloads accepting Func<Result> ─────────────────────

    /// <summary>
    /// Adds the first (start) node that executes <paramref name="run"/> synchronously.
    /// </summary>
    public static StateToken To(this StartToken token, Func<Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return token.To(new RelayState(run));
    }

    // ── IfBuilder / BranchBuilder / BranchEnd overloads ─────────────────

    /// <summary>
    /// Creates a "then" branch with a synchronous lambda.
    /// </summary>
    public static BranchBuilder Then(this IfBuilder ifBuilder, Func<Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return ifBuilder.Then(new RelayState(run));
    }

    /// <summary>
    /// Creates an "else" branch with a synchronous lambda.
    /// </summary>
    public static BranchEnd Else(this BranchBuilder branch, Func<Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return branch.Else(new RelayState(run));
    }

    /// <summary>
    /// Chains a synchronous state onto the "then" branch.
    /// </summary>
    public static BranchBuilder To(this BranchBuilder branch, Func<Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return branch.To(new RelayState(run));
    }

    /// <summary>
    /// Chains a synchronous state after the "else" branch.
    /// </summary>
    public static StateToken To(this BranchEnd branchEnd, Func<Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return branchEnd.To(new RelayState(run));
    }

    // ── SwitchBuilder overloads accepting Func<Result> ──────────────────

    /// <summary>
    /// Adds a case with a synchronous lambda.
    /// </summary>
    public static SwitchBuilder<TKey> Case<TKey>(this SwitchBuilder<TKey> switchBuilder, TKey key,
        Func<Result> run) where TKey : notnull
    {
        Guard.NotNull(run, nameof(run));
        return switchBuilder.Case(key, new RelayState(run));
    }

    /// <summary>
    /// Adds a default case with a synchronous lambda.
    /// </summary>
    public static SwitchBuilder<TKey> Default<TKey>(this SwitchBuilder<TKey> switchBuilder,
        Func<Result> run) where TKey : notnull
    {
        Guard.NotNull(run, nameof(run));
        return switchBuilder.Default(new RelayState(run));
    }

    // ── AsyncSwitchBuilder overloads accepting Func<Result> ──────────────

    /// <summary>
    /// Adds a case with a synchronous lambda to the async switch builder.
    /// </summary>
    public static AsyncSwitchBuilder<TKey> Case<TKey>(this AsyncSwitchBuilder<TKey> switchBuilder, TKey key,
        Func<Result> run) where TKey : notnull
    {
        Guard.NotNull(run, nameof(run));
        return switchBuilder.Case(key, new RelayState(run));
    }

    /// <summary>
    /// Adds a default case with a synchronous lambda to the async switch builder.
    /// </summary>
    public static AsyncSwitchBuilder<TKey> Default<TKey>(this AsyncSwitchBuilder<TKey> switchBuilder,
        Func<Result> run) where TKey : notnull
    {
        Guard.NotNull(run, nameof(run));
        return switchBuilder.Default(new RelayState(run));
    }
}

