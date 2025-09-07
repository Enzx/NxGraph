using System.Buffers;
using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

internal sealed class NodeDtoFormatter : GraphEntityFormatter<INodeDto>
{
    public static readonly NodeDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, INodeDto value, MessagePackSerializerOptions options)
    {
        // [0.Type, 1.Index, 2.Name, 3.Logic]
        switch (value)
        {
            case NodeTextDto t:
                writer.WriteArrayHeader(3);
                writer.Write(0); // 0.Type discriminator for text node
                writer.Write(t.Index); //1.Index
                writer.Write(t.Name); //2.Name
                writer.Write(t.Logic); //3.Logic
                break;

            case NodeBinaryDto b:
                writer.WriteArrayHeader(3);
                writer.Write(1); // Type discriminator for binary node
                writer.Write(b.Index);
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

        int type = reader.ReadInt32(); // 0.Type (0 = text, 1 = binary)
        int index = reader.ReadInt32(); // 1.index
        string name = reader.ReadString() ?? string.Empty; // 2.Name

        return type switch // 3.Logic
        {
            0 => new NodeTextDto(index, name, reader.ReadString() ?? string.Empty),
            1 => new NodeBinaryDto(index, name, reader.ReadBytes()?.ToArray() ?? []),
            _ => throw new InvalidOperationException($"Unknown node type: {type}")
        };
    }
}