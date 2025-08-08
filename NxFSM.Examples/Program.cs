// See https://aka.ms/new-console-template for more information

using NxGraph;
using NxGraph.Authoring;
using NxGraph.Fsm;


Console.WriteLine("Simple FSM Example");
Console.WriteLine();
StateMachine fsm = GraphBuilder
    .StartWith(_ =>
    {
        Console.WriteLine("Starting FSM...");
        return ResultHelpers.Success;
    })
    .To(_ =>
    {
        Console.WriteLine("Transitioning to next state...");
        return ResultHelpers.Success;
    })
    .To(_ => ResultHelpers.Success)
    .ToStateMachine();

Result result = await fsm.ExecuteAsync();

Console.WriteLine($"Simple FSM execution result: {result}");
Console.WriteLine();
Console.WriteLine();
Console.WriteLine("AI Enemy Example");
Console.WriteLine();
Example.AiEnemy aiEnemy = new();
await aiEnemy.ExecuteAsync();


return 0;

namespace Example
{
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
        private StateMachine<AiEnemy> StateMachine { get; set; }

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
                .ToStateMachine<AiEnemy>()
                .WithAgent(this);
        }

        public async ValueTask ExecuteAsync(CancellationToken ct = default)
        {
            Result result = await StateMachine.ExecuteAsync(ct);
            Console.WriteLine($"FSM execution result: {result}");
        }
    }

    public class IdleState : State<AiEnemy>
    {
        protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            Console.WriteLine();
            Console.WriteLine("Idling...");
            return ResultHelpers.Success;
        }
    }

    public class PatrolState : State<AiEnemy>
    {
        protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            Console.WriteLine($"Patrolling the area around {Agent.Target.Name}...");
            return ResultHelpers.Success;
        }
    }

    public class AttackState : State<AiEnemy>
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