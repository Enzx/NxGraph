using NxGraph.Graphs;

namespace NxGraph.Diagnostics.Replay;

public readonly struct ReplayEvent(
    EventType type,
    NodeId sourceIdId,
    NodeId? targetIdId = null,
    string? message = null,
    long timestamp = -1)
{
    public EventType Type { get; } = type;
    public NodeId SourceId { get; } = sourceIdId;
    public NodeId? TargetId { get; } = targetIdId;
    public string? Message { get; } = message;
    public long Timestamp { get; } = timestamp == -1 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : timestamp;
}