using NxGraph;
using NxGraph.Fsm;

namespace NxFSM.Examples.DungeonCrawler.States;

/// <summary>
/// Advances the room counter and rolls a random encounter.
/// The last room always triggers the <see cref="EncounterType.Boss"/>.
/// </summary>
public sealed class ExploreRoomState : State<DungeonContext>
{
    private static readonly EncounterType[] RegularEncounters =
    [
        EncounterType.Monster,
        EncounterType.Monster,
        EncounterType.Treasure,
        EncounterType.Trap,
        EncounterType.Empty
    ];

    protected override void OnEnter()
    {
        Agent.RoomNumber++;
        Log($"── Room {Agent.RoomNumber} ──────────────────────────────");
    }

    protected override Result OnRun()
    {
        if (Agent.RoomNumber > Agent.TotalRooms)
        {
            Log("A massive ornate door towers before you… the Boss Chamber!");
            Agent.CurrentEncounter = EncounterType.Boss;
        }
        else
        {
            EncounterType encounter = RegularEncounters[Agent.Rng.Next(RegularEncounters.Length)];
            Agent.CurrentEncounter = encounter;

            string description = encounter switch
            {
                EncounterType.Monster  => "You hear growling in the darkness…",
                EncounterType.Treasure => "A faint glimmer catches your eye…",
                EncounterType.Trap     => "The floor tiles look suspiciously uneven…",
                EncounterType.Empty    => "This chamber appears deserted.",
                _                      => "Something stirs ahead…"
            };

            Log(description);
        }

        return Result.Success;
    }

    protected override void OnExit()
    {
        Log($"Encounter determined: {Agent.CurrentEncounter}");
    }
}

