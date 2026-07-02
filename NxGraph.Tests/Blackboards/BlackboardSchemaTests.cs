using NxGraph.Blackboards;

namespace NxGraph.Tests.Blackboards;

/// <summary>
/// Schema registration, freezing, scope rules, and key identity.
/// </summary>
[TestFixture]
public class BlackboardSchemaTests
{
    [Test]
    public void register_returns_valid_keys_with_sequential_ordinals()
    {
        BlackboardSchema schema = new("test");
        BlackboardKey<int> health = schema.Register<int>("health");
        BlackboardKey<string> label = schema.Register<string>("label");
        BlackboardKey<float> speed = schema.Register<float>("speed");

        Assert.Multiple(() =>
        {
            Assert.That(health.IsValid, Is.True);
            Assert.That(label.IsValid, Is.True);
            Assert.That(speed.IsValid, Is.True);
            Assert.That(schema.KeyCount, Is.EqualTo(3));
            Assert.That(schema.Keys[0].Name, Is.EqualTo("health"));
            Assert.That(schema.Keys[0].Ordinal, Is.EqualTo(0));
            Assert.That(schema.Keys[1].Name, Is.EqualTo("label"));
            Assert.That(schema.Keys[1].Ordinal, Is.EqualTo(1));
            Assert.That(schema.Keys[2].Name, Is.EqualTo("speed"));
            Assert.That(schema.Keys[2].Ordinal, Is.EqualTo(2));
            Assert.That(schema.Keys[2].ValueType, Is.EqualTo(typeof(float)));
        });
    }

    [Test]
    public void duplicate_name_throws()
    {
        BlackboardSchema schema = new();
        schema.Register<int>("health");

        Assert.Throws<ArgumentException>(() => schema.Register<float>("health"));
    }

    [Test]
    public void register_after_freeze_throws_with_guidance()
    {
        BlackboardSchema schema = new("frozen");
        schema.Register<int>("health");
        _ = new Blackboard(schema);

        Assert.That(schema.IsFrozen, Is.True);
        InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => schema.Register<int>("late"));
        Assert.That(ex!.Message, Does.Contain("frozen").And.Contain("before constructing"));
    }

    [Test]
    public void try_get_key_finds_registered_names_only()
    {
        BlackboardSchema schema = new();
        schema.Register<int>("health", 42);

        Assert.Multiple(() =>
        {
            Assert.That(schema.TryGetKey("health", out BlackboardKeyDescriptor descriptor), Is.True);
            Assert.That(descriptor.ValueType, Is.EqualTo(typeof(int)));
            Assert.That(schema.TryGetKey("missing", out _), Is.False);
        });
    }

    [Test]
    public void key_equality_is_schema_reference_plus_ordinal()
    {
        BlackboardSchema schemaA = new("a");
        BlackboardSchema schemaB = new("b");

        BlackboardKey<int> first = schemaA.Register<int>("x");
        BlackboardKey<int> copy = first;
        BlackboardKey<int> second = schemaA.Register<int>("y");
        BlackboardKey<int> foreign = schemaB.Register<int>("x");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(copy));
            Assert.That(first == copy, Is.True);
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first, Is.Not.EqualTo(foreign), "Same name/ordinal on another schema must not be equal.");
            Assert.That(first, Is.Not.EqualTo(default(BlackboardKey<int>)));
            Assert.That(default(BlackboardKey<int>).IsValid, Is.False);
        });
    }

    [Test]
    public void scope_defaults_to_graph_and_can_be_global()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new BlackboardSchema().Scope, Is.EqualTo(BlackboardScope.Graph));
            Assert.That(new BlackboardSchema("world", BlackboardScope.Global).Scope,
                Is.EqualTo(BlackboardScope.Global));
        });
    }

    [Test]
    public void node_scope_is_reserved_and_rejected()
    {
        ArgumentOutOfRangeException? ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new BlackboardSchema("local", BlackboardScope.Node));
        Assert.That(ex!.Message, Does.Contain("reserved"));
    }

    [Test]
    public void undefined_scope_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new BlackboardSchema("bad", (BlackboardScope)99));
    }
}
