using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MessagePack;
using MessagePack.Resolvers;
using NxGraph.Fsm;
using NxGraph.Graphs;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

public sealed class GraphSerializer : IGraphJsonSerializer, IGraphBinarySerializer
{
    private readonly ILogicCodec _codec;
    private readonly MessagePackSerializerOptions _options;

    private readonly JsonSerializerOptions _jsonOptions;

    public GraphSerializer(ILogicCodec codec)
    {
        IFormatterResolver resolver = CompositeResolver.Create(
            formatters: [],
            resolvers:
            [
                GraphFormatterResolver.Instance, StandardResolver.Instance
            ]
        );
        _codec = codec;
        _options = MessagePackSerializerOptions.Standard.WithResolver(resolver);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        DefaultJsonTypeInfoResolver jsonTypeInfo = new()
        {
            Modifiers =
            {
                ti =>
                {
                    if (ti.Type != typeof(INodeDto))
                    {
                        return;
                    }

                    ti.PolymorphismOptions = new JsonPolymorphismOptions
                    {
                        TypeDiscriminatorPropertyName = "$type"
                    };
                    ti.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(typeof(NodeBinaryDto), "bin"));
                    ti.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(typeof(NodeTextDto), "txt"));
                }
            }
        };
        _jsonOptions.TypeInfoResolver = JsonTypeInfoResolver.Combine(
            _jsonOptions.TypeInfoResolver,
            jsonTypeInfo
        );
    }

    public async ValueTask ToJsonAsync(Graph graph, Stream destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(destination);

        GraphDto dto = ToDto(graph);

        await using StreamWriter writer = new(destination, new UTF8Encoding(false), leaveOpen: true);

        string json = JsonSerializer.Serialize(dto, _jsonOptions);
        await writer.WriteAsync(json.AsMemory(), ct).ConfigureAwait(false);
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<Graph> FromJsonAsync(Stream source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        using StreamReader reader = new(source,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);
        string json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        GraphDto dto = JsonSerializer.Deserialize<GraphDto>(json, _jsonOptions) ??
                       throw new InvalidOperationException("Failed to parse Graph JSON.");

        return FromDto(dto);
    }

    public async ValueTask ToBinaryAsync(Graph graph, Stream destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(destination);

        GraphDto dto = ToDto(graph);

        await MessagePackSerializer.SerializeAsync(destination, dto, _options, ct);

        await destination.FlushAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<Graph> FromBinaryAsync(Stream source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        GraphDto dto = await MessagePackSerializer.DeserializeAsync<GraphDto>(source, _options, ct);
        return FromDto(dto);
    }

    private GraphDto ToDto(Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (_codec is NullLogicCodec)
        {
            throw new InvalidOperationException("No ILogicCodec configured in GraphSerializer.");
        }

        int transitionCount = graph.TransitionCount;
        TransitionDto[] transitions = new TransitionDto[transitionCount];
        for (int index = 0; index < transitionCount; index++)
        {
            Transition tr = graph.GetTransitionByIndex(index);
            int dest = tr.IsEmpty ? -1 : tr.Destination.Index;
            transitions[index] = new TransitionDto(dest);
        }

        int nodeCount = graph.NodeCount;
        List<SubGraphDto> subGraphs = [];
        INodeDto[] nodes = new INodeDto[nodeCount];
        for (int index = 0; index < nodeCount; index++)
        {
            INode node = graph.GetNodeByIndex(index);
            switch (node)
            {
                case LogicNode logicNode:
                    if (logicNode.Logic is StateMachine stateMachine)
                    {
                        // Serialize state machine as sub-graph
                        GraphDto childDto = ToDto(stateMachine.Graph);
                        subGraphs.Add(new SubGraphDto(index, childDto));
                        nodes[index] = new NodeTextDto(index, node.Id.Name, LogicNode.StateMachineMarker.Id.Name);
                        break;
                    }

                    nodes[index] = _codec switch
                    {
                        ILogicCodec<string> t => new NodeTextDto(index, node.Id.Name, t.Serialize(logicNode.Logic)),
                        ILogicCodec<ReadOnlyMemory<byte>> b => new NodeBinaryDto(index, node.Id.Name,
                            b.Serialize(logicNode.Logic)),
                        _ => throw new InvalidOperationException("No ILogicCodec configured in GraphSerializer.")
                    };
                    break;
                case Graph childGraph:
                {
                    GraphDto childDto = ToDto(childGraph);
                    subGraphs.Add(new SubGraphDto(index, childDto));
                    break;
                }
            }
        }

        return new GraphDto(nodes, transitions, subGraphs.ToArray(), graph.Id.Index, graph.Id.Name);
    }

    private Graph FromDto(GraphDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        int nodesLength = dto.Nodes.Length;
        if (nodesLength == 0) throw new InvalidOperationException("GraphDto must contain at least one node.");

        var binaryCodec = _codec as ILogicCodec<ReadOnlyMemory<byte>>;
        var textCodec = _codec as ILogicCodec<string>;

        INode[] nodes = new INode[nodesLength];
        for (int index = 0; index < nodesLength; index++)
        {
            INodeDto nodeDto = dto.Nodes[index];
            if (nodeDto.Index < 0 || nodeDto.Index >= nodesLength)
                throw new InvalidOperationException(
                    $"Node DTO index {nodeDto.Index} is out of range (0..{nodesLength - 1}).");

            switch (nodeDto)
            {
                case NodeTextDto textDto:
                    if (textCodec is null)
                        throw new InvalidOperationException(
                            "GraphSerializer has no ILogicCodec<string> configured, cannot decode text nodes.");

                    if (textDto.Logic == LogicNode.StateMachineMarker.Id.Name)
                    {
                        // Keep a marker; we'll replace it after we rebuild subgraphs.
                        nodes[nodeDto.Index] = LogicNode.StateMachineMarker;
                        break;
                    }

                {
                    ILogic logic = textCodec.Deserialize(textDto.Logic);
                    nodes[nodeDto.Index] = new LogicNode(new NodeId(index, textDto.Name), logic);
                }
                    break;

                case NodeBinaryDto binaryDto:
                    if (binaryCodec is null)
                        throw new InvalidOperationException(
                            "GraphSerializer has no ILogicCodec<ReadOnlyMemory<byte>> configured, cannot decode binary nodes.");
                {
                    ILogic binLogic = binaryCodec.Deserialize(binaryDto.Logic);
                    nodes[nodeDto.Index] = new LogicNode(new NodeId(index, binaryDto.Name), binLogic);
                }
                    break;

                default:
                    throw new InvalidOperationException($"Unknown node DTO type: {nodeDto.GetType().FullName}");
            }
        }

        Dictionary<int, Graph> ownerToSubGraph = new();
        int subgraphCount = dto.SubGraphs.Length;
        for (int i = 0; i < subgraphCount; i++)
        {
            SubGraphDto subDto = dto.SubGraphs[i];

            if (subDto.OwnerIndex < 0 || subDto.OwnerIndex >= nodesLength)
                throw new InvalidOperationException(
                    $"Subgraph DTO owner index {subDto.OwnerIndex} is out of range (0..{nodesLength - 1}).");

            Graph childGraph = FromDto(subDto.Graph);
            if (nodes[subDto.OwnerIndex] == LogicNode.StateMachineMarker)
            {
                ownerToSubGraph[subDto.OwnerIndex] = childGraph;
            }
            else
            {
                nodes[subDto.OwnerIndex] = childGraph;
            }
        }

        for (int i = 0; i < nodesLength; i++)
        {
            if (nodes[i] is not LogicNode marker) continue;
            if (marker != LogicNode.StateMachineMarker) continue;

            if (!ownerToSubGraph.TryGetValue(i, out Graph? childGraph))
                throw new InvalidOperationException(
                    $"Node at index {i} was marked as a StateMachine but has no associated subgraph.");

            string nodeName = dto.Nodes[i] switch
            {
                NodeTextDto nt => nt.Name,
                NodeBinaryDto nb => nb.Name,
                _ => $"Node_{i}"
            };

            ILogic stateMachineLogic = new StateMachine(childGraph);
            nodes[i] = new LogicNode(new NodeId(i, nodeName), stateMachineLogic);
        }

        int transitionsLength = dto.Transitions.Length;
        Transition[] transitions = new Transition[transitionsLength];
        for (int i = 0; i < transitionsLength; i++)
        {
            TransitionDto trDto = dto.Transitions[i];
            if (trDto.Destination < -1 || trDto.Destination >= nodesLength)
                throw new InvalidOperationException(
                    $"Transition DTO destination index {trDto.Destination} is out of range (-1..{nodesLength - 1}).");

            transitions[i] = trDto.Destination == Transition.Empty.Destination.Index
                ? Transition.Empty
                : new Transition(nodes[trDto.Destination].Id);
        }

        NodeId graphId = new(dto.Index, dto.Name);
        return new Graph(graphId, nodes, transitions);
    }


    // ReSharper disable once UnusedMember.Global
    /// <summary>
    ///  Gets this instance as a JSON serializer.
    /// </summary>
    /// <returns></returns>
    public IGraphJsonSerializer AsJsonSerializer()
    {
        return this;
    }
    
    // ReSharper disable once UnusedMember.Global
    /// <summary>
    ///  Gets this instance as a binary serializer.
    /// </summary>
    /// <returns></returns>
    public IGraphBinarySerializer AsBinarySerializer()
    {
        return this;
    }
}