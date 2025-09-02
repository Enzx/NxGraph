namespace NxGraph.Diagnostics.Export;

/// <summary>Options specific to Mermaid flowchart export.</summary>
public sealed record MermaidExportOptions : ExportOptions
{
    // ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

    public FlowDirection Direction { get; init; } = FlowDirection.LeftToRight;

    /// <summary>If true, appends [#] to node labels to show indices.</summary>
    public bool ShowNodeIndices { get; init; } = true;

    /// <summary>If true, prefers NodeId.Name when present; otherwise falls back to "Node".</summary>
    public bool UseNodeNames { get; init; } = true;

    /// <summary>Add a single terminal "End" node and route terminal edges there.</summary>
    public bool AddTerminalNode { get; init; } = true;

    /// <summary>Label for the terminal node (only when <see cref="AddTerminalNode"/> is true).</summary>
    public string TerminalLabel { get; init; } = "End";

    /// <summary>
    /// Optional title comment at the top of the file. If null, exporter uses graph.Id.ToString().
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public string? Title { get; init; }

    // ReSharper restore AutoPropertyCanBeMadeGetOnly.Global
}