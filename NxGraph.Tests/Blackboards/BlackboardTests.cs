using NxGraph.Blackboards;

namespace NxGraph.Tests.Blackboards;

/// <summary>
/// Board semantics: defaults, typed round-trips, ref access, error cases, and
/// instance independence over a shared schema.
/// </summary>
[TestFixture]
public class BlackboardTests
{
    private readonly struct Vector2(float x, float y)
    {
        public readonly float X = x;
        public readonly float Y = y;
    }

    [Test]
    public void defaults_are_readable_before_any_set()
    {
        BlackboardSchema schema = new();
        BlackboardKey<int> plain = schema.Register<int>("plain");
        BlackboardKey<int> seeded = schema.Register<int>("seeded", 42);
        BlackboardKey<string> label = schema.Register<string>("label", "hello");

        Blackboard bb = new(schema);

        Assert.Multiple(() =>
        {
            Assert.That(bb.Get(plain), Is.EqualTo(0));
            Assert.That(bb.Get(seeded), Is.EqualTo(42));
            Assert.That(bb.Get(label), Is.EqualTo("hello"));
        });
    }

    [Test]
    public void get_set_round_trips_value_types_strings_nullables_and_structs()
    {
        BlackboardSchema schema = new();
        BlackboardKey<int> count = schema.Register<int>("count");
        BlackboardKey<string?> name = schema.Register<string?>("name");
        BlackboardKey<int?> maybe = schema.Register<int?>("maybe");
        BlackboardKey<Vector2> pos = schema.Register<Vector2>("pos");

        Blackboard bb = new(schema);
        bb.Set(count, 7);
        bb.Set(name, "enemy");
        bb.Set(maybe, 3);
        bb.Set(pos, new Vector2(1f, 2f));

        Assert.Multiple(() =>
        {
            Assert.That(bb.Get(count), Is.EqualTo(7));
            Assert.That(bb.Get(name), Is.EqualTo("enemy"));
            Assert.That(bb.Get(maybe), Is.EqualTo(3));
            Assert.That(bb.Get(pos).X, Is.EqualTo(1f));
            Assert.That(bb.Get(pos).Y, Is.EqualTo(2f));
        });
    }

    [Test]
    public void get_ref_mutates_struct_slots_in_place()
    {
        BlackboardSchema schema = new();
        BlackboardKey<int> counter = schema.Register<int>("counter");
        Blackboard bb = new(schema);

        bb.GetRef(counter)++;
        bb.GetRef(counter)++;

        Assert.That(bb.Get(counter), Is.EqualTo(2));
    }

    [Test]
    public void invalid_and_foreign_keys_throw_on_get_and_set()
    {
        BlackboardSchema mine = new("mine");
        BlackboardSchema other = new("other");
        mine.Register<int>("x");
        BlackboardKey<int> foreign = other.Register<int>("x");

        Blackboard bb = new(mine);

        Assert.Multiple(() =>
        {
            Assert.That(() => bb.Get(default(BlackboardKey<int>)),
                Throws.InvalidOperationException.With.Message.Contain("Uninitialized"));
            Assert.That(() => bb.Set(default(BlackboardKey<int>), 1), Throws.InvalidOperationException);
            Assert.That(() => bb.Get(foreign),
                Throws.InvalidOperationException.With.Message.Contain("mine").And.Message.Contain("other"));
            Assert.That(() => bb.Set(foreign, 1), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void try_get_returns_false_instead_of_throwing()
    {
        BlackboardSchema mine = new("mine");
        BlackboardSchema other = new("other");
        BlackboardKey<int> valid = mine.Register<int>("x", 5);
        BlackboardKey<int> foreign = other.Register<int>("x");

        Blackboard bb = new(mine);

        Assert.Multiple(() =>
        {
            Assert.That(bb.TryGet(valid, out int value), Is.True);
            Assert.That(value, Is.EqualTo(5));
            Assert.That(bb.TryGet(default(BlackboardKey<int>), out _), Is.False);
            Assert.That(bb.TryGet(foreign, out _), Is.False);
        });
    }

    [Test]
    public void reset_to_defaults_restores_registered_defaults()
    {
        BlackboardSchema schema = new();
        BlackboardKey<int> seeded = schema.Register<int>("seeded", 42);
        BlackboardKey<string> label = schema.Register<string>("label", "initial");

        Blackboard bb = new(schema);
        bb.Set(seeded, -1);
        bb.Set(label, "dirty");

        bb.ResetToDefaults();

        Assert.Multiple(() =>
        {
            Assert.That(bb.Get(seeded), Is.EqualTo(42));
            Assert.That(bb.Get(label), Is.EqualTo("initial"));
        });
    }

    [Test]
    public void two_boards_over_one_schema_are_independent()
    {
        BlackboardSchema schema = new("shared");
        BlackboardKey<int> value = schema.Register<int>("value", 10);

        Blackboard first = new(schema);
        Blackboard second = new(schema);

        first.Set(value, 111);

        Assert.Multiple(() =>
        {
            Assert.That(first.Get(value), Is.EqualTo(111));
            Assert.That(second.Get(value), Is.EqualTo(10), "Writes on one board must not leak into another.");
        });
    }

    [Test]
    public void descriptors_box_read_write_and_reset()
    {
        BlackboardSchema schema = new();
        BlackboardKey<int> count = schema.Register<int>("count", 3);
        Blackboard bb = new(schema);

        Assert.That(schema.TryGetKey("count", out BlackboardKeyDescriptor descriptor), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.GetValue(bb), Is.EqualTo(3));
            descriptor.SetValue(bb, 99);
            Assert.That(bb.Get(count), Is.EqualTo(99));
            descriptor.ResetValue(bb);
            Assert.That(bb.Get(count), Is.EqualTo(3));
            Assert.That(() => descriptor.SetValue(bb, "wrong type"), Throws.InstanceOf<InvalidCastException>());
        });
    }
}
