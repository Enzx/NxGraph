using MessagePack;

namespace NxGraph.Serialization;

internal sealed class CompositeDtoFormatter : GraphEntityFormatter<CompositeDto>
{
    public static readonly CompositeDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, CompositeDto value,
        MessagePackSerializerOptions options)
    {
        // [OwnerIndex, Kind, Mode, Children[], SelectorKey] — SelectorKey (v6) is appended
        // after Children so the v4/v5 4-element prefix parse is untouched.
        writer.WriteArrayHeader(5);
        writer.Write(value.OwnerIndex);
        writer.Write((byte)value.Kind);
        writer.Write(value.Mode);
        writer.WriteArrayHeader(value.Children.Length);
        for (int i = 0; i < value.Children.Length; i++)
            GraphDtoFormatter.Instance.Serialize(ref writer, value.Children[i], options);
        writer.Write(value.SelectorKey);
    }

    public override CompositeDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        // 4 elements = pre-v6 payload (no SelectorKey); old readers never see the 5-element
        // form — the strict-greater version gate rejects v6 payloads first.
        int count = reader.ReadArrayHeader();
        if (count is not (4 or 5))
            throw new InvalidOperationException($"CompositeDto: expected 4 or 5 elements, got {count}");
        int owner = reader.ReadInt32();
        byte kind = reader.ReadByte();
        if (kind > (byte)CompositeKind.SyncDynamicParallel)
            throw new InvalidOperationException($"CompositeDto: unknown composite kind {kind}.");
        byte mode = reader.ReadByte();
        int childCount = reader.ReadArrayHeader();
        GraphDto[] children = new GraphDto[childCount];
        for (int i = 0; i < childCount; i++)
            children[i] = GraphDtoFormatter.Instance.Deserialize(ref reader, options);
        string? selectorKey = count >= 5 ? reader.ReadString() : null;
        return new CompositeDto(owner, (CompositeKind)kind, mode, children, selectorKey);
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
