using System.Text;
using NxGraph.Authoring;
using NxGraph.Blackboards;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization.Tests;

/// <summary>
/// Payload version 6, part C: custom <see cref="ISubGraphProvider"/> containers (including
/// <see cref="AsyncCompositeState"/> subclasses) on the wire via a user container codec. The
/// serializer owns child-graph recursion (SubGraphs enumeration order = wire order = the
/// children order handed to the codec); the codec owns the reconstruction recipe, riding in
/// the node's ordinary logic slot. Container claims are markerless — the claim itself routes
/// the payload.
/// </summary>
[TestFixture]
[Category("serialization")]
public class ContainerCodecSerializationTests
{
    // ── Test doubles ─────────────────────────────────────────────────────

    private interface IKeyed
    {
        string Key { get; }
    }

    private sealed class KeyedState(string key, Func<Result> body) : IAsyncLogic, ILogic, IKeyed
    {
        public string Key => key;
        public Result Execute() => body();
        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => new(body());
    }

    private sealed class TestAgent;

    private sealed class KeyedAgentState(string key) : IAsyncLogic, ILogic, IKeyed, IAgentSettable<TestAgent>
    {
        public string Key => key;
        public TestAgent? Received;
        public void SetAgent(TestAgent agent) => Received = agent;
        public Result Execute() => Result.Success;
        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => ResultHelpers.Success;
    }

    private sealed class KeyedBlackboardWriterState(string key, BlackboardKey<int> bbKey, int value)
        : IAsyncLogic, ILogic, IKeyed, IBlackboardSettable
    {
        private BlackboardContext _bb;
        public string Key => key;
        void IBlackboardSettable.SetBlackboards(in BlackboardContext context) => _bb = context;

        public Result Execute()
        {
            _bb.Set(bbKey, value);
            return Result.Success;
        }

        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => new(Execute());
    }

    private sealed class RegistryCodec : ILogicTextCodec
    {
        private readonly Dictionary<string, IAsyncLogic> _byKey = new();

        public T Register<T>(string key, T logic) where T : IAsyncLogic
        {
            _byKey[key] = logic;
            return logic;
        }

        public KeyedState Log(string key, List<string> log)
            => Register(key, new KeyedState(key, () =>
            {
                log.Add(key);
                return Result.Success;
            }));

        public string Serialize(IAsyncLogic data) => ((IKeyed)data).Key;

        public IAsyncLogic Deserialize(string s) => _byKey.TryGetValue(s, out IAsyncLogic? logic)
            ? logic
            : throw new InvalidOperationException($"Unknown logic key '{s}'.");
    }

    /// <summary>
    /// A user container: runs its child machines sequentially, surfaces their graphs via
    /// <see cref="ISubGraphProvider"/> (enumeration order = region order), and forwards the
    /// blackboard context — the standing user-container contract the rebuilt instance must
    /// keep for agent stamping and blackboard walks to reach its children.
    /// </summary>
    private sealed class SequentialContainer : IAsyncLogic, ISubGraphProvider, IBlackboardSettable
    {
        private readonly AsyncStateMachine[] _children;

        public SequentialContainer(params Graph[] children)
        {
            _children = new AsyncStateMachine[children.Length];
            for (int i = 0; i < children.Length; i++)
            {
                _children[i] = new AsyncStateMachine(children[i]);
            }
        }

        public IEnumerable<Graph> SubGraphs
        {
            get
            {
                foreach (AsyncStateMachine child in _children)
                {
                    yield return child.Graph;
                }
            }
        }

        void IBlackboardSettable.SetBlackboards(in BlackboardContext context)
        {
            foreach (AsyncStateMachine child in _children)
            {
                ((IBlackboardSettable)child).SetBlackboards(in context);
            }
        }

        public async ValueTask<Result> ExecuteAsync(CancellationToken ct = default)
        {
            foreach (AsyncStateMachine child in _children)
            {
                Result result = await child.ExecuteAsync(ct).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    return result;
                }
            }

