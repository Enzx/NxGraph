using NxGraph.Authoring;
using NxGraph.Behaviors;
using NxGraph.Conditions;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxGraph.Tests;

/// <summary>
/// The data-built branch states' report channel: a condition inside a <see cref="ChoiceState"/>
/// calls <see cref="BehaviorContext.Report"/> and the message must reach the running machine's
/// observer (<c>OnLogReport</c>), attributed to the branch node — the same contract
/// <c>State.Log</c> and the behavior composites have (see <c>LogReportBridgeTests</c>).
/// <para>
/// A branch state is not a <c>State</c> subclass, so this only works because the machines' sync
/// report tables target the report <i>capability</i> (<c>ISyncLogReporter</c>) rather than the
/// base class. The fixture therefore pins the same invariants the base-class channel has: both
/// slots are machine-owned and reassigned per visit, an observer-less machine wires
/// <see langword="null"/> (so <c>HasReporter</c> is false and gated conditions pay nothing), and
/// the channel is live <b>before the director selects</b> — the report is raised from inside
/// selection, so its arrival is that ordering.
/// </para>
/// </summary>
[TestFixture]
[Category("branching_choice")]
public class BranchReportChannelTests
{
    private const string BranchNode = "branch";
    private const string ArmNode = "arm";
    private const string Message = "deciding";

    // ── Fixtures ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reports through the context (gated on <c>HasReporter</c>, as report-formatting
    /// conditions should be) and records what it saw on every evaluation.
    /// </summary>
    private sealed class ReportingCondition(bool answer = true) : ICondition
    {
        public readonly List<bool> ReporterSeen = [];

        public bool Evaluate(in BehaviorContext ctx)
        {
            ReporterSeen.Add(ctx.HasReporter);
            if (ctx.HasReporter)
            {
                ctx.Report(Message);
            }

            return answer;
        }
    }

    /// <summary>
    /// A choice as the start node: the true arm is a probe node, the false arm is the
    /// director's terminal exit. Both nodes are named so reports can be attributed.
    /// </summary>
    private static Graph BranchGraph(ICondition condition)
    {
        GraphBuilder builder = new();
        NodeId arm = builder.AddNode(new RelayState(() => Result.Success));
        builder.SetName(arm, ArmNode);
        NodeId branch = builder.AddNode((IAsyncLogic)new ChoiceState(condition, arm, NodeId.Default), isStart: true);
        builder.SetName(branch, BranchNode);
        return builder.Build(throwOnError: false);
    }

    private static Result RunToEnd(StateMachine machine)
    {
        Result result = machine.Execute();
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        return result;
    }

    // ── Delivery under all four machines ────────────────────────────────

