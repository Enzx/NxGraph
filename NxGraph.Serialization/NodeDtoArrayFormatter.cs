using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

public sealed class NodeDtoArrayFormatter : GraphEntityFormatter<INodeDto[]>
{
    public static readonly NodeDtoArrayFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, INodeDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            options.Resolver.GetFormatterWithVerify<INodeDto>()
                .Serialize(ref writer, value[index], options);
        }
    }

    public override INodeDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        INodeDto[] nodes = new INodeDto[count];
        for (int i = 0; i < count; i++)
        {
            nodes[i] = options.Resolver.GetFormatterWithVerify<INodeDto>()
                .Deserialize(ref reader, options);
        }

        return nodes;
    }
}