using System.Text.Json;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Serialization.Tests;

/// <summary>
/// The full durability loop: serialize the graph structure with NxGraph.Serialization,
/// suspend a running machine to a snapshot, ship both across a (simulated) process
/// boundary, rebuild the graph, resume, and run to completion.
/// </summary>
[TestFixture]
[Category("serialization")]
public class DurableSuspendResumeTests
{
    private readonly GraphSerializer _serializer = new(new DummyLogicTextCodec());

    private sealed class RecordingObserver : IAsyncStateMachineObserver
    {
        public readonly List<string> Events = [];

        public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
        {
            Events.Add($"entered:{id.Index}");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
        {
            Events.Add($"exited:{id.Index}");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnStateFailed(NodeId id, Exception? ex, CancellationToken ct = default)
        {
            Events.Add($"failed:{id.Index}");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
        {
            Events.Add($"transition:{from.Index}->{to.Index}");
            return ValueTask.CompletedTask;
        }
    }

    [Test]
    public async Task suspend_serialize_deserialize_resume_completes_the_flow()
    {
        Graph original = GraphBuilder
            .StartWithAsync(new DummyState { Data = "one" })
            .ToAsync(new DummyState { Data = "two" })
            .ToAsync(new DummyState { Data = "three" })
            .Build();

        // Run one step, then suspend mid-flow.
        AsyncStateMachine running = original.ToAsyncStateMachine();
        Result first = await running.StepAsync();
        Assert.That(first, Is.EqualTo(Result.InProgress));
        StateMachineSnapshot snapshot = running.Suspend();

        // Ship graph + snapshot as JSON, as a durable store would.
        await using MemoryStream graphStream = new();
        await _serializer.ToJsonAsync(original, graphStream);
        string snapshotJson = JsonSerializer.Serialize(snapshot);

        // Rebuild everything on the "other side".
        graphStream.Position = 0;
        Graph rebuilt = await _serializer.FromJsonAsync(graphStream);
        StateMachineSnapshot restored = JsonSerializer.Deserialize<StateMachineSnapshot>(snapshotJson)!;

        RecordingObserver observer = new();
        AsyncStateMachine resumed = rebuilt.ToAsyncStateMachine(observer);
        resumed.Resume(restored);

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = await resumed.StepAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observer.Events, Does.Not.Contain("entered:0"),
                "Node 0 completed before the suspend; the resumed machine must not re-enter it.");
            Assert.That(observer.Events, Does.Contain("exited:1"));
            Assert.That(observer.Events, Does.Contain("exited:2"));
        });
    }
}
