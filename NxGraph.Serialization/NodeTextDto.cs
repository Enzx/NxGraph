using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

internal sealed record NodeTextDto(int Index, string Name, string Logic) : INodeDto;