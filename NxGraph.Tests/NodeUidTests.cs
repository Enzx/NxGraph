using NxGraph.Authoring;
using NxGraph.Diagnostics.Validations;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// Per-node stable UIDs: authoring-time identity metadata for external tooling.
/// UIDs live in a sidecar array on <see cref="Graph"/> (never in <see cref="NodeId"/>),
/// are never read by any runtime, and resolve via <see cref="Graph.TryGetNodeByUid"/>.
/// </summary>
[TestFixture]
public class NodeUidTests
{
    private static AsyncRelayState Noop() => new(_ => ResultHelpers.Success);

    [Test]
    public void set_uid_rejects_guid_empty()
    {
        GraphBuilder builder = new();
        NodeId start = builder.AddNode(Noop(), isStart: true);

        Assert.Throws<ArgumentException>(() => builder.SetUid(start, Guid.Empty));
    }

    [Test]
    public void set_uid_requires_existing_node()
    {
        GraphBuilder builder = new();
        builder.AddNode(Noop(), isStart: true);

        Assert.Throws<InvalidOperationException>(() => builder.SetUid(new NodeId(42), Guid.NewGuid()));
    }

    [Test]
    public void set_uid_overwrites_on_same_node()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        GraphBuilder builder = new();
        NodeId start = builder.AddNode(Noop(), isStart: true);
        builder.SetUid(start, first);
        builder.SetUid(start, second);
        Graph graph = builder.Build(throwOnError: false);

        Assert.Multiple(() =>
        {
            Assert.That(graph.Uids![0], Is.EqualTo(second));
            Assert.That(graph.TryGetNodeByUid(first, out _), Is.False,
                "The overwritten uid must no longer resolve.");
            Assert.That(graph.TryGetNodeByUid(second, out _), Is.True);
        });
    }

    [Test]
    public void with_uid_on_start_node_lands_at_index_zero()
    {
        Guid uid = Guid.NewGuid();

        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .WithUid(uid)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(graph.Uids, Is.Not.Null);
            Assert.That(graph.Uids![0], Is.EqualTo(uid));
        });
    }

    [Test]
    public void uids_materialize_as_dense_array_with_empty_for_unassigned()
    {
        Guid uid = Guid.NewGuid();

        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .ToAsync(Noop()).WithUid(uid)
            .ToAsync(Noop())
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(graph.Uids, Is.Not.Null);
            Assert.That(graph.Uids!.Length, Is.EqualTo(graph.NodeCount));
            Assert.That(graph.Uids[0], Is.EqualTo(Guid.Empty), "Unassigned node stays uid-free.");
            Assert.That(graph.Uids[1], Is.EqualTo(uid));
            Assert.That(graph.Uids[2], Is.EqualTo(Guid.Empty), "Unassigned node stays uid-free.");
        });
    }

    [Test]
    public void graph_without_uids_has_null_uid_array()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .Build();

        Assert.That(graph.Uids, Is.Null);
    }

    [Test]
    public void try_get_node_by_uid_finds_the_node()
    {
        Guid uid = Guid.NewGuid();

        GraphBuilder builder = new();
        NodeId start = builder.AddNode(Noop(), isStart: true);
        NodeId target = builder.AddNode(Noop());
        builder.AddTransition(start, target);
        builder.SetUid(target, uid);
        Graph graph = builder.Build(throwOnError: false);

        bool found = graph.TryGetNodeByUid(uid, out INode node);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(node.Id.Index, Is.EqualTo(target.Index));
        });
    }

    [Test]
    public void try_get_node_by_uid_miss_returns_false()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .WithUid(Guid.NewGuid())
            .Build();

        Assert.That(graph.TryGetNodeByUid(Guid.NewGuid(), out INode node), Is.False);
        Assert.That(node, Is.EqualTo(LogicNode.Empty));
    }

    [Test]
    public void try_get_node_by_uid_on_uidless_graph_returns_false()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .Build();

        Assert.That(graph.TryGetNodeByUid(Guid.NewGuid(), out _), Is.False);
    }

    [Test]
    public void try_get_node_by_uid_with_guid_empty_returns_false()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .WithUid(Guid.NewGuid())
            .ToAsync(Noop()) // uid-free node occupies an Empty slot in the sidecar
            .Build();

        Assert.That(graph.TryGetNodeByUid(Guid.Empty, out _), Is.False,
            "Guid.Empty encodes 'no uid' and must never match a node.");
    }

    [Test]
    public void graph_ctor_rejects_uid_array_length_mismatch()
    {
        Graph valid = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success).Build();
        INode[] nodes = [valid.StartNode];
        Transition[] edges = [Transition.Empty];

        Assert.Throws<ArgumentException>(() =>
            _ = new Graph(new NodeId(1), nodes, edges, uids: new Guid[2]));
    }

    [Test]
    public void nested_subgraph_may_reuse_a_parent_uid()
    {
        Guid shared = Guid.NewGuid();

        Graph child = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .WithUid(shared)
            .Build();

        StateToken parentStart = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Success)
            .WithUid(shared);
        Graph parent = parentStart.SubGraph(child).Build();

        Assert.Multiple(() =>
        {
            Assert.That(parent.TryGetNodeByUid(shared, out INode parentNode), Is.True);
            Assert.That(parentNode.Id.Index, Is.EqualTo(0), "Parent lookup resolves within the parent graph only.");
            Assert.That(child.TryGetNodeByUid(shared, out INode childNode), Is.True);
            Assert.That(childNode.Id.Index, Is.EqualTo(0), "Child lookup resolves within the child graph only.");
            Assert.That(parent.Validate().HasErrors, Is.False,
                "Uid uniqueness is per-graph; reuse across nesting levels is legal.");
        });
    }
}
