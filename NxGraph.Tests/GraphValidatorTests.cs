using System.Diagnostics;
using NxGraph.Authoring;
using NxGraph.Diagnostics.Validations;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
public class GraphValidatorTests
{
    private static Graph BuildSimpleGraph(out GraphBuilder builder)
    {
        builder = new GraphBuilder();
        NodeId start = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success), isStart: true);
        NodeId end = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success));
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
        NodeId start = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success), true);
        NodeId end = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success));
        NodeId unreachable = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success));

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
        NodeId a1 = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success), isStart: true);
        NodeId a2 = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success));

        builder.SetName(a1, "A");
        builder.SetName(a2, "A"); // duplicate name

        builder.AddTransition(a1, a2);

        Graph graph = builder.Build(throwOnError: false);

        GraphValidationResult res = graph.Validate(new GraphValidationOptions { AllNodes = builder.GetAllNodeIds() });
        
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
        NodeId loop = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success), isStart: true);
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
        NodeId n1 = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success), isStart: true);
        NodeId n2 = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success));
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
    public void MissingAllNodes_IsCompleteByDefault_AndEmitsNoSkipInfo()
    {
        // The validator derives the node set from the graph itself when AllNodes is absent,
        // so the old "Skipped unreachable/duplicate-name checks" Info no longer exists.
        Graph graph = BuildSimpleGraph(out _);
        GraphValidationResult res = graph.Validate();

        Assert.Multiple(() =>
        {
            Assert.That(
                res.Diagnostics.Any(d =>
                    d.Severity == Severity.Info && d.Message.Contains("Skipped unreachable/duplicate-name checks",
                        StringComparison.OrdinalIgnoreCase)), Is.False,
                "The skip Info must not appear — the default path derives AllNodes from the graph.");

            Assert.That(res.HasErrors, Is.False, "Lack of AllNodes should not produce validation errors.");
        });
    }

    [Test]
    public void Validate_WithoutOptions_ReportsUnreachableNodes_AndDuplicateNames()
    {
        GraphBuilder builder = new();
        NodeId start = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success), isStart: true);
        NodeId reached = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success));
        NodeId unreachable = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success));
        builder.SetName(reached, "A");
        builder.SetName(unreachable, "A"); // duplicate name on the unreachable node
        builder.AddTransition(start, reached);

        Graph graph = builder.Build(throwOnError: false);

        GraphValidationResult res = graph.Validate(); // no options at all

        Assert.Multiple(() =>
        {
            Assert.That(
                res.Diagnostics.Any(d =>
                    d.Severity == Severity.Warning && d.Node.Index == unreachable.Index &&
                    d.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase)), Is.True,
                "Standalone Validate() must flag the unreachable node.");
            Assert.That(
                res.Diagnostics.Count(d =>
                    d.Severity == Severity.Warning &&
                    d.Message.Contains("Duplicate node name 'A'", StringComparison.Ordinal)), Is.EqualTo(2),
                "Standalone Validate() must flag both holders of the duplicate name.");
        });
    }

    [Test]
    public void SuppliedAllNodes_OverridesTheDerivedSet()
    {
        // Back-compat contract: an explicitly supplied AllNodes list is honored verbatim —
        // here it contains an id the graph does not know, which must surface as unreachable.
        Graph graph = BuildSimpleGraph(out GraphBuilder builder);
        List<NodeId> supplied = [..builder.GetAllNodeIds()!, new NodeId(99, "Phantom")];

        GraphValidationResult res = graph.Validate(new GraphValidationOptions { AllNodes = supplied });

        Assert.That(
            res.Diagnostics.Any(d =>
                d.Severity == Severity.Warning && d.Node.Index == 99 &&
                d.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase)), Is.True,
            "A supplied AllNodes list must be used instead of the graph-derived set.");
    }

    [Test]
    public void GetAllNodeIds_SingleNodeGraph_ReturnsStart()
    {
        GraphBuilder builder = new();
        NodeId start = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success), isStart: true);

        IReadOnlyList<NodeId>? ids = builder.GetAllNodeIds();

        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.Not.Null, "A builder holding only the start node must not return null.");
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids![0].Index, Is.EqualTo(start.Index));
        });
    }

    [Test]
    public void Validator_follows_choice_director_branches_for_reachability()
    {
        // Regression: the validator previously stopped at director nodes (TryGetTransition
        // returns Empty), so nodes only reachable via a director branch were flagged
        // unreachable. EnumerateStaticTargets exposes those branches to the BFS.
        GraphBuilder builder = new();
        NodeId start = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success), isStart: true);
        NodeId trueBranch = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success));
        NodeId falseBranch = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success));
        NodeId choice = builder.AddNode(new ChoiceState(() => true, trueBranch, falseBranch));

        builder.AddTransition(start, choice);

        Graph graph = builder.Build(throwOnError: false);

        GraphValidationResult res = graph.Validate(new GraphValidationOptions
            { AllNodes = builder.GetAllNodeIds() });

        bool trueBranchFlagged = res.Diagnostics.Any(d =>
            d.Node.Index == trueBranch.Index &&
            d.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase));
        bool falseBranchFlagged = res.Diagnostics.Any(d =>
            d.Node.Index == falseBranch.Index &&
            d.Message.Contains("unreachable", StringComparison.OrdinalIgnoreCase));

        Assert.Multiple(() =>
        {
            Assert.That(trueBranchFlagged, Is.False, "True branch is reachable via director — should not be flagged.");
            Assert.That(falseBranchFlagged, Is.False, "False branch is reachable via director — should not be flagged.");
        });
    }

    [Test]
    public void DuplicateUid_ShouldBeError()
    {
        Guid shared = Guid.NewGuid();

        GraphBuilder builder = new();
        NodeId a = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success), isStart: true);
        NodeId b = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success));
        builder.AddTransition(a, b);
        builder.SetUid(a, shared);
        builder.SetUid(b, shared);

        Graph graph = builder.Build(throwOnError: false);
        GraphValidationResult res = graph.Validate();

        GraphDiagnostic[] duplicates = res.Diagnostics
            .Where(d => d.Severity == Severity.Error &&
                        d.Message.Contains("Duplicate node UID", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(duplicates, Has.Length.EqualTo(2), "Every node claiming the UID gets its own Error.");
            Assert.That(duplicates.Select(d => d.Node.Index), Is.EquivalentTo(new[] { a.Index, b.Index }));
        });
    }

    [Test]
    public void DistinctUids_ProduceNoUidDiagnostics()
    {
        GraphBuilder builder = new();
        NodeId a = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success), isStart: true);
        NodeId b = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success));
        builder.AddTransition(a, b);
        builder.SetUid(a, Guid.NewGuid());
        builder.SetUid(b, Guid.NewGuid());

        Graph graph = builder.Build(throwOnError: false);
        GraphValidationResult res = graph.Validate(new GraphValidationOptions { AllNodes = builder.GetAllNodeIds() });

        Assert.Multiple(() =>
        {
            Assert.That(res.HasErrors, Is.False);
            Assert.That(res.Diagnostics.Any(d => d.Message.Contains("UID", StringComparison.Ordinal)), Is.False,
                "Distinct UIDs must not produce any uid diagnostic.");
        });
    }
}