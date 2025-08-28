using System.Runtime.CompilerServices;
using NxGraph.Graphs;

namespace NxGraph.Diagnostics.Validations;

public sealed class GraphValidationResult
{
    private readonly List<GraphDiagnostic> _list = [];
    public IReadOnlyList<GraphDiagnostic> Diagnostics => _list;
    public bool HasErrors => _list.Any(d => d.Severity == Severity.Error);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Add(Severity s, string msg, NodeId node) => _list.Add(new GraphDiagnostic(s, msg, node));

    public override string ToString() => string.Join(Environment.NewLine, _list.Select(x => x.ToString()));
}