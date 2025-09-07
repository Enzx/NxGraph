using MessagePack;

namespace NxGraph.Serialization;

internal sealed class SubGraphDtoFormatter : GraphEntityFormatter<SubGraphDto>
{
    public static readonly SubGraphDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, SubGraphDto value,
        MessagePackSerializerOptions options)
    {
        // [OwnerIndex, GraphDto]
        writer.WriteArrayHeader(2);
        writer.Write(value.OwnerIndex);
        GraphDtoFormatter.Instance.Serialize(ref writer, value.Graph, options);
    }

    public override SubGraphDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 2) throw new InvalidOperationException($"SubgraphDto: expected 2, got {count}");
        int owner = reader.ReadInt32();
        GraphDto graph = GraphDtoFormatter.Instance.Deserialize(ref reader, options);
        return new SubGraphDto(owner, graph);
    }
}