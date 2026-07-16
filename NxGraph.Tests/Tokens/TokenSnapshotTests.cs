using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxGraph.Tests.Tokens;

/// <summary>
/// Suspend/resume spec for the token runtimes: the multiset snapshot round-trips mid-run on
/// both machines (and across them — see
/// <see cref="AsyncTokenMachineTests.snapshots_are_interchangeable_between_token_runtimes"/>),
/// Node-scope scratch resumes as defaults, and structural mismatches fail loud.
/// </summary>
[TestFixture]
public class TokenSnapshotTests
{
    private static Graph Diamond(List<string> log)
    {
        JoinState join = new(JoinPolicy.All(2));
        return GraphBuilder.StartWith(() =>
            {
                log.Add("load");
                return Result.Success;
            })
            .ForkTo(
                b => b.To(() =>
                {
                    log.Add("a");
                    return Result.Success;
                }).To(join).To(() =>
                {
                    log.Add("finish");
                    return Result.Success;
                }),
                b => b.To(() =>
                {
                    log.Add("b1");
                    return Result.Success;
                }).To(() =>
                {
                    log.Add("b2");
                    return Result.Success;
                }).To(join))
            .Build();
    }

    [Test]
    public void sync_mid_run_suspend_resume_continues_to_the_same_outcome()
    {
        List<string> log = [];
        TokenMachine first = Diamond(log).ToTokenMachine();
        first.Execute(); // round 1: load
        first.Execute(); // round 2: a parks at the join, b1 runs

        TokenMachineSnapshot snapshot = first.Suspend();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.MidRun, Is.True);
            Assert.That(snapshot.Tokens, Has.Length.EqualTo(2));
            Assert.That(snapshot.Tokens.Count(t => t.Phase == TokenPhase.Parked), Is.EqualTo(1),
                "One token waits at the All-join.");
            Assert.That(snapshot.JoinArrivals.Sum(), Is.EqualTo(1), "One arrival is banked at the join.");
        });

        TokenMachine second = Diamond(log).ToTokenMachine();
        second.Resume(snapshot);

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = second.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "load", "a", "b1", "b2", "finish" }),
                "No node re-runs after the resume.");
        });
    }

    [Test]
    public async Task async_mid_run_suspend_resume_continues_to_the_same_outcome()
    {
        List<string> log = [];
        AsyncTokenMachine first = Diamond(log).ToAsyncTokenMachine();
        await first.StepAsync();
        await first.StepAsync();

        TokenMachineSnapshot snapshot = first.Suspend();
        Assert.That(snapshot.MidRun, Is.True);

        AsyncTokenMachine second = Diamond(log).ToAsyncTokenMachine();
        second.Resume(snapshot);

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = await second.StepAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "load", "a", "b1", "b2", "finish" }));
        });
    }

    [Test]
    public void any_token_died_survives_the_round_trip()
    {
        // One branch dies immediately; the other is long enough that the run is still going
        // when we suspend. The resumed machine must remember the death and end in Failure.
        List<string> log = [];
        Graph BuildGraph() => GraphBuilder.StartWith(() => Result.Success)
            .ForkTo(
                b => b.To(() => Result.Failure),
                b => b.To(() =>
                {
                    log.Add("s1");
                    return Result.Success;
                }).To(() =>
                {
                    log.Add("s2");
                    return Result.Success;
                }))
            .Build();

        TokenMachine first = BuildGraph().ToTokenMachine();
        first.Execute(); // round 1: root passes the fork
        first.Execute(); // round 2: one token dies, the other runs s1

        TokenMachineSnapshot snapshot = first.Suspend();
        Assert.That(snapshot.AnyTokenDied, Is.True);

        TokenMachine second = BuildGraph().ToTokenMachine();
        second.Resume(snapshot);

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = second.Execute();
        }

        Assert.That(result, Is.EqualTo(Result.Failure),
            "The resumed run still reports the pre-suspend token death.");
    }

    [Test]
    public void node_scope_scratch_resumes_as_defaults()
    {
        BlackboardSchema nodeSchema = new("scratch", BlackboardScope.Node);
        BlackboardKey<int> counter = nodeSchema.Register("counter", 0);

        List<int> observed = [];
        Graph BuildGraph() => GraphBuilder
            .StartWith(bb =>
            {
                // Multi-visit accumulation would need Graph scope; Node scratch is per visit
                // and per token — after a resume it must read the registered default again.
                observed.Add(bb.Get(counter));
                bb.Set(counter, 41);
                return Result.Success;
            })
            .To(_ => Result.Success)
            .Builder.WithSchema(nodeSchema).Build();

        TokenMachine first = BuildGraph().ToTokenMachine();
        first.Execute(); // round 1: start node writes 41 into its scratch

        TokenMachineSnapshot snapshot = first.Suspend();
        TokenMachine second = BuildGraph().ToTokenMachine();
        second.Resume(snapshot);

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = second.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(observed, Is.EqualTo(new[] { 0 }),
                "The start node ran once and saw the default — scratch never leaks across machines.");
        });
    }

    [Test]
    public void resume_rejects_a_pool_too_small_for_the_snapshot()
    {
        List<string> log = [];
        TokenMachine first = Diamond(log).ToTokenMachine();
        first.Execute();
        first.Execute();
        TokenMachineSnapshot snapshot = first.Suspend();

        TokenMachine tiny = Diamond(log).ToTokenMachine(maxTokens: 1);

        Assert.Throws<InvalidOperationException>(() => tiny.Resume(snapshot));
    }

    [Test]
    public void resume_rejects_join_bookkeeping_from_a_different_graph()
    {
        List<string> log = [];
        TokenMachine first = Diamond(log).ToTokenMachine();
        first.Execute();
        first.Execute();
        TokenMachineSnapshot snapshot = first.Suspend();

        Graph joinless = GraphBuilder.StartWith(() => Result.Success).Build();
        TokenMachine other = joinless.ToTokenMachine();

        Assert.Throws<InvalidOperationException>(() => other.Resume(snapshot));
    }

    [Test]
    public void suspend_from_inside_node_logic_is_rejected()
    {
        TokenMachine? machineRef = null;
        Graph graph = GraphBuilder.StartWith(() =>
        {
            Assert.Throws<InvalidOperationException>(() => machineRef!.Suspend());
            return Result.Success;
        }).Build();

        machineRef = graph.ToTokenMachine();
        machineRef.SetStepMode(ParallelStepMode.RunToJoin);

        Assert.That(machineRef.Execute(), Is.EqualTo(Result.Success));
    }
}
