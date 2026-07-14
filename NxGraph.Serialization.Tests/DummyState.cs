using NxGraph.Graphs;


namespace NxGraph.Serialization.Tests;

public class DummyState : IAsyncLogic, ILogic
{
    public string Data { get; init; } = string.Empty;

    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
        => ResultHelpers.Success;

    // Sync-runnable too, so nested sync StateMachine round-trips can rebuild runnable graphs.
    public Result Execute() => Result.Success;
}
