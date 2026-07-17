using NxGraph.Blackboards;
using NxGraph.Compatibility;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Authoring;

public static partial class Dsl
{
    // ── Step I/O ports: typed produce → pipe → consume sugar over the blackboard ──
    //
    // A "port" is an ordinary Graph-scoped BlackboardKey<T> — no wrapper type. These
    // overloads only read/write ports around the step lambda; routing, validation, and
    // durability are the blackboard's existing machinery.

    private static void ThrowIfNotPortScope<T>(in BlackboardKey<T> key, string paramName)
    {
        if (key.Schema is null)
        {
            throw new ArgumentException(
                "Invalid port key — obtain keys via BlackboardSchema.Register<T>(...) on a Graph-scoped schema.",
                paramName);
        }

        if (key.Schema.Scope == BlackboardScope.Node)
        {
            throw new ArgumentException(
                $"Port key '{key.Name}' is Node-scoped — Node scratch resets on the success transition, " +
                "so the value would be gone before the consumer runs. Register step I/O ports on a " +
                "Graph-scoped schema.", paramName);
        }
    }

    // ── Start relays ─────────────────────────────────────────────────────

    /// <summary>
    /// Starts the graph with a port-producing step: the lambda computes a value from the
    /// machine-bound routed blackboard context, the relay writes it to
    /// <paramref name="output"/> and returns <c>Success</c>. A port is an ordinary
    /// Graph-scoped <see cref="BlackboardKey{T}"/> — one key per producing step, named for
    /// the datum, not the step (several steps may legally produce the same port). Declare its
    /// schema on the graph via <c>WithSchema(...)</c> and bind a board per machine via
    /// <c>WithBlackboard(...)</c>, so one graph template serves N machines with distinct
    /// port values.
    /// <para>
    /// Producer steps cannot fail — the relay always succeeds after the write. A step that
    /// can fail keeps using <c>To(Func&lt;BlackboardContext, Result&gt;)</c> with an explicit
    /// <c>bb.Set(...)</c> on its success path; exceptions propagate as from any relay.
    /// Node-scoped keys are rejected at wiring time (their value resets before the consumer
    /// runs). Global-scoped keys are allowed but shared across machines — two machines over
    /// one template overwrite each other's values, so prefer Graph scope.
    /// </para>
    /// </summary>
    public static StateToken To<TOut>(this StartToken token, BlackboardKey<TOut> output,
        Func<BlackboardContext, TOut> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(output, nameof(output));
        return token.To(new PortProducerRelayState<TOut>(output, step));
    }

    /// <inheritdoc cref="To{TOut}(StartToken, BlackboardKey{TOut}, Func{BlackboardContext, TOut})"/>
    public static StateToken ToAsync<TOut>(this StartToken token, BlackboardKey<TOut> output,
        Func<BlackboardContext, CancellationToken, ValueTask<TOut>> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(output, nameof(output));
        return token.ToAsync(new AsyncPortProducerRelayState<TOut>(output, step));
    }

    /// <summary>
    /// Starts the graph with a port-consuming step: the relay reads <paramref name="input"/>
    /// and hands the value (plus the routed context) to the lambda, returning its result.
    /// A consumer that can run before any producer sees the key's registered default —
    /// register a meaningful default or guard in the step. Node-scoped keys are rejected at
    /// wiring time; Global-scoped keys are allowed but shared across machines — prefer
    /// Graph scope.
    /// </summary>
    public static StateToken To<TIn>(this StartToken token, BlackboardKey<TIn> input,
        Func<TIn, BlackboardContext, Result> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        return token.To(new PortConsumerRelayState<TIn>(input, step));
    }

    /// <inheritdoc cref="To{TIn}(StartToken, BlackboardKey{TIn}, Func{TIn, BlackboardContext, Result})"/>
    public static StateToken ToAsync<TIn>(this StartToken token, BlackboardKey<TIn> input,
        Func<TIn, BlackboardContext, CancellationToken, ValueTask<Result>> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        return token.ToAsync(new AsyncPortConsumerRelayState<TIn>(input, step));
    }

    /// <summary>
    /// Starts the graph with a port-piping step: the relay reads <paramref name="input"/>,
    /// hands the value (plus the routed context) to the lambda, writes the transformed value
    /// to <paramref name="output"/>, and returns <c>Success</c>. Pipe steps cannot fail —
    /// a step that can keeps using <c>To(Func&lt;BlackboardContext, Result&gt;)</c> with
    /// explicit <c>Get</c>/<c>Set</c>. Node-scoped keys are rejected at wiring time;
    /// Global-scoped keys are allowed but shared across machines — prefer Graph scope.
    /// </summary>
    public static StateToken To<TIn, TOut>(this StartToken token, BlackboardKey<TIn> input,
        BlackboardKey<TOut> output, Func<TIn, BlackboardContext, TOut> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        ThrowIfNotPortScope(output, nameof(output));
        return token.To(new PortPipeRelayState<TIn, TOut>(input, output, step));
    }