    [Test]
    public void condition_report_reaches_the_sync_machine_observer()
    {
        ReportingCondition condition = new();
        RecordingSyncObserver observer = new();
        StateMachine machine = BranchGraph(condition).ToStateMachine(observer);

        Result result = RunToEnd(machine);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observer.Messages, Is.EqualTo(new[] { Message }));
            Assert.That(observer.NodeNames, Is.EqualTo(new[] { BranchNode }),
                "The report is attributed to the branch node whose decision raised it.");
            Assert.That(condition.ReporterSeen, Is.EqualTo(new[] { true }));
        });
    }

    [Test]
    public async Task condition_report_reaches_the_async_machine_observer()
    {
        ReportingCondition condition = new();
        RecordingAsyncObserver observer = new();
        AsyncStateMachine machine = BranchGraph(condition).ToAsyncStateMachine(observer);

        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observer.Messages, Is.EqualTo(new[] { Message }));
            Assert.That(observer.NodeNames, Is.EqualTo(new[] { BranchNode }));
            Assert.That(condition.ReporterSeen, Is.EqualTo(new[] { true }));
        });
    }

    [Test]
    public void condition_report_reaches_the_token_machine_observer()
    {
        ReportingCondition condition = new();
        RecordingTokenObserver observer = new();
        TokenMachine machine = BranchGraph(condition).ToTokenMachine(observer);
        machine.SetStepMode(ParallelStepMode.RunToJoin);

        Result result = machine.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observer.Messages, Is.EqualTo(new[] { Message }));
            Assert.That(observer.NodeNames, Is.EqualTo(new[] { BranchNode }));
            Assert.That(observer.TokenIds, Is.EqualTo(new[] { 0 }));
        });
    }

    [Test]
    public async Task condition_report_reaches_the_async_token_machine_observer()
    {
        ReportingCondition condition = new();
        RecordingAsyncTokenObserver observer = new();
        AsyncTokenMachine machine = BranchGraph(condition).ToAsyncTokenMachine(observer);

        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observer.Messages, Is.EqualTo(new[] { Message }));
            Assert.That(observer.NodeNames, Is.EqualTo(new[] { BranchNode }));
            Assert.That(observer.TokenIds, Is.EqualTo(new[] { 0 }));
        });
    }

    // ── Ordering: wired before the director selects ─────────────────────

    [Test]
    public async Task the_report_channel_is_live_before_the_director_selects([Values] bool sync)
    {
        // The report is raised from inside SelectNext, so its arrival already proves the
        // channel was wired before selection ran. The ordered trace pins the second half:
        // it lands while the machine is still on the branch node, ahead of the transition
        // this very decision produced.
        ReportingCondition condition = new();
        Graph graph = BranchGraph(condition);
        List<string> trace;

        if (sync)
        {
            RecordingSyncObserver observer = new();
            RunToEnd(graph.ToStateMachine(observer));
            trace = observer.Trace;
        }
        else
        {
            RecordingAsyncObserver observer = new();
            await graph.ToAsyncStateMachine(observer).ExecuteAsync();
            trace = observer.Trace;
        }

        Assert.That(trace, Is.EqualTo(new[] { $"report:{BranchNode}", $"transition:{BranchNode}->{ArmNode}" }));
    }

    // ── Observer-less machines: the channel is inert and free ───────────

    [Test]
    public async Task has_reporter_is_false_on_an_observer_less_machine([Values] bool sync)
    {
        ReportingCondition condition = new();
        Graph graph = BranchGraph(condition);

        Result result = sync
            ? RunToEnd(graph.ToStateMachine())
            : await graph.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(condition.ReporterSeen, Is.EqualTo(new[] { false }),
                "An observer-less machine wires null into both slots, so report-formatting " +
                "conditions pay nothing.");
        });
    }

    // ── Machines sharing one graph: per-visit reassignment ──────────────

    [Test]
    public async Task two_machines_over_one_shared_graph_each_receive_only_their_own_condition_reports(
        [Values] bool sync)
    {
        Graph shared = BranchGraph(new ReportingCondition());

        if (sync)
        {
            RecordingSyncObserver first = new();
            RecordingSyncObserver second = new();
            StateMachine machineA = shared.ToStateMachine(first);
            StateMachine machineB = shared.ToStateMachine(second);

            RunToEnd(machineA);
            RunToEnd(machineB);
            RunToEnd(machineA);

            Assert.Multiple(() =>
            {
                Assert.That(first.Messages, Has.Count.EqualTo(2), "Two runs, one decision each.");
                Assert.That(second.Messages, Has.Count.EqualTo(1));
            });
        }
        else
        {
            RecordingAsyncObserver first = new();
            RecordingAsyncObserver second = new();
            AsyncStateMachine machineA = shared.ToAsyncStateMachine(first);
            AsyncStateMachine machineB = shared.ToAsyncStateMachine(second);

            await machineA.ExecuteAsync();
            await machineB.ExecuteAsync();
            await machineA.ExecuteAsync();

            Assert.Multiple(() =>
            {
                Assert.That(first.Messages, Has.Count.EqualTo(2));
                Assert.That(second.Messages, Has.Count.EqualTo(1));
            });
        }
    }

    [Test]
    public async Task an_observer_less_sync_run_does_not_leak_condition_reports_to_a_previous_async_observer()
    {
        // Without the per-visit clearing of the async slot, the observer-less sync machine
        // would null only the sync callback and the report bridge would fall back to the async
        // callback the async machine left on the branch node — stale attribution.
        ReportingCondition condition = new();
        Graph shared = BranchGraph(condition);
        RecordingAsyncObserver asyncObserver = new();

        await shared.ToAsyncStateMachine(asyncObserver).ExecuteAsync();
        RunToEnd(shared.ToStateMachine());

        Assert.Multiple(() =>
        {
            Assert.That(asyncObserver.Messages, Has.Count.EqualTo(1),
                "The observer-less sync run must not deliver through the stale async callback.");
            Assert.That(condition.ReporterSeen, Is.EqualTo(new[] { true, false }));
        });
    }

    [Test]
    public async Task an_observer_less_async_run_does_not_leak_condition_reports_to_a_previous_sync_observer()
    {
        ReportingCondition condition = new();
        Graph shared = BranchGraph(condition);
        RecordingSyncObserver syncObserver = new();

        RunToEnd(shared.ToStateMachine(syncObserver));
        await shared.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(syncObserver.Messages, Has.Count.EqualTo(1),
                "The observer-less async run must not deliver through the stale sync callback.");
            Assert.That(condition.ReporterSeen, Is.EqualTo(new[] { true, false }));
        });
    }

    [Test]
    public async Task a_sync_run_after_an_async_run_reports_only_to_its_own_observer()
    {
        // Cross-family staleness with an observer on both sides: each family wires its own
        // slot and clears the other's, so neither observer sees the other's run.
        ReportingCondition condition = new();
        Graph shared = BranchGraph(condition);
        RecordingAsyncObserver asyncObserver = new();
        RecordingSyncObserver syncObserver = new();

        await shared.ToAsyncStateMachine(asyncObserver).ExecuteAsync();
        RunToEnd(shared.ToStateMachine(syncObserver));

        Assert.Multiple(() =>
        {
            Assert.That(asyncObserver.Messages, Has.Count.EqualTo(1));
            Assert.That(syncObserver.Messages, Has.Count.EqualTo(1));
            Assert.That(condition.ReporterSeen, Is.EqualTo(new[] { true, true }));
        });
    }

    // ── Observers ───────────────────────────────────────────────────────

    private sealed class RecordingSyncObserver : IStateMachineObserver
    {
        public readonly List<string> Messages = [];
        public readonly List<string> NodeNames = [];
        public readonly List<string> Trace = [];

        void IStateMachineObserver.OnLogReport(NodeId nodeId, string message)
        {
            Messages.Add(message);
            NodeNames.Add(nodeId.Name);
            Trace.Add($"report:{nodeId.Name}");
        }

        void IStateMachineObserver.OnTransition(NodeId from, NodeId to) =>
            Trace.Add($"transition:{from.Name}->{to.Name}");
    }

    private sealed class RecordingAsyncObserver : IAsyncStateMachineObserver
    {
        public readonly List<string> Messages = [];
        public readonly List<string> NodeNames = [];
        public readonly List<string> Trace = [];

        public ValueTask OnLogReport(NodeId nodeId, string message, CancellationToken ct = default)
        {
            Messages.Add(message);
            NodeNames.Add(nodeId.Name);
            Trace.Add($"report:{nodeId.Name}");
            return default;
        }

        public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
        {
            Trace.Add($"transition:{from.Name}->{to.Name}");
            return default;
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
