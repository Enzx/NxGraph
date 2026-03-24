using System.Runtime.CompilerServices;

namespace NxGraph.Graphs;

/// <summary>
/// Adapts an <see cref="ILogic"/> (synchronous) to the <see cref="IAsyncLogic"/> contract
/// so that synchronous nodes can participate in a <see cref="Graph"/> that is consumed by
/// an async runtime.
/// <para>
/// The <see cref="ExecuteAsync"/> call wraps the synchronous <see cref="ILogic.Execute"/>
/// result in a completed <see cref="ValueTask{Result}"/> (zero-allocation on .NET 8+).
/// </para>
/// </summary>
public sealed class SyncLogicAdapter(ILogic logic) : IAsyncLogic
{
    /// <summary>
    /// The underlying synchronous logic being adapted.
    /// </summary>
    public ILogic Logic { get; } = logic ?? throw new ArgumentNullException(nameof(logic));

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
    {
        return new ValueTask<Result>(Logic.Execute());
    }
}

