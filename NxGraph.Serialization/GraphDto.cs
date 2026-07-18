namespace NxGraph.Serialization;

/// <summary>
/// Minimal DTO for a graph that can be serialized/deserialized.
/// Keeps indices stable and captures names and edges.
/// </summary>
internal sealed class GraphDto
{
    /// <summary>
    /// Minimal DTO for a graph that can be serialized/deserialized.
    /// Keeps indices stable and captures names and edges.
    /// </summary>
    public GraphDto(INodeDto[] nodes, TransitionDto[] transitions, SubGraphDto[]? subGraphs = null, int index = -1,
        string? name = null, RetryPolicyDto[]? retryPolicies = null, OutcomeCodeDto[]? outcomeCodes = null,
        OutcomeNameDto[]? outcomeNames = null, CompositeDto[]? composites = null, UidDto[]? uids = null,
        ForkDto[]? forks = null, JoinDto[]? joins = null, ContainerDto[]? containers = null,
        EventEntryDto[]? eventEntries = null, BehaviorDto[]? behaviors = null)
    {
        if (nodes.Length != transitions.Length)
            throw new ArgumentException("Nodes and transitions must have the same length.", nameof(transitions));
        Nodes = nodes;
        Transitions = transitions;
        SubGraphs =   subGraphs ?? [];
        Name = name;
        Index = index;
        RetryPolicies = retryPolicies ?? [];
        OutcomeCodes = outcomeCodes ?? [];
        OutcomeNames = outcomeNames ?? [];
        Composites = composites ?? [];
        Uids = uids ?? [];
        Forks = forks ?? [];
        Joins = joins ?? [];
        Containers = containers ?? [];
        EventEntries = eventEntries ?? [];
        Behaviors = behaviors ?? [];
    }

    /// <summary>
    /// Serialized payload version. Defaults to <see cref="SerializationVersion.Version"/> for
    /// freshly-constructed DTOs so the JSON/MessagePack writers emit a version on the wire,
    /// and is overwritten by the deserializer when reading an existing payload so callers can
    /// detect version mismatches.
    /// </summary>
    public int Version { get; set; } = SerializationVersion.Version;

    public INodeDto[] Nodes { get; set; }
    public TransitionDto[] Transitions { get; set; }
    public SubGraphDto[] SubGraphs { get; set; }
    public string? Name { get; set; }
    public int Index { get; set; }
    public RetryPolicyDto[] RetryPolicies { get; set; }
    public OutcomeCodeDto[] OutcomeCodes { get; set; }
    public OutcomeNameDto[] OutcomeNames { get; set; }
    public CompositeDto[] Composites { get; set; }
    public UidDto[] Uids { get; set; }
    public ForkDto[] Forks { get; set; }
    public JoinDto[] Joins { get; set; }
    public ContainerDto[] Containers { get; set; }
    public EventEntryDto[] EventEntries { get; set; }
    public BehaviorDto[] Behaviors { get; set; }

}