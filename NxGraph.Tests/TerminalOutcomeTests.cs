using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

namespace NxGraph.Tests;

[TestFixture]
public class TerminalOutcomeTests
{
    private const int Approved = 1;
    private const int Rejected = 2;

    private static Graph ApprovalGraph(Func<bool> approve)
    {
        GraphBuilder builder = new();
        NodeId approved = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success));
        NodeId rejected = builder.AddNode(new AsyncRelayState(_ => ResultHelpers.Success));
        builder.AddNode(new AsyncChoiceState(() => new ValueTask<bool>(approve()), approved, rejected),
            isStart: true);
        builder.SetName(approved, "Approved");
        builder.SetName(rejected, "Rejected");
        builder.SetOutcome(approved, Approved, "Approved");
        builder.SetOutcome(rejected, Rejected, "Rejected");
        return builder.Build(throwOnError: false);
    }

    [Test]
    public async Task branch_ending_in_rejected_reports_its_outcome_code_and_name()
    {
        Graph graph = ApprovalGraph(() => false);
        AsyncStateMachine machine = graph.ToAsyncStateMachine();

        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(machine.LastOutcome, Is.EqualTo(Rejected));
            Assert.That(machine.LastOutcomeName, Is.EqualTo("Rejected"));
        });
    }

    [Test]
    public async Task branch_ending_in_approved_reports_its_outcome_code_and_name()
    {
        Graph graph = ApprovalGraph(() => true);
        AsyncStateMachine machine = graph.ToAsyncStateMachine();

        await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(machine.LastOutcome, Is.EqualTo(Approved));
            Assert.That(machine.LastOutcomeName, Is.EqualTo("Approved"));
        });
    }

    [Test]
    public async Task outcome_defaults_to_zero_when_unset()
    {
        Graph graph = GraphBuilder.StartWithAsync(_ => ResultHelpers.Success).Build();
        AsyncStateMachine machine = graph.ToAsyncStateMachine();

        await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(machine.LastOutcome, Is.Zero);
            Assert.That(machine.LastOutcomeName, Is.Null);
        });
    }

    [Test]
    public async Task failing_terminal_node_reports_its_outcome()
    {
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ResultHelpers.Failure)
            .WithOutcome(Rejected, "Rejected")
            .Build();

        AsyncStateMachine machine = graph.ToAsyncStateMachine();
        Result result = await machine.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Failure));
            Assert.That(machine.LastOutcome, Is.EqualTo(Rejected));
        });
    }

    [Test]
    public void sync_machine_reports_terminal_outcomes()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success)
            .To(() => Result.Success)
            .WithOutcome(Approved, "Approved")
            .Build();

        StateMachine machine = graph.ToStateMachine();
        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = machine.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(machine.LastOutcome, Is.EqualTo(Approved));
            Assert.That(machine.LastOutcomeName, Is.EqualTo("Approved"));
        });
    }

    [Test]
    public async Task outcome_resets_when_a_new_run_starts()
    {
        int runs = 0;
        Graph graph = GraphBuilder
            .StartWithAsync(_ => ++runs == 1 ? ResultHelpers.Failure : ResultHelpers.Success)
            .WithOutcome(Rejected)
            .ToAsync(_ => ResultHelpers.Success)
            .Build();

        AsyncStateMachine machine = graph.ToAsyncStateMachine();

        await machine.ExecuteAsync();
        Assert.That(machine.LastOutcome, Is.EqualTo(Rejected),
            "First run terminates at the failing start node, which carries the Rejected outcome.");

        await machine.ExecuteAsync();
        Assert.That(machine.LastOutcome, Is.Zero,
            "Second run terminates at the unmarked second node, so the stale outcome must not leak.");
    }
}
