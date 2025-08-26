using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

public sealed class TransitionArrayDtoFormatter : GraphEntityFormatter<TransitionDto[]>
{
    public static readonly TransitionArrayDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, TransitionDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            options.Resolver.GetFormatterWithVerify<TransitionDto>()
                .Serialize(ref writer, value[index], options);
        }
    }

    public override TransitionDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        TransitionDto[] transitions = new TransitionDto[count];
        for (int i = 0; i < count; i++)
        {
            transitions[i] = options.Resolver.GetFormatterWithVerify<TransitionDto>()
                .Deserialize(ref reader, options);
        }

        return transitions;
    }
}