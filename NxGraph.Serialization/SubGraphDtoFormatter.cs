using MessagePack;

namespace NxGraph.Serialization;

internal sealed class SubGraphDtoFormatter : GraphEntityFormatter<SubGraphDto>
{
    public static readonly SubGraphDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, SubGraphDto value,
        MessagePackSerializerOptions options)
    {
        // [OwnerIndex, GraphDto, OutcomeCodeKeyName, OutcomeNameKeyName] — the outcome key
        // names (v11) are appended after the graph so the pre-v11 2-element prefix parse is
        // untouched.
        writer.WriteArrayHeader(4);
        writer.Write(value.OwnerIndex);
        GraphDtoFormatter.Instance.Serialize(ref writer, value.Graph, options);
        writer.Write(value.OutcomeCodeKeyName);
        writer.Write(value.OutcomeNameKeyName);
    }

    public override SubGraphDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        // 2 elements = pre-v11 payload (no outcome key names); old readers never see the
        // 4-element form — the strict-greater version gate rejects v11 payloads first.
        int count = reader.ReadArrayHeader();
        if (count is not (2 or 4))
            throw new InvalidOperationException($"SubgraphDto: expected 2 or 4 elements, got {count}");
        int owner = reader.ReadInt32();
        GraphDto graph = GraphDtoFormatter.Instance.Deserialize(ref reader, options);
        string? outcomeCodeKeyName = count >= 4 ? reader.ReadString() : null;
        string? outcomeNameKeyName = count >= 4 ? reader.ReadString() : null;
        return new SubGraphDto(owner, graph, outcomeCodeKeyName, outcomeNameKeyName);
    }
}