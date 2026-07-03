using NxGraph;
using NxGraph.Fsm.Async;

namespace NxFSM.Examples.BlackboardDemo;

/// <summary>
/// The guard's senses: reads the shared world board, writes both boards. Demonstrates the
/// two channels side by side — <c>Agent</c> (who am I) and <c>Bb</c> (what do I / we know),
/// with global and graph keys flowing through the same routed context.
/// </summary>
public sealed class SenseState : AsyncState<Guard>
{
    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        int room = Bb.Get(GuardKeys.Room);
        int intruderRoom = Bb.Get(WorldKeys.IntruderRoom);

        if (!Bb.Get(WorldKeys.IntruderCaught) && room == intruderRoom)
        {
            Bb.Set(WorldKeys.IntruderCaught, true);
            Bb.Set(WorldKeys.CaughtBy, Agent.Name);
            Console.WriteLine($"    {Agent.Name} corners the intruder in room {room} — caught!");
        }
        else if (!Bb.Get(WorldKeys.IntruderCaught) && Math.Abs(room - intruderRoom) == 1)
        {
            // Spotted one room away: the guard gives chase, the intruder keeps moving.
            Bb.Set(GuardKeys.Suspicion, 100);
            Bb.Set(WorldKeys.LastSeenRoom, intruderRoom);   // global write — every guard sees it
            Bb.GetRef(WorldKeys.Sightings)++;               // in-place mutation via GetRef
            if (Bb.Get(WorldKeys.AlarmLevel) < 2)
            {
                Bb.GetRef(WorldKeys.AlarmLevel)++;
            }

            Console.WriteLine($"    {Agent.Name} spots the intruder near room {intruderRoom}! " +
                              $"(alarm level {Bb.Get(WorldKeys.AlarmLevel)})");
        }
        else
        {
            ref int suspicion = ref Bb.GetRef(GuardKeys.Suspicion);
            suspicion = Math.Max(0, suspicion - 25); // cools off when nothing is seen
        }

        return ResultHelpers.Success;
    }
}

/// <summary>Walks the guard's fixed route; direction is an agent trait, position is board state.</summary>
public sealed class PatrolState : AsyncState<Guard>
{
    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        ref int room = ref Bb.GetRef(GuardKeys.Room);
        room = (room + Agent.PatrolDirection + BlackboardDemoExample.RoomCount) % BlackboardDemoExample.RoomCount;
        Bb.GetRef(GuardKeys.Stamina) -= 10;

        Console.WriteLine($"    {Agent.Name} patrols to room {room}.");
        return ResultHelpers.Success;
    }
}

/// <summary>Moves one room toward the last reported sighting (global memory).</summary>
public sealed class InvestigateState : AsyncState<Guard>
{
    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        ref int room = ref Bb.GetRef(GuardKeys.Room);
        int target = Bb.Get(WorldKeys.LastSeenRoom);
        room += Math.Sign(target - room);
        Bb.GetRef(GuardKeys.Stamina) -= 15;

        Console.WriteLine($"    {Agent.Name} investigates room {room} (last sighting: room {target}).");
        return ResultHelpers.Success;
    }
}

/// <summary>Sprints toward the last reported sighting; the catch resolves next tick in <see cref="SenseState"/>.</summary>
public sealed class ChaseState : AsyncState<Guard>
{
    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        ref int room = ref Bb.GetRef(GuardKeys.Room);
        int target = Bb.Get(WorldKeys.LastSeenRoom);
        room += Math.Sign(target - room);
        Bb.GetRef(GuardKeys.Stamina) -= 25;

        Console.WriteLine($"    {Agent.Name} chases toward room {target} " +
                          $"(now in room {room}, stamina {Bb.Get(GuardKeys.Stamina)}).");
        return ResultHelpers.Success;
    }
}

/// <summary>Catches breath (or stands down once the intruder is caught).</summary>
public sealed class RestState : AsyncState<Guard>
{
    protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
    {
        ref int stamina = ref Bb.GetRef(GuardKeys.Stamina);
        stamina = Math.Min(100, stamina + 40);

        Console.WriteLine(Bb.Get(WorldKeys.IntruderCaught)
            ? $"    {Agent.Name} stands down."
            : $"    {Agent.Name} rests (stamina {stamina}).");
        return ResultHelpers.Success;
    }
}
