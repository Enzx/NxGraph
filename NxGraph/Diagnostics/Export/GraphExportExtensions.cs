using NxGraph.Graphs;

namespace NxGraph.Diagnostics.Export;

public static class GraphExportExtensions
{
     /// <summary>
     /// Exports the graph to Mermaid format.
     /// </summary>
     /// <param name="graph">The graph to export.</param>
     /// <param name="options">Optional export options.</param>
     /// <returns>The graph in Mermaid format.</returns>
    public static string ToMermaid(this Graph graph, MermaidExportOptions? options = null)
        => new MermaidGraphExporter().Export(graph, options);

    /// <summary>
    /// Exports the graph using the specified exporter.
    /// </summary>
    /// <param name="graph">The graph to export.</param>
    /// <param name="exporter">The exporter to use.</param>
    /// <param name="options">Optional export options.</param>
    /// <returns>The exported graph as a string.</returns>
    // ReSharper disable once UnusedMember.Global
    public static string ExportWith(this Graph graph, IGraphExporter exporter, ExportOptions? options = null)
        => exporter.Export(graph, options);
}