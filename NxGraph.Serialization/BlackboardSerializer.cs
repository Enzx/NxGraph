using System.Buffers;
using System.Text;
using System.Text.Json;
using MessagePack;
using NxGraph.Blackboards;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

/// <summary>
/// Serializes a single <see cref="Blackboard"/> to JSON or MessagePack as an independent
/// durability artifact: a saved flow is (graph payload, machine snapshot, one payload per
/// bound board). Each scope's board serializes separately — a global board once per world,
/// graph boards per entity.
/// <para>
/// Values are written per key via the schema's <see cref="BlackboardKeyDescriptor"/>s
/// (boxing is fine off the hot path). Custom value types plug in through the
/// caller-supplied <see cref="JsonSerializerOptions"/> / <see cref="MessagePackSerializerOptions"/>.
/// </para>
/// <para>
/// Restore is restore-into: the schema is code and cannot be reconstructed from a payload,
/// so the caller supplies a live board and the payload is applied over its defaults —
/// post-state is always defaults + payload. The payload's type names are verification data
/// only; they are never used to resolve a type.
/// </para>
/// </summary>
public sealed class BlackboardSerializer : IBlackboardJsonSerializer, IBlackboardBinarySerializer
{
    internal const int PayloadVersion = 1; // independent of SerializationVersion.Version (graph payloads)

    private readonly JsonSerializerOptions _jsonOptions;
    private readonly MessagePackSerializerOptions _binaryOptions;

    public BlackboardSerializer(JsonSerializerOptions? jsonOptions = null,
        MessagePackSerializerOptions? binaryOptions = null)
    {
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        // UntrustedData hardening, matching GraphSerializer — blackboard payloads cross the
        // same disk/network trust boundaries.
        _binaryOptions = binaryOptions ?? MessagePackSerializerOptions.Standard
            .WithSecurity(MessagePackSecurity.UntrustedData);
    }

    // ── JSON ────────────────────────────────────────────────────────────

