using NxGraph.Graphs;

namespace NxGraph.Diagnostics.Replay;

public class StateMachineReplay(ReadOnlySpan<ReplayEvent> events)
{
    // Wire format: [4-byte magic "NXRP"][1-byte version][4-byte count][events...]
    // Pre-magic payloads (the original format) are rejected — the format was untrusted-input
    // unsafe (no bounds on count, unchecked enum) so backwards compat would lock the bug in.
    private static readonly byte[] Magic = "NXRP"u8.ToArray();
    private const byte FormatVersion = 1;

    // Lower bound for the wire size of one event: 1 (type) + 4 (source idx) + 1 (has-target bool)
    // + 8 (timestamp) + 1 (empty BinaryWriter string = single 0 length byte).
    private const int MinEventSize = 15;

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

        writer.Write(Magic);
        writer.Write(FormatVersion);
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
        if (data is null) throw new ArgumentNullException(nameof(data));

        using MemoryStream ms = new(data);
        using BinaryReader reader = new(ms);

        if (data.Length < Magic.Length + sizeof(byte) + sizeof(int))
        {
            throw new InvalidDataException("Replay payload is too short to contain a header.");
        }

        Span<byte> magic = stackalloc byte[Magic.Length];
        int magicRead = reader.Read(magic);
        if (magicRead != Magic.Length || !magic.SequenceEqual(Magic))
        {
            throw new InvalidDataException("Replay payload magic header does not match 'NXRP'.");
        }

        byte version = reader.ReadByte();
        if (version != FormatVersion)
        {
            throw new InvalidDataException(
                $"Replay payload version {version} is not supported (expected {FormatVersion}).");
        }

        int count = reader.ReadInt32();
        if (count < 0)
        {
            throw new InvalidDataException($"Replay payload event count {count} is negative.");
        }

        // Reject counts that cannot possibly fit in the remaining buffer. The check is a
        // floor on event size (15 bytes) so this only rejects obviously-too-large counts;
        // a payload may still be truncated mid-event and fail later, which is fine.
        long remaining = ms.Length - ms.Position;
        long maxCount = remaining / MinEventSize;
        if (count > maxCount)
        {
            throw new InvalidDataException(
                $"Replay payload claims {count} events but only {remaining} bytes remain (max {maxCount}).");
        }

        ReplayEvent[] events = new ReplayEvent[count];

        for (int i = 0; i < count; i++)
        {
            byte rawType = reader.ReadByte();
            if (!Enum.IsDefined(typeof(EventType), rawType))
            {
                throw new InvalidDataException(
                    $"Replay payload event {i} has unknown EventType byte {rawType}.");
            }
            EventType type = (EventType)rawType;
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