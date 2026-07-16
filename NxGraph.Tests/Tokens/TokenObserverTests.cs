using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxGraph.Tests.Tokens;

/// <summary>
/// Codifies the token observers' event ordering around fork passage and join firing — this
/// fixture is the behavior spec for the event schema, like <see cref="AsyncObserverTests"/>
/// is for the FSM runtimes.
/// </summary>
[TestFixture]
public class TokenObserverTests
{
    /// <summary>load → fork(a, b) → join(All 2) → finish, with named nodes.</summary>
    private static Graph Diamond(out JoinState join)
    {
        JoinState j = new(JoinPolicy.All(2));
        join = j;
        StateToken start = GraphBuilder.StartWith(() => Result.Success);
        start.Builder.SetName(start.Id, "load");
        ForkToken fork = start.ForkTo(
            b =>
            {
                StateToken a = b.To(() => Result.Success);
                a.Builder.SetName(a.Id, "a");
                StateToken joined = a.To(j);
                joined.Builder.SetName(joined.Id, "join");
                StateToken finish = joined.To(() => Result.Success);
                finish.Builder.SetName(finish.Id, "finish");
                return finish;
            },
            b =>
            {
                StateToken bNode = b.To(() => Result.Success);
                bNode.Builder.SetName(bNode.Id, "b");
                return bNode.To(j);
            });
        return fork.SetName("fork").Build();
    }

    private static readonly string[] ExpectedDiamondTrace =
    [
        "spawned t0 parent -1 at load",
        "entered t0 load",
        // round 1: load succeeds, token passes through the fork
        "exited t0 load",
        "transition t0 load->fork",
        "entered t0 fork",
        "exited t0 fork",
        "transition t0 fork->a",
        "entered t0 a",
        "spawned t1 parent 0 at fork",
        "transition t1 fork->b",
        "entered t1 b",
        // round 2: a parks at the join; b arrives and fires it
        "exited t0 a",
        "transition t0 a->join",
        "entered t0 join",
        "exited t1 b",
        "transition t1 b->join",
        "entered t1 join",
        "retired t0 at join Joined",
        "join fired join survivor t1",
        "exited t1 join",
        "transition t1 join->finish",
        "entered t1 finish",
        // round 3: finish completes the surviving token
        "exited t1 finish",
        "retired t1 at finish Completed",
    ];

    [Test]
    public void sync_diamond_emits_the_canonical_event_order()
    {
        List<string> trace = [];
        Graph graph = Diamond(out _);
        TokenMachine machine = graph.ToTokenMachine(new SyncRecorder(trace));
        machine.SetStepMode(ParallelStepMode.RunToJoin);

        Result result = machine.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(ExpectedDiamondTrace));
        });
    }

    [Test]
    public async Task async_diamond_emits_the_same_event_order()
    {
        List<string> trace = [];
        Graph graph = Diamond(out _);
        AsyncTokenMachine machine = graph.ToAsyncTokenMachine(new AsyncRecorder(trace));

        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(ExpectedDiamondTrace),
                "Runtime parity: the async machine's token events match the sync machine's exactly.");
        });
    }

    [Test]
    public void machine_lifecycle_events_fire_once_per_run()
    {
        List<string> lifecycle = [];
        Graph graph = Diamond(out _);
        TokenMachine machine = graph.ToTokenMachine(new SyncLifecycleRecorder(lifecycle));
        machine.SetStepMode(ParallelStepMode.RunToJoin);

        machine.Execute();

        Assert.That(lifecycle, Is.EqualTo(new[]
        {
            "started",
            "completed Success",
            "reset", // RestartPolicy.Auto resets to Ready after completion
        }));
    }

    private sealed class SyncRecorder(List<string> trace) : ITokenMachineObserver
    {
        public void OnTokenSpawned(int tokenId, int parentTokenId, NodeId at) =>
            trace.Add($"spawned t{tokenId} parent {parentTokenId} at {at.Name}");

        public void OnTokenRetired(int tokenId, NodeId at, TokenRetireReason reason) =>
            trace.Add($"retired t{tokenId} at {at.Name} {reason}");

        public void OnJoinFired(NodeId joinNode, int survivingTokenId) =>
            trace.Add($"join fired {joinNode.Name} survivor t{survivingTokenId}");

        public void OnStateEntered(int tokenId, NodeId id) => trace.Add($"entered t{tokenId} {id.Name}");

        public void OnStateExited(int tokenId, NodeId id) => trace.Add($"exited t{tokenId} {id.Name}");

        public void OnTransition(int tokenId, NodeId from, NodeId to) =>
            trace.Add($"transition t{tokenId} {from.Name}->{to.Name}");
    }

    private sealed class AsyncRecorder(List<string> trace) : IAsyncTokenMachineObserver
    {
        public ValueTask OnTokenSpawned(int tokenId, int parentTokenId, NodeId at, CancellationToken ct = default)
        {
            trace.Add($"spawned t{tokenId} parent {parentTokenId} at {at.Name}");
            return default;
        }

        public ValueTask OnTokenRetired(int tokenId, NodeId at, TokenRetireReason reason,
            CancellationToken ct = default)
        {
            trace.Add($"retired t{tokenId} at {at.Name} {reason}");
            return default;
        }

        public ValueTask OnJoinFired(NodeId joinNode, int survivingTokenId, CancellationToken ct = default)
        {
            trace.Add($"join fired {joinNode.Name} survivor t{survivingTokenId}");
            return default;
        }

        public ValueTask OnStateEntered(int tokenId, NodeId id, CancellationToken ct = default)
        {
            trace.Add($"entered t{tokenId} {id.Name}");
            return default;
        }

        public ValueTask OnStateExited(int tokenId, NodeId id, CancellationToken ct = default)
        {
            trace.Add($"exited t{tokenId} {id.Name}");
            return default;
        }

        public ValueTask OnTransition(int tokenId, NodeId from, NodeId to, CancellationToken ct = default)
        {
            trace.Add($"transition t{tokenId} {from.Name}->{to.Name}");
            return default;
        }
    }

    private sealed class SyncLifecycleRecorder(List<string> lifecycle) : ITokenMachineObserver
    {
        public void OnTokenMachineStarted(NodeId graphId) => lifecycle.Add("started");

        public void OnTokenMachineCompleted(NodeId graphId, Result result) =>
            lifecycle.Add($"completed {result}");

        public void OnTokenMachineReset(NodeId graphId) => lifecycle.Add("reset");
    }
}
