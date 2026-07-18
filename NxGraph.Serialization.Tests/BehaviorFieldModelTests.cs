using NxGraph.Behaviors;
using NxGraph.Blackboards;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization.Tests;

/// <summary>
/// The neutral behavior field model (payload version 8): every kind the
/// <see cref="BehaviorFieldWriter"/> can write reads back through the
/// <see cref="BehaviorFieldReader"/>, and the deliberately small surface fails loud —
/// duplicate names, missing fields, kind mismatches, non-primitive binding literals.
/// </summary>
[TestFixture]
[Category("serialization")]
public class BehaviorFieldModelTests
{
    private enum Mood
    {
        Calm,
        Feisty,
    }

    private static BehaviorFieldReader RoundTrip(Action<BehaviorFieldWriter> write)
    {
        BehaviorFieldWriter writer = new();
        write(writer);
        return new BehaviorFieldReader(writer.ToFields());
    }

    [Test]
    public void Every_primitive_kind_roundtrips()
    {
        BehaviorFieldReader reader = RoundTrip(w =>
        {
            w.WriteString("s", "text");
            w.WriteString("sNull", null);
            w.WriteBool("b", true);
            w.WriteInt32("i32", -7);
            w.WriteInt64("i64", 1L << 40);
            w.WriteSingle("f32", 1.5f);
            w.WriteDouble("f64", 2.25);
            w.WriteEnum("mood", Mood.Feisty);
        });

        Assert.Multiple(() =>
        {
            Assert.That(reader.Has("s"), Is.True);
            Assert.That(reader.Has("missing"), Is.False);
            Assert.That(reader.ReadString("s"), Is.EqualTo("text"));
            Assert.That(reader.ReadString("sNull"), Is.Null);
            Assert.That(reader.ReadBool("b"), Is.True);
            Assert.That(reader.ReadInt32("i32"), Is.EqualTo(-7));
            Assert.That(reader.ReadInt64("i64"), Is.EqualTo(1L << 40));
            Assert.That(reader.ReadSingle("f32"), Is.EqualTo(1.5f));
            Assert.That(reader.ReadDouble("f64"), Is.EqualTo(2.25));
            Assert.That(reader.ReadEnum<Mood>("mood"), Is.EqualTo(Mood.Feisty));
        });
    }

    [Test]
    public void Bindings_roundtrip_in_key_and_literal_forms()
    {
        BlackboardSchema schema = new("model");
        BlackboardKey<int> intKey = schema.Register("count", 0);

        BehaviorFieldReader reader = RoundTrip(w =>
        {
            w.WriteBinding<int>("keyForm", intKey);
            w.WriteBinding<int>("intLiteral", 9);
            w.WriteBinding<long>("longLiteral", 10L);
            w.WriteBinding<float>("floatLiteral", 0.5f);
            w.WriteBinding<double>("doubleLiteral", 0.25);
            w.WriteBinding<bool>("boolLiteral", true);
            w.WriteBinding<string>("stringLiteral", "hello");
            w.WriteBinding<Mood>("enumLiteral", Mood.Calm);
        });

        Assert.Multiple(() =>
        {
            BlackboardValue<int> keyForm = reader.ReadBinding<int>("keyForm");
            Assert.That(keyForm.IsBound, Is.True);
            Assert.That(keyForm.KeyName, Is.EqualTo("count"));

            Assert.That(reader.ReadBinding<int>("intLiteral").Literal, Is.EqualTo(9));
            Assert.That(reader.ReadBinding<long>("longLiteral").Literal, Is.EqualTo(10L));
            Assert.That(reader.ReadBinding<float>("floatLiteral").Literal, Is.EqualTo(0.5f));
            Assert.That(reader.ReadBinding<double>("doubleLiteral").Literal, Is.EqualTo(0.25));
            Assert.That(reader.ReadBinding<bool>("boolLiteral").Literal, Is.True);
            Assert.That(reader.ReadBinding<string>("stringLiteral").Literal, Is.EqualTo("hello"));
            Assert.That(reader.ReadBinding<Mood>("enumLiteral").Literal, Is.EqualTo(Mood.Calm));
        });
    }

