using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

internal sealed class TransitionDtoFormatter : GraphEntityFormatter<TransitionDto>
{
    public static readonly TransitionDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, TransitionDto value,
        MessagePackSerializerOptions options)
    {
        // [Destination] ; slot index is the "from"
        writer.WriteArrayHeader(1);
        writer.Write(value.Destination);
    }

    public override TransitionDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 1) throw new InvalidOperationException($"TransitionDto: expected 1, got {count}");

        int dest = reader.ReadInt32();
        return new TransitionDto(dest); // from ignored; array slot determines it
    }
}