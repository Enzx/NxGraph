using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

public sealed record NodeBinaryDto(string Name, ReadOnlyMemory<byte> Logic) : INodeDto;