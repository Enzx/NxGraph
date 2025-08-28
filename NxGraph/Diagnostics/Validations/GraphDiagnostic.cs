using NxGraph.Graphs;

namespace NxGraph.Diagnostics.Validations;

public sealed record GraphDiagnostic(Severity Severity, string Message, NodeId Node)
{
    public override string ToString() => $"[{Severity}] {Node}: {Message}";
}