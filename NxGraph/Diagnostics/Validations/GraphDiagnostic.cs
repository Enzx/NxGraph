using NxGraph.Graphs;

namespace NxGraph.Diagnostics.Validations;

public sealed class GraphDiagnostic(Severity severity, string message, NodeId node)
{
    public Severity Severity { get; } = severity;
    public string Message { get; } = message;
    public NodeId Node { get; } = node;

    public override string ToString() => $"[{Severity}] {Node}: {Message}";
}