    [Test]
    public void Non_primitive_binding_literal_is_rejected_at_write_time()
    {
        BehaviorFieldWriter writer = new();
        NotSupportedException? ex = Assert.Throws<NotSupportedException>(() =>
            writer.WriteBinding<int[]>("bad", new[] { 1, 2 }));
        Assert.That(ex!.Message, Does.Contain("outside the behavior field model"));

        NotSupportedException? nullEx = Assert.Throws<NotSupportedException>(() =>
            writer.WriteBinding<int[]>("badNull", default));
        Assert.That(nullEx!.Message, Does.Contain("outside the behavior field model"));
    }

    [Test]
    public void Duplicate_and_empty_field_names_are_rejected()
    {
        BehaviorFieldWriter writer = new();
        writer.WriteBool("flag", true);

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => writer.WriteBool("flag", false));
            Assert.Throws<ArgumentException>(() => writer.WriteBool("", true));
        });
    }

    [Test]
    public void Missing_field_and_kind_mismatch_throw_targeted()
    {
        BehaviorFieldReader reader = RoundTrip(w => w.WriteInt32("i", 1));

        Assert.Multiple(() =>
        {
            InvalidOperationException? missing =
                Assert.Throws<InvalidOperationException>(() => reader.ReadInt32("nope"));
            Assert.That(missing!.Message, Does.Contain("'nope'").And.Contain("missing"));

            InvalidOperationException? mismatch =
                Assert.Throws<InvalidOperationException>(() => reader.ReadBool("i"));
            Assert.That(mismatch!.Message, Does.Contain("'i'").And.Contain("expected"));
        });
    }

    [Test]
    public void Binding_literal_type_mismatch_throws_targeted()
    {
        BehaviorFieldReader reader = RoundTrip(w => w.WriteBinding<int>("n", 3));

        InvalidOperationException? ex =
            Assert.Throws<InvalidOperationException>(() => reader.ReadBinding<string>("n"));
        Assert.That(ex!.Message, Does.Contain("'n'").And.Contain("does not match"));
    }

    [Test]
    public void Unknown_enum_member_throws_targeted()
    {
        BehaviorFieldReader reader = RoundTrip(w => w.WriteEnum("mood", Mood.Feisty));

        // Read the member back under a different enum type that lacks the member.
        InvalidOperationException? ex =
            Assert.Throws<InvalidOperationException>(() => reader.ReadEnum<LogSeverity>("mood"));
        Assert.That(ex!.Message, Does.Contain("Feisty").And.Contain("not defined"));
    }

    [Test]
    public void Enum_binding_literal_with_wrong_enum_type_throws()
    {
        BehaviorFieldReader reader = RoundTrip(w => w.WriteBinding<Mood>("mood", Mood.Feisty));

        InvalidOperationException? ex =
            Assert.Throws<InvalidOperationException>(() => reader.ReadBinding<LogSeverity>("mood"));
        Assert.That(ex!.Message, Does.Contain("Feisty").And.Contain("not defined"));
    }

    [Test]
    public void Corrupt_binding_payloads_throw_targeted()
    {
        BehaviorFieldReader noPayload = new([
            new BehaviorField("b", new BehaviorFieldValue(BehaviorFieldKind.Binding)),
        ]);
        BehaviorFieldReader neither = new([
            new BehaviorField("b", new BehaviorFieldValue(BehaviorFieldKind.Binding,
                binding: new BehaviorBinding(keyName: null, literal: null))),
        ]);
        BehaviorFieldReader nested = new([
            new BehaviorField("b", new BehaviorFieldValue(BehaviorFieldKind.Binding,
                binding: new BehaviorBinding(keyName: null,
                    literal: new BehaviorFieldValue(BehaviorFieldKind.Binding)))),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(Assert.Throws<InvalidOperationException>(() => noPayload.ReadBinding<int>("b"))!
                .Message, Does.Contain("no binding payload"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => neither.ReadBinding<int>("b"))!
                .Message, Does.Contain("neither a key name nor a literal"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => nested.ReadBinding<int>("b"))!
                .Message, Does.Contain("nests a binding"));
        });
    }

    [Test]
    public void Reader_and_bound_value_reject_invalid_arguments()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => _ = new BehaviorFieldReader(null!));
            Assert.Throws<ArgumentException>(() => BlackboardValue<int>.Bound(""));
        });
    }
}
