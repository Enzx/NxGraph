// See https://aka.ms/new-console-template for more information

using NxFSM.Examples;
using NxFSM.Examples.DungeonCrawler;
using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;

IAsyncStateMachineObserver observer = new ConsoleStateObserver();
Console.WriteLine("Simple FSM Example");
Console.WriteLine();
AsyncStateMachine fsm = GraphBuilder
    .StartWith(_ =>
    {
        Console.WriteLine("Initializing workflow...");
        return ResultHelpers.Success;
    }).SetName("Initial")
    .To(_ =>
    {
        Console.WriteLine("Running first step...");
        return ResultHelpers.Success;
    }).SetName("Second")
    .To(_ =>
    {
        Console.WriteLine("Running second step...");
        return ResultHelpers.Success;
    }).SetName("End")
    .ToAsyncStateMachine(observer);

Result result = await fsm.ExecuteAsync();


Console.WriteLine($"Simple FSM execution result: {result}");
Console.WriteLine();
Console.WriteLine();
Console.WriteLine("AI Enemy Example");
Console.WriteLine();
AiEnemy aiEnemy = new();
await aiEnemy.ExecuteAsync();

await SerializationExample.Run();

ExportersExample.Run();

Console.WriteLine();
Console.WriteLine();
Console.WriteLine("Dungeon Crawler Example (Sync FSM)");
Console.WriteLine();
DungeonCrawlerExample.Run();

return 0;


namespace NxFSM.Examples
{
    public class ConsoleStateObserver : IAsyncStateMachineObserver
    {
        public ValueTask OnStateEntered(NodeId id, CancellationToken ct = default)
        {
            Console.WriteLine($"{id.Name}::Enter");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnStateExited(NodeId id, CancellationToken ct = default)
        {
            Console.WriteLine($"{id.Name}::Exit");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnTransition(NodeId from, NodeId to, CancellationToken ct = default)
        {
            Console.WriteLine($"{from.Name}->{to.Name}");
            return ValueTask.CompletedTask;
        }
    }

    public sealed class Player
    {
        public int Health { get; set; } = 3;

        public bool IsAlive => Health > 0;

        public int DistanceToTarget { get; set; }

        public bool IsTargetInRange
        {
            get
            {
                DistanceToTarget++;
                return DistanceToTarget <= 3 && IsAlive;
            }
        }

        public string Name { get; set; } = "Player";
    }

    public class AiEnemy
    {
        public Player Target { get; set; } = new();
        private AsyncStateMachine<AiEnemy> StateMachine { get; set; }

        public AiEnemy()
        {
            IdleState idleState = new();
            AttackState attackState = new();
            PatrolState patrolState = new();
            StateMachine = GraphBuilder
                .StartWith(idleState)
                .If(() => Target.IsTargetInRange)
                .Then(attackState).WaitFor(1.Seconds()).To(idleState)
                .Else(patrolState)
                .ToAsyncStateMachine<AiEnemy>()
                .WithAgent(this);
        }

        public async ValueTask ExecuteAsync(CancellationToken ct = default)
        {
            Result result = await StateMachine.ExecuteAsync(ct);
            Console.WriteLine($"FSM execution result: {result}");
        }
    }

    public class IdleState : AsyncState<AiEnemy>
    {
        protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            Console.WriteLine();
            Console.WriteLine("Idling...");
            return ResultHelpers.Success;
        }
    }

    public class PatrolState : AsyncState<AiEnemy>
    {
        protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            Console.WriteLine($"Patrolling the area around {Agent.Target.Name}...");
            return ResultHelpers.Success;
        }
    }

    public class AttackState : AsyncState<AiEnemy>
    {
        protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            Console.WriteLine($"Attacking the target({Agent.Target.Name})...");
            if (Agent.Target.IsAlive)
            {
                Agent.Target.Health--;
                Console.WriteLine($"Target's health is now {Agent.Target.Health}.");
            }

            if (Agent.Target.IsAlive == false)
            {
                Console.WriteLine("Target is defeated!");
            }

            return ResultHelpers.Success;
        }
    }
}