using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

internal sealed class TransitionDtoFormatter : GraphEntityFormatter<TransitionDto>
{
    public static readonly TransitionDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, TransitionDto value,
        MessagePackSerializerOptions options)
    {
        // [Destination, FailureDestination] ; slot index is the "from"
        writer.WriteArrayHeader(2);
        writer.Write(value.Destination);
        writer.Write(value.FailureDestination);
    }

    public override TransitionDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        // Version-1 payloads carried only the success destination; accept them with no
        // failure edge so old graphs stay readable.
        switch (count)
        {
            case 1:
                return new TransitionDto(reader.ReadInt32());
            case 2:
            {
                int dest = reader.ReadInt32();
                int failureDest = reader.ReadInt32();
                return new TransitionDto(dest, failureDest);
            }
            default:
                throw new InvalidOperationException($"TransitionDto: expected 1 or 2 elements, got {count}");
        }
    }
}
