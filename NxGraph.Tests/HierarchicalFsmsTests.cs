using System.Text.Json;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Serialization;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Tests;

[TestFixture(Category = "NxFSM")]
public class HierarchicalFsmTests
{
    private class HierarchicalDummyState(string? data = null) : IAsyncLogic
    {
        public string Data { get; init; } = data ?? string.Empty;


        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
        {
            ExecutionLog.Add(Data);
            return ResultHelpers.Success;
        }
    }

    private class DummyLogicTextCodec : ILogicCodec<string>
    {
        public string Serialize(IAsyncLogic asyncLogic) =>
            JsonSerializer.Serialize((HierarchicalDummyState)asyncLogic);

        public IAsyncLogic Deserialize(string data) =>
            JsonSerializer.Deserialize<HierarchicalDummyState>(data)
            ?? new HierarchicalDummyState();
    }

    private static readonly List<string> ExecutionLog = [];
    private static readonly List<string> ExpectedLog = ["parent start", "child start", "child end", "parent end"];

    [SetUp]
    public void SetUp() => ExecutionLog.Clear();


    [Test]
    public async Task Executes_HierarchicalFsm_InExpected_Order()
    {
        Graph childGraph = GraphBuilder
            .StartWithAsync(new HierarchicalDummyState("child start"))
            .ToAsync(new HierarchicalDummyState("child end")).Build();
        AsyncStateMachine childFsm = childGraph.ToAsyncStateMachine();
        Graph parentGraph = GraphBuilder
            .StartWithAsync(new HierarchicalDummyState("parent start"))
            .ToAsync(childFsm)
            .ToAsync(new HierarchicalDummyState("parent end"))
            .Build();
        AsyncStateMachine parentFsm = parentGraph.ToAsyncStateMachine();
        await parentFsm.ExecuteAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parentGraph.NodeCount, Is.EqualTo(3));
            Assert.That(parentGraph.TransitionCount, Is.EqualTo(3));
            Assert.That(((HierarchicalDummyState)parentGraph.StartNode.AsyncLogic).Data, Is.EqualTo("parent start"));
            Assert.That((parentGraph.GetNodeByIndex(1).AsyncLogic), Is.EqualTo(childFsm));
            Assert.That(((HierarchicalDummyState)parentGraph.GetNodeByIndex(2).AsyncLogic).Data,
                Is.EqualTo("parent end"));
            Assert.That(childGraph.NodeCount, Is.EqualTo(2));
            Assert.That(childGraph.TransitionCount, Is.EqualTo(2));
            Assert.That(((HierarchicalDummyState)childGraph.StartNode.AsyncLogic).Data, Is.EqualTo("child start"));
            Assert.That(((HierarchicalDummyState)childGraph.GetNodeByIndex(1).AsyncLogic).Data,
                Is.EqualTo("child end"));
            Assert.That(ExecutionLog, Is.EquivalentTo(ExpectedLog));
        }
    }

    // ── Sync hierarchical FSMs ──────────────────────────────────────────

    [Test]
    public void sync_child_fsm_used_as_node_executes_before_parent_continues()
    {
        List<string> log = [];

        StateMachine childFsm = GraphBuilder
            .StartWith(() => { log.Add("child-1"); return Result.Success; }).SetName("Child-Init")
            .To(new RelayState(
                run: () => { log.Add("child-2"); return Result.Success; },
                onExit: () => log.Add("child-exit")))
            .ToStateMachine();

        StateMachine parentFsm = GraphBuilder
            .StartWith(childFsm).SetName("Child")
            .To(new RelayState(
                run: () => { log.Add("parent-end"); return Result.Success; },
                onExit: () => log.Add("parent-exit")))
            .SetName("Cleanup")
            .ToStateMachine();
        parentFsm.SetResetPolicy(RestartPolicy.Manual);

        Result r1 = parentFsm.Execute(); // child node 1 runs
        Result r2 = parentFsm.Execute(); // child node 2 runs → child done, parent transitions to Cleanup
        Result r3 = parentFsm.Execute(); // Cleanup runs → parent done

        using (Assert.EnterMultipleScope())
        {
            Assert.That(r1, Is.EqualTo(Result.Continue));
            Assert.That(r2, Is.EqualTo(Result.Continue));
            Assert.That(r3, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(["child-1", "child-2", "child-exit", "parent-end", "parent-exit"]));
        }
    }

    [Test]
    public void sync_nested_fsm_can_be_arbitrarily_deep()
    {
        // Three levels: grandchild → child → parent
        StateMachine grandchildFsm = GraphBuilder
            .StartWith(() => Result.Success)
            .ToStateMachine();

        StateMachine childFsm = GraphBuilder
            .StartWith(grandchildFsm)
            .To(() => Result.Success)
            .ToStateMachine();

        StateMachine parentFsm = GraphBuilder
            .StartWith(childFsm)
            .To(() => Result.Success)
            .ToStateMachine();
        parentFsm.SetResetPolicy(RestartPolicy.Manual);

        // Tick 1: grandchild's only node
        // Tick 2: child's second node  (grandchild finished → child transitions)
        // Tick 3: parent's second node (child finished → parent transitions)
        Assert.That(parentFsm.Execute(), Is.EqualTo(Result.Continue));
        Assert.That(parentFsm.Execute(), Is.EqualTo(Result.Continue));
        Assert.That(parentFsm.Execute(), Is.EqualTo(Result.Success));
    }

    [Test]
    public async Task Serializes_And_Deserializes_HierarchicalFsm_Correctly()
    {
        Graph childGraph = GraphBuilder
            .StartWithAsync(new HierarchicalDummyState("child start"))
            .ToAsync(new HierarchicalDummyState("child end")).Build();
        AsyncStateMachine childFsm = childGraph.ToAsyncStateMachine();
        Graph parentGraph = GraphBuilder
            .StartWithAsync(new HierarchicalDummyState("parent start"))
            .ToAsync(childFsm)
            .ToAsync(new HierarchicalDummyState("parent end"))
            .Build();
        AsyncStateMachine parentFsm = parentGraph.ToAsyncStateMachine();
        await parentFsm.ExecuteAsync();
        GraphSerializer serializer = new(new DummyLogicTextCodec());
        await using MemoryStream stream = new();
        await serializer.ToJsonAsync(parentGraph, stream);
        string json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        Console.WriteLine(json);
        stream.Position = 0;
        Graph roundTripped = await serializer.FromJsonAsync(stream);
        AsyncStateMachine roundTrippedFsm = roundTripped.ToAsyncStateMachine();

        ExecutionLog.Clear();
        await roundTrippedFsm.ExecuteAsync();
        Assert.That(ExecutionLog, Is.EquivalentTo(ExpectedLog));
    }
}