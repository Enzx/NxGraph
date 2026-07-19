using System.Text;
using System.Text.Json;
using MessagePack;
using MessagePack.Resolvers;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization.Tests;

/// <summary>
/// The blackboard as a durability artifact: JSON/MessagePack round-trips, mismatch policies,
/// and the full three-artifact loop (graph + snapshot + boards).
/// </summary>
[TestFixture]
[Category("serialization")]
public class BlackboardSerializationTests
{
    private readonly BlackboardSerializer _serializer = new();

    private static BlackboardSchema NewSchema(string? name = "stats")
    {
        return new BlackboardSchema(name);
    }

    private static async Task<MemoryStream> SaveJson(BlackboardSerializer serializer, Blackboard board)
    {
        MemoryStream stream = new();
        await serializer.ToJsonAsync(board, stream);
        stream.Position = 0;
        return stream;
    }

    private static async Task<MemoryStream> SaveBinary(BlackboardSerializer serializer, Blackboard board)
    {
        MemoryStream stream = new();
        await serializer.ToBinaryAsync(board, stream);
        stream.Position = 0;
        return stream;
    }

    [Test]
    public async Task json_round_trip_restores_values_and_defaults()
    {
        BlackboardSchema schema = NewSchema();
        BlackboardKey<int> score = schema.Register<int>("score");
        BlackboardKey<string> label = schema.Register<string>("label", "fresh");
        BlackboardKey<float> speed = schema.Register<float>("speed", 1.5f);

        Blackboard source = new(schema);
        source.Set(score, 42);
        source.Set(label, "saved");
        // speed left on its default

        await using MemoryStream stream = await SaveJson(_serializer, source);

        Blackboard target = new(schema);
        target.Set(speed, 99f); // stale value — restore must reset it to defaults + payload
        await _serializer.RestoreFromJsonAsync(target, stream);

        Assert.Multiple(() =>
        {
            Assert.That(target.Get(score), Is.EqualTo(42));
            Assert.That(target.Get(label), Is.EqualTo("saved"));
            Assert.That(target.Get(speed), Is.EqualTo(1.5f), "Restore is defaults + payload, never stale state.");
        });
    }

    [Test]
    public async Task binary_round_trip_restores_values_and_defaults()
    {
        BlackboardSchema schema = NewSchema();
        BlackboardKey<int> score = schema.Register<int>("score");
        BlackboardKey<string> label = schema.Register<string>("label", "fresh");
        BlackboardKey<bool> flag = schema.Register<bool>("flag");

        Blackboard source = new(schema);
        source.Set(score, -7);
        source.Set(flag, true);

        await using MemoryStream stream = await SaveBinary(_serializer, source);

        Blackboard target = new(schema);
        await _serializer.RestoreFromBinaryAsync(target, stream);

        Assert.Multiple(() =>
        {
            Assert.That(target.Get(score), Is.EqualTo(-7));
            Assert.That(target.Get(label), Is.EqualTo("fresh"));
            Assert.That(target.Get(flag), Is.True);
        });
    }

    public sealed record Loadout(string Weapon, int Ammo);

    [Test]
    public async Task custom_types_round_trip_via_caller_supplied_options()
    {
        BlackboardSchema schema = NewSchema("loadouts");
        BlackboardKey<Loadout?> loadout = schema.Register<Loadout?>("loadout");

        Blackboard source = new(schema);
        source.Set(loadout, new Loadout("bow", 12));

        // JSON handles POCOs out of the box; MessagePack gets a contractless resolver via
        // the options extension point.
        BlackboardSerializer serializer = new(
            binaryOptions: MessagePackSerializerOptions.Standard
                .WithSecurity(MessagePackSecurity.UntrustedData)
                .WithResolver(ContractlessStandardResolver.Instance));

        await using MemoryStream json = await SaveJson(serializer, source);
        Blackboard jsonTarget = new(schema);
        await serializer.RestoreFromJsonAsync(jsonTarget, json);

        await using MemoryStream binary = await SaveBinary(serializer, source);
        Blackboard binaryTarget = new(schema);
        await serializer.RestoreFromBinaryAsync(binaryTarget, binary);

        Assert.Multiple(() =>
        {
            Assert.That(jsonTarget.Get(loadout), Is.EqualTo(new Loadout("bow", 12)));
            Assert.That(binaryTarget.Get(loadout), Is.EqualTo(new Loadout("bow", 12)));
        });
    }

