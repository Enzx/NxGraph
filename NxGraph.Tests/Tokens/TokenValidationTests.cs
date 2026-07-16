using NxGraph.Authoring;
using NxGraph.Diagnostics.Export;
using NxGraph.Diagnostics.Validations;
using NxGraph.Fsm;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxGraph.Tests.Tokens;

/// <summary>
/// Validator lints and tooling visibility for token fork/join nodes (spec 007).
/// </summary>
[TestFixture]
public class TokenValidationTests
{
    private static Graph Diamond()
    {
        JoinState join = new(JoinPolicy.All(2));
        return GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => Result.Success).To(join).To(() => Result.Success),
                b => b.To(() => Result.Success).To(join))
            .Build();
    }

    [Test]
    public void token_graph_gets_the_token_runtime_info()
    {
        GraphValidationResult result = Diamond().Validate();

        Assert.Multiple(() =>
        {
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Diagnostics.Any(d =>
                    d.Severity == Severity.Info && d.Message.Contains("TokenMachine")),
                Is.True, "Fork/join presence is surfaced as an Info pointing at the token machines.");
        });
    }

    [Test]
    public void fork_with_a_wired_success_edge_is_an_error()
    {
        ForkToken fork = GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => Result.Success),
                b => b.To(() => Result.Success));

        // The DSL never wires an edge from a fork; force one through the raw builder API.
        NodeId stray = fork.Builder.AddNode(new RelayState(() => Result.Success));
        fork.Builder.AddTransition(fork.Id, stray);
        Graph graph = fork.Builder.Build(throwOnError: false);

        GraphValidationResult result = graph.Validate();

        Assert.That(result.Diagnostics.Any(d =>
                d.Severity == Severity.Error && d.Message.Contains("Fork node has a wired")),
            Is.True);
    }

    [Test]
    public void unsatisfiable_quorum_is_a_warning()
    {
        // Only two static inbound edges reach the join, but the quorum wants three.
        JoinState join = new(JoinPolicy.Quorum(3));
        Graph graph = GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => Result.Success).To(join).To(() => Result.Success),
                b => b.To(() => Result.Success).To(join))
            .Build();

        GraphValidationResult result = graph.Validate();

        Assert.That(result.Diagnostics.Any(d =>
                d.Severity == Severity.Warning && d.Message.Contains("can never fire")),
            Is.True);
    }

    [Test]
    public void satisfiable_quorum_stays_clean()
    {
        JoinState join = new(JoinPolicy.Quorum(2));
        Graph graph = GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => Result.Success).To(join).To(() => Result.Success),
                b => b.To(() => Result.Success).To(join),
                b => b.To(() => Result.Success).To(join))
            .Build();

        GraphValidationResult result = graph.Validate();

        Assert.That(result.Diagnostics.Any(d => d.Severity == Severity.Warning &&
                d.Message.Contains("can never fire")),
            Is.False, "Three static inbound edges satisfy a 2-of-N quorum.");
    }

    [Test]
    public void fork_branches_are_reachable_via_static_targets()
    {
        JoinState join = new(JoinPolicy.All(2));
        ForkToken fork = GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => Result.Success).To(join).To(() => Result.Success),
                b => b.To(() => Result.Success).To(join));
        Graph graph = fork.Build();

        GraphValidationResult result = graph.Validate(new GraphValidationOptions
        {
            AllNodes = fork.Builder.GetAllNodeIds(),
        });

        Assert.That(result.Diagnostics.Any(d =>
                d.Severity == Severity.Warning && d.Message.Contains("unreachable")),
            Is.False,
            "Fork branches surface through EnumerateStaticTargets, so branch nodes are reachable.");
    }

    [Test]
    public void mermaid_export_renders_fork_and_join_first_class()
    {
        // Diamond node indices: load=0, a=1, join=2, finish=3, b=4, fork=5 (branch heads are
        // created before the fork).
        string mermaid = Diamond().ToMermaid();

        Assert.Multiple(() =>
        {
            // Fork: subroutine-bar shape, solid labeled AND-split edges, and no terminal
            // edge (its empty transition slot is structure, not a terminal exit).
            Assert.That(mermaid, Does.Contain("n5[[\""), "Fork renders as a bar, not a decision rhombus.");
            Assert.That(mermaid, Does.Contain("n5 -- fork --> n1"), "Fork → branch a (solid, labeled).");
            Assert.That(mermaid, Does.Contain("n5 -- fork --> n4"), "Fork → branch b (solid, labeled).");
            Assert.That(mermaid, Does.Not.Contain("n5 --> End"), "A fork never ends a token.");
            Assert.That(mermaid, Does.Not.Contain("n5 -.-> "), "Fork branches are not dashed director edges.");

            // Join: bar shape with the firing policy in the label, ordinary edges otherwise.
            Assert.That(mermaid, Does.Contain(": All(2)\"]]"), "Join label carries its policy.");
            Assert.That(mermaid, Does.Contain("n1 --> n2"), "Branch a converges on the join.");
            Assert.That(mermaid, Does.Contain("n4 --> n2"), "Branch b converges on the join.");
            Assert.That(mermaid, Does.Contain("n2 --> n3"), "The join's success edge is an ordinary edge.");
        });
    }

    [Test]
    public void mermaid_export_labels_each_join_policy()
    {
        JoinState any = new(JoinPolicy.Any);
        JoinState quorum = new(JoinPolicy.Quorum(2));
        Graph graph = GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => Result.Success).To(any).To(() => Result.Success).To(quorum),
                b => b.To(() => Result.Success).To(any),
                b => b.To(() => Result.Success).To(quorum))
            .Build();

        string mermaid = graph.ToMermaid();

        Assert.Multiple(() =>
        {
            Assert.That(mermaid, Does.Contain(": Any\"]]"));
            Assert.That(mermaid, Does.Contain(": Quorum(2)\"]]"));
        });
    }

    [Test]
    public void fork_ctor_rejects_empty_and_default_branches()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => _ = new ForkState());
            Assert.Throws<ArgumentException>(() => _ = new ForkState(NodeId.Default));
        });
    }

    [Test]
    public void join_ctor_rejects_uninitialized_policy()
    {
        Assert.Throws<ArgumentException>(() => _ = new JoinState(default));
    }

    [Test]
    public void join_policy_factories_validate_counts()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = JoinPolicy.All(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = JoinPolicy.Quorum(0));
            Assert.That(JoinPolicy.Any.RequiredCount, Is.EqualTo(1));
            Assert.That(JoinPolicy.All(3).RequiredCount, Is.EqualTo(3));
            Assert.That(JoinPolicy.Quorum(2).RequiredCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void fork_branch_lambda_must_add_a_state()
    {
        Assert.Throws<InvalidOperationException>(() =>
            GraphBuilder.StartWith(() => Result.Success).ForkTo(b => default));
    }
}