            return Result.Success;
        }
    }

    /// <summary>A container with no child graphs: it wraps a plain (non-graph) logic instance.</summary>
    private sealed class LeafContainer(IAsyncLogic inner) : IAsyncLogic, ISubGraphProvider
    {
        public IAsyncLogic Inner => inner;
        public IEnumerable<Graph> SubGraphs => [];
        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => inner.ExecuteAsync(ct);
    }

    /// <summary>
    /// An <see cref="AsyncCompositeState"/> subclass. The protected <c>Child</c> accessor is
    /// how a user codec reads back what the primary constructor captured.
    /// </summary>
    private sealed class WrapperComposite(IAsyncLogic child) : AsyncCompositeState(child)
    {
        public IAsyncLogic ChildForCodec => Child;
    }

    /// <summary>
    /// Test container codec: the payload is a small "<c>shape</c>" recipe string; non-graph
    /// children reuse the logic codec's keys, graph children are rebuilt by the serializer
    /// and handed over in wire order.
    /// </summary>
    private sealed class TestContainerCodec(RegistryCodec logicCodec) : IContainerTextCodec
    {
        public string Serialize(ISubGraphProvider container) => container switch
        {
            SequentialContainer => "seq",
            LeafContainer leaf => "leaf:" + logicCodec.Serialize(leaf.Inner),
            WrapperComposite => "wrapper",
            _ => throw new NotSupportedException($"Unknown container type '{container.GetType().Name}'.")
        };

        public IAsyncLogic Deserialize(string payload, IReadOnlyList<Graph> children)
        {
            if (payload == "seq")
            {
                return new SequentialContainer(children.ToArray());
            }

            if (payload.StartsWith("leaf:", StringComparison.Ordinal))
            {
                return new LeafContainer(logicCodec.Deserialize(payload["leaf:".Length..]));
            }

            if (payload == "wrapper")
            {
                return new WrapperComposite(new AsyncStateMachine(children[0]));
            }

            throw new InvalidOperationException($"Unknown container payload '{payload}'.");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static async Task<Graph> RoundTrip(GraphSerializer serializer, Graph graph, bool binary)
    {
        await using MemoryStream stream = new();
        if (binary)
        {
            await serializer.ToBinaryAsync(graph, stream);
            stream.Position = 0;
            return await serializer.FromBinaryAsync(stream);
        }

        await serializer.ToJsonAsync(graph, stream);
        stream.Position = 0;
        return await serializer.FromJsonAsync(stream);
    }

    private static async Task<Graph> FromJson(GraphSerializer serializer, string json)
    {
        using MemoryStream source = new(Encoding.UTF8.GetBytes(json));
        return await serializer.FromJsonAsync(source);
    }

    private static GraphSerializer SerializerWith(RegistryCodec codec)
        => new(codec, new GraphSerializerOptions { ContainerCodec = new TestContainerCodec(codec) });

    // ── Round-trips ──────────────────────────────────────────────────────

    [Test]
    public async Task Custom_container_with_two_children_roundtrips_with_order_pinned([Values] bool binary)
    {
        List<string> log = [];
        RegistryCodec codec = new();

        Graph c0 = GraphBuilder.StartWithAsync(codec.Log("c0", log)).Build();
        Graph c1 = GraphBuilder.StartWithAsync(codec.Log("c1", log)).Build();
        Graph parent = GraphBuilder
            .StartWithAsync(codec.Log("p0", log))
            .ToAsync(new SequentialContainer(c0, c1))
            .Build();

        Graph rebuilt = await RoundTrip(SerializerWith(codec), parent, binary);
        LogicNode containerNode = (LogicNode)rebuilt.GetNodeByIndex(1);
        Result result = await rebuilt.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(containerNode.AsyncLogic, Is.InstanceOf<SequentialContainer>(),
                "The container claim routed the payload to the container codec.");
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "p0", "c0", "c1" }),
                "SubGraphs enumeration order = wire order = Deserialize children order.");
        });
    }

    [Test]
    public async Task Zero_subgraph_container_with_a_non_graph_child_roundtrips([Values] bool binary)
    {
        // The container has no child graphs at all; its payload encodes the non-graph child
        // via the logic codec's key ("leaf:inner").
        List<string> log = [];
        RegistryCodec codec = new();
        KeyedState inner = codec.Log("inner", log);

        Graph parent = GraphBuilder
            .StartWithAsync(new LeafContainer(inner))
            .Build();

        Graph rebuilt = await RoundTrip(SerializerWith(codec), parent, binary);
        LogicNode node = (LogicNode)rebuilt.StartNode;
        Result result = await rebuilt.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(node.AsyncLogic, Is.InstanceOf<LeafContainer>());
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "inner" }));
        });
    }

    [Test]
    public async Task AsyncCompositeState_subclass_roundtrips_through_a_user_codec_reading_Child(
        [Values] bool binary)
    {
        List<string> log = [];
        RegistryCodec codec = new();
        Graph child = GraphBuilder.StartWithAsync(codec.Log("c0", log)).Build();

        Graph parent = GraphBuilder
            .StartWithAsync(new WrapperComposite(new AsyncStateMachine(child)))
            .Build();

        Graph rebuilt = await RoundTrip(SerializerWith(codec), parent, binary);
        LogicNode node = (LogicNode)rebuilt.StartNode;
        Result result = await rebuilt.ToAsyncStateMachine().ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(node.AsyncLogic, Is.InstanceOf<WrapperComposite>());
            Assert.That(((WrapperComposite)node.AsyncLogic).ChildForCodec, Is.InstanceOf<AsyncStateMachine>(),
                "The subclass read the protected Child accessor to rebuild the wrapped machine.");
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(log, Is.EqualTo(new[] { "c0" }));
        });
    }

    // ── Agent and blackboard walks on rebuilt containers ─────────────────

    [Test]
    public async Task Agent_stamping_reaches_nodes_inside_rebuilt_container_children()
    {
        RegistryCodec codec = new();
        KeyedAgentState agentState = codec.Register("agent", new KeyedAgentState("agent"));

        Graph child = GraphBuilder.StartWithAsync(agentState).Build();
        Graph parent = GraphBuilder.StartWithAsync(new SequentialContainer(child)).Build();

        Graph rebuilt = await RoundTrip(SerializerWith(codec), parent, binary: false);

        TestAgent agent = new();
        rebuilt.SetAgent(agent);

        Assert.That(agentState.Received, Is.SameAs(agent),
            "The rebuilt container surfaces its new child graphs via SubGraphs, so the agent walk reaches them.");
    }

    [Test]
    public async Task Blackboard_forwarding_reaches_rebuilt_container_children()
    {
        BlackboardSchema schema = new("test");
        BlackboardKey<int> key = schema.Register<int>("value");

        RegistryCodec codec = new();
        Graph child = GraphBuilder
            .StartWithAsync(codec.Register("writer", new KeyedBlackboardWriterState("writer", key, 42)))
            .Build();
        Graph parent = GraphBuilder.StartWithAsync(new SequentialContainer(child)).Build();

        Graph rebuilt = await RoundTrip(SerializerWith(codec), parent, binary: false);

        Blackboard board = new(schema);
        Result result = await rebuilt.ToAsyncStateMachine().WithBlackboard(board).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(Result.Success));
            Assert.That(board.Get(key), Is.EqualTo(42),
                "The rebuilt container keeps IBlackboardSettable forwarding into its children.");
        });
    }

    // ── Setup and spoof failures ─────────────────────────────────────────

    private sealed class BinaryContainerCodec : IContainerBinaryCodec
    {
        public ReadOnlyMemory<byte> Serialize(ISubGraphProvider container) => ReadOnlyMemory<byte>.Empty;
        public IAsyncLogic Deserialize(ReadOnlyMemory<byte> payload, IReadOnlyList<Graph> children)
            => throw new NotSupportedException();
    }

    [Test]
    public void Wire_type_mismatch_between_logic_and_container_codec_fails_at_construction()
    {
        RegistryCodec textLogicCodec = new();
        Assert.Throws<ArgumentException>(() => _ = new GraphSerializer(textLogicCodec,
                new GraphSerializerOptions { ContainerCodec = new BinaryContainerCodec() }),
            "A text logic codec cannot pair with a binary container codec — same node DTO slot.");
    }

    [Test]
    public void Container_claim_with_no_codec_configured_throws_the_targeted_error()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "seq" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [], "composites": [],
              "containers": [ { "ownerIndex": 0, "children": [] } ],
              "name": null, "index": -1
            }
            """;

        RegistryCodec codec = new();
        GraphSerializer serializer = new(codec);
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(serializer, json));
        Assert.That(ex!.Message, Does.Contain("GraphSerializerOptions.ContainerCodec"));
    }

    [Test]
    public void A_claim_added_onto_an_ordinary_node_routes_its_payload_to_the_container_codec_which_rejects_it()
    {
        // Spoof direction 1: a crafted claim on an ordinary node routes the ordinary payload
        // to the container codec, which fails loud on the unknown recipe.
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "a" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [], "composites": [],
              "containers": [ { "ownerIndex": 0, "children": [] } ],
              "name": null, "index": -1
            }
            """;

        RegistryCodec codec = new();
        codec.Register("a", new KeyedState("a", () => Result.Success));
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(SerializerWith(codec), json));
        Assert.That(ex!.Message, Does.Contain("Unknown container payload 'a'"));
    }

    [Test]
    public void A_deleted_claim_routes_the_container_payload_to_the_logic_codec_which_rejects_it()
    {
        // Spoof direction 2: with the claim removed, the container recipe string is ordinary
        // payload for the logic codec, which fails loud on the unknown key.
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "seq" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [], "composites": [],
              "containers": [],
              "name": null, "index": -1
            }
            """;

        RegistryCodec codec = new();
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(SerializerWith(codec), json));
        Assert.That(ex!.Message, Does.Contain("Unknown logic key 'seq'"));
    }

    [Test]
    public void Out_of_range_container_owner_throws()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "a" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [], "composites": [],
              "containers": [ { "ownerIndex": 5, "children": [] } ],
              "name": null, "index": -1
            }
            """;

        RegistryCodec codec = new();
        codec.Register("a", new KeyedState("a", () => Result.Success));
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(SerializerWith(codec), json));
        Assert.That(ex!.Message, Does.Contain("out of range"));
    }

    [Test]
    public void Null_from_the_container_codec_is_guarded()
    {
        string json = $$"""
            {
              "version": {{SerializationVersion.Version}},
              "nodes": [ { "$type": "txt", "index": 0, "name": "a", "logic": "null" } ],
              "transitions": [ { "destination": -1 } ],
              "subGraphs": [], "composites": [],
              "containers": [ { "ownerIndex": 0, "children": [] } ],
              "name": null, "index": -1
            }
            """;

        RegistryCodec codec = new();
        GraphSerializer serializer = new(codec,
            new GraphSerializerOptions { ContainerCodec = new NullReturningContainerCodec() });
        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FromJson(serializer, json));
        Assert.That(ex!.Message, Does.Contain("returned null"));
    }

    private sealed class NullReturningContainerCodec : IContainerTextCodec
    {
        public string Serialize(ISubGraphProvider container) => "null";
        public IAsyncLogic Deserialize(string payload, IReadOnlyList<Graph> children) => null!;
    }

