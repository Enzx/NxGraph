using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

/// <summary>
/// One entry of a serialized behavior composite (payload version 8): the behavior's
/// runtime-stable CLR type name (the same rendering the blackboard payloads use) plus its
/// fields in the neutral field model.
/// </summary>
internal sealed record BehaviorEntryDto(string BehaviorTypeName, BehaviorField[] Fields);

/// <summary>
/// Payload entry for a behavior composite node (payload version 8). <paramref name="IsSync"/>
/// discriminates the runtime (wire markers "BehaviorState"/"AsyncBehaviorState" must match);
/// <paramref name="AgentTypeName"/> is the runtime-stable name closing
/// <c>BehaviorState&lt;TAgent&gt;</c> on read, or null for the untyped composites — the agent
/// itself never rides (it is runtime context, re-attached via <c>SetAgent</c>).
/// </summary>
internal sealed record BehaviorDto(int OwnerIndex, bool IsSync, string? AgentTypeName, BehaviorEntryDto[] Entries);

internal sealed class BehaviorDtoFormatter : GraphEntityFormatter<BehaviorDto>
{
    public static readonly BehaviorDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, BehaviorDto value,
        MessagePackSerializerOptions options)
    {
        // [OwnerIndex, IsSync, AgentTypeName?, [[typeName, [[name, value], ...]], ...]] —
        // hand-rolled to pin the payload shape; field values nest one level for bindings.
        writer.WriteArrayHeader(4);
        writer.Write(value.OwnerIndex);
        writer.Write(value.IsSync);
        writer.Write(value.AgentTypeName);
        writer.WriteArrayHeader(value.Entries.Length);
        foreach (BehaviorEntryDto entry in value.Entries)
        {
            writer.WriteArrayHeader(2);
            writer.Write(entry.BehaviorTypeName);
            writer.WriteArrayHeader(entry.Fields.Length);
            foreach (BehaviorField field in entry.Fields)
            {
                writer.WriteArrayHeader(2);
                writer.Write(field.Name);
                WriteValue(ref writer, field.Value);
            }
        }
    }

    private static void WriteValue(ref MessagePackWriter writer, BehaviorFieldValue value)
    {
        // [Kind, Text?, Flag, Integer, Number, Binding?]
        writer.WriteArrayHeader(6);
        writer.Write((byte)value.Kind);
        writer.Write(value.Text);
        writer.Write(value.Flag);
        writer.Write(value.Integer);
        writer.Write(value.Number);
        if (value.Binding is { } binding)
        {
            // [KeyName?, Literal?]
            writer.WriteArrayHeader(2);
            writer.Write(binding.KeyName);
            if (binding.Literal is { } literal)
            {
                WriteValue(ref writer, literal);
            }
            else
            {
                writer.WriteNil();
            }
        }
        else
        {
            writer.WriteNil();
        }
    }

    public override BehaviorDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 4) throw new InvalidOperationException($"BehaviorDto: expected 4 elements, got {count}");

        int owner = reader.ReadInt32();
        bool isSync = reader.ReadBoolean();
        string? agentTypeName = reader.ReadString();
        int entryCount = reader.ReadArrayHeader();
        BehaviorEntryDto[] entries = new BehaviorEntryDto[entryCount];
        for (int i = 0; i < entryCount; i++)
        {
            int entryLength = reader.ReadArrayHeader();
            if (entryLength != 2)
                throw new InvalidOperationException(
                    $"BehaviorDto: entry {i} has {entryLength} elements, expected 2");

            string typeName = reader.ReadString() ??
                              throw new InvalidOperationException(
                                  "BehaviorDto: behavior type name cannot be null.");
            int fieldCount = reader.ReadArrayHeader();
            BehaviorField[] fields = new BehaviorField[fieldCount];
            for (int f = 0; f < fieldCount; f++)
            {
                int fieldLength = reader.ReadArrayHeader();
                if (fieldLength != 2)
                    throw new InvalidOperationException(
                        $"BehaviorDto: field {f} has {fieldLength} elements, expected 2");

                string name = reader.ReadString() ??
                              throw new InvalidOperationException("BehaviorDto: field name cannot be null.");
                fields[f] = new BehaviorField(name, ReadValue(ref reader, depth: 0));
            }

            entries[i] = new BehaviorEntryDto(typeName, fields);
        }

        return new BehaviorDto(owner, isSync, agentTypeName, entries);
    }

    private static BehaviorFieldValue ReadValue(ref MessagePackReader reader, int depth)
    {
        // Bindings nest exactly one literal value; anything deeper is a crafted payload.
        if (depth > 1)
            throw new InvalidOperationException("BehaviorDto: field value nesting exceeds the binding model.");

        int length = reader.ReadArrayHeader();
        if (length != 6)
            throw new InvalidOperationException($"BehaviorDto: field value has {length} elements, expected 6");

        byte kind = reader.ReadByte();
        if (kind > (byte)BehaviorFieldKind.Binding)
            throw new InvalidOperationException($"BehaviorDto: unknown field kind {kind}.");

        string? text = reader.ReadString();
        bool flag = reader.ReadBoolean();
        long integer = reader.ReadInt64();
        double number = reader.ReadDouble();

        BehaviorBinding? binding = null;
        if (reader.TryReadNil())
        {
            // no binding payload
        }
        else
        {
            int bindingLength = reader.ReadArrayHeader();
            if (bindingLength != 2)
                throw new InvalidOperationException(
                    $"BehaviorDto: binding has {bindingLength} elements, expected 2");

            string? keyName = reader.ReadString();
            BehaviorFieldValue? literal = null;
            if (!reader.TryReadNil())
            {
                literal = ReadValue(ref reader, depth + 1);
            }

            binding = new BehaviorBinding(keyName, literal);
        }

        return new BehaviorFieldValue((BehaviorFieldKind)kind, text, flag, integer, number, binding);
    }
}

internal sealed class BehaviorArrayDtoFormatter : GraphEntityFormatter<BehaviorDto[]>
{
    public static readonly BehaviorArrayDtoFormatter Instance = new();

    public override void Serialize(ref MessagePackWriter writer, BehaviorDto[] value,
        MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(value.Length);
        for (int i = 0; i < value.Length; i++)
            BehaviorDtoFormatter.Instance.Serialize(ref writer, value[i], options);
    }

    public override BehaviorDto[] Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        BehaviorDto[] arr = new BehaviorDto[count];
        for (int i = 0; i < count; i++)
            arr[i] = BehaviorDtoFormatter.Instance.Deserialize(ref reader, options);
        return arr;
    }
}
