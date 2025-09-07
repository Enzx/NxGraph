using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

internal sealed record NodeBinaryDto(int Index, string Name, ReadOnlyMemory<byte> Logic) : INodeDto;