using System.Runtime.CompilerServices;
using NxGraph.Blackboards;

namespace NxGraph.Fsm;

/// <summary>
/// Shared validation and rebinding for the outcome keys the machine-wrapping composites
/// (nested machines and the history states) publish a child's terminal outcome through.
/// Construction rejects Node-scoped keys (the ports rule: Node scratch resets before the
/// parent's next node runs, so the outcome would be gone before anyone could branch on it);
/// name-bound (deserialized) keys resolve per execution against the machine's bound boards'
/// schemas in Graph → Global order via <see cref="BlackboardSchema.TryResolve{T}"/>, with
/// targeted miss/type-mismatch errors in the event-entry rebind style.
/// </summary>
internal static class OutcomeKeys
{
    /// <summary>Validates a live outcome key at construction and returns its name.</summary>
    internal static string Validate<T>(in BlackboardKey<T> key, string paramName)
    {
        if (key.Schema is null)
        {
            throw new ArgumentException(
                "Invalid outcome key — obtain keys via BlackboardSchema.Register<T>(...) on a Graph- or " +
                "Global-scoped schema.", paramName);
        }

        if (key.Schema.Scope == BlackboardScope.Node)
        {
            throw new ArgumentException(
                $"Outcome key '{key.Name}' is Node-scoped — Node scratch resets before the parent's next " +
                "node runs, so the child's outcome would be gone before anyone could branch on it. " +
                "Register outcome keys on a Graph- or Global-scoped schema.", paramName);
        }

        return key.Name;
    }

    /// <summary>Validates a name-bound outcome key name (deserialization rebind form).</summary>
    internal static string ValidateName(string name, string paramName)
    {
        if (name.Length == 0)
        {
            throw new ArgumentException("Outcome key name cannot be empty.", paramName);
        }

        return name;
    }

    /// <summary>
    /// Resolves a name-bound outcome key against the bound boards' schemas (Graph, then
    /// Global — Node scope is rejected at construction and never probed). One dictionary
    /// lookup per publish, zero allocation; failures are targeted <c>NoInlining</c> throws.
    /// </summary>
    internal static BlackboardKey<T> Resolve<T>(in BlackboardContext bb, string name)
    {
        if (bb.Graph is { } graph && graph.Schema.TryResolve(name, out BlackboardKey<T> key))
        {
            return key;
        }

        if (bb.Global is { } global && global.Schema.TryResolve(name, out key))
        {
            return key;
        }

        return ThrowUnresolved<T>(bb, name);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BlackboardKey<T> ThrowUnresolved<T>(in BlackboardContext bb, string name)
    {
        // A same-name declaration with a different value type gets the precise mismatch
        // message; otherwise the name is simply absent from every probed schema.
        ReportMismatch<T>(bb.Graph, name, "Graph");
        ReportMismatch<T>(bb.Global, name, "Global");

        throw new InvalidOperationException(
            $"Outcome key '{name}' does not exist on any bound blackboard schema (checked Graph and " +
            "Global scopes) — a deserialized composite resolves its outcome keys by name against the " +
            "machine's bound boards.");
    }

    private static void ReportMismatch<T>(Blackboard? board, string name, string scopeWord)
    {
        if (board is not null && board.Schema.TryGetKey(name, out BlackboardKeyDescriptor descriptor))
        {
            throw new InvalidOperationException(
                $"Outcome key '{name}' is declared as '{descriptor.ValueType}' on the bound {scopeWord} " +
                $"schema '{board.Schema.Name ?? "<unnamed>"}' but the composite publishes " +
                $"'{typeof(T)}'.");
        }
    }
}
