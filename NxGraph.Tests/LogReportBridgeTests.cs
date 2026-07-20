using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxGraph.Tests;

/// <summary>
/// Codifies the cross-runtime log-report bridge: <see cref="State"/>.<c>Log</c> must reach
/// the observer under all four machines — the sync machines wire the sync callback, the
/// async machines wire the async slot and <c>Log</c> falls back to it, waiting a genuinely
/// asynchronous observer out so delivery completes before <c>Log</c> returns. Also codifies
/// the per-visit reassignment contract across machines sharing one graph: both report slots
/// are machine-owned per visit, so reports never leak to a previously-running machine's
/// observer (in either direction, with or without an observer on the current machine).
/// </summary>
[TestFixture]
public class LogReportBridgeTests
{
    // ── Four-runtime delivery (the spec-017 repro as a regression) ──────

    /// <summary>Two named sync states, each emitting one message via State.Log.</summary>
    private static Graph LoggingChain()
    {
        return GraphBuilder
            .StartWith(new LoggingState("from-a")).SetName("a")
            .To(new LoggingState("from-b")).SetName("b")
            .Build();
    }

    [Test]
    public void sync_state_log_reaches_the_sync_machine_observer()
    {
        RecordingSyncObserver observer = new();
        StateMachine machine = LoggingChain().ToStateMachine(observer);

        Result result = RunToEnd(machine);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observer.Messages, Is.EqualTo(new[] { "from-a", "from-b" }));
            Assert.That(observer.NodeNames, Is.EqualTo(new[] { "a", "b" }));
        });
    }

    [Test]
    public async Task sync_state_log_reaches_the_async_machine_observer()
    {
        // The original repro: before the bridge the async machine wired only the async
        // slot, State.Log read only the sync one, and the message was silently dropped.
        RecordingAsyncObserver observer = new();
        AsyncStateMachine machine = LoggingChain().ToAsyncStateMachine(observer);

        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observer.Messages, Is.EqualTo(new[] { "from-a", "from-b" }));
            Assert.That(observer.NodeNames, Is.EqualTo(new[] { "a", "b" }));
        });
    }

    [Test]
    public void sync_state_log_reaches_the_token_machine_observer()
    {
        RecordingTokenObserver observer = new();
        TokenMachine machine = LoggingChain().ToTokenMachine(observer);
        machine.SetStepMode(ParallelStepMode.RunToJoin);

        Result result = machine.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observer.Messages, Is.EqualTo(new[] { "from-a", "from-b" }));
            Assert.That(observer.NodeNames, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(observer.TokenIds, Is.EqualTo(new[] { 0, 0 }));
        });
    }

    [Test]
    public async Task sync_state_log_reaches_the_async_token_machine_observer()
    {
        RecordingAsyncTokenObserver observer = new();
        AsyncTokenMachine machine = LoggingChain().ToAsyncTokenMachine(observer);

        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observer.Messages, Is.EqualTo(new[] { "from-a", "from-b" }));
            Assert.That(observer.NodeNames, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(observer.TokenIds, Is.EqualTo(new[] { 0, 0 }));
        });
    }

    // ── Delivery-before-return for genuinely asynchronous observers ─────

    [Test]
    public async Task async_observer_delivery_completes_before_log_returns()
    {
        YieldingAsyncObserver observer = new();
        DeliveryProbeState probe = new(observer);
        AsyncStateMachine machine = GraphBuilder
            .StartWith(probe)
            .ToAsyncStateMachine(observer);

        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(probe.MessagesSeenAfterLog, Is.EqualTo(1),
                "An observer completing asynchronously must still have recorded the report " +
                "before State.Log returned (delivery-before-return).");
        });
    }

    // ── Observer-less machines: Log is a no-op ──────────────────────────

    [Test]
    public async Task log_from_sync_state_is_a_noop_on_an_observer_less_async_machine()
    {
        AsyncStateMachine machine = LoggingChain().ToAsyncStateMachine();

        Result result = await machine.ExecuteAsync();

        Assert.That(result, Is.EqualTo(Result.Success));
    }

    // ── Machines sharing one graph: per-visit reassignment contract ─────

    [Test]
    public async Task interleaved_async_machines_sharing_one_graph_attribute_reports_to_their_own_observer()
    {
        Graph shared = LoggingChain();
        RecordingAsyncObserver first = new();
        RecordingAsyncObserver second = new();
        AsyncStateMachine machineA = shared.ToAsyncStateMachine(first);
        AsyncStateMachine machineB = shared.ToAsyncStateMachine(second);

        await machineA.ExecuteAsync();
        await machineB.ExecuteAsync();
        await machineA.ExecuteAsync();
        await machineB.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.Messages, Has.Count.EqualTo(4),
                "Two runs over the two-node chain report twice each to this machine's observer.");
            Assert.That(second.Messages, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public async Task interleaved_sync_and_async_machines_sharing_one_graph_attribute_reports_to_their_own_observer()
    {
        Graph shared = LoggingChain();
        RecordingSyncObserver syncObserver = new();
        RecordingAsyncObserver asyncObserver = new();
        StateMachine syncMachine = shared.ToStateMachine(syncObserver);
        AsyncStateMachine asyncMachine = shared.ToAsyncStateMachine(asyncObserver);

        RunToEnd(syncMachine);
        await asyncMachine.ExecuteAsync();
        RunToEnd(syncMachine);
        await asyncMachine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(syncObserver.Messages, Has.Count.EqualTo(4));
            Assert.That(asyncObserver.Messages, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public async Task observer_less_sync_machine_does_not_leak_reports_to_a_previous_async_machines_observer()
    {
        // Without the per-visit clearing of the async slot, the observer-less sync machine
        // would null only SyncLogReport and State.Log would fall back to the async callback
        // the async machine left behind — delivering this run's reports to that machine's
        // observer with stale attribution.
        Graph shared = LoggingChain();
        RecordingAsyncObserver asyncObserver = new();
        AsyncStateMachine asyncMachine = shared.ToAsyncStateMachine(asyncObserver);
        StateMachine observerLess = shared.ToStateMachine();

        await asyncMachine.ExecuteAsync();
        RunToEnd(observerLess);

        Assert.That(asyncObserver.Messages, Has.Count.EqualTo(2),
            "The observer-less sync run must not deliver through the stale async callback.");
    }

    [Test]
    public async Task observer_less_async_machine_does_not_leak_reports_to_a_previous_sync_machines_observer()
    {
        // Mirror case: the observer-less async machine nulls the async slot and must also
        // clear the sync callback the sync machine left behind, or State.Log would prefer it.
        Graph shared = LoggingChain();
        RecordingSyncObserver syncObserver = new();
        StateMachine syncMachine = shared.ToStateMachine(syncObserver);
        AsyncStateMachine observerLess = shared.ToAsyncStateMachine();

        RunToEnd(syncMachine);
        await observerLess.ExecuteAsync();

        Assert.That(syncObserver.Messages, Has.Count.EqualTo(2),
            "The observer-less async run must not deliver through the stale sync callback.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static Result RunToEnd(StateMachine machine)
    {
        Result result = machine.Execute();
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        return result;
    }

    /// <summary>A sync state that emits one log message per run via State.Log.</summary>
    private sealed class LoggingState(string message) : State
    {
        protected override Result OnRun()
        {
            Log(message);
            return Result.Success;
        }
    }

    /// <summary>
    /// Logs once, then captures how many reports the observer had recorded at the moment
    /// Log returned — the delivery-before-return probe.
    /// </summary>
    private sealed class DeliveryProbeState(YieldingAsyncObserver observer) : State
    {
        public int MessagesSeenAfterLog = -1;

        protected override Result OnRun()
        {
            Log("bridged");
            MessagesSeenAfterLog = observer.Messages.Count;
            return Result.Success;
        }
    }

    private sealed class RecordingSyncObserver : IStateMachineObserver
    {
        public readonly List<string> Messages = [];
        public readonly List<string> NodeNames = [];

        void IStateMachineObserver.OnLogReport(NodeId nodeId, string message)
        {
            Messages.Add(message);
            NodeNames.Add(nodeId.Name);
        }
    }

    private sealed class RecordingAsyncObserver : IAsyncStateMachineObserver
    {
        public readonly List<string> Messages = [];
        public readonly List<string> NodeNames = [];

        public ValueTask OnLogReport(NodeId nodeId, string message, CancellationToken ct = default)
        {
            Messages.Add(message);
            NodeNames.Add(nodeId.Name);
            return default;
        }
    }

    /// <summary>Records on a thread-pool hop so recording genuinely completes asynchronously.</summary>
    private sealed class YieldingAsyncObserver : IAsyncStateMachineObserver
    {
        public readonly List<string> Messages = [];

        public async ValueTask OnLogReport(NodeId nodeId, string message, CancellationToken ct = default)
        {
            await Task.Yield();
            Messages.Add(message);
        }
    }

    private sealed class RecordingTokenObserver : ITokenMachineObserver
    {
        public readonly List<string> Messages = [];
        public readonly List<string> NodeNames = [];
        public readonly List<int> TokenIds = [];

        void ITokenMachineObserver.OnLogReport(int tokenId, NodeId nodeId, string message)
        {
            Messages.Add(message);
            NodeNames.Add(nodeId.Name);
            TokenIds.Add(tokenId);
        }
    }

    private sealed class RecordingAsyncTokenObserver : IAsyncTokenMachineObserver
    {
        public readonly List<string> Messages = [];
        public readonly List<string> NodeNames = [];
        public readonly List<int> TokenIds = [];

        public ValueTask OnLogReport(int tokenId, NodeId nodeId, string message, CancellationToken ct = default)
        {
            Messages.Add(message);
            NodeNames.Add(nodeId.Name);
            TokenIds.Add(tokenId);
            return default;
        }
    }
}
