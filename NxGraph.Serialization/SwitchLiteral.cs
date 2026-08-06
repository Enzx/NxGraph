using System.Reflection;
using NxGraph.Behaviors;
using NxGraph.Graphs;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

/// <summary>
/// Encodes a data-built switch's <b>case literals</b> (payload version 10) through the neutral
/// field model, so the branch sections need no wire vocabulary of their own: a case value is
/// written as a one-field <see cref="BehaviorFieldWriter.WriteBinding{T}"/> payload and read
/// back through <see cref="BehaviorFieldReader.ReadBinding{T}"/>. Two consequences are
/// deliberate — the model's literal validation applies verbatim (a <c>SwitchState&lt;T&gt;</c>
/// whose <c>T</c> is not string/bool/int/long/float/double/enum fails at save time with the
/// field model's own targeted error, here re-thrown naming the node), and a crafted payload
/// cannot smuggle a <i>key binding</i> into a case value: a switch matches literals only.
/// </summary>
internal static class SwitchLiteral
{
    // The field model is name-addressed; a switch case carries exactly one anonymous value,
    // so the name is a constant that never reaches a user.
    private const string FieldName = "v";

    /// <summary>
    /// Encodes one boxed case value of runtime type <paramref name="valueType"/>. Cold path:
    /// the closed <c>T</c> comes from <c>ISwitchNode.ValueType</c>, so the generic writer is
    /// reached by reflection (the <c>BehaviorRegistry.ReadSetValue</c> recipe).
    /// </summary>
    internal static BehaviorFieldValue Write(Type valueType, object? value, NodeId nodeId)
    {
        MethodInfo writer = typeof(SwitchLiteral)
            .GetMethod(nameof(WriteGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(valueType);
        try
        {
            return (BehaviorFieldValue)writer.Invoke(null, [value])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is NotSupportedException inner)
        {
            // Reflection hides the field model's targeted literal error behind a
            // TargetInvocationException; rethrow naming the offending node, the way every
            // other unsupported-node error in this serializer reads.
            throw new NotSupportedException(
                $"Node '{nodeId}' is a data-built switch whose case values cannot ride the payload. " +
                inner.Message, inner);
        }
    }

    /// <summary>Decodes one case literal, rejecting the key-binding form.</summary>
    internal static T Read<T>(BehaviorFieldValue literal, string owner)
    {
        BehaviorFieldReader reader = new([new BehaviorField(FieldName, literal)]);
        BlackboardValue<T> value = reader.ReadBinding<T>(FieldName);
        if (value.IsBound)
        {
            throw new InvalidOperationException(
                $"{owner} carries a case value bound to key '{value.KeyName}'. Switch cases are literals — a " +
                "key-bound case value would make case distinctness undecidable.");
        }

        return value.Literal;
    }

    private static BehaviorFieldValue WriteGeneric<T>(object? value)
    {
        BehaviorFieldWriter writer = new();
        writer.WriteBinding<T>(FieldName, (T)value!);
        return writer.ToFields()[0].Value;
    }
}
