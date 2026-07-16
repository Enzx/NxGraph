namespace NxGraph.Serialization;

/// <summary>
/// Discriminates the machine-wrapping composite kinds that ride the graph payload
/// (payload version 4; the dynamic kinds arrived with version 6). The sync kinds carry
/// their <c>ParallelStepMode</c> in <see cref="CompositeDto.Mode"/>; for async kinds the
/// mode is written as zero and ignored.
/// </summary>
internal enum CompositeKind : byte
{
    AsyncHistory = 0,
    SyncHistory = 1,
    AsyncParallel = 2,
    SyncParallel = 3,
    AsyncDynamicParallel = 4,
    SyncDynamicParallel = 5,
}

/// <summary>
/// Payload entry for a history/parallel composite node (payload version 4). Lives beside
/// <see cref="SubGraphDto"/> — plain nested machines keep their v3 shape untouched; composites
/// get their own section so pre-v4 readers reject the payload via the version gate instead of
/// misreading it. <paramref name="Children"/> holds the child/region graphs in region order
/// (order is identity for <c>RegionMask</c> bits); history kinds carry exactly one child.
/// <paramref name="SelectorKey"/> (payload version 6) names the region selector of the
/// dynamic kinds, resolved through the configured <c>IRegionSelectorRegistry</c> — it is
/// required for kinds 4–5 and must be null for kinds 0–3.
/// </summary>
internal sealed record CompositeDto(
    int OwnerIndex,
    CompositeKind Kind,
    byte Mode,
    GraphDto[] Children,
    string? SelectorKey = null);
