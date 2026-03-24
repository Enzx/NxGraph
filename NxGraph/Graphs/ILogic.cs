using System.Runtime.CompilerServices;

namespace NxGraph.Graphs;

/// <summary>
/// Synchronous counterpart of <see cref="IAsyncLogic"/>.
/// Implementations must be entirely synchronous – no <c>Task</c>, no <c>ValueTask</c>,
/// no <c>CancellationToken</c>.  Designed for hot-path, zero-allocation execution.
/// </summary>
public interface ILogic
{
    /// <summary>
    /// Executes the logic synchronously and returns a <see cref="Result"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Result Execute();
}