    [Test]
    public async Task generic_key_type_names_are_runtime_stable_and_round_trip()
    {
        // Regression: Type.FullName embeds the core-lib assembly version for constructed
        // generics ("...List`1[[System.Int32, System.Private.CoreLib, Version=8.0.0.0, ...]]"),
        // so a payload saved on one runtime failed Strict verification (or silently dropped
        // values under Skip) after a runtime upgrade.
        BlackboardSchema schema = NewSchema("inventory");
        BlackboardKey<List<string>> items = schema.Register<List<string>>("items", []);
        BlackboardKey<int?> optional = schema.Register<int?>("optional");

        Blackboard source = new(schema);
        source.Set(items, ["sword", "shield"]);
        source.Set(optional, 5);

        await using MemoryStream stream = await SaveJson(_serializer, source);
        string payload = Encoding.UTF8.GetString(stream.ToArray());
        stream.Position = 0;

        Blackboard target = new(schema);
        await _serializer.RestoreFromJsonAsync(target, stream);

        Assert.Multiple(() =>
        {
            Assert.That(payload, Does.Not.Contain("Version="),
                "Payload type names must not embed runtime assembly versions.");
            Assert.That(target.Get(items), Is.EqualTo(new List<string> { "sword", "shield" }));
            Assert.That(target.Get(optional), Is.EqualTo(5));
        });
    }

