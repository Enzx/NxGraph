using NxGraph.Graphs;

namespace NxGraph.Diagnostics.Replay;

public class StateMachineReplay(ReadOnlySpan<ReplayEvent> events)
{
    private readonly ReplayEvent[] _events = events.ToArray();

    public IEnumerable<ReplayEvent> Events => _events;

    // Execute a callback for each event in sequence
    public void ReplayAll(Action<ReplayEvent> eventHandler)
    {
        for (int index = 0; index < _events.Length; index++)
        {
            ReplayEvent evt = _events[index];
            eventHandler(evt);
        }
    }

    // Serialize events for storage
    public byte[] Serialize()
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(_events.Length);

        foreach (ReplayEvent evt in _events)
        {
            writer.Write((byte)evt.Type);
            writer.Write(evt.SourceId.Index);
            writer.Write(evt.TargetId.HasValue);
            if (evt.TargetId.HasValue)
                writer.Write(evt.TargetId.Value.Index);
            writer.Write(evt.Timestamp);
            writer.Write(evt.Message ?? string.Empty);
        }

        return ms.ToArray();
    }

    public static ReplayEvent[] Deserialize(byte[] data)
    {
        using MemoryStream ms = new(data);
        using BinaryReader reader = new(ms);

        int count = reader.ReadInt32();
        ReplayEvent[] events = new ReplayEvent[count];

        for (int i = 0; i < count; i++)
        {
            EventType type = (EventType)reader.ReadByte();
            NodeId primary = new(reader.ReadInt32());

            NodeId? secondary = null;
            if (reader.ReadBoolean())
                secondary = new NodeId(reader.ReadInt32());

            long timestamp = reader.ReadInt64();
            string msg = reader.ReadString();


            events[i] = new ReplayEvent(type, primary, secondary, msg, timestamp);
        }

        return events;
    }
}