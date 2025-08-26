using NxGraph.Graphs;


namespace NxGraph.Serialization.Tests;

public class DummyState : ILogic
{
    public string Data { get; init; } = string.Empty;

    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
        => ResultHelpers.Success;
}
