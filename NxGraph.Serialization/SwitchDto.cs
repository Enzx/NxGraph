using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

/// <summary>
/// One arm of a data-built switch on the wire (payload version 10): the case's
/// <b>literal</b> value plus the arm's head node index. The literal rides as an ordinary
/// <see cref="BehaviorFieldValue"/> of kind <see cref="BehaviorFieldKind.Binding"/> — see
/// <see cref="SwitchLiteral"/> — so the case values inherit the field model's literal
/// validation and need no wire vocabulary of their own.
/// </summary>
internal sealed record SwitchCaseDto(BehaviorFieldValue Literal, int TargetIndex);

/// <summary>
/// Payload entry for a data-built <c>SwitchState&lt;T&gt;</c> node (payload version 10). The
/// typed key never rides typed: <paramref name="KeyName"/> plus the runtime-stable
/// <paramref name="ValueTypeName"/> rebuild an unbound switch that resolves its key by name
/// against the machine's bound schemas at selection (the <see cref="EventEntryDto"/> recipe).
/// <paramref name="DefaultTarget"/> is <c>-1</c> for <c>NodeId.Default</c> (a terminal
/// no-match exit). Reserved marker: "SwitchState" — one for both runtimes and every closed
/// <c>T</c>.
/// </summary>
internal sealed record SwitchDto(int OwnerIndex, string KeyName, string ValueTypeName, SwitchCaseDto[] Cases,
    int DefaultTarget);

internal sealed class SwitchDtoFormatter : GraphEntityFormatter<SwitchDto>
{
    public static readonly SwitchDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, SwitchDto value,
        MessagePackSerializerOptions options)
    {
        // [OwnerIndex, KeyName, ValueTypeName, [[literal, targetIndex], ...], DefaultTarget] —
        // hand-rolled to pin the payload shape; literals reuse the behavior field model's
        // value encoding.
        writer.WriteArrayHeader(5);
        writer.Write(value.OwnerIndex);
        writer.Write(value.KeyName);
        writer.Write(value.ValueTypeName);
        writer.WriteArrayHeader(value.Cases.Length);
        foreach (SwitchCaseDto caseDto in value.Cases)
        {
            writer.WriteArrayHeader(2);
            BehaviorDtoFormatter.WriteValue(ref writer, caseDto.Literal);
            writer.Write(caseDto.TargetIndex);
        }

        writer.Write(value.DefaultTarget);
    }

    public override SwitchDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 5) throw new InvalidOperationException($"SwitchDto: expected 5 elements, got {count}");

        int owner = reader.ReadInt32();
        string keyName = reader.ReadString() ??
                         throw new InvalidOperationException("SwitchDto: key name cannot be null.");
        string valueTypeName = reader.ReadString() ??
                               throw new InvalidOperationException("SwitchDto: value type name cannot be null.");
        int caseCount = reader.ReadArrayHeader();
        SwitchCaseDto[] cases = new SwitchCaseDto[caseCount];
        for (int i = 0; i < caseCount; i++)
        {
            int caseLength = reader.ReadArrayHeader();
            if (caseLength != 2)
                throw new InvalidOperationException(
                    $"SwitchDto: case {i} has {caseLength} elements, expected 2");

            BehaviorFieldValue literal = BehaviorDtoFormatter.ReadValue(ref reader, bindingDepth: 0,
                behaviorDepth: 0, conditionDepth: 0);
            cases[i] = new SwitchCaseDto(literal, reader.ReadInt32());
        }

        int defaultTarget = reader.ReadInt32();
        return new SwitchDto(owner, keyName, valueTypeName, cases, defaultTarget);
    }
}

internal sealed class SwitchArrayDtoFormatter : GraphEntityFormatter<SwitchDto[]>
{
    public static readonly SwitchArrayDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, SwitchDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
            SwitchDtoFormatter.Instance.Serialize(ref writer, value[i], options);
    }

    public override SwitchDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        SwitchDto[] arr = new SwitchDto[count];
        for (int i = 0; i < count; i++)
            arr[i] = SwitchDtoFormatter.Instance.Deserialize(ref reader, options);
        return arr;
    }
}
