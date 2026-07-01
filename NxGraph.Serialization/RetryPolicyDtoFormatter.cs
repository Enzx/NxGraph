using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

internal sealed class RetryPolicyDtoFormatter : GraphEntityFormatter<RetryPolicyDto>
{
    public static readonly RetryPolicyDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, RetryPolicyDto value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(4);
        writer.Write(value.Index);
        writer.Write(value.MaxAttempts);
        writer.Write(value.BackoffTicks);
        writer.Write(value.BackoffKind);
    }

    public override RetryPolicyDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 4) throw new InvalidOperationException($"RetryPolicyDto: expected 4 elements, got {count}");

        int index = reader.ReadInt32();
        byte maxAttempts = reader.ReadByte();
        long backoffTicks = reader.ReadInt64();
        byte backoffKind = reader.ReadByte();
        return new RetryPolicyDto(index, maxAttempts, backoffTicks, backoffKind);
    }
}

internal sealed class RetryPolicyArrayDtoFormatter : GraphEntityFormatter<RetryPolicyDto[]>
{
    public static readonly RetryPolicyArrayDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, RetryPolicyDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            options.Resolver.GetFormatterWithVerify<RetryPolicyDto>()
                .Serialize(ref writer, value[index], options);
        }
    }

    public override RetryPolicyDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        RetryPolicyDto[] policies = new RetryPolicyDto[count];
        for (int i = 0; i < count; i++)
        {
            policies[i] = options.Resolver.GetFormatterWithVerify<RetryPolicyDto>()
                .Deserialize(ref reader, options);
        }

        return policies;
    }
}
