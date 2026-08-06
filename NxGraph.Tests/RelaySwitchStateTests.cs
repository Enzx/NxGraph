using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;

namespace NxGraph.Tests;

/// <summary>
/// The delegate-backed <see cref="RelaySwitchState{TKey}"/> — the <c>.Switch(selector)</c>
/// path. The data-built <see cref="SwitchState{T}"/> (spec 023) is covered by
/// <c>SwitchStateTests</c> / <c>SwitchDefaultCaseTests</c>.
/// </summary>
[TestFixture]
[Category("switch_default")]
public class RelaySwitchStateTests
{
    private enum Mode
    {
        A,
        B,
        C
    }

    [Test]
    public async Task relay_switch_state_should_follow_matching_case()
    {
        const Mode mode = Mode.B;

        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .Switch(() => mode)
            .CaseAsync(Mode.A, new AsyncRelayState(_ => ResultHelpers.Failure))
            .CaseAsync(Mode.B, new AsyncRelayState(_ => ResultHelpers.Success))
            .CaseAsync(Mode.C, new AsyncRelayState(_ => ResultHelpers.Failure))
            .DefaultAsync(new AsyncRelayState(_ => ResultHelpers.Failure))
            .End()
            .Build().ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public void sync_relay_switch_state_should_follow_matching_case()
    {
        const Mode mode = Mode.B;
        bool matchedCaseRan = false;

        StateMachine fsm = GraphBuilder
            .Start()
            .Switch(() => mode)
            .Case(Mode.A, () => Result.Failure)
            .Case(Mode.B, () =>
            {
                matchedCaseRan = true;
                return Result.Success;
            })
            .Case(Mode.C, () => Result.Failure)
            .Default(() => Result.Failure)
            .End()
            .ToStateMachine();

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = fsm.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(matchedCaseRan, Is.True, "The sync runtime must route through the matching case body.");
        });
    }

    [Test]
    public void sync_relay_switch_state_should_use_default_when_no_match()
    {
        const int selector = 99; // no matching case
        bool defaultRan = false;

        StateMachine fsm = GraphBuilder
            .Start()
            .Switch(() => selector)
            .Case(0, () => Result.Failure)
            .Case(1, () => Result.Failure)
            .Default(() =>
            {
                defaultRan = true;
                return Result.Success;
            })
            .End()
            .ToStateMachine();

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = fsm.Execute();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(defaultRan, Is.True, "The sync runtime must route through the explicit default body.");
        });
    }

    [Test]
    public async Task switch_should_follow_default_when_no_case_matches()
    {
        const string key = "nope";

        AsyncStateMachine? fsm = GraphBuilder
            .Start()
            .Switch(() => key)
            .CaseAsync("a", _ => ResultHelpers.Failure)
            .CaseAsync("b", _ => ResultHelpers.Failure)
            .DefaultAsync(_ => ResultHelpers.Success)
            .End()
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public async Task async_switch_without_default_should_terminate_when_no_case_matches()
    {
        // Regression: previously AsyncRelaySwitchState defaulted _defaultNode to default(NodeId)
        // (index 0 = Start) so a no-match case silently looped to Start instead of
        // exiting cleanly. The fix routes the no-default case through NodeId.Default,
        // which the async runtime treats as terminal success.
        const string key = "nope";

        AsyncStateMachine fsm = GraphBuilder
            .Start()
            .Switch(() => key)
            .CaseAsync("a", _ => ResultHelpers.Failure)
            .CaseAsync("b", _ => ResultHelpers.Failure)
            .End()
            .ToAsyncStateMachine();

        Result result = await fsm.ExecuteAsync();
        Assert.That(result, Is.EqualTo(Result.Success));
    }

    [Test]
    public void sync_switch_without_default_should_terminate_when_no_case_matches()
    {
        // Regression mirror of the async test above for the sync runtime.
        const string key = "nope";

        StateMachine fsm = GraphBuilder
            .Start()
            .Switch(() => key)
            .Case("a", () => Result.Failure)
            .Case("b", () => Result.Failure)
            .End()
            .ToStateMachine();

        Result result;
        do
        {
            result = fsm.Execute();
        } while (result == Result.InProgress);

        Assert.That(result, Is.EqualTo(Result.Success));
    }
}