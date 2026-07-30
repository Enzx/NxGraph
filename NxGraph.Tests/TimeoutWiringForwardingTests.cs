using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// The timeout wrappers are transparent to machine wiring: the blackboard context, the
/// log-report channel, and the agent stamped by the machine must reach the state inside
/// <see cref="AsyncTimeoutState"/>/<see cref="TimeoutState"/> exactly as they reach a bare
/// state. Before this contract, the wiring stopped at the wrapper — a blackboard-using state
/// inside a timeout silently lost its board and report context, and an agent-taking state
/// made <c>SetAgent</c> throw "no nodes implement IAgentSettable".
/// </summary>
[TestFixture]
public class TimeoutWiringForwardingTests
{
    private sealed class TestAgent
    {
        public string Name = "";
    }

    // ── Async wrapper ────────────────────────────────────────────────────

    [Test]
    public async Task board_reaches_state_wrapped_in_async_timeout()
    {
        BlackboardSchema schema = new("wiring");
        BlackboardKey<string> key = schema.Register<string>("value", "");
        BoardReadingAsyncState inner = new(key);

        Graph graph = GraphBuilder.Start()
            .ToWithTimeoutAsync(TimeSpan.FromSeconds(5), inner)
            .WithSchema(schema)
            .Build();

        Blackboard board = new(schema);
        board.Set(key, "board-reached");
        AsyncStateMachine fsm = graph.ToAsyncStateMachine().WithBlackboard(board);

        Result result = await fsm.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(inner.Seen, Is.EqualTo("board-reached"),
                "The machine-bound board must reach the state inside the timeout wrapper.");
        });
    }

    [Test]
    public async Task log_report_from_state_wrapped_in_async_timeout_reaches_observer()
    {
        LoggingAsyncState inner = new("from-inside-timeout");
        Graph graph = GraphBuilder.Start()
            .ToWithTimeoutAsync(TimeSpan.FromSeconds(5), inner)
            .Build();

        LogRecordingObserver observer = new();
        AsyncStateMachine fsm = graph.ToAsyncStateMachine(observer);

        await fsm.ExecuteAsync();

        Assert.That(observer.Messages, Is.EqualTo(new[] { "from-inside-timeout" }),
            "A log report from inside the timeout wrapper must reach the machine's observer.");
    }

    [Test]
    public async Task agent_reaches_state_wrapped_in_async_timeout()
    {
        AgentReadingAsyncState inner = new();
        Graph graph = GraphBuilder.Start()
            .ToWithTimeoutAsync(TimeSpan.FromSeconds(5), inner)
            .Build();

        AsyncStateMachine<TestAgent> fsm = graph.ToAsyncStateMachine<TestAgent>();
        TestAgent agent = new() { Name = "agent-reached" };

        Assert.DoesNotThrow(() => fsm.SetAgent(agent),
            "An agent-taking state inside a timeout wrapper must count as an agent acceptor.");
        await fsm.ExecuteAsync();

        Assert.That(inner.Seen, Is.SameAs(agent));
    }

    // ── Sync twin ────────────────────────────────────────────────────────

    [Test]
    public void board_reaches_state_wrapped_in_sync_timeout()
    {
        BlackboardSchema schema = new("wiring");
        BlackboardKey<string> key = schema.Register<string>("value", "");
        BoardReadingState inner = new(key);

        Graph graph = GraphBuilder.Start()
            .ToWithTimeout(TimeSpan.FromSeconds(5), inner)
            .WithSchema(schema)
            .Build();

        Blackboard board = new(schema);
        board.Set(key, "board-reached");
        StateMachine fsm = graph.ToStateMachine();
        fsm.SetBlackboard(board);
        fsm.SetStepMode(ParallelStepMode.RunToJoin);

        Result result = fsm.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(inner.Seen, Is.EqualTo("board-reached"));
        });
    }

    [Test]
    public void log_report_from_state_wrapped_in_sync_timeout_reaches_observer()
    {
        LoggingState inner = new("from-inside-timeout");
        Graph graph = GraphBuilder.Start()
            .ToWithTimeout(TimeSpan.FromSeconds(5), inner)
            .Build();

        LogRecordingSyncObserver observer = new();
        StateMachine fsm = graph.ToStateMachine(observer);
        fsm.SetStepMode(ParallelStepMode.RunToJoin);

        fsm.Execute();

        Assert.That(observer.Messages, Is.EqualTo(new[] { "from-inside-timeout" }));
    }

    [Test]
    public void agent_reaches_state_wrapped_in_sync_timeout()
    {
        AgentReadingState inner = new();
        Graph graph = GraphBuilder.Start()
            .ToWithTimeout(TimeSpan.FromSeconds(5), inner)
            .Build();

        StateMachine<TestAgent> fsm = graph.ToStateMachine<TestAgent>();
        fsm.SetStepMode(ParallelStepMode.RunToJoin);
        TestAgent agent = new() { Name = "agent-reached" };

        Assert.DoesNotThrow(() => fsm.SetAgent(agent));
        fsm.Execute();

        Assert.That(inner.Seen, Is.SameAs(agent));
    }

    // ── The wrapped state keeps behaving like a bare one under the async machine ──

    [Test]
    public async Task sync_state_wrapped_in_sync_timeout_reports_under_the_async_machine()
    {
        // The bridge case: a sync State inside the sync TimeoutState, run by the async
        // machine behind the sync-logic adapter — State.Log must still reach the observer.
        LoggingState inner = new("bridged-report");
        Graph graph = GraphBuilder.Start()
            .ToWithTimeout(TimeSpan.FromSeconds(5), inner)
            .Build();

        LogRecordingObserver observer = new();
        AsyncStateMachine fsm = graph.ToAsyncStateMachine(observer);

        await fsm.ExecuteAsync();

        Assert.That(observer.Messages, Is.EqualTo(new[] { "bridged-report" }));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private sealed class BoardReadingAsyncState(BlackboardKey<string> key) : AsyncState
    {
        public string? Seen;

        protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            Seen = Bb.Get(key);
            return ResultHelpers.Success;
        }
    }

    private sealed class LoggingAsyncState(string message) : AsyncState
    {
        protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            await LogAsync(message, ct);
            return Result.Success;
        }
    }

    private sealed class AgentReadingAsyncState : AsyncState<TestAgent>
    {
        public TestAgent? Seen;

        protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            Seen = Agent;
            return ResultHelpers.Success;
        }
    }

    private sealed class BoardReadingState(BlackboardKey<string> key) : State
    {
        public string? Seen;

        protected override Result OnRun()
        {
            Seen = Bb.Get(key);
            return Result.Success;
        }
    }

    private sealed class LoggingState(string message) : State
    {
        protected override Result OnRun()
        {
            Log(message);
            return Result.Success;
        }
    }

    private sealed class AgentReadingState : State<TestAgent>
    {
        public TestAgent? Seen;

        protected override Result OnRun()
        {
            Seen = Agent;
            return Result.Success;
        }
    }

    private sealed class LogRecordingObserver : IAsyncStateMachineObserver
    {
        public readonly List<string> Messages = [];

        public ValueTask OnLogReport(NodeId nodeId, string message, CancellationToken ct = default)
        {
            Messages.Add(message);
            return default;
        }
    }

    private sealed class LogRecordingSyncObserver : IStateMachineObserver
    {
        public readonly List<string> Messages = [];

        void IStateMachineObserver.OnLogReport(NodeId nodeId, string message) => Messages.Add(message);
    }
}
