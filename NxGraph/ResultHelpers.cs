namespace NxGraph;

/// <summary>
/// Provides cached <see cref="ValueTask{Result}"/> singletons for zero-allocation async paths.
/// </summary>
public static class ResultHelpers
{
    /// <summary>
    /// Represents a successful result.
    /// </summary>
    public static readonly ValueTask<Result> Success = new(Result.Success);

    /// <summary>
    /// Represents a failed result.
    /// </summary>
    public static readonly ValueTask<Result> Failure = new(Result.Failure);
    /// <summary>
    /// Represents a completed operation with no meaningful result (e.g. for lifecycle events).
    /// </summary>
    public static readonly ValueTask CompletedTask = default;
}