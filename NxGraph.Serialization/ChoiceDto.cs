using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

/// <summary>
/// Payload entry for a data-built <c>ChoiceState</c> node (payload version 10):
/// <paramref name="Match"/> is the <c>ConditionMatch</c> mode (All/Any),
/// <paramref name="Conditions"/> the decision itself in evaluation order, and the two targets
/// the arms — <c>-1</c> encodes <c>NodeId.Default</c> (a terminal arm), exactly as
/// <see cref="EventEntryDto.DefaultTarget"/> does. Conditions ride the neutral field model, so
/// the standard set round-trips with zero options through the default
/// <see cref="ConditionRegistry"/>. Reserved marker: "ChoiceState" (one for both runtimes —
/// the data-built branch is a single class implementing both logic and both director
/// interfaces).
/// </summary>
internal sealed record ChoiceDto(int OwnerIndex, byte Match, ConditionEntry[] Conditions, int TrueTarget,
    int FalseTarget);

internal sealed class ChoiceDtoFormatter : GraphEntityFormatter<ChoiceDto>
{
    public static readonly ChoiceDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, ChoiceDto value,
        MessagePackSerializerOptions options)
    {
        // [OwnerIndex, Match, [[typeName, [[name, value], ...]], ...], TrueTarget, FalseTarget]
        // — hand-rolled to pin the payload shape; condition entries reuse the behavior field
        // model's encoding and nest recursively (write side; the read side caps).
        writer.WriteArrayHeader(5);
        writer.Write(value.OwnerIndex);
        writer.Write(value.Match);
        writer.WriteArrayHeader(value.Conditions.Length);
        foreach (ConditionEntry entry in value.Conditions)
        {
            BehaviorDtoFormatter.WriteConditionEntry(ref writer, entry);
        }

        writer.Write(value.TrueTarget);
        writer.Write(value.FalseTarget);
    }

    public override ChoiceDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 5) throw new InvalidOperationException($"ChoiceDto: expected 5 elements, got {count}");

        int owner = reader.ReadInt32();
        byte match = reader.ReadByte();
        int conditionCount = reader.ReadArrayHeader();
        ConditionEntry[] conditions = new ConditionEntry[conditionCount];
        for (int i = 0; i < conditionCount; i++)
        {
            conditions[i] = BehaviorDtoFormatter.ReadConditionEntry(ref reader, behaviorDepth: 0,
                conditionDepth: 0);
        }

        int trueTarget = reader.ReadInt32();
        int falseTarget = reader.ReadInt32();
        return new ChoiceDto(owner, match, conditions, trueTarget, falseTarget);
    }
}

internal sealed class ChoiceArrayDtoFormatter : GraphEntityFormatter<ChoiceDto[]>
{
    public static readonly ChoiceArrayDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, ChoiceDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
            ChoiceDtoFormatter.Instance.Serialize(ref writer, value[i], options);
    }

    public override ChoiceDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        ChoiceDto[] arr = new ChoiceDto[count];
        for (int i = 0; i < count; i++)
            arr[i] = ChoiceDtoFormatter.Instance.Deserialize(ref reader, options);
        return arr;
    }
}
