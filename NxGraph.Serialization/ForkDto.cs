using MessagePack;

namespace NxGraph.Serialization;

/// <summary>
/// Payload entry for a token fork node (payload version 6). <paramref name="Branches"/>
/// holds the branch-head node indexes in declaration order — branch 0 continues the arriving
/// token (normative). Fork branch edges are not transitions: the owner's transition slot
/// stays empty on the wire, exactly as it is in the live graph.
/// </summary>
internal sealed record ForkDto(int OwnerIndex, int[] Branches);

internal sealed class ForkDtoFormatter : GraphEntityFormatter<ForkDto>
{
    public static readonly ForkDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, ForkDto value,
        MessagePackSerializerOptions options)
    {
        // [OwnerIndex, [branch, ...]] — hand-rolled to pin the payload shape.
        writer.WriteArrayHeader(2);
        writer.Write(value.OwnerIndex);
        writer.WriteArrayHeader(value.Branches.Length);
        for (int i = 0; i < value.Branches.Length; i++)
            writer.Write(value.Branches[i]);
    }

    public override ForkDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 2) throw new InvalidOperationException($"ForkDto: expected 2 elements, got {count}");
        int owner = reader.ReadInt32();
        int branchCount = reader.ReadArrayHeader();
        int[] branches = new int[branchCount];
        for (int i = 0; i < branchCount; i++)
            branches[i] = reader.ReadInt32();
        return new ForkDto(owner, branches);
    }
}

internal sealed class ForkArrayDtoFormatter : GraphEntityFormatter<ForkDto[]>
{
    public static readonly ForkArrayDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, ForkDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
            ForkDtoFormatter.Instance.Serialize(ref writer, value[i], options);
    }

    public override ForkDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        ForkDto[] arr = new ForkDto[count];
        for (int i = 0; i < count; i++)
            arr[i] = ForkDtoFormatter.Instance.Deserialize(ref reader, options);
        return arr;
    }
}
