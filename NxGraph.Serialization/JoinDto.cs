using MessagePack;

namespace NxGraph.Serialization;

/// <summary>
/// Payload entry for a token join node (payload version 6): the <c>JoinPolicy</c> as raw
/// <paramref name="Kind"/>/<paramref name="Count"/>. The deserializer reconstructs the
/// policy through its public factories so their validation re-runs — the wire itself stays
/// minimal, like the other sparse sections.
/// </summary>
internal sealed record JoinDto(int OwnerIndex, byte Kind, int Count);

internal sealed class JoinDtoFormatter : GraphEntityFormatter<JoinDto>
{
    public static readonly JoinDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, JoinDto value,
        MessagePackSerializerOptions options)
    {
        // [OwnerIndex, Kind, Count]
        writer.WriteArrayHeader(3);
        writer.Write(value.OwnerIndex);
        writer.Write(value.Kind);
        writer.Write(value.Count);
    }

    public override JoinDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 3) throw new InvalidOperationException($"JoinDto: expected 3 elements, got {count}");
        int owner = reader.ReadInt32();
        byte kind = reader.ReadByte();
        int policyCount = reader.ReadInt32();
        return new JoinDto(owner, kind, policyCount);
    }
}

internal sealed class JoinArrayDtoFormatter : GraphEntityFormatter<JoinDto[]>
{
    public static readonly JoinArrayDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, JoinDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
            JoinDtoFormatter.Instance.Serialize(ref writer, value[i], options);
    }

    public override JoinDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        JoinDto[] arr = new JoinDto[count];
        for (int i = 0; i < count; i++)
            arr[i] = JoinDtoFormatter.Instance.Deserialize(ref reader, options);
        return arr;
    }
}
