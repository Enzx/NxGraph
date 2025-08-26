using System.Buffers;
using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

internal sealed class NodeDtoFormatter : GraphEntityFormatter<INodeDto>
{
    public static readonly NodeDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, INodeDto value, MessagePackSerializerOptions options)
    {
        // [Type, Name, Logic]
        switch (value)
        {
            case NodeTextDto t:
                writer.WriteArrayHeader(3);
                writer.Write(0); // Type discriminator for text node
                writer.Write(t.Name);
                writer.Write(t.Logic);
                break;

            case NodeBinaryDto b:
                writer.WriteArrayHeader(3);
                writer.Write(1); // Type discriminator for binary node
                writer.Write(b.Name);
                writer.Write(b.Logic.Span);
                break;

            default:
                throw new InvalidOperationException($"Unsupported node type: {value.GetType().Name}");
        }
    }

    public override INodeDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 3) throw new InvalidOperationException($"INodeDto: expected 3, got {count}");

        int type = reader.ReadInt32();
        string name = reader.ReadString() ?? string.Empty;

        return type switch
        {
            0 => new NodeTextDto(name, reader.ReadString() ?? string.Empty), // index ignored
            1 => new NodeBinaryDto(name, reader.ReadBytes()?.ToArray() ?? []),
            _ => throw new InvalidOperationException($"Unknown node type: {type}")
        };
    }
}