    public async ValueTask ToJsonAsync(Blackboard blackboard, Stream destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(blackboard);
        ArgumentNullException.ThrowIfNull(destination);

        IReadOnlyList<BlackboardKeyDescriptor> keys = blackboard.Schema.Keys;
        BlackboardEntryDto[] entries = new BlackboardEntryDto[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            BlackboardKeyDescriptor descriptor = keys[i];
            JsonElement value = JsonSerializer.SerializeToElement(
                descriptor.GetValue(blackboard), descriptor.ValueType, _jsonOptions);
            entries[i] = new BlackboardEntryDto(descriptor.Name, descriptor.ValueType.FullName!, value);
        }

        BlackboardDto dto = new(entries, blackboard.Schema.Name, (int)blackboard.Schema.Scope);

        await using StreamWriter writer = new(destination, new UTF8Encoding(false), leaveOpen: true);
        string json = JsonSerializer.Serialize(dto, _jsonOptions);
        await writer.WriteAsync(json.AsMemory(), ct).ConfigureAwait(false);
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask RestoreFromJsonAsync(Blackboard target, Stream source,
        BlackboardMismatchPolicy policy = BlackboardMismatchPolicy.Strict, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        using StreamReader reader = new(source, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        string json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        BlackboardDto dto = JsonSerializer.Deserialize<BlackboardDto>(json, _jsonOptions) ??
                            throw new InvalidOperationException("Failed to parse Blackboard JSON.");

        if (dto.Version > PayloadVersion)
        {
            throw new InvalidOperationException(
                $"BlackboardDto: payload version {dto.Version} is newer than serializer version {PayloadVersion}.");
        }

        if (!HeaderMatches(target, dto.Schema, dto.Scope, policy))
        {
            return; // Skip policy: header mismatch — leave the target untouched.
        }

        // Deterministic post-state: defaults + payload. Keys absent from the payload land on
        // their registered defaults (schema evolution), and stale pre-restore values never leak.
        target.ResetToDefaults();

        foreach (BlackboardEntryDto entry in dto.Values)
        {
            if (!TryMatchEntry(target, entry.Key, entry.Type, policy, out BlackboardKeyDescriptor? descriptor))
            {
                continue;
            }

            object? value;
            try
            {
                value = entry.Value.Deserialize(descriptor!.ValueType, _jsonOptions);
            }
            catch (Exception ex)
            {
                // Corrupt values throw under both policies — Skip is for schema evolution.
                throw new InvalidOperationException(
                    $"Failed to deserialize blackboard key '{entry.Key}' as {descriptor!.ValueType}.", ex);
            }

            descriptor.SetValue(target, value);
        }
    }

    // ── MessagePack ─────────────────────────────────────────────────────
    // Wire shape (hand-written, GraphDtoFormatter style):
    //   [Version, SchemaName, Scope, Entries[]] — each entry [key, typeFullName, value].
    // Extra elements in either array are skipped on read (forward compatibility).

    public async ValueTask ToBinaryAsync(Blackboard blackboard, Stream destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(blackboard);
        ArgumentNullException.ThrowIfNull(destination);

        // MessagePackWriter is a ref struct and cannot live in an async method.
        ArrayBufferWriter<byte> buffer = new();
        WriteToBuffer(blackboard, buffer);

        await destination.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
        await destination.FlushAsync(ct).ConfigureAwait(false);
    }

    private void WriteToBuffer(Blackboard blackboard, ArrayBufferWriter<byte> buffer)
    {
        MessagePackWriter writer = new(buffer);

        writer.WriteArrayHeader(4);
        writer.Write(PayloadVersion);
        writer.Write(blackboard.Schema.Name);
        writer.Write((int)blackboard.Schema.Scope);

        IReadOnlyList<BlackboardKeyDescriptor> keys = blackboard.Schema.Keys;
        writer.WriteArrayHeader(keys.Count);
        foreach (BlackboardKeyDescriptor descriptor in keys)
        {
            writer.WriteArrayHeader(3);
            writer.Write(descriptor.Name);
            writer.Write(descriptor.ValueType.FullName);
            MessagePackSerializer.Serialize(descriptor.ValueType, ref writer,
                descriptor.GetValue(blackboard), _binaryOptions);
        }

        writer.Flush();
    }

    public async ValueTask RestoreFromBinaryAsync(Blackboard target, Stream source,
        BlackboardMismatchPolicy policy = BlackboardMismatchPolicy.Strict, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        using MemoryStream buffered = new();
        await source.CopyToAsync(buffered, ct).ConfigureAwait(false);

        RestoreFromBuffer(target, buffered.GetBuffer().AsMemory(0, (int)buffered.Length), policy);
    }

    private void RestoreFromBuffer(Blackboard target, ReadOnlyMemory<byte> payload, BlackboardMismatchPolicy policy)
    {
        MessagePackReader reader = new(payload);

        int headerCount = reader.ReadArrayHeader();
        if (headerCount < 4)
        {
            throw new InvalidOperationException(
                $"BlackboardDto: expected at least 4 header elements, found {headerCount}.");
        }

        int version = reader.ReadInt32();
        if (version > PayloadVersion)
        {
            throw new InvalidOperationException(
                $"BlackboardDto: payload version {version} is newer than serializer version {PayloadVersion}.");
        }

        string? schemaName = reader.ReadString();
        int scope = reader.ReadInt32();

        if (!HeaderMatches(target, schemaName, scope, policy))
        {
            return; // Skip policy: header mismatch — leave the target untouched.
        }

        target.ResetToDefaults();

        int entryCount = reader.ReadArrayHeader();
        for (int i = 0; i < entryCount; i++)
        {
            int entryLength = reader.ReadArrayHeader();
            if (entryLength < 3)
            {
                throw new InvalidOperationException(
                    $"BlackboardDto: entry {i} has {entryLength} elements, expected at least 3.");
            }

            string key = reader.ReadString() ??
                         throw new InvalidOperationException($"BlackboardDto: entry {i} has a null key.");
            string? typeName = reader.ReadString();

            if (!TryMatchEntry(target, key, typeName, policy, out BlackboardKeyDescriptor? descriptor))
            {
                reader.Skip(); // skip the unread value
            }
            else
            {
                object? value;
                try
                {
                    value = MessagePackSerializer.Deserialize(descriptor!.ValueType, ref reader, _binaryOptions);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to deserialize blackboard key '{key}' as {descriptor!.ValueType}.", ex);
                }

                descriptor.SetValue(target, value);
            }

            for (int extra = 3; extra < entryLength; extra++)
            {
                reader.Skip(); // forward compatibility: ignore future per-entry elements
            }
        }

        for (int extra = 4; extra < headerCount; extra++)
        {
            reader.Skip(); // forward compatibility: ignore future header elements
        }
    }

    // ── Shared policy checks ────────────────────────────────────────────

    /// <summary>
    /// Verifies the payload header against the live schema. Returns <see langword="false"/>
    /// to skip the whole restore (Skip policy); throws under Strict.
    /// </summary>
    private static bool HeaderMatches(Blackboard target, string? payloadSchemaName, int payloadScope,
        BlackboardMismatchPolicy policy)
    {
        bool nameMismatch = payloadSchemaName is not null && target.Schema.Name is not null &&
                            !string.Equals(payloadSchemaName, target.Schema.Name, StringComparison.Ordinal);
        bool scopeMismatch = payloadScope != (int)target.Schema.Scope;

        if (!nameMismatch && !scopeMismatch)
        {
            return true;
        }

        if (policy == BlackboardMismatchPolicy.Strict)
        {
            throw new InvalidOperationException(nameMismatch
                ? $"Blackboard payload was saved from schema '{payloadSchemaName}' but the target uses " +
                  $"schema '{target.Schema.Name}'."
                : $"Blackboard payload scope {(BlackboardScope)payloadScope} does not match the target " +
                  $"schema scope {target.Schema.Scope}.");
        }

        return false;
    }

    /// <summary>
    /// Matches one payload entry to a live slot. Unknown keys and changed value types throw
    /// under Strict and are skipped under Skip. The payload type name only verifies — the
    /// schema dictates the deserialization target, so payloads cannot inject types.
    /// </summary>
    private static bool TryMatchEntry(Blackboard target, string key, string? payloadTypeName,
        BlackboardMismatchPolicy policy, out BlackboardKeyDescriptor? descriptor)
    {
        if (!target.Schema.TryGetKey(key, out BlackboardKeyDescriptor found))
        {
            descriptor = null;
            return policy == BlackboardMismatchPolicy.Strict
                ? throw new InvalidOperationException(
                    $"Blackboard payload contains unknown key '{key}' (schema '{target.Schema.Name ?? "<unnamed>"}').")
                : false;
        }

        if (!string.Equals(payloadTypeName, found.ValueType.FullName, StringComparison.Ordinal))
        {
            descriptor = null;
            return policy == BlackboardMismatchPolicy.Strict
                ? throw new InvalidOperationException(
                    $"Blackboard key '{key}' was saved as '{payloadTypeName}' but the schema now declares " +
                    $"'{found.ValueType.FullName}'.")
                : false;
        }

        descriptor = found;
        return true;
    }
}
