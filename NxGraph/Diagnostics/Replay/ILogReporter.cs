namespace NxGraph.Diagnostics.Replay;

public interface ILogReporter
{
    Func<string, CancellationToken, ValueTask>? LogReport { get; set; }
}