using System.Text.Json;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Conditions;
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

    /// <summary>
    /// <see cref="DummyLogicTextCodec"/> plus the branch pads: <c>.If(...)</c> wires its two
    /// arms through empty pad nodes, which are ordinary logic and therefore the codec's problem,
    /// not the branch section's.
    /// </summary>
    private sealed class PadTolerantCodec : ILogicTextCodec
    {
        private const string Pad = "pad";

        public string Serialize(IAsyncLogic asyncLogic) => asyncLogic is DummyState dummy
            ? JsonSerializer.Serialize(dummy)
            : Pad;

        public IAsyncLogic Deserialize(string s) => s == Pad
            ? new EmptyAsyncLogic()
            : JsonSerializer.Deserialize<DummyState>(s)
              ?? throw new InvalidOperationException("Failed to deserialize DummyState from text.");
    }

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

    /// <summary>
    /// The capstone of spec 023: a graph that <b>branches</b> survives the durability loop.
    /// Before data-built branching this was impossible — the decision was a closure, so the
    /// graph could not ride the payload at all. All three artifacts travel: the graph payload,
    /// the machine snapshot, and the blackboard the decision reads.
    /// </summary>
    [Test]
    public async Task a_branching_graph_survives_the_full_durability_loop()
    {
        GraphSerializer serializer = new(new PadTolerantCodec());
        BlackboardSchema schema = new("routing");
        BlackboardKey<string> tier = schema.Register("tier", "standard");

        Graph original = GraphBuilder
            .StartWithAsync(new DummyState { Data = "intake" })
            .If(new KeyEquals<string>(tier, "premium"))
            .ThenAsync(new DummyState { Data = "premium" })
            .ElseAsync(new DummyState { Data = "standard" })
            .WithSchema(schema)
            .Build();

        Blackboard board = new(schema);
        board.Set(tier, "premium");

        // Run the intake node, then suspend before the branch has been taken.
        AsyncStateMachine running = original.ToAsyncStateMachine().WithBlackboard(board);
        Assert.That(await running.StepAsync(), Is.EqualTo(Result.InProgress));
        StateMachineSnapshot snapshot = running.Suspend();

        // Ship all three artifacts, as a durable store would.
        await using MemoryStream graphStream = new();
        await serializer.ToJsonAsync(original, graphStream);
        string snapshotJson = JsonSerializer.Serialize(snapshot);
        await using MemoryStream boardStream = new();
        BlackboardSerializer boardSerializer = new();
        await boardSerializer.ToJsonAsync(board, boardStream);

        // Rebuild everything on the "other side" — the choice reconstructs from the payload's
        // condition list, and its key rebinds by name against the restored board.
        graphStream.Position = 0;
        Graph rebuilt = await serializer.FromJsonAsync(graphStream);
        StateMachineSnapshot restored = JsonSerializer.Deserialize<StateMachineSnapshot>(snapshotJson)!;
        Blackboard restoredBoard = new(schema);
        boardStream.Position = 0;
        await boardSerializer.RestoreFromJsonAsync(restoredBoard, boardStream);

        RecordingObserver observer = new();
        AsyncStateMachine resumed = rebuilt.ToAsyncStateMachine(observer).WithBlackboard(restoredBoard);
        resumed.Resume(restored);

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = await resumed.StepAsync();
        }

        // Node layout: 0 intake, 1 truePad, 2 falsePad, 3 choice, 4 premium, 5 standard.
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observer.Events, Does.Contain("exited:4"), "the premium arm must run");
            Assert.That(observer.Events, Does.Not.Contain("entered:5"), "the standard arm must not run");
        });
    }
}
