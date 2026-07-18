using MessagePack;

namespace NxGraph.Serialization;

/// <summary>
/// One entry of an event dispatcher's dispatch table on the wire (payload version 7):
/// the delivery key's registered name, the event's runtime-stable CLR type name (the same
/// rendering the blackboard payloads use), and the entry chain's head node index. Keys never
/// ride the payload (schemas do not serialize), so the read side rebuilds unbound
/// registrations resolved by name at raise time.
/// </summary>
internal sealed record EventRegistrationDto(string KeyName, string EventTypeName, int TargetIndex);

/// <summary>
/// Payload entry for an <c>EventEntryState</c> dispatcher node (payload version 7).
/// <paramref name="DefaultTarget"/> is the <c>Otherwise</c> chain's head node index, or -1
/// when the graph has no plain-run entry.
/// </summary>
internal sealed record EventEntryDto(int OwnerIndex, int DefaultTarget, EventRegistrationDto[] Entries);

internal sealed class EventEntryDtoFormatter : GraphEntityFormatter<EventEntryDto>
{
    public static readonly EventEntryDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, EventEntryDto value,
        MessagePackSerializerOptions options)
    {
        // [OwnerIndex, DefaultTarget, [[keyName, eventTypeName, targetIndex], ...]] —
        // hand-rolled to pin the payload shape.
        writer.WriteArrayHeader(3);
        writer.Write(value.OwnerIndex);
        writer.Write(value.DefaultTarget);
        writer.WriteArrayHeader(value.Entries.Length);
        for (int i = 0; i < value.Entries.Length; i++)
        {
            EventRegistrationDto entry = value.Entries[i];
            writer.WriteArrayHeader(3);
            writer.Write(entry.KeyName);
            writer.Write(entry.EventTypeName);
            writer.Write(entry.TargetIndex);
        }
    }

    public override EventEntryDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 3) throw new InvalidOperationException($"EventEntryDto: expected 3 elements, got {count}");

        int owner = reader.ReadInt32();
        int defaultTarget = reader.ReadInt32();
        int entryCount = reader.ReadArrayHeader();
        EventRegistrationDto[] entries = new EventRegistrationDto[entryCount];
        for (int i = 0; i < entryCount; i++)
        {
            int entryLength = reader.ReadArrayHeader();
            if (entryLength != 3)
                throw new InvalidOperationException(
                    $"EventEntryDto: entry {i} has {entryLength} elements, expected 3");

            string keyName = reader.ReadString() ??
                             throw new InvalidOperationException("EventEntryDto: key name cannot be null.");
            string eventTypeName = reader.ReadString() ??
                                   throw new InvalidOperationException(
                                       "EventEntryDto: event type name cannot be null.");
            int targetIndex = reader.ReadInt32();
            entries[i] = new EventRegistrationDto(keyName, eventTypeName, targetIndex);
        }

        return new EventEntryDto(owner, defaultTarget, entries);
    }
}

internal sealed class EventEntryArrayDtoFormatter : GraphEntityFormatter<EventEntryDto[]>
{
    public static readonly EventEntryArrayDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, EventEntryDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
            EventEntryDtoFormatter.Instance.Serialize(ref writer, value[i], options);
    }

    public override EventEntryDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        EventEntryDto[] arr = new EventEntryDto[count];
        for (int i = 0; i < count; i++)
            arr[i] = EventEntryDtoFormatter.Instance.Deserialize(ref reader, options);
        return arr;
    }
}
