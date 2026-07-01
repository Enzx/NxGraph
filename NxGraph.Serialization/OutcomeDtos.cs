using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

/// <summary>Sparse per-node terminal outcome code entry.</summary>
internal sealed record OutcomeCodeDto(int NodeIndex, int Code);

/// <summary>Display name for an outcome code.</summary>
internal sealed record OutcomeNameDto(int Code, string Name);

internal sealed class OutcomeCodeDtoFormatter : GraphEntityFormatter<OutcomeCodeDto>
{
    public static readonly OutcomeCodeDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, OutcomeCodeDto value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(2);
        writer.Write(value.NodeIndex);
        writer.Write(value.Code);
    }

    public override OutcomeCodeDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 2) throw new InvalidOperationException($"OutcomeCodeDto: expected 2 elements, got {count}");

        int nodeIndex = reader.ReadInt32();
        int code = reader.ReadInt32();
        return new OutcomeCodeDto(nodeIndex, code);
    }
}

internal sealed class OutcomeCodeArrayDtoFormatter : GraphEntityFormatter<OutcomeCodeDto[]>
{
    public static readonly OutcomeCodeArrayDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, OutcomeCodeDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            options.Resolver.GetFormatterWithVerify<OutcomeCodeDto>().Serialize(ref writer, value[index], options);
        }
    }

    public override OutcomeCodeDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        OutcomeCodeDto[] codes = new OutcomeCodeDto[count];
        for (int i = 0; i < count; i++)
        {
            codes[i] = options.Resolver.GetFormatterWithVerify<OutcomeCodeDto>().Deserialize(ref reader, options);
        }

        return codes;
    }
}

internal sealed class OutcomeNameDtoFormatter : GraphEntityFormatter<OutcomeNameDto>
{
    public static readonly OutcomeNameDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, OutcomeNameDto value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(2);
        writer.Write(value.Code);
        writer.Write(value.Name);
    }

    public override OutcomeNameDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 2) throw new InvalidOperationException($"OutcomeNameDto: expected 2 elements, got {count}");

        int code = reader.ReadInt32();
        string name = reader.ReadString() ??
                      throw new InvalidOperationException("OutcomeNameDto: name cannot be null.");
        return new OutcomeNameDto(code, name);
    }
}

internal sealed class OutcomeNameArrayDtoFormatter : GraphEntityFormatter<OutcomeNameDto[]>
{
    public static readonly OutcomeNameArrayDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, OutcomeNameDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            options.Resolver.GetFormatterWithVerify<OutcomeNameDto>().Serialize(ref writer, value[index], options);
        }
    }

    public override OutcomeNameDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        OutcomeNameDto[] names = new OutcomeNameDto[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = options.Resolver.GetFormatterWithVerify<OutcomeNameDto>().Deserialize(ref reader, options);
        }

        return names;
    }
}
