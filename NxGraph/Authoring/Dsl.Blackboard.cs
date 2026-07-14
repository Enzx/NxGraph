using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Authoring;

public static partial class Dsl
{
    // ── Schema declaration (routed by the schema's scope) ────────────────

    /// <summary>
    /// Declares a blackboard schema on the graph being built, routed by the schema's scope:
    /// Graph-scoped schemas become the graph's own declaration, Global-scoped schemas record
    /// the global board the graph requires, and Node-scoped schemas declare transient
    /// per-visit scratch that each machine auto-creates its own board for. Machines validate
    /// bound boards against these declarations at <c>WithBlackboard(...)</c> time.
    /// </summary>
    public static StartToken WithSchema(this StartToken token, BlackboardSchema schema)
    {
        token.Builder.WithSchema(schema);
        return token;
    }

    /// <inheritdoc cref="WithSchema(StartToken, BlackboardSchema)"/>
    public static StateToken WithSchema(this StateToken prev, BlackboardSchema schema)
    {
        prev.Builder.WithSchema(schema);
        return prev;
    }

    /// <inheritdoc cref="WithSchema(StartToken, BlackboardSchema)"/>
    public static BranchBuilder WithSchema(this BranchBuilder branch, BlackboardSchema schema)
    {
        branch.Builder.WithSchema(schema);
        return branch;
    }

    /// <inheritdoc cref="WithSchema(StartToken, BlackboardSchema)"/>
    public static BranchEnd WithSchema(this BranchEnd branchEnd, BlackboardSchema schema)
    {
        branchEnd.Builder.WithSchema(schema);
        return branchEnd;
    }

    // ── Machine binding ───────────────────────────────────────────────────

    /// <summary>
    /// Binds a blackboard to the machine, routed into the scope slot its schema declares.
    /// Call once per scope; rebinding a scope replaces the board. Chainable in any order
    /// with <c>WithAgent</c>:
    /// <c>graph.ToAsyncStateMachine&lt;Enemy&gt;().WithBlackboard(worldBb).WithBlackboard(enemyBb).WithAgent(enemy)</c>.
    /// </summary>
    public static T WithBlackboard<T>(this T target, Blackboard blackboard) where T : class, IBlackboardBindable
    {
        Guard.NotNull(target, nameof(target));
        target.SetBlackboard(blackboard);
        return target;
    }

    // ── Start / chain relays receiving the routed context ────────────────

    /// <summary>
    /// Adds the first (start) node executing <paramref name="run"/> synchronously with the
    /// machine-bound routed blackboard context.
    /// </summary>
    public static StateToken To(this StartToken token, Func<BlackboardContext, Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return token.To(new RelayState(run));
    }

    /// <summary>
    /// Adds the first (start) node executing <paramref name="run"/> asynchronously with the
    /// machine-bound routed blackboard context.
    /// </summary>
    public static StateToken ToAsync(this StartToken token,
        Func<BlackboardContext, CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return token.ToAsync(new AsyncRelayState(run));
    }

    /// <summary>
    /// Chains a synchronous state whose lambda receives the machine-bound routed blackboard
    /// context — no closure capture, so one graph template serves N machines with distinct boards.
    /// </summary>
    public static StateToken To(this StateToken prev, Func<BlackboardContext, Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return prev.To(new RelayState(run));
    }

    /// <summary>
    /// Chains an asynchronous state whose lambda receives the machine-bound routed blackboard context.
    /// </summary>
    public static StateToken ToAsync(this StateToken prev,
        Func<BlackboardContext, CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return prev.ToAsync(new AsyncRelayState(run));
    }

    /// <summary>
    /// Chains a synchronous state whose lambda receives both the stamped agent and the
    /// routed blackboard context. The agent type cannot be inferred from the lambda alone —
    /// supply it explicitly: <c>.To&lt;Enemy&gt;((enemy, bb) =&gt; ...)</c>.
    /// </summary>
    public static StateToken To<TAgent>(this StateToken prev, Func<TAgent, BlackboardContext, Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return prev.To(new RelayState<TAgent>(run));
    }

    /// <summary>
    /// Chains an asynchronous state whose lambda receives both the stamped agent and the
    /// routed blackboard context: <c>.ToAsync&lt;Enemy&gt;((enemy, bb, ct) =&gt; ...)</c>.
    /// </summary>
    public static StateToken ToAsync<TAgent>(this StateToken prev,
        Func<TAgent, BlackboardContext, CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return prev.ToAsync(new AsyncRelayState<TAgent>(run));
    }

    // ── Blackboard-driven conditions ──────────────────────────────────────

    /// <summary>
    /// Creates a conditional branch whose predicate reads the routed blackboard context:
    /// <c>.If(bb =&gt; bb.Get(Keys.Health) &gt; 50).Then(chase).Else(flee)</c>.
    /// </summary>
    public static IfBuilder If(this StateToken prev, Func<BlackboardContext, bool> predicate)
    {
        Guard.NotNull(predicate, nameof(predicate));
        return new IfBuilder(prev, predicate);
    }

