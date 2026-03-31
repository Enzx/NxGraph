using NxGraph.Graphs;

namespace NxGraph.Diagnostics.Replay;

public readonly struct ReplayEvent(
    EventType type,
    NodeId sourceId,
    NodeId? targetId = null,
    string? message = null,
    long timestamp = -1)
{
    public EventType Type { get; } = type;
    public NodeId SourceId { get; } = sourceId;
    public NodeId? TargetId { get; } = targetId;
    public string? Message { get; } = message;
    public long Timestamp { get; } = timestamp == -1 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : timestamp;
}