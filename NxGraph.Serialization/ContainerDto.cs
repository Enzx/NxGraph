using MessagePack;

namespace NxGraph.Serialization;

/// <summary>
/// Payload entry for a container-codec node (payload version 6): the child graphs the
/// serializer recursed into, in <c>ISubGraphProvider.SubGraphs</c> enumeration order
/// (order is identity — the codec's <c>Deserialize</c> receives them in the same order).
/// There is no marker string for containers: the claim itself is the discriminator that
/// routes the node's ordinary logic payload to the container codec instead of the logic
/// codec.
/// </summary>
internal sealed record ContainerDto(int OwnerIndex, GraphDto[] Children);

internal sealed class ContainerDtoFormatter : GraphEntityFormatter<ContainerDto>
{
    public static readonly ContainerDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, ContainerDto value,
        MessagePackSerializerOptions options)
    {
        // [OwnerIndex, Children[]]
        writer.WriteArrayHeader(2);
        writer.Write(value.OwnerIndex);
        writer.WriteArrayHeader(value.Children.Length);
        for (int i = 0; i < value.Children.Length; i++)
            GraphDtoFormatter.Instance.Serialize(ref writer, value.Children[i], options);
    }

    public override ContainerDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 2) throw new InvalidOperationException($"ContainerDto: expected 2 elements, got {count}");
        int owner = reader.ReadInt32();
        int childCount = reader.ReadArrayHeader();
        GraphDto[] children = new GraphDto[childCount];
        for (int i = 0; i < childCount; i++)
            children[i] = GraphDtoFormatter.Instance.Deserialize(ref reader, options);
        return new ContainerDto(owner, children);
    }
}

internal sealed class ContainerArrayDtoFormatter : GraphEntityFormatter<ContainerDto[]>
{
    public static readonly ContainerArrayDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, ContainerDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
            ContainerDtoFormatter.Instance.Serialize(ref writer, value[i], options);
    }

    public override ContainerDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        ContainerDto[] arr = new ContainerDto[count];
        for (int i = 0; i < count; i++)
            arr[i] = ContainerDtoFormatter.Instance.Deserialize(ref reader, options);
        return arr;
    }
}