    /// <inheritdoc cref="If(StateToken, Func{BlackboardContext, bool})"/>
    public static IfBuilder If(this StartToken root, Func<BlackboardContext, bool> predicate)
    {
        Guard.NotNull(predicate, nameof(predicate));
        return new IfBuilder(root, predicate);
    }

    /// <summary>
    /// Creates a switch whose key selector reads the routed blackboard context.
    /// </summary>
    public static SwitchBuilder<TKey> Switch<TKey>(this StateToken prev,
        Func<BlackboardContext, TKey> selector)
        where TKey : notnull
    {
        Guard.NotNull(selector, nameof(selector));
        return new SwitchBuilder<TKey>(prev, selector);
    }

    /// <inheritdoc cref="Switch{TKey}(StateToken, Func{BlackboardContext, TKey})"/>
    public static SwitchBuilder<TKey> Switch<TKey>(this StartToken root,
        Func<BlackboardContext, TKey> selector)
        where TKey : notnull
    {
        Guard.NotNull(selector, nameof(selector));
        return new SwitchBuilder<TKey>(root, selector);
    }

    // ── Branch bodies receiving the routed context ────────────────────────

    /// <summary>Creates a "then" branch with a synchronous context lambda.</summary>
    public static BranchBuilder Then(this IfBuilder ifBuilder, Func<BlackboardContext, Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return ifBuilder.Then(new RelayState(run));
    }

    /// <summary>Creates a "then" branch with an asynchronous context lambda.</summary>
    public static BranchBuilder ThenAsync(this IfBuilder ifBuilder,
        Func<BlackboardContext, CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return ifBuilder.ThenAsync(new AsyncRelayState(run));
    }

    /// <summary>Creates an "else" branch with a synchronous context lambda.</summary>
    public static BranchEnd Else(this BranchBuilder branch, Func<BlackboardContext, Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return branch.Else(new RelayState(run));
    }

    /// <summary>Creates an "else" branch with an asynchronous context lambda.</summary>
    public static BranchEnd ElseAsync(this BranchBuilder branch,
        Func<BlackboardContext, CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return branch.ElseAsync(new AsyncRelayState(run));
    }

    /// <summary>Chains a synchronous context lambda onto the "then" branch.</summary>
    public static BranchBuilder To(this BranchBuilder branch, Func<BlackboardContext, Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return branch.To(new RelayState(run));
    }

    /// <summary>Chains an asynchronous context lambda onto the "then" branch.</summary>
    public static BranchBuilder ToAsync(this BranchBuilder branch,
        Func<BlackboardContext, CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return branch.ToAsync(new AsyncRelayState(run));
    }

    /// <summary>Chains a synchronous context lambda after the "else" branch.</summary>
    public static StateToken To(this BranchEnd branchEnd, Func<BlackboardContext, Result> run)
    {
        Guard.NotNull(run, nameof(run));
        return branchEnd.To(new RelayState(run));
    }

    /// <summary>Chains an asynchronous context lambda after the "else" branch.</summary>
    public static StateToken ToAsync(this BranchEnd branchEnd,
        Func<BlackboardContext, CancellationToken, ValueTask<Result>> run)
    {
        Guard.NotNull(run, nameof(run));
        return branchEnd.ToAsync(new AsyncRelayState(run));
    }

    /// <summary>Adds a case with a synchronous context lambda.</summary>
    public static SwitchBuilder<TKey> Case<TKey>(this SwitchBuilder<TKey> switchBuilder, TKey key,
        Func<BlackboardContext, Result> run) where TKey : notnull
    {
        Guard.NotNull(run, nameof(run));
        return switchBuilder.Case(key, new RelayState(run));
    }

    /// <summary>Adds a case with an asynchronous context lambda.</summary>
    public static SwitchBuilder<TKey> CaseAsync<TKey>(this SwitchBuilder<TKey> switchBuilder, TKey key,
        Func<BlackboardContext, CancellationToken, ValueTask<Result>> run) where TKey : notnull
    {
        Guard.NotNull(run, nameof(run));
        return switchBuilder.CaseAsync(key, new AsyncRelayState(run));
    }

    /// <summary>Adds a default case with a synchronous context lambda.</summary>
    public static SwitchBuilder<TKey> Default<TKey>(this SwitchBuilder<TKey> switchBuilder,
        Func<BlackboardContext, Result> run) where TKey : notnull
    {
        Guard.NotNull(run, nameof(run));
        return switchBuilder.Default(new RelayState(run));
    }

    /// <summary>Adds a default case with an asynchronous context lambda.</summary>
    public static SwitchBuilder<TKey> DefaultAsync<TKey>(this SwitchBuilder<TKey> switchBuilder,
        Func<BlackboardContext, CancellationToken, ValueTask<Result>> run) where TKey : notnull
    {
        Guard.NotNull(run, nameof(run));
        return switchBuilder.DefaultAsync(new AsyncRelayState(run));
    }
}
