using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

internal sealed record NodeBinaryDto(string Name, ReadOnlyMemory<byte> Logic) : INodeDto;