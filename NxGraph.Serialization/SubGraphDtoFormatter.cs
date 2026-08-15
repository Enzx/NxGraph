using MessagePack;

namespace NxGraph.Serialization;

internal sealed class SubGraphDtoFormatter : GraphEntityFormatter<SubGraphDto>
{
    public static readonly SubGraphDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, SubGraphDto value,
        MessagePackSerializerOptions options)
    {
        // [OwnerIndex, GraphDto, OutcomeCodeKeyName, OutcomeNameKeyName, OwnsBoard, Ports] —
        // the outcome key names (v11) and the ports fields (v12) are appended after the graph
        // so the older prefix parses are untouched.
        writer.WriteArrayHeader(6);
        writer.Write(value.OwnerIndex);
        GraphDtoFormatter.Instance.Serialize(ref writer, value.Graph, options);
        writer.Write(value.OutcomeCodeKeyName);
        writer.Write(value.OutcomeNameKeyName);
        writer.Write(value.OwnsBoard);
        SubGraphPortWire.WritePortList(ref writer, value.Ports);
    }

    public override SubGraphDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        // 2 elements = pre-v11 payload (no outcome key names), 4 = v11 (no ports fields);
        // old readers never see the longer forms — the strict-greater version gate rejects
        // newer payloads first.
        int count = reader.ReadArrayHeader();
        if (count is not (2 or 4 or 6))
            throw new InvalidOperationException($"SubgraphDto: expected 2, 4 or 6 elements, got {count}");
        int owner = reader.ReadInt32();
        GraphDto graph = GraphDtoFormatter.Instance.Deserialize(ref reader, options);
        string? outcomeCodeKeyName = count >= 4 ? reader.ReadString() : null;
        string? outcomeNameKeyName = count >= 4 ? reader.ReadString() : null;
        bool ownsBoard = count >= 6 && reader.ReadBoolean();
        SubGraphPortDto[]? ports = count >= 6 ? SubGraphPortWire.ReadPortList(ref reader) : null;
        return new SubGraphDto(owner, graph, outcomeCodeKeyName, outcomeNameKeyName, ownsBoard, ports);
    }
}