    /// <inheritdoc cref="To{TIn, TOut}(StartToken, BlackboardKey{TIn}, BlackboardKey{TOut}, Func{TIn, BlackboardContext, TOut})"/>
    public static StateToken ToAsync<TIn, TOut>(this StartToken token, BlackboardKey<TIn> input,
        BlackboardKey<TOut> output, Func<TIn, BlackboardContext, CancellationToken, ValueTask<TOut>> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        ThrowIfNotPortScope(output, nameof(output));
        return token.ToAsync(new AsyncPortPipeRelayState<TIn, TOut>(input, output, step));
    }

    // ── Chain relays ─────────────────────────────────────────────────────

    /// <summary>
    /// Chains a port-producing step (see
    /// <see cref="To{TOut}(StartToken, BlackboardKey{TOut}, Func{BlackboardContext, TOut})"/>).
    /// </summary>
    /// <inheritdoc cref="To{TOut}(StartToken, BlackboardKey{TOut}, Func{BlackboardContext, TOut})"/>
    public static StateToken To<TOut>(this StateToken prev, BlackboardKey<TOut> output,
        Func<BlackboardContext, TOut> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(output, nameof(output));
        return prev.To(new PortProducerRelayState<TOut>(output, step));
    }

    /// <inheritdoc cref="To{TOut}(StartToken, BlackboardKey{TOut}, Func{BlackboardContext, TOut})"/>
    public static StateToken ToAsync<TOut>(this StateToken prev, BlackboardKey<TOut> output,
        Func<BlackboardContext, CancellationToken, ValueTask<TOut>> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(output, nameof(output));
        return prev.ToAsync(new AsyncPortProducerRelayState<TOut>(output, step));
    }

    /// <summary>
    /// Chains a port-consuming step (see
    /// <see cref="To{TIn}(StartToken, BlackboardKey{TIn}, Func{TIn, BlackboardContext, Result})"/>).
    /// </summary>
    /// <inheritdoc cref="To{TIn}(StartToken, BlackboardKey{TIn}, Func{TIn, BlackboardContext, Result})"/>
    public static StateToken To<TIn>(this StateToken prev, BlackboardKey<TIn> input,
        Func<TIn, BlackboardContext, Result> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        return prev.To(new PortConsumerRelayState<TIn>(input, step));
    }

    /// <inheritdoc cref="To{TIn}(StartToken, BlackboardKey{TIn}, Func{TIn, BlackboardContext, Result})"/>
    public static StateToken ToAsync<TIn>(this StateToken prev, BlackboardKey<TIn> input,
        Func<TIn, BlackboardContext, CancellationToken, ValueTask<Result>> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        return prev.ToAsync(new AsyncPortConsumerRelayState<TIn>(input, step));
    }

    /// <summary>
    /// Chains a port-piping step (see
    /// <see cref="To{TIn, TOut}(StartToken, BlackboardKey{TIn}, BlackboardKey{TOut}, Func{TIn, BlackboardContext, TOut})"/>).
    /// </summary>
    /// <inheritdoc cref="To{TIn, TOut}(StartToken, BlackboardKey{TIn}, BlackboardKey{TOut}, Func{TIn, BlackboardContext, TOut})"/>
    public static StateToken To<TIn, TOut>(this StateToken prev, BlackboardKey<TIn> input,
        BlackboardKey<TOut> output, Func<TIn, BlackboardContext, TOut> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        ThrowIfNotPortScope(output, nameof(output));
        return prev.To(new PortPipeRelayState<TIn, TOut>(input, output, step));
    }

    /// <inheritdoc cref="To{TIn, TOut}(StartToken, BlackboardKey{TIn}, BlackboardKey{TOut}, Func{TIn, BlackboardContext, TOut})"/>
    public static StateToken ToAsync<TIn, TOut>(this StateToken prev, BlackboardKey<TIn> input,
        BlackboardKey<TOut> output, Func<TIn, BlackboardContext, CancellationToken, ValueTask<TOut>> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        ThrowIfNotPortScope(output, nameof(output));
        return prev.ToAsync(new AsyncPortPipeRelayState<TIn, TOut>(input, output, step));
    }

    // ── "Then" branch relays ─────────────────────────────────────────────

    /// <inheritdoc cref="To{TOut}(StartToken, BlackboardKey{TOut}, Func{BlackboardContext, TOut})"/>
    public static BranchBuilder To<TOut>(this BranchBuilder branch, BlackboardKey<TOut> output,
        Func<BlackboardContext, TOut> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(output, nameof(output));
        return branch.To(new PortProducerRelayState<TOut>(output, step));
    }

    /// <inheritdoc cref="To{TOut}(StartToken, BlackboardKey{TOut}, Func{BlackboardContext, TOut})"/>
    public static BranchBuilder ToAsync<TOut>(this BranchBuilder branch, BlackboardKey<TOut> output,
        Func<BlackboardContext, CancellationToken, ValueTask<TOut>> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(output, nameof(output));
        return branch.ToAsync(new AsyncPortProducerRelayState<TOut>(output, step));
    }

