using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

internal sealed class GraphDtoFormatter : GraphEntityFormatter<GraphDto>
{
    internal static readonly GraphDtoFormatter Instance = new();

    private const int VersionOneHeaderCount = 6;

    public override void Serialize(ref MessagePackWriter writer, GraphDto value, MessagePackSerializerOptions options)
    {
        // [0.Version 1.Index, 2.Name, 3.Nodes[], 4.Transitions[], 5.SubGraphs[]]
        writer.WriteArrayHeader(VersionOneHeaderCount);
        writer.Write(GraphDto.Version);
        writer.Write(value.Index);
        writer.Write(value.Name);
        options.Resolver.GetFormatterWithVerify<INodeDto[]>().Serialize(ref writer, value.Nodes, options);
        options.Resolver.GetFormatterWithVerify<TransitionDto[]>().Serialize(ref writer, value.Transitions, options);
        options.Resolver.GetFormatterWithVerify<SubGraphDto[]>().Serialize(ref writer, value.SubGraphs, options);
    }

    public override GraphDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();

        int version = reader.ReadInt32();
        switch (version)
        {
            case > SerializationVersion.Version:
                throw new InvalidOperationException($"GraphDto: version {version} is not supported.");
            case 1 when count < VersionOneHeaderCount:
                throw new InvalidOperationException(
                    $"GraphDto: expected at least {VersionOneHeaderCount} elements, got {count}");
        }

        int index = reader.ReadInt32();
        string? name = reader.ReadString();
        INodeDto[] nodes = options.Resolver.GetFormatterWithVerify<INodeDto[]>().Deserialize(ref reader, options);
        TransitionDto[] transitions =
            options.Resolver.GetFormatterWithVerify<TransitionDto[]>().Deserialize(ref reader, options);
        SubGraphDto[] subGraphs =
            options.Resolver.GetFormatterWithVerify<SubGraphDto[]>().Deserialize(ref reader, options);


        return new GraphDto(nodes, transitions, subGraphs, index, name);
    }
}