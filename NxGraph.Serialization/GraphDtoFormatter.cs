using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

public sealed class GraphDtoFormatter : GraphEntityFormatter<GraphDto>
{
    public static readonly GraphDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, GraphDto value, MessagePackSerializerOptions options)
    {
        // [Name, Nodes[], Transitions[]]
        writer.WriteArrayHeader(3);
        writer.Write(value.Name);
        options.Resolver.GetFormatterWithVerify<INodeDto[]>().Serialize(ref writer, value.Nodes, options);
        options.Resolver.GetFormatterWithVerify<TransitionDto[]>().Serialize(ref writer, value.Transitions, options);
    }

    public override GraphDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 3) throw new InvalidOperationException($"GraphDto: expected 3, got {count}");

        string? name = reader.ReadString();
        INodeDto[] nodes = options.Resolver.GetFormatterWithVerify<INodeDto[]>().Deserialize(ref reader, options);
        TransitionDto[] transitions =
            options.Resolver.GetFormatterWithVerify<TransitionDto[]>().Deserialize(ref reader, options);

        return new GraphDto(nodes, transitions, name);
    }
}