    [Test]
    public async Task strict_restore_failure_leaves_the_target_board_untouched()
    {
        // Regression: restore used to ResetToDefaults before validating entries, so a Strict
        // mismatch part-way through destroyed the caller's pre-restore state (defaults +
        // partial payload). Restore is now all-or-nothing.
        BlackboardSchema savedSchema = NewSchema();
        BlackboardKey<int> savedScore = savedSchema.Register<int>("score");
        BlackboardKey<int> extra = savedSchema.Register<int>("renamed_key");

        Blackboard source = new(savedSchema);
        source.Set(savedScore, 42);
        source.Set(extra, 7);
        await using MemoryStream jsonStream = await SaveJson(_serializer, source);
        await using MemoryStream binaryStream = await SaveBinary(_serializer, source);

        // The live schema no longer declares "renamed_key" — Strict restore must throw.
        BlackboardSchema liveSchema = NewSchema();
        BlackboardKey<int> liveScore = liveSchema.Register<int>("score");

        Blackboard target = new(liveSchema);
        target.Set(liveScore, 1234); // live mid-run value that a failed restore must not destroy

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _serializer.RestoreFromJsonAsync(target, jsonStream));
        Assert.That(target.Get(liveScore), Is.EqualTo(1234),
            "A failed JSON restore must leave the target unmodified.");

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _serializer.RestoreFromBinaryAsync(target, binaryStream));
        Assert.That(target.Get(liveScore), Is.EqualTo(1234),
            "A failed binary restore must leave the target unmodified.");
    }

    // ── Mismatch matrix ──────────────────────────────────────────────────

    [Test]
    public async Task unknown_payload_key_throws_strict_and_skips_skip()
    {
        BlackboardSchema saved = NewSchema();
        BlackboardKey<int> kept = saved.Register<int>("kept");
        saved.Register<int>("removed");

        Blackboard source = new(saved);
        source.Set(kept, 5);

        // The live schema no longer has "removed".
        BlackboardSchema live = NewSchema();
        BlackboardKey<int> liveKept = live.Register<int>("kept");

        await using (MemoryStream strictStream = await SaveJson(_serializer, source))
        {
            Blackboard target = new(live);
            Assert.That(() => _serializer.RestoreFromJsonAsync(target, strictStream).AsTask().GetAwaiter().GetResult(),
                Throws.InvalidOperationException.With.Message.Contain("unknown key 'removed'"));
        }

        await using (MemoryStream skipStream = await SaveJson(_serializer, source))
        {
            Blackboard target = new(live);
            await _serializer.RestoreFromJsonAsync(target, skipStream, BlackboardMismatchPolicy.Skip);
            Assert.That(target.Get(liveKept), Is.EqualTo(5), "Known keys still restore under Skip.");
        }
    }

    [Test]
    public async Task changed_value_type_throws_strict_and_leaves_default_skip()
    {
        BlackboardSchema saved = NewSchema();
        BlackboardKey<int> savedScore = saved.Register<int>("score");
        Blackboard source = new(saved);
        source.Set(savedScore, 9);

        BlackboardSchema live = NewSchema();
        BlackboardKey<string> liveScore = live.Register<string>("score", "default");

        await using (MemoryStream strictStream = await SaveJson(_serializer, source))
        {
            Blackboard target = new(live);
            Assert.That(() => _serializer.RestoreFromJsonAsync(target, strictStream).AsTask().GetAwaiter().GetResult(),
                Throws.InvalidOperationException.With.Message.Contain("saved as").And.Message.Contain("score"));
        }

        await using (MemoryStream skipStream = await SaveJson(_serializer, source))
        {
            Blackboard target = new(live);
            await _serializer.RestoreFromJsonAsync(target, skipStream, BlackboardMismatchPolicy.Skip);
            Assert.That(target.Get(liveScore), Is.EqualTo("default"));
        }
    }

    [Test]
    public async Task schema_added_key_lands_on_default_under_both_policies()
    {
        BlackboardSchema saved = NewSchema();
        BlackboardKey<int> old = saved.Register<int>("old");
        Blackboard source = new(saved);
        source.Set(old, 3);

        BlackboardSchema live = NewSchema();
        BlackboardKey<int> liveOld = live.Register<int>("old");
        BlackboardKey<int> added = live.Register<int>("added", 77);

        foreach (BlackboardMismatchPolicy policy in new[]
                     { BlackboardMismatchPolicy.Strict, BlackboardMismatchPolicy.Skip })
        {
            await using MemoryStream stream = await SaveJson(_serializer, source);
            Blackboard target = new(live);
            await _serializer.RestoreFromJsonAsync(target, stream, policy);

            Assert.Multiple(() =>
            {
                Assert.That(target.Get(liveOld), Is.EqualTo(3));
                Assert.That(target.Get(added), Is.EqualTo(77),
                    $"Keys added after the save keep their defaults ({policy}).");
            });
        }
    }

    [Test]
    public async Task schema_name_or_scope_mismatch_throws_strict_and_no_ops_skip()
    {
        BlackboardSchema saved = new("world", BlackboardScope.Global);
        BlackboardKey<int> savedKey = saved.Register<int>("value");
        Blackboard source = new(saved);
        source.Set(savedKey, 1);

        // Same key layout, but Graph-scoped and differently named.
        BlackboardSchema live = new("enemy");
        BlackboardKey<int> liveKey = live.Register<int>("value", 100);

        await using (MemoryStream strictStream = await SaveJson(_serializer, source))
        {
            Blackboard target = new(live);
            Assert.That(() => _serializer.RestoreFromJsonAsync(target, strictStream).AsTask().GetAwaiter().GetResult(),
                Throws.InvalidOperationException);
        }

        await using (MemoryStream skipStream = await SaveJson(_serializer, source))
        {
            Blackboard target = new(live);
            target.Set(liveKey, 55);
            await _serializer.RestoreFromJsonAsync(target, skipStream, BlackboardMismatchPolicy.Skip);
            Assert.That(target.Get(liveKey), Is.EqualTo(55),
                "A header mismatch under Skip must leave the target completely untouched.");
        }
    }

    [Test]
    public async Task skip_restore_that_stages_nothing_leaves_live_values_untouched()
    {
        // The header matches but every payload entry mismatches (value type changed): under
        // Skip the restore stages zero entries and must leave the board untouched — wiping
        // live values to defaults while applying no payload data would be pure data loss,
        // the same conclusion as a header mismatch.
        BlackboardSchema saved = NewSchema();
        BlackboardKey<int> savedScore = saved.Register<int>("score");
        Blackboard source = new(saved);
        source.Set(savedScore, 9);

        BlackboardSchema live = NewSchema();
        BlackboardKey<string> liveScore = live.Register<string>("score", "default");

        await using (MemoryStream jsonStream = await SaveJson(_serializer, source))
        {
            Blackboard target = new(live);
            target.Set(liveScore, "live");
            await _serializer.RestoreFromJsonAsync(target, jsonStream, BlackboardMismatchPolicy.Skip);
            Assert.That(target.Get(liveScore), Is.EqualTo("live"),
                "A JSON Skip restore that applies nothing must leave live values untouched.");
        }

        await using (MemoryStream binaryStream = await SaveBinary(_serializer, source))
        {
            Blackboard target = new(live);
            target.Set(liveScore, "live");
            await _serializer.RestoreFromBinaryAsync(target, binaryStream, BlackboardMismatchPolicy.Skip);
            Assert.That(target.Get(liveScore), Is.EqualTo("live"),
                "A binary Skip restore that applies nothing must leave live values untouched.");
        }
    }

    [Test]
    public async Task entryless_payload_no_ops_under_skip_and_restores_defaults_under_strict()
    {
        // A legitimately entry-less payload (saved from a schema with zero keys). Codified
        // rule: Skip mutates the target only when at least one entry stages, so it no-ops;
        // Strict has nothing to mismatch and keeps the documented "defaults + payload"
        // post-state — all defaults.
        BlackboardSchema saved = NewSchema(); // zero keys registered
        Blackboard source = new(saved);

        BlackboardSchema live = NewSchema();
        BlackboardKey<int> liveKey = live.Register<int>("value", 100);

        await using (MemoryStream jsonSkip = await SaveJson(_serializer, source))
        {
            Blackboard target = new(live);
            target.Set(liveKey, 55);
            await _serializer.RestoreFromJsonAsync(target, jsonSkip, BlackboardMismatchPolicy.Skip);
            Assert.That(target.Get(liveKey), Is.EqualTo(55),
                "Skip: an entry-less payload stages nothing and must not touch the board (JSON).");
        }

        await using (MemoryStream binarySkip = await SaveBinary(_serializer, source))
        {
            Blackboard target = new(live);
            target.Set(liveKey, 55);
            await _serializer.RestoreFromBinaryAsync(target, binarySkip, BlackboardMismatchPolicy.Skip);
            Assert.That(target.Get(liveKey), Is.EqualTo(55),
                "Skip: an entry-less payload stages nothing and must not touch the board (binary).");
        }

        await using (MemoryStream jsonStrict = await SaveJson(_serializer, source))
        {
            Blackboard target = new(live);
            target.Set(liveKey, 55);
            await _serializer.RestoreFromJsonAsync(target, jsonStrict);
            Assert.That(target.Get(liveKey), Is.EqualTo(100),
                "Strict: defaults + payload — an entry-less payload restores all defaults (JSON).");
        }

        await using (MemoryStream binaryStrict = await SaveBinary(_serializer, source))
        {
            Blackboard target = new(live);
            target.Set(liveKey, 55);
            await _serializer.RestoreFromBinaryAsync(target, binaryStrict);
            Assert.That(target.Get(liveKey), Is.EqualTo(100),
                "Strict: defaults + payload — an entry-less payload restores all defaults (binary).");
        }
    }

    [Test]
    public async Task skip_partial_match_still_resets_unmatched_live_keys_to_defaults()
    {
        // With at least one staged entry, Skip keeps the documented reset-then-apply
        // semantics: post-state is defaults + the matching subset, so live values on keys
        // the payload cannot restore land back on their registered defaults.
        BlackboardSchema saved = NewSchema();
        BlackboardKey<int> savedKept = saved.Register<int>("kept");
        saved.Register<int>("removed");

        Blackboard source = new(saved);
        source.Set(savedKept, 5);

        BlackboardSchema live = NewSchema();
        BlackboardKey<int> liveKept = live.Register<int>("kept");
        BlackboardKey<int> liveOther = live.Register<int>("other", 7);

        await using MemoryStream stream = await SaveJson(_serializer, source);
        Blackboard target = new(live);
        target.Set(liveKept, 111);
        target.Set(liveOther, 222);
        await _serializer.RestoreFromJsonAsync(target, stream, BlackboardMismatchPolicy.Skip);

        Assert.Multiple(() =>
        {
            Assert.That(target.Get(liveKept), Is.EqualTo(5), "The matching subset restores.");
            Assert.That(target.Get(liveOther), Is.EqualTo(7),
                "Keys outside the staged subset land on their defaults — reset-then-apply is " +
                "unchanged once at least one entry stages.");
        });
    }

    [Test]
    public void newer_payload_version_is_rejected()
    {
        BlackboardSchema schema = NewSchema();
        schema.Register<int>("x");
        Blackboard target = new(schema);

        const string json = """{"values":[],"schema":"stats","scope":1,"version":99}""";
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));

        Assert.That(() => _serializer.RestoreFromJsonAsync(target, stream).AsTask().GetAwaiter().GetResult(),
            Throws.InvalidOperationException.With.Message.Contain("version 99"));
    }

    // ── Three artifacts: graph + snapshot + boards ────────────────────────

    private static class DurableKeys
    {
        public static readonly BlackboardSchema World = new("world", BlackboardScope.Global);
        public static readonly BlackboardKey<int> TotalSteps = World.Register<int>("totalSteps");

        public static readonly BlackboardSchema Flow = new("flow");
        public static readonly BlackboardKey<int> Steps = Flow.Register<int>("steps");
    }

    private sealed class StepCountingState : AsyncState
    {
        protected override ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            Bb.GetRef(DurableKeys.Steps)++;
            Bb.GetRef(DurableKeys.TotalSteps)++;
            return ResultHelpers.Success;
        }
    }

    private sealed class StepCountingCodec : ILogicTextCodec
    {
        public string Serialize(IAsyncLogic asyncLogic) => "step";

        public IAsyncLogic Deserialize(string data) => new StepCountingState();
    }

    [Test]
    public async Task three_artifact_durability_loop_restores_boards_and_resumes()
    {
        GraphSerializer graphSerializer = new(new StepCountingCodec());

        Graph original = GraphBuilder
            .StartWithAsync(new StepCountingState())
            .ToAsync(new StepCountingState())
            .ToAsync(new StepCountingState())
            .WithSchema(DurableKeys.Flow)
            .WithSchema(DurableKeys.World)
            .Build();

        Blackboard world = new(DurableKeys.World);
        Blackboard flow = new(DurableKeys.Flow);

        AsyncStateMachine running = original.ToAsyncStateMachine()
            .WithBlackboard(world)
            .WithBlackboard(flow);

        Result first = await running.StepAsync();
        Assert.That(first, Is.EqualTo(Result.InProgress));
        StateMachineSnapshot snapshot = running.Suspend();

        // Ship all artifacts: graph, snapshot, and one payload per board.
        await using MemoryStream graphStream = new();
        await graphSerializer.ToJsonAsync(original, graphStream);
        string snapshotJson = JsonSerializer.Serialize(snapshot);
        await using MemoryStream worldStream = await SaveJson(_serializer, world);
        await using MemoryStream flowStream = await SaveJson(_serializer, flow);

        // Rebuild on the "other side".
        graphStream.Position = 0;
        Graph rebuilt = await graphSerializer.FromJsonAsync(graphStream);

        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.Schema, Is.Null,
                "Schema declarations are code — the graph payload does not carry them (GraphDto unchanged).");
            Assert.That(rebuilt.GlobalSchema, Is.Null);
        });

        Blackboard restoredWorld = new(DurableKeys.World);
        Blackboard restoredFlow = new(DurableKeys.Flow);
        await _serializer.RestoreFromJsonAsync(restoredWorld, worldStream);
        await _serializer.RestoreFromJsonAsync(restoredFlow, flowStream);

        StateMachineSnapshot restoredSnapshot = JsonSerializer.Deserialize<StateMachineSnapshot>(snapshotJson)!;

        AsyncStateMachine resumed = rebuilt.ToAsyncStateMachine()
            .WithBlackboard(restoredWorld) // permissive: rebuilt graph has no declarations
            .WithBlackboard(restoredFlow);
        resumed.Resume(restoredSnapshot);

        Result result = Result.InProgress;
        while (result == Result.InProgress)
        {
            result = await resumed.StepAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(restoredFlow.Get(DurableKeys.Steps), Is.EqualTo(3),
                "One step before the suspend plus two after the resume.");
            Assert.That(restoredWorld.Get(DurableKeys.TotalSteps), Is.EqualTo(3));
        });
    }
}
