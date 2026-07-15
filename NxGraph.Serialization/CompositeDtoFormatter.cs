using MessagePack;

namespace NxGraph.Serialization;

internal sealed class CompositeDtoFormatter : GraphEntityFormatter<CompositeDto>
{
    public static readonly CompositeDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, CompositeDto value,
        MessagePackSerializerOptions options)
    {
        // [OwnerIndex, Kind, Mode, Children[]]
        writer.WriteArrayHeader(4);
        writer.Write(value.OwnerIndex);
        writer.Write((byte)value.Kind);
        writer.Write(value.Mode);
        writer.WriteArrayHeader(value.Children.Length);
        for (int i = 0; i < value.Children.Length; i++)
            GraphDtoFormatter.Instance.Serialize(ref writer, value.Children[i], options);
    }

    public override CompositeDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 4) throw new InvalidOperationException($"CompositeDto: expected 4 elements, got {count}");
        int owner = reader.ReadInt32();
        byte kind = reader.ReadByte();
        if (kind > (byte)CompositeKind.SyncParallel)
            throw new InvalidOperationException($"CompositeDto: unknown composite kind {kind}.");
        byte mode = reader.ReadByte();
        int childCount = reader.ReadArrayHeader();
        GraphDto[] children = new GraphDto[childCount];
        for (int i = 0; i < childCount; i++)
            children[i] = GraphDtoFormatter.Instance.Deserialize(ref reader, options);
        return new CompositeDto(owner, (CompositeKind)kind, mode, children);
    }
}

internal sealed class CompositeArrayDtoFormatter : GraphEntityFormatter<CompositeDto[]>
{
    public static readonly CompositeArrayDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, CompositeDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
            CompositeDtoFormatter.Instance.Serialize(ref writer, value[i], options);
    }

    public override CompositeDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        CompositeDto[] arr = new CompositeDto[count];
        for (int i = 0; i < count; i++)
            arr[i] = CompositeDtoFormatter.Instance.Deserialize(ref reader, options);
        return arr;
    }
}
