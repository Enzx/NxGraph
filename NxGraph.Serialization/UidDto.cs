using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

/// <summary>Sparse per-node stable UID entry (shipped with payload version 5).</summary>
internal sealed record UidDto(int Index, Guid Uid);

internal sealed class UidDtoFormatter : GraphEntityFormatter<UidDto>
{
    public static readonly UidDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, UidDto value,
        MessagePackSerializerOptions options)
    {
        // The uid rides as the canonical "D" string — identical to the JSON representation,
        // stable across MessagePack-CSharp versions, and readable in payload dumps.
        writer.WriteArrayHeader(2);
        writer.Write(value.Index);
        writer.Write(value.Uid.ToString("D"));
    }

    public override UidDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 2) throw new InvalidOperationException($"UidDto: expected 2 elements, got {count}");

        int index = reader.ReadInt32();
        string uidText = reader.ReadString() ??
                         throw new InvalidOperationException("UidDto: uid cannot be null.");
        return new UidDto(index, Guid.ParseExact(uidText, "D"));
    }
}

internal sealed class UidArrayDtoFormatter : GraphEntityFormatter<UidDto[]>
{
    public static readonly UidArrayDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, UidDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            options.Resolver.GetFormatterWithVerify<UidDto>().Serialize(ref writer, value[index], options);
        }
    }

    public override UidDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        UidDto[] uids = new UidDto[count];
        for (int i = 0; i < count; i++)
        {
            uids[i] = options.Resolver.GetFormatterWithVerify<UidDto>().Deserialize(ref reader, options);
        }

        return uids;
    }
}
