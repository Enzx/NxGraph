using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

internal sealed record NodeTextDto(string Name, string Logic) : INodeDto;