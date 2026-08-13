using System.Reflection;
using MessagePack;
using NxGraph.Behaviors;
using NxGraph.Fsm;
using NxGraph.Graphs;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

/// <summary>
/// One declared sub-graph port on the wire (payload version 12): direction (0 = input,
/// 1 = output), the source — a key <b>name</b> (parent-side for inputs, child-side for
/// outputs) or, for a literal input, a field-model literal (the <see cref="SwitchLiteral"/>
/// recipe: kind <see cref="BehaviorFieldKind.Binding"/> validation applies verbatim) — the
/// target key name, and the runtime-stable value type name that closes the typed pair on
/// read. Keys never ride typed: a deserialized port resolves its names per application
/// against the boards bound at that moment, with targeted miss/type-mismatch errors.
/// </summary>
internal sealed record SubGraphPortDto(byte Direction, string? SourceKeyName,
    BehaviorFieldValue? SourceLiteral, string TargetName, string ValueTypeName);

/// <summary>
/// The ports declaration of one region of a composite (payload version 12): the region index
/// (0 for the single child of a history composite) plus its port list. The entry's
/// <b>presence</b> is the owns-board flag — a region with an entry runs on its own board even
/// when the port list is empty; absent regions keep the shared-board default.
/// </summary>
internal sealed record RegionPortsDto(int RegionIndex, SubGraphPortDto[] Ports);

/// <summary>
/// Hand-rolled MessagePack encoding for the port entries — shared by
/// <see cref="SubGraphDtoFormatter"/> and <see cref="CompositeDtoFormatter"/>, which own the
/// enclosing array shapes. Literals reuse the behavior field model's value encoding.
/// </summary>
internal static class SubGraphPortWire
{
    internal static void WritePortList(ref MessagePackWriter writer, SubGraphPortDto[]? ports)
    {
        if (ports is null)
        {
            writer.WriteNil();
            return;
        }

        writer.WriteArrayHeader(ports.Length);
        foreach (SubGraphPortDto port in ports)
        {
            // [Direction, SourceKeyName?, SourceLiteral?, TargetName, ValueTypeName]
            writer.WriteArrayHeader(5);
            writer.Write(port.Direction);
            writer.Write(port.SourceKeyName);
            if (port.SourceLiteral is { } literal)
            {
                BehaviorDtoFormatter.WriteValue(ref writer, literal);
            }
            else
            {
                writer.WriteNil();
            }

            writer.Write(port.TargetName);
            writer.Write(port.ValueTypeName);
        }
    }

    internal static SubGraphPortDto[]? ReadPortList(ref MessagePackReader reader)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        int count = reader.ReadArrayHeader();
        SubGraphPortDto[] ports = new SubGraphPortDto[count];
        for (int i = 0; i < count; i++)
        {
            int length = reader.ReadArrayHeader();
            if (length != 5)
                throw new InvalidOperationException(
                    $"SubGraphPortDto: expected 5 elements, got {length}");

            byte direction = reader.ReadByte();
            string? sourceKeyName = reader.ReadString();
            BehaviorFieldValue? sourceLiteral = null;
            if (!reader.TryReadNil())
            {
                sourceLiteral = BehaviorDtoFormatter.ReadValue(ref reader, bindingDepth: 0,
                    behaviorDepth: 0, conditionDepth: 0);
            }

            string targetName = reader.ReadString() ??
                                throw new InvalidOperationException(
                                    "SubGraphPortDto: target name cannot be null.");
            string valueTypeName = reader.ReadString() ??
                                   throw new InvalidOperationException(
                                       "SubGraphPortDto: value type name cannot be null.");
            ports[i] = new SubGraphPortDto(direction, sourceKeyName, sourceLiteral, targetName, valueTypeName);
        }

        return ports;
    }

    internal static void WriteRegionPorts(ref MessagePackWriter writer, RegionPortsDto[] regionPorts)
    {
        writer.WriteArrayHeader(regionPorts.Length);
        foreach (RegionPortsDto entry in regionPorts)
        {
            writer.WriteArrayHeader(2);
            writer.Write(entry.RegionIndex);
            WritePortList(ref writer, entry.Ports);
        }
    }

    internal static RegionPortsDto[] ReadRegionPorts(ref MessagePackReader reader)
    {
        int count = reader.ReadArrayHeader();
        RegionPortsDto[] entries = new RegionPortsDto[count];
        for (int i = 0; i < count; i++)
        {
            int length = reader.ReadArrayHeader();
            if (length != 2)
                throw new InvalidOperationException(
                    $"RegionPortsDto: expected 2 elements, got {length}");

            int regionIndex = reader.ReadInt32();
            SubGraphPortDto[] ports = ReadPortList(ref reader) ??
                                      throw new InvalidOperationException(
                                          "RegionPortsDto: port list cannot be nil.");
            entries[i] = new RegionPortsDto(regionIndex, ports);
        }

        return entries;
    }
}

/// <summary>
/// Encodes a literal input-port source through the neutral field model (the
/// <see cref="SwitchLiteral"/> recipe): the value is written as a one-field
/// <c>WriteBinding</c> payload and read back through <c>ReadBinding</c>, so port literals
/// inherit the field model's validation — a value type outside string/bool/int/long/float/
/// double/enum fails at save time with a targeted error naming the node.
/// </summary>
internal static class PortLiteral
{
    private const string FieldName = "v";

    /// <summary>Encodes one boxed literal of runtime type <paramref name="valueType"/> (cold path).</summary>
    internal static BehaviorFieldValue Write(Type valueType, object? value, NodeId nodeId)
    {
        MethodInfo writer = typeof(PortLiteral)
            .GetMethod(nameof(WriteGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(valueType);
        try
        {
            return (BehaviorFieldValue)writer.Invoke(null, [value])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is NotSupportedException inner)
        {
            throw new NotSupportedException(
                $"Node '{nodeId}' declares a sub-graph input port whose literal cannot ride the payload. " +
                inner.Message, inner);
        }
    }

    /// <summary>Decodes one literal, rejecting the key-binding form (keys ride as names, never as literals).</summary>
    internal static BlackboardValue<T> Read<T>(BehaviorFieldValue literal, string owner)
    {
        BehaviorFieldReader reader = new([new BehaviorField(FieldName, literal)]);
        BlackboardValue<T> value = reader.ReadBinding<T>(FieldName);
        if (value.IsBound)
        {
            throw new InvalidOperationException(
                $"{owner} carries an input port literal bound to key '{value.KeyName}' — key-form sources " +
                "ride as source key names, never inside the literal slot.");
        }

        return value;
    }

    private static BehaviorFieldValue WriteGeneric<T>(object? value)
    {
        BehaviorFieldWriter writer = new();
        writer.WriteBinding<T>(FieldName, (T)value!);
        return writer.ToFields()[0].Value;
    }
}
