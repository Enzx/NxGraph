using NxGraph.Authoring;
using NxGraph.Diagnostics.Validations;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
public class OpaqueDirectorValidationTests
{
    private sealed class OpaqueDirector : AsyncState, IDirector
    {
        public NodeId SelectNext() => NodeId.Default;

        protected override ValueTask<Result> OnRunAsync(CancellationToken ct) => ResultHelpers.Success;
    }

    private sealed class TransparentDirector : AsyncState, IDirector
    {
        private readonly NodeId _target;

        public TransparentDirector(NodeId target) => _target = target;

        public NodeId SelectNext() => _target;

        public IEnumerable<NodeId> EnumerateStaticTargets() => [_target, NodeId.Default];

        protected override ValueTask<Result> OnRunAsync(CancellationToken ct) => ResultHelpers.Success;
    }

    [Test]
    public void bare_custom_director_emits_opacity_warning()
    {
        GraphBuilder builder = new();
        builder.AddNode(new OpaqueDirector(), isStart: true);
        Graph graph = builder.Build(throwOnError: false);

        GraphValidationResult result = graph.Validate();

        Assert.That(result.Diagnostics.Any(d =>
                d.Severity == Severity.Warning &&
                d.Message.Contains("no static targets", StringComparison.OrdinalIgnoreCase)),
            Is.True, "Expected an opacity warning for a director with no static targets.");
    }

    [Test]
    public void director_with_static_targets_stays_clean()
    {
        GraphBuilder builder = new();
        NodeId start = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success), isStart: true);
        NodeId sink = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success));
        NodeId director = builder.AddNode(new TransparentDirector(sink));
        builder.AddTransition(start, director);
        Graph graph = builder.Build(throwOnError: false);

        GraphValidationResult result = graph.Validate();

        Assert.That(result.Diagnostics.Any(d =>
                d.Message.Contains("no static targets", StringComparison.OrdinalIgnoreCase)),
            Is.False, "A director that surfaces its targets must not be flagged.");
    }

    [Test]
    public void builtin_choice_state_stays_clean()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .If(() => true)
            .ThenAsync(_ => ResultHelpers.Success)
            .ElseAsync(_ => ResultHelpers.Success)
            .Build();

        GraphValidationResult result = graph.Validate();

        Assert.That(result.Diagnostics.Any(d =>
                d.Message.Contains("no static targets", StringComparison.OrdinalIgnoreCase)),
            Is.False, "Built-in ChoiceState surfaces its branches and must not be flagged.");
    }
}