    /// <inheritdoc cref="To{TIn}(StartToken, BlackboardKey{TIn}, Func{TIn, BlackboardContext, Result})"/>
    public static BranchBuilder To<TIn>(this BranchBuilder branch, BlackboardKey<TIn> input,
        Func<TIn, BlackboardContext, Result> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        return branch.To(new PortConsumerRelayState<TIn>(input, step));
    }

    /// <inheritdoc cref="To{TIn}(StartToken, BlackboardKey{TIn}, Func{TIn, BlackboardContext, Result})"/>
    public static BranchBuilder ToAsync<TIn>(this BranchBuilder branch, BlackboardKey<TIn> input,
        Func<TIn, BlackboardContext, CancellationToken, ValueTask<Result>> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        return branch.ToAsync(new AsyncPortConsumerRelayState<TIn>(input, step));
    }

    /// <inheritdoc cref="To{TIn, TOut}(StartToken, BlackboardKey{TIn}, BlackboardKey{TOut}, Func{TIn, BlackboardContext, TOut})"/>
    public static BranchBuilder To<TIn, TOut>(this BranchBuilder branch, BlackboardKey<TIn> input,
        BlackboardKey<TOut> output, Func<TIn, BlackboardContext, TOut> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        ThrowIfNotPortScope(output, nameof(output));
        return branch.To(new PortPipeRelayState<TIn, TOut>(input, output, step));
    }

    /// <inheritdoc cref="To{TIn, TOut}(StartToken, BlackboardKey{TIn}, BlackboardKey{TOut}, Func{TIn, BlackboardContext, TOut})"/>
    public static BranchBuilder ToAsync<TIn, TOut>(this BranchBuilder branch, BlackboardKey<TIn> input,
        BlackboardKey<TOut> output, Func<TIn, BlackboardContext, CancellationToken, ValueTask<TOut>> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        ThrowIfNotPortScope(output, nameof(output));
        return branch.ToAsync(new AsyncPortPipeRelayState<TIn, TOut>(input, output, step));
    }

    // ── Post-"else" relays ───────────────────────────────────────────────

    /// <inheritdoc cref="To{TOut}(StartToken, BlackboardKey{TOut}, Func{BlackboardContext, TOut})"/>
    public static StateToken To<TOut>(this BranchEnd branchEnd, BlackboardKey<TOut> output,
        Func<BlackboardContext, TOut> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(output, nameof(output));
        return branchEnd.To(new PortProducerRelayState<TOut>(output, step));
    }

    /// <inheritdoc cref="To{TOut}(StartToken, BlackboardKey{TOut}, Func{BlackboardContext, TOut})"/>
    public static StateToken ToAsync<TOut>(this BranchEnd branchEnd, BlackboardKey<TOut> output,
        Func<BlackboardContext, CancellationToken, ValueTask<TOut>> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(output, nameof(output));
        return branchEnd.ToAsync(new AsyncPortProducerRelayState<TOut>(output, step));
    }

    /// <inheritdoc cref="To{TIn}(StartToken, BlackboardKey{TIn}, Func{TIn, BlackboardContext, Result})"/>
    public static StateToken To<TIn>(this BranchEnd branchEnd, BlackboardKey<TIn> input,
        Func<TIn, BlackboardContext, Result> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        return branchEnd.To(new PortConsumerRelayState<TIn>(input, step));
    }

    /// <inheritdoc cref="To{TIn}(StartToken, BlackboardKey{TIn}, Func{TIn, BlackboardContext, Result})"/>
    public static StateToken ToAsync<TIn>(this BranchEnd branchEnd, BlackboardKey<TIn> input,
        Func<TIn, BlackboardContext, CancellationToken, ValueTask<Result>> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        return branchEnd.ToAsync(new AsyncPortConsumerRelayState<TIn>(input, step));
    }

    /// <inheritdoc cref="To{TIn, TOut}(StartToken, BlackboardKey{TIn}, BlackboardKey{TOut}, Func{TIn, BlackboardContext, TOut})"/>
    public static StateToken To<TIn, TOut>(this BranchEnd branchEnd, BlackboardKey<TIn> input,
        BlackboardKey<TOut> output, Func<TIn, BlackboardContext, TOut> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        ThrowIfNotPortScope(output, nameof(output));
        return branchEnd.To(new PortPipeRelayState<TIn, TOut>(input, output, step));
    }

    /// <inheritdoc cref="To{TIn, TOut}(StartToken, BlackboardKey{TIn}, BlackboardKey{TOut}, Func{TIn, BlackboardContext, TOut})"/>
    public static StateToken ToAsync<TIn, TOut>(this BranchEnd branchEnd, BlackboardKey<TIn> input,
        BlackboardKey<TOut> output, Func<TIn, BlackboardContext, CancellationToken, ValueTask<TOut>> step)
    {
        Guard.NotNull(step, nameof(step));
        ThrowIfNotPortScope(input, nameof(input));
        ThrowIfNotPortScope(output, nameof(output));
        return branchEnd.ToAsync(new AsyncPortPipeRelayState<TIn, TOut>(input, output, step));
    }
}
