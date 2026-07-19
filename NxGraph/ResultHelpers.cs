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
    /// A zero-initialized <see cref="ValueTask"/> is already the completed task.
    /// </summary>
    public static readonly ValueTask CompletedTask;

    /// <summary>
    /// Represents an in-progress (not yet finished) result.
    /// </summary>
    public static readonly ValueTask<Result> InProgress = new(Result.InProgress);

    /// <inheritdoc cref="InProgress"/>
    [Obsolete("Renamed to ResultHelpers.InProgress — 'Continue' read like a command rather than a status.")]
    public static readonly ValueTask<Result> Continue = new(Result.InProgress);
}