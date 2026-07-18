using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

/// <summary>
/// Payload entry for a behavior composite node (payload version 8). <paramref name="IsSync"/>
/// discriminates the runtime (wire markers "BehaviorState"/"AsyncBehaviorState" must match);
/// <paramref name="AgentTypeName"/> is the runtime-stable name closing
/// <c>BehaviorState&lt;TAgent&gt;</c> on read, or null for the untyped composites — the agent
/// itself never rides (it is runtime context, re-attached via <c>SetAgent</c>). Entries ride
/// as the abstraction's <see cref="BehaviorEntry"/> since payload version 9, which lifted the
/// entry shape out of this file so nested bodies (<see cref="BehaviorFieldKind.Behaviors"/>)
/// reuse it recursively.
/// </summary>
internal sealed record BehaviorDto(int OwnerIndex, bool IsSync, string? AgentTypeName, BehaviorEntry[] Entries);

internal sealed class BehaviorDtoFormatter : GraphEntityFormatter<BehaviorDto>
{
    public static readonly BehaviorDtoFormatter Instance = new();

    // Read-side cap on Behaviors-field recursion (payload version 9): a deeper nesting is a
    // crafted or corrupt payload, not a real graph — without the cap an attacker can
    // stack-overflow the reader (the deep-suspend depth-cap precedent). Bindings keep their
    // own nest-one rule.
    internal const int MaxBehaviorNestingDepth = 32;

    public override void Serialize(ref MessagePackWriter writer, BehaviorDto value,
        MessagePackSerializerOptions options)
    {
        // [OwnerIndex, IsSync, AgentTypeName?, [[typeName, [[name, value], ...]], ...]] —
        // hand-rolled to pin the payload shape; field values nest one level for bindings and
        // recursively (write side; the read side caps) for nested behavior entries.
        writer.WriteArrayHeader(4);
        writer.Write(value.OwnerIndex);
        writer.Write(value.IsSync);
        writer.Write(value.AgentTypeName);
        writer.WriteArrayHeader(value.Entries.Length);
        foreach (BehaviorEntry entry in value.Entries)
        {
            WriteEntry(ref writer, entry);
        }
    }

    private static void WriteEntry(ref MessagePackWriter writer, BehaviorEntry entry)
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

    private static void WriteValue(ref MessagePackWriter writer, BehaviorFieldValue value)
    {
        // [Kind, Text?, Flag, Integer, Number, Binding?, Entries?] — the Entries slot arrived
        // with payload version 9 (pre-v9 payloads wrote 6 elements; both shapes read).
        writer.WriteArrayHeader(7);
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

        if (value.Entries is { } entries)
        {
            writer.WriteArrayHeader(entries.Length);
            foreach (BehaviorEntry entry in entries)
            {
                WriteEntry(ref writer, entry);
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
        BehaviorEntry[] entries = new BehaviorEntry[entryCount];
        for (int i = 0; i < entryCount; i++)
        {
            entries[i] = ReadEntry(ref reader, behaviorDepth: 0);
        }

        return new BehaviorDto(owner, isSync, agentTypeName, entries);
    }

    private static BehaviorEntry ReadEntry(ref MessagePackReader reader, int behaviorDepth)
    {
        int entryLength = reader.ReadArrayHeader();
        if (entryLength != 2)
            throw new InvalidOperationException(
                $"BehaviorDto: entry has {entryLength} elements, expected 2");

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
            fields[f] = new BehaviorField(name, ReadValue(ref reader, bindingDepth: 0, behaviorDepth));
        }

        return new BehaviorEntry(typeName, fields);
    }

    private static BehaviorFieldValue ReadValue(ref MessagePackReader reader, int bindingDepth, int behaviorDepth)
    {
        // Bindings nest exactly one literal value; anything deeper is a crafted payload.
        if (bindingDepth > 1)
            throw new InvalidOperationException("BehaviorDto: field value nesting exceeds the binding model.");

        int length = reader.ReadArrayHeader();
        if (length != 6 && length != 7)
            throw new InvalidOperationException(
                $"BehaviorDto: field value has {length} elements, expected 6 (pre-v9) or 7");

        byte kind = reader.ReadByte();
        if (kind > (byte)BehaviorFieldKind.Behaviors)
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
                literal = ReadValue(ref reader, bindingDepth + 1, behaviorDepth);
            }

            binding = new BehaviorBinding(keyName, literal);
        }

        // The Entries slot (payload version 9): pre-v9 values end after the binding.
        BehaviorEntry[]? entries = null;
        if (length == 7 && !reader.TryReadNil())
        {
            if (behaviorDepth >= MaxBehaviorNestingDepth)
                throw new InvalidOperationException(
                    $"BehaviorDto: nested behavior entries exceed the maximum nesting depth " +
                    $"({MaxBehaviorNestingDepth}).");

            int nestedCount = reader.ReadArrayHeader();
            entries = new BehaviorEntry[nestedCount];
            for (int i = 0; i < nestedCount; i++)
            {
                entries[i] = ReadEntry(ref reader, behaviorDepth + 1);
            }
        }

        return new BehaviorFieldValue((BehaviorFieldKind)kind, text, flag, integer, number, binding, entries);
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
