using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MessagePack;
using MessagePack.Resolvers;
using NxGraph.Fsm;
using NxGraph.Fsm.Async;
using NxGraph.Graphs;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

public sealed class GraphSerializer : IGraphJsonSerializer, IGraphBinarySerializer
{
    // Limits subgraph recursion in ToDto/FromDto. A deeper nesting in a payload almost
    // certainly indicates a malicious or corrupt input rather than a real graph; without
    // the cap an attacker can stack-overflow the deserializer with a deeply nested payload.
    internal const int MaxSubGraphDepth = 64;

    // Pre-fix payloads wrote the nested-machine marker as "Default" (LogicNode.StateMachineMarker
    // was constructed with NodeId.Default). Still accepted on read for back compatibility.
    private const string LegacyStateMachineMarkerName = "Default";

    private readonly ILogicCodec _codec;
    private readonly MessagePackSerializerOptions _options;

    private readonly JsonSerializerOptions _jsonOptions;

    public GraphSerializer(ILogicCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        IFormatterResolver resolver = CompositeResolver.Create(
            formatters: [],
            resolvers:
            [
                GraphFormatterResolver.Instance, StandardResolver.Instance
            ]
        );
        _codec = codec;
        // UntrustedData enables MessagePack-CSharp's hardening against pathological inputs
        // (collection size caps, hash-collision DoS guards, etc.). Required because graph
        // payloads can come from disk or the network — both are untrusted boundaries.
        _options = MessagePackSerializerOptions.Standard
            .WithSecurity(MessagePackSecurity.UntrustedData)
            .WithResolver(resolver);
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

        // Match the version-check behaviour of GraphDtoFormatter (MessagePack path). Future
        // versions are rejected; matching versions pass through. Lower versions are accepted
        // for forward compatibility (older payloads remain readable when the format adds
        // optional fields), so only the strict-greater check fires here.
        if (dto.Version > SerializationVersion.Version)
        {
            throw new InvalidOperationException(
                $"GraphDto: JSON payload version {dto.Version} is newer than serializer version {SerializationVersion.Version}.");
        }

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

    private GraphDto ToDto(Graph graph) => ToDto(graph, depth: 0);

    private GraphDto ToDto(Graph graph, int depth)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (depth > MaxSubGraphDepth)
        {
            throw new InvalidOperationException(
                $"Subgraph nesting exceeded MaxSubGraphDepth ({MaxSubGraphDepth}) while serializing.");
        }
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
            int failureDest = tr.HasFailureDestination ? tr.FailureDestination.Index : -1;
            transitions[index] = new TransitionDto(dest, failureDest);
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
                    if (logicNode.AsyncLogic is AsyncStateMachine stateMachine)
                    {
                        // Serialize state machine as sub-graph
                        GraphDto childDto = ToDto(stateMachine.Graph, depth + 1);
                        subGraphs.Add(new SubGraphDto(index, childDto));
                        nodes[index] = new NodeTextDto(index, node.Id.Name, LogicNode.StateMachineMarker.Id.Name);
                        break;
                    }

                    // Sync twin: a plain nested sync StateMachine round-trips with its own
                    // marker so the deserialized graph stays sync-runnable. Machine-level
                    // config (step mode, restart policy) is not structure and does not ride.
                    if (logicNode.Logic is StateMachine syncStateMachine)
                    {
                        GraphDto childDto = ToDto(syncStateMachine.Graph, depth + 1);
                        subGraphs.Add(new SubGraphDto(index, childDto));
                        nodes[index] = new NodeTextDto(index, node.Id.Name,
                            LogicNode.SyncStateMachineMarker.Id.Name);
                        break;
                    }

                    // Other composites (history states, parallel states, custom containers)
                    // carry child graphs a user codec structurally cannot serialize — only
                    // GraphSerializer can recurse into them, and it doesn't yet. Fail with a
                    // targeted error instead of handing the composite to the codec, which
                    // would either throw an opaque cast error or silently drop the children.
                    if (logicNode.AsyncLogic is ISubGraphProvider || logicNode.Logic is ISubGraphProvider)
                    {
                        // Name the actual composite: sync composites sit behind a
                        // SyncLogicAdapter, whose type name would only obscure the error.
                        string compositeName = logicNode.Logic is ISubGraphProvider
                            ? logicNode.Logic.GetType().Name
                            : logicNode.AsyncLogic.GetType().Name;
                        throw new NotSupportedException(
                            $"Node '{node.Id}' holds composite logic '{compositeName}' " +
                            "which is not serializable. Only plain nested state machines " +
                            "(.SubGraph(...) without history, async or sync) are supported by GraphSerializer.");
                    }

                    // Unwrap SyncLogicAdapter so the codec receives the actual IAsyncLogic/ILogic,
                    // not the adapter wrapper. If the inner ILogic also implements IAsyncLogic, use it.
                    IAsyncLogic logicForCodec = logicNode.AsyncLogic;
                    if (logicForCodec is SyncLogicAdapter sla && sla.Logic is IAsyncLogic innerAsync)
                    {
                        logicForCodec = innerAsync;
                    }

                    nodes[index] = _codec switch
                    {
                        ILogicCodec<string> t => new NodeTextDto(index, node.Id.Name, t.Serialize(logicForCodec)),
                        ILogicCodec<ReadOnlyMemory<byte>> b => new NodeBinaryDto(index, node.Id.Name,
                            b.Serialize(logicForCodec)),
                        _ => throw new InvalidOperationException("No ILogicCodec configured in GraphSerializer.")
                    };
                    break;
                default:
                    // Raw Graph-as-node never comes out of GraphBuilder (it always wraps in
                    // LogicNode); the old branch here emitted a SubGraphDto while leaving a
                    // null slot in Nodes[], producing an unreadable payload.
                    throw new NotSupportedException(
                        $"Node at index {index} of type '{node.GetType().Name}' is not serializable.");
            }
        }

        RetryPolicyDto[]? retryPolicies = null;
        if (graph.RetryPolicies is { } policies)
        {
            List<RetryPolicyDto> entries = [];
            for (int index = 0; index < policies.Length; index++)
            {
                RetryPolicy policy = policies[index];
                if (policy.MaxAttempts == 0)
                {
                    continue;
                }

                entries.Add(new RetryPolicyDto(index, policy.MaxAttempts, policy.Backoff.Ticks,
                    (byte)policy.BackoffKind));
            }

            retryPolicies = entries.ToArray();
        }

        OutcomeCodeDto[]? outcomeCodes = null;
        if (graph.OutcomeCodes is { } codes)
        {
            List<OutcomeCodeDto> entries = [];
            for (int index = 0; index < codes.Length; index++)
            {
                if (codes[index] != 0)
                {
                    entries.Add(new OutcomeCodeDto(index, codes[index]));
                }
            }

            outcomeCodes = entries.ToArray();
        }

        OutcomeNameDto[]? outcomeNames = null;
        if (graph.OutcomeNames is { Count: > 0 } names)
        {
            List<OutcomeNameDto> entries = [];
            foreach ((int code, string outcomeName) in names)
            {
                entries.Add(new OutcomeNameDto(code, outcomeName));
            }

            outcomeNames = entries.ToArray();
        }

        return new GraphDto(nodes, transitions, subGraphs.ToArray(), graph.Id.Index, graph.Id.Name, retryPolicies,
            outcomeCodes, outcomeNames);
    }

    private Graph FromDto(GraphDto dto) => FromDto(dto, depth: 0);

    private Graph FromDto(GraphDto dto, int depth)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (depth > MaxSubGraphDepth)
        {
            throw new InvalidOperationException(
                $"Subgraph nesting exceeded MaxSubGraphDepth ({MaxSubGraphDepth}) while deserializing.");
        }

        // Version-gate every nesting level (the JSON entry point only checks the root DTO;
        // the MessagePack formatter checks per-graph — this makes both formats consistent).
        if (dto.Version > SerializationVersion.Version)
        {
            throw new InvalidOperationException(
                $"GraphDto: payload version {dto.Version} is newer than serializer version {SerializationVersion.Version}.");
        }

        int nodesLength = dto.Nodes.Length;
        if (nodesLength == 0) throw new InvalidOperationException("GraphDto must contain at least one node.");

        var binaryCodec = _codec as ILogicCodec<ReadOnlyMemory<byte>>;
        var textCodec = _codec as ILogicCodec<string>;

        // Owner indices of subgraph payloads, resolved up front: a node is only treated as a
        // nested-machine marker when a SubGraphDto actually claims it. This kills the in-band
        // collision where a codec legitimately emits the marker string for ordinary logic.
        HashSet<int>? subGraphOwners = null;
        foreach (SubGraphDto subDto in dto.SubGraphs)
        {
            (subGraphOwners ??= []).Add(subDto.OwnerIndex);
        }

        INode[] nodes = new INode[nodesLength];
        bool[] slotFilled = new bool[nodesLength];
        for (int index = 0; index < nodesLength; index++)
        {
            INodeDto nodeDto = dto.Nodes[index];
            if (nodeDto.Index < 0 || nodeDto.Index >= nodesLength)
                throw new InvalidOperationException(
                    $"Node DTO index {nodeDto.Index} is out of range (0..{nodesLength - 1}).");
            if (slotFilled[nodeDto.Index])
                throw new InvalidOperationException(
                    $"Node DTO index {nodeDto.Index} is duplicated in the payload.");

            switch (nodeDto)
            {
                case NodeTextDto textDto:
                    // Marker check runs before the codec guard: the marker needs no codec, so a
                    // binary-codec serializer can read back the subgraph payloads it wrote.
                    // "Default" is the legacy marker string (pre-fix payloads); markers are only
                    // honored when a subgraph payload actually claims this node index.
                    if ((textDto.Logic == LogicNode.StateMachineMarker.Id.Name ||
                         textDto.Logic == LegacyStateMachineMarkerName) &&
                        subGraphOwners is not null && subGraphOwners.Contains(nodeDto.Index))
                    {
                        // Keep a marker; we'll replace it after we rebuild subgraphs.
                        nodes[nodeDto.Index] = LogicNode.StateMachineMarker;
                        break;
                    }

                    if (textDto.Logic == LogicNode.SyncStateMachineMarker.Id.Name &&
                        subGraphOwners is not null && subGraphOwners.Contains(nodeDto.Index))
                    {
                        nodes[nodeDto.Index] = LogicNode.SyncStateMachineMarker;
                        break;
                    }

                    if (textCodec is null)
                        throw new InvalidOperationException(
                            "GraphSerializer has no ILogicCodec<string> configured, cannot decode text nodes.");

                {
                    IAsyncLogic asyncLogic = textCodec.Deserialize(textDto.Logic);
                    nodes[nodeDto.Index] = new LogicNode(new NodeId(nodeDto.Index, textDto.Name), asyncLogic);
                }
                    break;

                case NodeBinaryDto binaryDto:
                    if (binaryCodec is null)
                        throw new InvalidOperationException(
                            "GraphSerializer has no ILogicCodec<ReadOnlyMemory<byte>> configured, cannot decode binary nodes.");
                {
                    IAsyncLogic binAsyncLogic = binaryCodec.Deserialize(binaryDto.Logic);
                    nodes[nodeDto.Index] = new LogicNode(new NodeId(nodeDto.Index, binaryDto.Name), binAsyncLogic);
                }
                    break;

                default:
                    throw new InvalidOperationException($"Unknown node DTO type: {nodeDto.GetType().FullName}");
            }

            slotFilled[nodeDto.Index] = true;
        }

        // Reject payloads with gaps. With nodesLength items and no duplicates already
        // enforced above, a single unfilled slot means the payload skipped an index
        // — corrupt routing if we proceeded.
        for (int i = 0; i < nodesLength; i++)
        {
            if (!slotFilled[i])
                throw new InvalidOperationException($"Node DTO payload is missing entry for index {i}.");
        }

        Dictionary<int, Graph> ownerToSubGraph = new();
        int subgraphCount = dto.SubGraphs.Length;
        for (int i = 0; i < subgraphCount; i++)
        {
            SubGraphDto subDto = dto.SubGraphs[i];

            if (subDto.OwnerIndex < 0 || subDto.OwnerIndex >= nodesLength)
                throw new InvalidOperationException(
                    $"Subgraph DTO owner index {subDto.OwnerIndex} is out of range (0..{nodesLength - 1}).");

            // A subgraph may only attach to a marker node. The old fallback installed the raw
            // child Graph as the node itself — with the child graph's own (negative) NodeId —
            // silently corrupting transition routing; a crafted payload could also use it to
            // swap decoded logic for a nested machine.
            if (nodes[subDto.OwnerIndex] != LogicNode.StateMachineMarker &&
                nodes[subDto.OwnerIndex] != LogicNode.SyncStateMachineMarker)
                throw new InvalidOperationException(
                    $"Subgraph DTO owner index {subDto.OwnerIndex} does not reference a state-machine marker node.");

            Graph childGraph = FromDto(subDto.Graph, depth + 1);
            ownerToSubGraph[subDto.OwnerIndex] = childGraph;
        }

        for (int i = 0; i < nodesLength; i++)
        {
            if (nodes[i] is not LogicNode marker) continue;
            bool isSyncMarker = marker == LogicNode.SyncStateMachineMarker;
            if (marker != LogicNode.StateMachineMarker && !isSyncMarker) continue;

            if (!ownerToSubGraph.TryGetValue(i, out Graph? childGraph))
                throw new InvalidOperationException(
                    $"Node at index {i} was marked as a StateMachine but has no associated subgraph.");

            string nodeName = dto.Nodes[i] switch
            {
                NodeTextDto nt => nt.Name,
                NodeBinaryDto nb => nb.Name,
                _ => $"Node_{i}"
            };

            // The marker kind discriminates the composite runtime: sync-nested machines come
            // back as sync StateMachines (behind the sync-logic adapter), async ones as an
            // AsyncStateMachine. Machine-level config (step mode, restart policy) is runtime
            // configuration, not structure — deserialized machines carry the defaults.
            IAsyncLogic stateMachineAsyncLogic = isSyncMarker
                ? new SyncLogicAdapter(new StateMachine(childGraph))
                : new AsyncStateMachine(childGraph);
            nodes[i] = new LogicNode(new NodeId(i, nodeName), stateMachineAsyncLogic);
        }

        int transitionsLength = dto.Transitions.Length;
        Transition[] transitions = new Transition[transitionsLength];
        for (int i = 0; i < transitionsLength; i++)
        {
            TransitionDto trDto = dto.Transitions[i];
            if (trDto.Destination < -1 || trDto.Destination >= nodesLength)
                throw new InvalidOperationException(
                    $"Transition DTO destination index {trDto.Destination} is out of range (-1..{nodesLength - 1}).");
            if (trDto.FailureDestination < -1 || trDto.FailureDestination >= nodesLength)
                throw new InvalidOperationException(
                    $"Transition DTO failure destination index {trDto.FailureDestination} is out of range (-1..{nodesLength - 1}).");

            NodeId dest = trDto.Destination == -1 ? NodeId.Default : nodes[trDto.Destination].Id;
            NodeId failureDest = trDto.FailureDestination == -1 ? NodeId.Default : nodes[trDto.FailureDestination].Id;
            transitions[i] = new Transition(dest, failureDest);
        }

        RetryPolicy[]? retryPolicies = null;
        if (dto.RetryPolicies is { Length: > 0 })
        {
            retryPolicies = new RetryPolicy[nodesLength];
            foreach (RetryPolicyDto entry in dto.RetryPolicies)
            {
                if (entry.Index < 0 || entry.Index >= nodesLength)
                    throw new InvalidOperationException(
                        $"Retry policy DTO index {entry.Index} is out of range (0..{nodesLength - 1}).");
                if (entry.MaxAttempts == 0)
                    throw new InvalidOperationException(
                        $"Retry policy DTO for node {entry.Index} must allow at least one attempt.");
                if (entry.BackoffTicks < 0)
                    throw new InvalidOperationException(
                        $"Retry policy DTO for node {entry.Index} has a negative backoff.");
                if (entry.BackoffKind > (byte)BackoffKind.Exponential)
                    throw new InvalidOperationException(
                        $"Retry policy DTO for node {entry.Index} has unknown backoff kind {entry.BackoffKind}.");

                retryPolicies[entry.Index] = new RetryPolicy(entry.MaxAttempts,
                    TimeSpan.FromTicks(entry.BackoffTicks), (BackoffKind)entry.BackoffKind);
            }
        }

        int[]? outcomeCodes = null;
        if (dto.OutcomeCodes is { Length: > 0 })
        {
            outcomeCodes = new int[nodesLength];
            foreach (OutcomeCodeDto entry in dto.OutcomeCodes)
            {
                if (entry.NodeIndex < 0 || entry.NodeIndex >= nodesLength)
                    throw new InvalidOperationException(
                        $"Outcome code DTO index {entry.NodeIndex} is out of range (0..{nodesLength - 1}).");

                outcomeCodes[entry.NodeIndex] = entry.Code;
            }
        }

        Dictionary<int, string>? outcomeNames = null;
        if (dto.OutcomeNames is { Length: > 0 })
        {
            outcomeNames = new Dictionary<int, string>(dto.OutcomeNames.Length);
            foreach (OutcomeNameDto entry in dto.OutcomeNames)
            {
                outcomeNames[entry.Code] = entry.Name;
            }
        }

        NodeId graphId = new(dto.Index, dto.Name);
        return new Graph(graphId, nodes, transitions, logic: null, retryPolicies, outcomeCodes, outcomeNames);
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