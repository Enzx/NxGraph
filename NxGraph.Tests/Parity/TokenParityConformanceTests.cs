using NxGraph.Authoring;
using NxGraph.Graphs;
using NxGraph.Tokens;

namespace NxGraph.Tests.Parity;

/// <summary>
/// Parity conformance matrix for the token runtimes: every scenario runs through the four
/// token adapters (sync RunToJoin, sync stepped, async ExecuteAsync, async StepAsync) and
/// must produce the same observable trace, order-exact and unnormalized. Fork/join graphs
/// are compared between <see cref="TokenMachine"/> and <see cref="AsyncTokenMachine"/> only —
/// the FSM runtimes throw on fork/join nodes by design. Plain graphs are additionally
/// compared across the families (see the cross-family test), which is where the documented
/// normalizations N1/N2 apply.
/// </summary>
[TestFixture]
public class TokenParityConformanceTests
{
    // ── Recipes ─────────────────────────────────────────────────────────

    /// <summary>load → fork(a | b) → join(All 2) → finish.</summary>
    private static ParityScenario Diamond()
    {
        JoinState join = new(JoinPolicy.All(2));
        StateToken load = GraphBuilder.StartWith(() => Result.Success).SetName("load");
        ForkToken fork = load.ForkTo(
            b => b.To(() => Result.Success).SetName("a")
                .To(join).SetName("join")
                .To(() => Result.Success).SetName("finish"),
            b => b.To(() => Result.Success).SetName("b")
                .To(join));
        return new ParityScenario { Graph = fork.SetName("fork").Build() };
    }

    /// <summary>
    /// load → fork(a | b | c) → join(Quorum 2) → finish: the third token arrives after the
    /// join already fired this run and must absorb benignly (quorum leftovers).
    /// </summary>
    private static ParityScenario QuorumLeftover()
    {
        JoinState join = new(JoinPolicy.Quorum(2));
        StateToken load = GraphBuilder.StartWith(() => Result.Success).SetName("load");
        ForkToken fork = load.ForkTo(
            b => b.To(() => Result.Success).SetName("a")
                .To(join).SetName("join")
                .To(() => Result.Success).SetName("finish"),
            b => b.To(() => Result.Success).SetName("b")
                .To(join),
            b => b.To(() => Result.Success).SetName("c")
                .To(join));
        return new ParityScenario { Graph = fork.SetName("fork").Build() };
    }

    /// <summary>
    /// load → fork(dies | waits → join(All 2)): the first branch's token dies (failure, no
    /// retry, no failure edge), so the waiting token starves at the join that never fires.
    /// </summary>
    private static ParityScenario Starvation()
    {
        JoinState join = new(JoinPolicy.All(2));
        StateToken load = GraphBuilder.StartWith(() => Result.Success).SetName("load");
        ForkToken fork = load.ForkTo(
            b => b.To(() => Result.Failure).SetName("dies"),
            b => b.To(() => Result.Success).SetName("waits")
                .To(join).SetName("join"));
        return new ParityScenario { Graph = fork.SetName("fork").Build() };
    }

    /// <summary>Plain three-node chain — no fork/join, runnable by every runtime family.</summary>
    private static ParityScenario PlainChain()
    {
        Graph graph = GraphBuilder
            .StartWith(() => Result.Success).SetName("a")
            .To(() => Result.Success).SetName("b")
            .To(() => Result.Success).SetName("c")
            .Build();
        return new ParityScenario { Graph = graph };
    }

    // ── Token matrix ────────────────────────────────────────────────────

    [Test]
    public async Task fork_join_diamond_runs_identically()
    {
        List<string> baseline = await ParityRunner.AssertTokenParityAsync(Diamond);
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("join-fired join survivor t1"),
                "Branch order is deterministic: t0 parks first, t1 arrives second and fires the join.");
            Assert.That(baseline, Does.Contain("retired t0 at join Joined"));
            Assert.That(baseline, Does.Contain("run-result Success"));
        });
    }

    [Test]
    public async Task quorum_leftover_absorbs_identically()
    {
        List<string> baseline = await ParityRunner.AssertTokenParityAsync(QuorumLeftover);
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("retired t2 at join Absorbed"),
                "A token parked at a join that already fired this run absorbs benignly.");
            Assert.That(baseline, Does.Contain("run-result Success"));
        });
    }

    [Test]
    public async Task starvation_fails_identically()
    {
        List<string> baseline = await ParityRunner.AssertTokenParityAsync(Starvation);
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("retired t0 at dies Failed"),
                "The failing branch's token dies after exhausting the (absent) fault handling.");
            Assert.That(baseline, Does.Contain("retired t1 at join Starved"),
                "The waiting token starves at the join that never fired.");
            Assert.That(baseline, Does.Contain("run-result Failure"));
        });
    }

    // ── Cross-family: plain graphs run identically under either family ──

    [Test]
    public async Task plain_graph_runs_identically_under_fsm_and_token_machines()
    {
        // Pins the documented claim that graphs without fork/join nodes run identically
        // under either runtime family. This is the one comparison that needs the
        // documented normalizations: N1 strips the FSM machines' Transitioning status
        // hops, N2 strips the token machines' token-id dimension (see TraceNormalizer).
        List<string> baseline = await ParityRunner.AssertCrossFamilyParityAsync(PlainChain);
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Does.Contain("entered a"));
            Assert.That(baseline, Does.Contain("transition b->c"));
            Assert.That(baseline, Does.Contain("machine-completed Success"));
            Assert.That(baseline, Does.Contain("run-result Success"));
        });
    }
}
