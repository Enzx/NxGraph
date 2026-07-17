using NxGraph.Blackboards;

namespace NxGraph.Fsm;

/// <summary>
/// Synchronous counterpart of <see cref="Async.AsyncPortProducerRelayState{TOut}"/>: runs the
/// step with the machine-bound routed context, writes its value to the output port
/// (an ordinary <see cref="BlackboardKey{T}"/>), and returns <c>Success</c>. The step cannot
/// fail — a step that can must use the plain context relay with an explicit <c>Set</c>.
/// </summary>
public sealed class PortProducerRelayState<TOut> : State
{
    private readonly BlackboardKey<TOut> _output;
    private readonly Func<BlackboardContext, TOut> _step;

    public PortProducerRelayState(BlackboardKey<TOut> output, Func<BlackboardContext, TOut> step)
    {
        _output = output;
        _step = step ?? throw new ArgumentNullException(nameof(step));
    }

    protected override Result OnRun()
    {
        Bb.Set(_output, _step(Bb));
        return Result.Success;
    }
}

/// <summary>
/// Synchronous counterpart of <see cref="Async.AsyncPortConsumerRelayState{TIn}"/>: reads the
/// input port and hands the value (plus the routed context) to the step, returning its result.
/// </summary>
public sealed class PortConsumerRelayState<TIn> : State
{
    private readonly BlackboardKey<TIn> _input;
    private readonly Func<TIn, BlackboardContext, Result> _step;

    public PortConsumerRelayState(BlackboardKey<TIn> input, Func<TIn, BlackboardContext, Result> step)
    {
        _input = input;
        _step = step ?? throw new ArgumentNullException(nameof(step));
    }

    protected override Result OnRun() => _step(Bb.Get(_input), Bb);
}

/// <summary>
/// Synchronous counterpart of <see cref="Async.AsyncPortPipeRelayState{TIn,TOut}"/>: reads the
/// input port, transforms, writes the output port, and returns <c>Success</c>. The step cannot
/// fail — a step that can must use the plain context relay with an explicit <c>Set</c>.
/// </summary>
public sealed class PortPipeRelayState<TIn, TOut> : State
{
    private readonly BlackboardKey<TIn> _input;
    private readonly BlackboardKey<TOut> _output;
    private readonly Func<TIn, BlackboardContext, TOut> _step;

    public PortPipeRelayState(BlackboardKey<TIn> input, BlackboardKey<TOut> output,
        Func<TIn, BlackboardContext, TOut> step)
    {
        _input = input;
        _output = output;
        _step = step ?? throw new ArgumentNullException(nameof(step));
    }

    protected override Result OnRun()
    {
        Bb.Set(_output, _step(Bb.Get(_input), Bb));
        return Result.Success;
    }
}
