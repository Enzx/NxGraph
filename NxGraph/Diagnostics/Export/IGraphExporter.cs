using NxGraph.Graphs;
// ReSharper disable UnusedMember.Global

namespace NxGraph.Diagnostics.Export;

/// <summary>
/// Pluggable graph exporter interface. Implement for any format (Mermaid, DOT, PlantUML, etc).
/// </summary>
public interface IGraphExporter
{
    /// <summary>The human-readable format name, e.g. "Mermaid".</summary>
    string Format { get; }

    /// <summary>Suggested file extension including dot, e.g. ".mmd".</summary>
    string FileExtension { get; }

    /// <summary>Content type for the exported text (if applicable).</summary>
    string ContentType { get; }

    /// <summary>Export <see cref="Graph"/> to a textual representation.</summary>
    string Export(Graph graph, ExportOptions? options = null);
}