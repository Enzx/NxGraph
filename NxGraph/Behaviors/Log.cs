namespace NxGraph.Behaviors;

/// <summary>
/// Standard behavior: emits <c>"[{severity}] {message}"</c> through the owning node's report
/// channel (<see cref="BehaviorContext.Report"/> → observer <c>OnLogReport</c>). The report
/// channel — never the console — is the sanctioned output: where the message lands is the
/// observer's decision, and baking console I/O into core would be an imposed policy.
/// <para>
/// Both fields are <see cref="BlackboardValue{T}"/> bindings, so severity and message may be
/// literals or blackboard keys. The string is formatted <b>only when a reporter is wired</b>
/// — observer-less machines build no string and pay nothing. One class implements both
/// behavior interfaces (the <c>ForkState</c> dual-interface shape), so a single instance
/// authors either runtime.
/// </para>
/// </summary>
public sealed class Log : IBehavior, IAsyncBehavior
{
    /// <summary>Creates a log entry with <see cref="LogSeverity.Info"/> severity.</summary>
    public Log(BlackboardValue<string> message)
        : this(LogSeverity.Info, message)
    {
    }

    /// <summary>Creates a log entry with an explicit (literal or key-bound) severity.</summary>
    public Log(BlackboardValue<LogSeverity> severity, BlackboardValue<string> message)
    {
        Severity = severity;
        Message = message;
    }

    /// <summary>The severity binding — literal or key.</summary>
    public BlackboardValue<LogSeverity> Severity { get; }

    /// <summary>The message binding — literal or key.</summary>
    public BlackboardValue<string> Message { get; }

    /// <inheritdoc />
    public Result Execute(in BehaviorContext ctx)
    {
        Emit(in ctx);
        return Result.Success;
    }

    /// <inheritdoc />
    public ValueTask<Result> ExecuteAsync(BehaviorContext ctx, CancellationToken ct)
    {
        Emit(in ctx);
        return ResultHelpers.Success;
    }

    private void Emit(in BehaviorContext ctx)
    {
        if (!ctx.HasReporter)
        {
            return; // No observer, no string — the hot path stays allocation-free.
        }

        LogSeverity severity = ctx.Resolve(Severity);
        string message = ctx.Resolve(Message);
        ctx.Report($"[{SeverityName(severity)}] {message}");
    }

    private static string SeverityName(LogSeverity severity) => severity switch
    {
        LogSeverity.Trace => "Trace",
        LogSeverity.Warning => "Warning",
        LogSeverity.Error => "Error",
        _ => "Info",
    };
}
