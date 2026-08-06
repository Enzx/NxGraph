using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Graphs;

namespace NxGraph.Tests;

/// <summary>
/// The default arm of the data-built <see cref="SwitchState{T}"/> (spec 023): where an
/// unmatched value goes. An explicit <c>.Default(...)</c> arm runs; a switch built without one
/// carries <see cref="NodeId.Default"/> and therefore <b>terminates the run</b> — silently
/// enough that the validator warns about it (see <c>GraphValidatorTests</c>).
/// </summary>
[TestFixture]
[Category("switch_default")]
public class SwitchDefaultCaseTests
{
    private static RelayState Probe(string name, List<string> trace) => new(() =>
    {
        trace.Add(name);
        return Result.Success;
    });

    private static (BlackboardSchema schema, Blackboard board, BlackboardKey<string> mode) Boards()
    {
        BlackboardSchema schema = new("switch-default");
        BlackboardKey<string> mode = schema.Register("mode", "alpha");
        return (schema, new Blackboard(schema), mode);
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

    private static async Task<Result> RunAsync(Graph graph, Blackboard board, bool sync)
    {
        return sync
            ? RunToEnd(graph.ToStateMachine().WithBlackboard(board))
            : await graph.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();
    }

    [Test]
    public async Task An_explicit_default_arm_runs_when_no_case_matches([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<string> mode) = Boards();
        board.Set(mode, "omega");
        List<string> trace = [];

        Graph graph = GraphBuilder.Start()
            .Switch(mode)
            .Case("alpha", Probe("case:alpha", trace))
            .Case("beta", Probe("case:beta", trace))
            .Default(Probe("default", trace))
            .End()
            .WithSchema(schema)
            .Build();

        Result result = await RunAsync(graph, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "default" }));
        });
    }

    [Test]
    public async Task A_switch_without_a_default_terminates_the_run_when_no_case_matches([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<string> mode) = Boards();
        board.Set(mode, "omega");
        List<string> trace = [];

        Graph graph = GraphBuilder.Start()
            .Switch(mode)
            .Case("alpha", Probe("case:alpha", trace))
            .Case("beta", Probe("case:beta", trace))
            .End()
            .WithSchema(schema)
            .Build();

        Result result = await RunAsync(graph, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success),
                "An unmatched value with no default target exits cleanly through NodeId.Default.");
            Assert.That(trace, Is.Empty, "No case arm may run when nothing matched.");
        });
    }

    [Test]
    public async Task A_switch_without_a_default_still_routes_a_matching_case([Values] bool sync)
    {
        (BlackboardSchema schema, Blackboard board, BlackboardKey<string> mode) = Boards();
        board.Set(mode, "beta");
        List<string> trace = [];

        Graph graph = GraphBuilder.Start()
            .Switch(mode)
            .Case("alpha", Probe("case:alpha", trace))
            .Case("beta", Probe("case:beta", trace))
            .End()
            .WithSchema(schema)
            .Build();

        Result result = await RunAsync(graph, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.EqualTo(new[] { "case:beta" }));
        });
    }

    [Test]
    public async Task A_default_target_of_NodeId_Default_terminates_the_run([Values] bool sync)
    {
        // The same contract at the state level: the DSL is not the only way to reach it.
        (BlackboardSchema schema, Blackboard board, BlackboardKey<string> mode) = Boards();
        board.Set(mode, "omega");
        List<string> trace = [];

        GraphBuilder builder = new();
        SwitchCase<string>[] cases = [new("alpha", builder.AddNode(Probe("case:alpha", trace)))];
        builder.AddNode((IAsyncLogic)new SwitchState<string>(mode, cases, NodeId.Default), isStart: true);
        builder.WithSchema(schema);
        Graph graph = builder.Build(throwOnError: false);

        Result result = await RunAsync(graph, board, sync);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(trace, Is.Empty);
        });
    }
}
