using NxGraph.Authoring;
using NxGraph.Diagnostics.Export;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
[Category("export_mermaid")]
public class MermaidGraphExporterTests
{
    [Test]
    public void should_emit_header_and_start_node_for_single_terminal_graph()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .Build();

        MermaidGraphExporter exporter = new();

        string mmd = exporter.Export(graph);

        Assert.That(mmd, Does.Contain("flowchart LR"), "Default direction should be LR.");
        // No space between node id and shape: n0([ not "n0 ([" 
        Assert.That(mmd, Does.Contain("\n  n0(["), "Start node must have no space between id and shape.");
        Assert.That(mmd, Does.Not.Contain("n0 (["));
        Assert.That(mmd, Does.Not.Contain("n0 ("));

        // Capital End node emitted
        Assert.That(mmd, Does.Contain("\n  End((\"End\"))"));
        Assert.That(mmd, Does.Contain("n0 --> End"));
        Assert.That(mmd.Contains(" end((") || mmd.Contains(" --> end"), Is.False,
            "Terminal symbol must be capitalized 'End'.");
    }

    [Test]
    public void should_link_linear_two_nodes_and_use_capital_End()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .Build();

        MermaidGraphExporter exporter = new();

        string mmd = exporter.Export(graph);

        Assert.That(mmd, Does.Contain("\n  n0([")); // start node stadium
        Assert.That(mmd, Does.Contain("\n  n1[")); // rectangle for regular node
        Assert.That(mmd, Does.Not.Contain("n1 [")); // no space

        Assert.That(mmd, Does.Contain("n0 --> n1"));
        Assert.That(mmd, Does.Contain("n1 --> End"));
        Assert.That(mmd, Does.Contain("\n  End((\"End\"))"));
    }

    [Test]
    public void should_support_top_to_bottom_direction()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .Build();

        MermaidGraphExporter exporter = new();
        MermaidExportOptions opts = new()
        {
            Direction = FlowDirection.TopToBottom
        };

        string mmd = exporter.Export(graph, opts);

        Assert.That(mmd, Does.Contain("flowchart TB"));
    }

    [Test]
    public void should_disable_terminal_node_when_opted_out()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .Build();

        MermaidGraphExporter exporter = new();
        MermaidExportOptions opts = new()
        {
            AddTerminalNode = false
        };

        string mmd = exporter.Export(graph, opts);

        Assert.That(mmd, Does.Not.Contain("\n  End(("), "End node must not be emitted when AddTerminalNode is false.");
        Assert.That(mmd, Does.Not.Contain(" --> End"), "No arrow to End when terminal is disabled.");
    }

    [Test]
    public void should_emit_custom_terminal_label_but_keep_symbol_End()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .Build();

        MermaidGraphExporter exporter = new();
        MermaidExportOptions opts = new()
        {
            TerminalLabel = "Done"
        };

        string mmd = exporter.Export(graph, opts);

        // Label should change, symbol remains "End"
        Assert.That(mmd, Does.Contain("\n  End((\"Done\"))"));
        Assert.That(mmd, Does.Contain("n1 --> End"));
    }

    [Test]
    public void should_hide_indices_when_option_disabled()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(_ => ResultHelpers.Success)
            .Build();

        MermaidGraphExporter exporter = new();
        MermaidExportOptions opts = new()
        {
            ShowNodeIndices = false
        };

        string mmd = exporter.Export(graph, opts);

        Assert.That(mmd, Does.Not.Contain(" [0]"), "Index [0] should be hidden in labels.");
        Assert.That(mmd, Does.Not.Contain(" [1]"), "Index [1] should be hidden in labels.");
    }

    [Test]
    public void should_include_title_comment_when_provided()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .Build();

        MermaidGraphExporter exporter = new();
        MermaidExportOptions opts = new()
        {
            Title = "Enemy AI FSM"
        };

        string mmd = exporter.Export(graph, opts);

        Assert.That(mmd, Does.Contain("%% NxGraph → Mermaid export: Enemy AI FSM"));
    }

    [Test]
    public void director_name_with_quotes_is_escaped_exactly_once()
    {
        // Regression: BuildNodeLabel escapes the label, and the director-rhombus branch used
        // to escape it a second time — names containing '"' or '\' rendered mangled
        // (\\\" instead of \") on If/Switch nodes only.
        GraphBuilder builder = new();
        NodeId start = builder.AddNode(new RelayState(() => Result.Success), isStart: true);
        NodeId thenBranch = builder.AddNode(new RelayState(() => Result.Success)); // n1
        NodeId elseBranch = builder.AddNode(new RelayState(() => Result.Success)); // n2
        NodeId choice = builder.AddNode(new ChoiceState(() => true, thenBranch, elseBranch)); // n3
        builder.SetName(choice, "say \"hi\"");
        builder.AddTransition(start, choice);
        Graph graph = builder.Build(throwOnError: false);

        string mmd = new MermaidGraphExporter().Export(graph);

        Assert.Multiple(() =>
        {
            Assert.That(mmd, Does.Contain("n3{\"say \\\"hi\\\" [3]\"}"),
                "The director label must carry each quote escaped exactly once.");
            Assert.That(mmd, Does.Not.Contain(@"\\"),
                "No double backslashes — the name has none, so any pair proves double-escaping.");
        });
    }

    [Test]
    public void should_render_director_nodes_with_curly_braces_and_no_space()
    {
        const bool condition = true;
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success) // n0
            .If(() => condition)
            .ThenAsync(_ => ResultHelpers.Success)
            .ElseAsync(_ => ResultHelpers.Success)
            .Build();

        MermaidGraphExporter exporter = new();

        // Act
        string mmd = exporter.Export(graph);

        // Assert: n1{"..."} with NO space between id and '{'
        Assert.That(mmd, Does.Contain("\n  n3{\""),
            "Director node must use {} shape with quotes and no space after id.");
        Assert.That(mmd, Does.Not.Contain("n1 {\""));

        // Regular nodes should keep their shapes without spaces
        Assert.That(mmd, Does.Contain("\n  n0(["));
        Assert.That(mmd, Does.Not.Contain("n0 ("));
    }
}
