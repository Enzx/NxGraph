namespace NxGraph;

/// <summary>
/// Provides helper methods for working with <see cref="Result"/> types.
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

    public static readonly ValueTask CompletedTask = default;
}