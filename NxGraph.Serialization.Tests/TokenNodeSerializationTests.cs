using NxGraph.Tokens;

namespace NxGraph.Serialization.Tests;

/// <summary>
/// The machine state (<see cref="TokenMachineSnapshot"/>) is a plain record and serializes
/// independently with any serializer, which the round-trip test below pins. The graph-payload
/// wire format for fork/join nodes (payload version 6) is covered by
/// <see cref="ForkJoinSerializationTests"/>.
/// </summary>
[TestFixture]
public class TokenNodeSerializationTests
{
    [Test]
    public void token_machine_snapshot_round_trips_through_plain_json()
    {
        TokenMachineSnapshot snapshot = new(
            Fsm.ExecutionStatus.Running,
            MidRun: true,
            NextTokenId: 3,
            Tokens:
            [
                new TokenRecord(0, 2, 0, false, TokenPhase.Parked),
                new TokenRecord(2, 4, 1, true, TokenPhase.Runnable),
            ],
            JoinArrivals: [0, 0, 1, 0, 0, 0])
        {
            AnyTokenDied = true,
            JoinsFired = [false, false, true, false, false, false],
        };

        string json = System.Text.Json.JsonSerializer.Serialize(snapshot);
        TokenMachineSnapshot? rebuilt =
            System.Text.Json.JsonSerializer.Deserialize<TokenMachineSnapshot>(json);

        Assert.Multiple(() =>
        {
            Assert.That(rebuilt, Is.Not.Null);
            Assert.That(rebuilt!.Status, Is.EqualTo(Fsm.ExecutionStatus.Running));
            Assert.That(rebuilt.MidRun, Is.True);
            Assert.That(rebuilt.NextTokenId, Is.EqualTo(3));
            Assert.That(rebuilt.AnyTokenDied, Is.True);
            Assert.That(rebuilt.Tokens, Has.Length.EqualTo(2));
            Assert.That(rebuilt.Tokens[0].Phase, Is.EqualTo(TokenPhase.Parked));
            Assert.That(rebuilt.Tokens[1].Attempts, Is.EqualTo(1));
            Assert.That(rebuilt.JoinArrivals[2], Is.EqualTo(1));
            Assert.That(rebuilt.JoinsFired[2], Is.True);
        });
    }
}
