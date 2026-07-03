using NxGraph.Blackboards;

namespace NxGraph.Tests.Blackboards;

/// <summary>
/// The routed context: scope routing, replace semantics, unbound-scope errors — no FSM involved.
/// </summary>
[TestFixture]
public class BlackboardContextTests
{
    private static (BlackboardSchema world, BlackboardKey<bool> alarm) GlobalSchema()
    {
        BlackboardSchema world = new("world", BlackboardScope.Global);
        BlackboardKey<bool> alarm = world.Register<bool>("AlarmRaised");
        return (world, alarm);
    }

    private static (BlackboardSchema enemy, BlackboardKey<int> health) GraphSchema()
    {
        BlackboardSchema enemy = new("enemy");
        BlackboardKey<int> health = enemy.Register<int>("health", 100);
        return (enemy, health);
    }

    [Test]
    public void routes_keys_by_their_schema_scope()
    {
        (BlackboardSchema world, BlackboardKey<bool> alarm) = GlobalSchema();
        (BlackboardSchema enemy, BlackboardKey<int> health) = GraphSchema();

        Blackboard worldBoard = new(world);
        Blackboard enemyBoard = new(enemy);
        BlackboardContext ctx = new(worldBoard, enemyBoard);

        ctx.Set(alarm, true);
        ctx.Set(health, 50);

        Assert.Multiple(() =>
        {
            Assert.That(ctx.Get(alarm), Is.True);
            Assert.That(ctx.Get(health), Is.EqualTo(50));
            Assert.That(worldBoard.Get(alarm), Is.True, "Global key must land on the global board.");
            Assert.That(enemyBoard.Get(health), Is.EqualTo(50), "Graph key must land on the graph board.");
        });
    }

    [Test]
    public void with_routes_by_scope_and_replaces_on_rebind()
    {
        (BlackboardSchema world, BlackboardKey<bool> alarm) = GlobalSchema();
        (BlackboardSchema enemy, BlackboardKey<int> health) = GraphSchema();

        Blackboard worldBoard = new(world);
        Blackboard enemyA = new(enemy);
        Blackboard enemyB = new(enemy);

        BlackboardContext ctx = default;
        ctx = ctx.With(worldBoard).With(enemyA);

        Assert.Multiple(() =>
        {
            Assert.That(ctx.Global, Is.SameAs(worldBoard));
            Assert.That(ctx.Graph, Is.SameAs(enemyA));
        });

        ctx = ctx.With(enemyB);

        Assert.Multiple(() =>
        {
            Assert.That(ctx.Graph, Is.SameAs(enemyB), "Rebinding a scope must replace the board.");
            Assert.That(ctx.Global, Is.SameAs(worldBoard), "Rebinding one scope must not disturb the other.");
            _ = alarm;
            _ = health;
        });
    }

    [Test]
    public void empty_context_reports_and_throws_precisely()
    {
        (_, BlackboardKey<bool> alarm) = GlobalSchema();
        (_, BlackboardKey<int> health) = GraphSchema();

        BlackboardContext ctx = default;

        Assert.Multiple(() =>
        {
            Assert.That(ctx.IsEmpty, Is.True);
            Assert.That(ctx.HasBoard(BlackboardScope.Global), Is.False);
            Assert.That(ctx.HasBoard(BlackboardScope.Graph), Is.False);
            Assert.That(ctx.Board(BlackboardScope.Global), Is.Null);
            Assert.That(() => ctx.Get(alarm),
                Throws.InvalidOperationException.With.Message
                    .Contain("global key 'AlarmRaised'").And.Message.Contain("WithBlackboard"));
            Assert.That(() => ctx.Get(health),
                Throws.InvalidOperationException.With.Message.Contain("graph key 'health'"));
            Assert.That(() => ctx.Set(health, 1), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void try_get_is_false_for_unbound_scope_and_foreign_key()
    {
        (BlackboardSchema world, BlackboardKey<bool> alarm) = GlobalSchema();
        (_, BlackboardKey<int> health) = GraphSchema();
        BlackboardSchema otherEnemy = new("other-enemy");
        BlackboardKey<int> foreignHealth = otherEnemy.Register<int>("health");

        BlackboardContext ctx = new(new Blackboard(world), new Blackboard(otherEnemy));

        Assert.Multiple(() =>
        {
            Assert.That(ctx.TryGet(alarm, out bool alarmValue), Is.True);
            Assert.That(alarmValue, Is.False);
            Assert.That(ctx.TryGet(health, out _), Is.False, "Key from a different graph schema must not resolve.");
            Assert.That(ctx.TryGet(foreignHealth, out int fh), Is.True);
            Assert.That(fh, Is.EqualTo(0));
            Assert.That(default(BlackboardContext).TryGet(alarm, out _), Is.False);
            Assert.That(ctx.TryGet(default(BlackboardKey<int>), out _), Is.False);
        });
    }

    [Test]
    public void get_ref_routes_and_mutates_in_place()
    {
        (BlackboardSchema world, BlackboardKey<bool> _) = GlobalSchema();
        (BlackboardSchema enemy, BlackboardKey<int> health) = GraphSchema();

        BlackboardContext ctx = new(new Blackboard(world), new Blackboard(enemy));
        ctx.GetRef(health) -= 30;

        Assert.That(ctx.Get(health), Is.EqualTo(70));
    }

    [Test]
    public void constructor_rejects_boards_in_the_wrong_slot()
    {
        (BlackboardSchema world, _) = GlobalSchema();
        (BlackboardSchema enemy, _) = GraphSchema();

        Assert.Multiple(() =>
        {
            Assert.That(() => new BlackboardContext(new Blackboard(enemy), null), Throws.ArgumentException);
            Assert.That(() => new BlackboardContext(null, new Blackboard(world)), Throws.ArgumentException);
        });
    }

    [Test]
    public void unbound_scope_message_names_the_scope_and_key()
    {
        (_, BlackboardKey<bool> alarm) = GlobalSchema();
        (BlackboardSchema enemy, _) = GraphSchema();

        // Graph board bound, global missing — the error must point at the *global* gap.
        BlackboardContext ctx = new(null, new Blackboard(enemy));

        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => ctx.Get(alarm));
        Assert.That(ex!.Message,
            Does.Contain("global key 'AlarmRaised' used but no global blackboard bound"));
    }
}