#if DEBUG
    /// <summary>
    /// Deliberately violates the SubGraphs ordering contract: every enumeration alternates
    /// between forward and reversed child order, so any two consecutive enumerations differ
    /// (an unordered backing collection would misbehave the same way). Alternation — rather
    /// than "reverse from the second enumeration on" — keeps the violation visible no matter
    /// how many times validation walked SubGraphs before the serializer does.
    /// </summary>
    private sealed class UnstableOrderContainer(params Graph[] children) : IAsyncLogic, ISubGraphProvider
    {
        private int _enumerations;

        public IEnumerable<Graph> SubGraphs
        {
            get
            {
                bool reverse = _enumerations++ % 2 == 1;
                for (int i = 0; i < children.Length; i++)
                {
                    yield return children[reverse ? children.Length - 1 - i : i];
                }
            }
        }

        public ValueTask<Result> ExecuteAsync(CancellationToken ct = default) => ResultHelpers.Success;
    }

    [Test]
    public void Unstable_subgraph_enumeration_order_is_caught_by_the_debug_check()
    {
        // DEBUG builds double-enumerate SubGraphs in the serializer's container path and fail
        // loud on an unstable sequence — wire order is reconstruction identity, so an
        // unordered backing collection would otherwise corrupt the rebuilt children silently.
        // (Release builds compile the check out; this test only exists in DEBUG.)
        RegistryCodec codec = new();
        Graph c0 = GraphBuilder
            .StartWithAsync(codec.Register("c0", new KeyedState("c0", () => Result.Success)))
            .Build();
        Graph c1 = GraphBuilder
            .StartWithAsync(codec.Register("c1", new KeyedState("c1", () => Result.Success)))
            .Build();
        Graph parent = GraphBuilder
            .StartWithAsync(new UnstableOrderContainer(c0, c1))
            .Build();

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await SerializerWith(codec).ToJsonAsync(parent, new MemoryStream()));
        Assert.That(ex!.Message, Does.Contain("stable, deterministic order"));
    }
#endif
}
