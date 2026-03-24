using System.Text.Json;
using NxGraph.Authoring;
using NxGraph.Fsm;
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


    [Test]
    public async Task Executes_HierarchicalFsm_InExpected_Order()
    {
        Graph childGraph = GraphBuilder
            .StartWith(new HierarchicalDummyState("child start"))
            .To(new HierarchicalDummyState("child end")).Build();
        AsyncStateMachine childFsm = childGraph.ToAsyncStateMachine();
        Graph parentGraph = GraphBuilder
            .StartWith(new HierarchicalDummyState("parent start"))
            .To(childFsm)
            .To(new HierarchicalDummyState("parent end"))
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

    [Test]
    public async Task Serializes_And_Deserializes_HierarchicalFsm_Correctly()
    {
        Graph childGraph = GraphBuilder
            .StartWith(new HierarchicalDummyState("child start"))
            .To(new HierarchicalDummyState("child end")).Build();
        AsyncStateMachine childFsm = childGraph.ToAsyncStateMachine();
        Graph parentGraph = GraphBuilder
            .StartWith(new HierarchicalDummyState("parent start"))
            .To(childFsm)
            .To(new HierarchicalDummyState("parent end"))
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