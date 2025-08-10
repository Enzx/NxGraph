using System.Diagnostics;
using NxGraph.Authoring;
using NxGraph.Diagnostics;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
public class GraphValidatorTests
{
    private static Graph BuildSimpleGraph(out GraphBuilder builder)
    {
        builder = new GraphBuilder();
        NodeId start = builder.AddNode(new RelayState(_ => ResultHelpers.Success), isStart: true);
        NodeId end = builder.AddNode(new RelayState(_ => ResultHelpers.Success));
        builder.AddTransition(start, end);
        return builder.Build(throwOnError: false);
    }

    [Test]
    public void ValidGraph_ShouldHaveNoErrors_AndReachTerminal()
    {
        Graph graph = BuildSimpleGraph(out GraphBuilder builder);

        GraphValidationResult result = graph.Validate(new GraphValidationOptions
        {
            AllNodes = builder.GetAllNodeIds(),
            WarnOnSelfLoop = true,
            StrictNoTerminalPath = true
        });

        Assert.That(result.HasErrors, Is.False, "Expected no validation errors for a simple valid graph.");
    }

    [Test]
    public void UnreachableNode_ShouldBeReported_AsWarning()
    {
        GraphBuilder builder = new();
        NodeId start = builder.AddNode(new RelayState(_ => ResultHelpers.Success), true);
        NodeId end = builder.AddNode(new RelayState(_ => ResultHelpers.Success));
        NodeId unreachable = builder.AddNode(new RelayState(_ => ResultHelpers.Success));

        builder.AddTransition(start, end);
        Graph graph = builder.Build(throwOnError: false);

        GraphValidationResult res = graph.Validate(new GraphValidationOptions { AllNodes = builder.GetAllNodeIds() });

        Assert.That(
            res.Diagnostics.Any(d =>
                d.Severity == Severity.Warning && d.Node.Index == unreachable.Index &&
                d.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase)), Is.True,
            "Expected an unreachable node warning for 'Unreachable'.");
    }

    [Test]
    public void DuplicateNames_ShouldBeReported_AsWarnings()
    {
        GraphBuilder builder = new();
        NodeId a1 = builder.AddNode(new RelayState(_ => ResultHelpers.Success), isStart: true);
        NodeId a2 = builder.AddNode(new RelayState(_ => ResultHelpers.Success));

        builder.SetName(a1, "A");
        builder.SetName(a2, "A"); // duplicate name

        builder.AddTransition(a1, a2);

        Graph graph = builder.Build(throwOnError: false);

        GraphValidationResult res = graph.Validate(new GraphValidationOptions { AllNodes = builder.GetAllNodeIds() });
        foreach (var r in res.Diagnostics)
        {
            Debug.WriteLine($"{r.Severity}: {r.Message} (Node: {r.Node})");
        }

        Assert.That(
            res.Diagnostics.Any(d =>
                d.Severity == Severity.Warning &&
                d.Message.Contains("Duplicate node name", StringComparison.OrdinalIgnoreCase)), Is.True,
            "Expected duplicate name warnings for nodes named 'A'.");
    }

    [Test]
    public void SelfLoop_ShouldBeWarning_WhenEnabled()
    {
        GraphBuilder builder = new();
        NodeId loop = builder.AddNode(new RelayState(_ => ResultHelpers.Success), isStart: true);
        builder.AddTransition(loop, loop); // self-loop

        Graph graph = builder.Build(throwOnError: false);
        GraphValidationResult res = graph.Validate(new GraphValidationOptions
            { AllNodes = builder.GetAllNodeIds(), WarnOnSelfLoop = true });

        Assert.That(
            res.Diagnostics.Any(d =>
                d.Severity == Severity.Warning && d.Node.Index == loop.Index &&
                d.Message.Contains("Self-loop", StringComparison.OrdinalIgnoreCase)), Is.True,
            "Expected a self-loop warning for node 'Loop'.");
    }

    [Test]
    public void NoTerminalPath_ShouldWarnOrError_BasedOnOption()
    {
        GraphBuilder builder = new();
        NodeId n1 = builder.AddNode(new RelayState(_ => ResultHelpers.Success), isStart: true);
        NodeId n2 = builder.AddNode(new RelayState(_ => ResultHelpers.Success));
        builder.AddTransition(n1, n2);
        builder.AddTransition(n2, n1); // cycle; no terminal

        Graph graph = builder.Build(throwOnError: false);

        GraphValidationResult warn = graph.Validate(new GraphValidationOptions
            { AllNodes = builder.GetAllNodeIds(), StrictNoTerminalPath = false });
        Assert.That(
            warn.Diagnostics.Any(d =>
                d.Severity == Severity.Warning &&
                d.Message.Contains("No terminal path", StringComparison.OrdinalIgnoreCase)), Is.True,
            "Expected a warning when no terminal path exists and StrictNoTerminalPath=false.");

        GraphValidationResult err = graph.Validate(new GraphValidationOptions
            { AllNodes = builder.GetAllNodeIds(), StrictNoTerminalPath = true });
        Assert.That(
            err.Diagnostics.Any(d =>
                d.Severity == Severity.Error &&
                d.Message.Contains("No terminal path", StringComparison.OrdinalIgnoreCase)), Is.True,
            "Expected an error when no terminal path exists and StrictNoTerminalPath=true.");
    }

    [Test]
    public void MissingAllNodes_ShouldEmitInfo_AndStillValidateReachablePart()
    {
        Graph graph = BuildSimpleGraph(out _);
        GraphValidationResult res = graph.Validate();

        Assert.Multiple(() =>
        {
            Assert.That(
                res.Diagnostics.Any(d =>
                    d.Severity == Severity.Info && d.Message.Contains("Skipped unreachable/duplicate-name checks",
                        StringComparison.OrdinalIgnoreCase)), Is.True,
                "Expected an info diagnostic when AllNodes is not provided.");

            Assert.That(res.HasErrors, Is.False, "Lack of AllNodes should not produce validation errors.");
        });
    }
}