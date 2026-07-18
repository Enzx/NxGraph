using NxGraph.Blackboards;

namespace NxGraph.Behaviors;

/// <summary>
/// The execution context a behavior composite hands to each of its entries: the machine-bound
/// routed <see cref="BlackboardContext"/>, typed binding resolution, and the owning node's
/// report channel. A default-constructed context is valid but inert — blackboard access
/// throws the usual unbound-scope errors and <see cref="Report"/> is a no-op.
/// </summary>
public readonly struct BehaviorContext
{
    private readonly BlackboardContext _bb;
    private readonly IBehaviorReportSink? _sink;

    internal BehaviorContext(in BlackboardContext bb, IBehaviorReportSink? sink)
    {
        _bb = bb;
        _sink = sink;
    }

    /// <summary>The machine-bound routed blackboard context (see <see cref="BlackboardContext"/>).</summary>
    public BlackboardContext Bb => _bb;

    /// <summary>
    /// <see langword="true"/> when the owning composite's report channel is wired to an
    /// observer. Behaviors that build report strings should check this first so observer-less
    /// machines pay nothing (the library <see cref="Log"/> behavior does).
    /// </summary>
    public bool HasReporter => _sink is not null && _sink.HasReporter;

    /// <summary>
    /// Resolves a <see cref="BlackboardValue{T}"/>: the literal as-is, or a typed
    /// <see cref="BlackboardContext.Get{T}"/> through the bound key. Zero boxing, zero
    /// allocation.
    /// </summary>
    public T Resolve<T>(in BlackboardValue<T> value) => value.Resolve(in _bb);

    /// <summary>
    /// Emits a message on the owning node's report channel — routed to the machine observer's
    /// <c>OnLogReport</c>, exactly like <c>State.Log</c>. Where the message lands is the
    /// observer's decision (the library imposes no I/O policy); a no-op when unwired.
    /// </summary>
    public void Report(string message)
    {
        _sink?.Report(message);
    }
}

/// <summary>
/// Internal bridge between <see cref="BehaviorContext.Report"/> and the owning composite's
/// machine-wired log-report callback. Implemented by the four behavior composites.
/// </summary>
internal interface IBehaviorReportSink
{
    /// <summary><see langword="true"/> when a log-report callback is currently wired.</summary>
    bool HasReporter { get; }

    /// <summary>Delivers one report message through the wired callback.</summary>
    void Report(string message);
}
