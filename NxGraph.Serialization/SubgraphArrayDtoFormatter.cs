using MessagePack;

namespace NxGraph.Serialization;

internal sealed class SubgraphArrayDtoFormatter : GraphEntityFormatter<SubGraphDto[]>
{
    public static readonly SubgraphArrayDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, SubGraphDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
            SubGraphDtoFormatter.Instance.Serialize(ref writer, value[i], options);
    }

    public override SubGraphDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        SubGraphDto[] arr = new SubGraphDto[count];
        for (int i = 0; i < count; i++)
            arr[i] = SubGraphDtoFormatter.Instance.Deserialize(ref reader, options);
        return arr;
    }
}