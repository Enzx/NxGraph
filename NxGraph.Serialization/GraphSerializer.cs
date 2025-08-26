using System.Text;
using MessagePack;
using MessagePack.Resolvers;
using NxGraph.Graphs;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

public abstract class GraphSerializer : IGraphJsonSerializer, IGraphBinarySerializer
{
    private static ILogicCodec _codec;
    private static readonly MessagePackSerializerOptions Options;

    static GraphSerializer()
    {
        IFormatterResolver resolver = CompositeResolver.Create(
            formatters: [],
            resolvers:
            [
                GraphFormatterResolver.Instance, StandardResolver.Instance
            ]
        );
        _codec = new NullLogicCodec();
        Options = MessagePackSerializerOptions.Standard.WithResolver(resolver);
    }

    public static void SetLogicCodec<TWire>(ILogicCodec<TWire> codec)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
    }

    public static async ValueTask ToJsonAsync(Graph graph, Stream destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(destination);

        GraphDto dto = ToDto(graph);

        await using StreamWriter writer = new(destination, leaveOpen: true);

        string json = MessagePackSerializer.SerializeToJson(dto, cancellationToken: ct, options: Options);
        await writer.WriteAsync(json.AsMemory(), ct).ConfigureAwait(false);
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async ValueTask<Graph> FromJsonAsync(Stream source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        using StreamReader reader = new(source, Encoding.UTF8, leaveOpen: true);
        string json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        // Convert JSON -> MessagePack bytes -> deserialize with resolvers
        byte[] mp = MessagePackSerializer.ConvertFromJson(json, options: Options, cancellationToken: ct);
        GraphDto dto = MessagePackSerializer.Deserialize<GraphDto>(mp, options: Options, cancellationToken: ct);

        return FromDto(dto);
    }

    public static async ValueTask ToBinaryAsync(Graph graph, Stream destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(destination);

        GraphDto dto = ToDto(graph);

        await MessagePackSerializer.SerializeAsync(destination, dto, Options, ct);

        await destination.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async ValueTask<Graph> FromBinaryAsync(Stream source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        GraphDto dto = await MessagePackSerializer.DeserializeAsync<GraphDto>(source, Options, ct);
        return FromDto(dto);
    }

    private static GraphDto ToDto(Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (_codec is NullLogicCodec)
        {
            throw new InvalidOperationException("No ILogicCodec configured in GraphSerializer.");
        }

        int n = graph.NodeCount;

        INodeDto[] nodes = new INodeDto[n];
        for (int i = 0; i < n; i++)
        {
            Node node = graph.GetNodeByIndex(i);

            nodes[i] = _codec switch
            {
                ILogicCodec<string> t => new NodeTextDto(node.Id.Name, t.Serialize(node.Logic)),
                ILogicCodec<ReadOnlyMemory<byte>> b => new NodeBinaryDto(node.Id.Name,
                    b.Serialize(node.Logic)),
                _ => throw new InvalidOperationException("No ILogicCodec configured in GraphSerializer.")
            };
        }

        TransitionDto[] transitions = new TransitionDto[n];
        for (int i = 0; i < n; i++)
        {
            Transition tr = graph.GetTransitionByIndex(i);
            int dest = tr.IsEmpty ? -1 : tr.Destination.Index;
            transitions[i] = new TransitionDto(dest);
        }

        return new GraphDto(nodes, transitions, graph.Id.Name);
    }


    private static Graph FromDto(GraphDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        int n = dto.Nodes.Length;
        if (n == 0) throw new InvalidOperationException("GraphDto must contain at least one node.");

        Node[] nodes = new Node[n];
        for (int i = 0; i < n; i++)
        {
            switch (dto.Nodes[i])
            {
                case NodeTextDto t:
                    ILogicCodec<string> codec = (ILogicCodec<string>)_codec;
                    nodes[i] = new Node(i == 0 ? NodeId.Start : new NodeId(i, t.Name),
                        codec.Deserialize(t.Logic));
                    break;

                case NodeBinaryDto b:
                    ILogicCodec<ReadOnlyMemory<byte>> binCodec = (ILogicCodec<ReadOnlyMemory<byte>>)_codec;
                    nodes[i] = new Node(i == 0 ? NodeId.Start : new NodeId(i, b.Name),
                        binCodec.Deserialize(b.Logic));
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported node type: {dto.Nodes[i].GetType().Name}");
            }
        }

        Transition[] edges = new Transition[n]; // default = Empty
        TransitionDto[] trs = dto.Transitions;
        int m = Math.Min(n, trs.Length);

        for (int i = 0; i < m; i++)
        {
            int dest = trs[i].Destination; // -1 means "no edge"
            edges[i] = (dest >= 0 && dest < n) ? new Transition(nodes[dest].Id) : Transition.Empty;
        }

        NodeId graphId = new(0, dto.Name ?? string.Empty);
        return new Graph(graphId, nodes, edges);
    }
}

public static class GraphSerializerExtensions
{
    public static IGraphJsonSerializer AsJsonSerializer(this GraphSerializer serializer)
    {
        return serializer;
    }

    public static IGraphBinarySerializer AsBinarySerializer(this GraphSerializer serializer)
    {
        return serializer;
    }
}