namespace NxGraph.Behaviors;

/// <summary>
/// Severity of a <see cref="Log"/> behavior's report message. The <see cref="Log"/> behavior
/// defaults to <see cref="Info"/> when no severity is given.
/// </summary>
public enum LogSeverity : byte
{
    /// <summary>Fine-grained diagnostic detail.</summary>
    Trace = 0,

    /// <summary>Ordinary informational message — the <see cref="Log"/> behavior's default.</summary>
    Info = 1,

    /// <summary>Something unexpected but recoverable.</summary>
    Warning = 2,

    /// <summary>A genuine error condition.</summary>
    Error = 3,
}
