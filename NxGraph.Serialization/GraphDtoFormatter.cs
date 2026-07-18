using MessagePack;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

internal sealed class GraphDtoFormatter : GraphEntityFormatter<GraphDto>
{
    internal static readonly GraphDtoFormatter Instance = new();

    private const int VersionOneHeaderCount = 6;
    private const int VersionTwoHeaderCount = 9;
    private const int VersionFourHeaderCount = 10;
    private const int VersionFiveHeaderCount = 11;
    private const int VersionSixHeaderCount = 14;
    private const int VersionSevenHeaderCount = 15;
    private const int VersionEightHeaderCount = 16;

    public override void Serialize(ref MessagePackWriter writer, GraphDto value, MessagePackSerializerOptions options)
    {
        // [0.Version 1.Index, 2.Name, 3.Nodes[], 4.Transitions[], 5.SubGraphs[],
        //  6.RetryPolicies[], 7.OutcomeCodes[], 8.OutcomeNames[], 9.Composites[], 10.Uids[],
        //  11.Forks[], 12.Joins[], 13.Containers[], 14.EventEntries[], 15.Behaviors[]]
        writer.WriteArrayHeader(VersionEightHeaderCount);
        writer.Write(value.Version);
        writer.Write(value.Index);
        writer.Write(value.Name);
        options.Resolver.GetFormatterWithVerify<INodeDto[]>().Serialize(ref writer, value.Nodes, options);
        options.Resolver.GetFormatterWithVerify<TransitionDto[]>().Serialize(ref writer, value.Transitions, options);
        options.Resolver.GetFormatterWithVerify<SubGraphDto[]>().Serialize(ref writer, value.SubGraphs, options);
        options.Resolver.GetFormatterWithVerify<RetryPolicyDto[]>()
            .Serialize(ref writer, value.RetryPolicies, options);
        options.Resolver.GetFormatterWithVerify<OutcomeCodeDto[]>()
            .Serialize(ref writer, value.OutcomeCodes, options);
        options.Resolver.GetFormatterWithVerify<OutcomeNameDto[]>()
            .Serialize(ref writer, value.OutcomeNames, options);
        options.Resolver.GetFormatterWithVerify<CompositeDto[]>()
            .Serialize(ref writer, value.Composites, options);
        options.Resolver.GetFormatterWithVerify<UidDto[]>()
            .Serialize(ref writer, value.Uids, options);
        options.Resolver.GetFormatterWithVerify<ForkDto[]>()
            .Serialize(ref writer, value.Forks, options);
        options.Resolver.GetFormatterWithVerify<JoinDto[]>()
            .Serialize(ref writer, value.Joins, options);
        options.Resolver.GetFormatterWithVerify<ContainerDto[]>()
            .Serialize(ref writer, value.Containers, options);
        options.Resolver.GetFormatterWithVerify<EventEntryDto[]>()
            .Serialize(ref writer, value.EventEntries, options);
        options.Resolver.GetFormatterWithVerify<BehaviorDto[]>()
            .Serialize(ref writer, value.Behaviors, options);
    }

    public override GraphDto Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        // Depth accounting: GraphDto → SubGraphDto → GraphDto recursion is driven purely by
        // payload content. Without DepthStep a crafted payload nesting thousands of subgraph
        // levels overflows the stack (uncatchable) before GraphSerializer's own MaxSubGraphDepth
        // check — which only runs after the DTO tree is fully materialized.
        options.Security.DepthStep(ref reader);

        int count = reader.ReadArrayHeader();

