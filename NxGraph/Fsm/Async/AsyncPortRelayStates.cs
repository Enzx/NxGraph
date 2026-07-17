using NxGraph.Blackboards;

namespace NxGraph.Fsm.Async;

/// <summary>
/// Port-producing relay: runs the step with the machine-bound routed context, writes its value
/// to the output port (an ordinary <see cref="BlackboardKey{T}"/>), and returns <c>Success</c>.
/// The step cannot fail — a step that can must use the plain context relay with an explicit
/// <c>Set</c>. Steps completing synchronously keep the hot path allocation-free.
/// </summary>
public sealed class AsyncPortProducerRelayState<TOut> : AsyncState
{
    private readonly BlackboardKey<TOut> _output;
    private readonly Func<BlackboardContext, CancellationToken, ValueTask<TOut>> _step;

    public AsyncPortProducerRelayState(BlackboardKey<TOut> output,
        Func<BlackboardContext, CancellationToken, ValueTask<TOut>> step)
    {
        _output = output;
        _step = step ?? throw new ArgumentNullException(nameof(step));
    }

    protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        Bb.Set(_output, await _step(Bb, ct).ConfigureAwait(false));
        return Result.Success;
    }
}

/// <summary>
/// Port-consuming relay: reads the input port and hands the value (plus the routed context)
/// to the step, returning its result.
/// </summary>
public sealed class AsyncPortConsumerRelayState<TIn> : AsyncState
{
    private readonly BlackboardKey<TIn> _input;
    private readonly Func<TIn, BlackboardContext, CancellationToken, ValueTask<Result>> _step;

    public AsyncPortConsumerRelayState(BlackboardKey<TIn> input,
        Func<TIn, BlackboardContext, CancellationToken, ValueTask<Result>> step)
    {
        _input = input;
        _step = step ?? throw new ArgumentNullException(nameof(step));
    }

    protected override ValueTask<Result> OnRunAsync(CancellationToken ct) => _step(Bb.Get(_input), Bb, ct);
}

/// <summary>
/// Port-piping relay: reads the input port, transforms, writes the output port, and returns
/// <c>Success</c>. The step cannot fail — a step that can must use the plain context relay
/// with an explicit <c>Set</c>.
/// </summary>
public sealed class AsyncPortPipeRelayState<TIn, TOut> : AsyncState
{
    private readonly BlackboardKey<TIn> _input;
    private readonly BlackboardKey<TOut> _output;
    private readonly Func<TIn, BlackboardContext, CancellationToken, ValueTask<TOut>> _step;

    public AsyncPortPipeRelayState(BlackboardKey<TIn> input, BlackboardKey<TOut> output,
        Func<TIn, BlackboardContext, CancellationToken, ValueTask<TOut>> step)
    {
        _input = input;
        _output = output;
        _step = step ?? throw new ArgumentNullException(nameof(step));
    }

    protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        Bb.Set(_output, await _step(Bb.Get(_input), Bb, ct).ConfigureAwait(false));
        return Result.Success;
    }
}