        int version = reader.ReadInt32();
        switch (version)
        {
            case > SerializationVersion.Version:
                throw new InvalidOperationException($"GraphDto: version {version} is not supported.");
            case 1 when count < VersionOneHeaderCount:
                throw new InvalidOperationException(
                    $"GraphDto: expected at least {VersionOneHeaderCount} elements, got {count}");
            case 2 or 3 when count < VersionTwoHeaderCount:
                throw new InvalidOperationException(
                    $"GraphDto: expected at least {VersionTwoHeaderCount} elements, got {count}");
            case 4 when count < VersionFourHeaderCount:
                throw new InvalidOperationException(
                    $"GraphDto: expected at least {VersionFourHeaderCount} elements, got {count}");
            case 5 when count < VersionFiveHeaderCount:
                throw new InvalidOperationException(
                    $"GraphDto: expected at least {VersionFiveHeaderCount} elements, got {count}");
            case 6 when count < VersionSixHeaderCount:
                throw new InvalidOperationException(
                    $"GraphDto: expected at least {VersionSixHeaderCount} elements, got {count}");
            case 7 when count < VersionSevenHeaderCount:
                throw new InvalidOperationException(
                    $"GraphDto: expected at least {VersionSevenHeaderCount} elements, got {count}");
            // v9 changed only the behavior field model (nested entries), not the header shape.
            case 8 or 9 when count < VersionEightHeaderCount:
                throw new InvalidOperationException(
                    $"GraphDto: expected at least {VersionEightHeaderCount} elements, got {count}");
        }

        int index = reader.ReadInt32();
        string? name = reader.ReadString();
        INodeDto[] nodes = options.Resolver.GetFormatterWithVerify<INodeDto[]>().Deserialize(ref reader, options);
        TransitionDto[] transitions =
            options.Resolver.GetFormatterWithVerify<TransitionDto[]>().Deserialize(ref reader, options);
        SubGraphDto[] subGraphs =
            options.Resolver.GetFormatterWithVerify<SubGraphDto[]>().Deserialize(ref reader, options);

        // Version-1 payloads end after SubGraphs; retry policies and outcomes arrived with version 2.
        RetryPolicyDto[] retryPolicies = [];
        OutcomeCodeDto[] outcomeCodes = [];
        OutcomeNameDto[] outcomeNames = [];
        if (count >= VersionTwoHeaderCount)
        {
            retryPolicies = options.Resolver.GetFormatterWithVerify<RetryPolicyDto[]>()
                .Deserialize(ref reader, options);
            outcomeCodes = options.Resolver.GetFormatterWithVerify<OutcomeCodeDto[]>()
                .Deserialize(ref reader, options);
            outcomeNames = options.Resolver.GetFormatterWithVerify<OutcomeNameDto[]>()
                .Deserialize(ref reader, options);
        }

        // Pre-v4 payloads end after OutcomeNames; the composite section arrived with version 4.
        CompositeDto[] composites = [];
        if (count >= VersionFourHeaderCount)
        {
            composites = options.Resolver.GetFormatterWithVerify<CompositeDto[]>()
                .Deserialize(ref reader, options);
        }

        // Pre-v5 payloads end after Composites; the uid section arrived with version 5.
        UidDto[] uids = [];
        if (count >= VersionFiveHeaderCount)
        {
            uids = options.Resolver.GetFormatterWithVerify<UidDto[]>()
                .Deserialize(ref reader, options);
        }

        // Pre-v6 payloads end after Uids; forks, joins, and containers arrived with version 6.
        ForkDto[] forks = [];
        JoinDto[] joins = [];
        ContainerDto[] containers = [];
        if (count >= VersionSixHeaderCount)
        {
            forks = options.Resolver.GetFormatterWithVerify<ForkDto[]>()
                .Deserialize(ref reader, options);
            joins = options.Resolver.GetFormatterWithVerify<JoinDto[]>()
                .Deserialize(ref reader, options);
            containers = options.Resolver.GetFormatterWithVerify<ContainerDto[]>()
                .Deserialize(ref reader, options);
        }

        // Pre-v7 payloads end after Containers; the event entry section arrived with version 7.
        EventEntryDto[] eventEntries = [];
        if (count >= VersionSevenHeaderCount)
        {
            eventEntries = options.Resolver.GetFormatterWithVerify<EventEntryDto[]>()
                .Deserialize(ref reader, options);
        }

        // Pre-v8 payloads end after EventEntries; the behavior section arrived with version 8.
        BehaviorDto[] behaviors = [];
        if (count >= VersionEightHeaderCount)
        {
            behaviors = options.Resolver.GetFormatterWithVerify<BehaviorDto[]>()
                .Deserialize(ref reader, options);
        }

        reader.Depth--;

        return new GraphDto(nodes, transitions, subGraphs, index, name, retryPolicies, outcomeCodes, outcomeNames,
            composites, uids, forks, joins, containers, eventEntries, behaviors) { Version = version };
    }
}