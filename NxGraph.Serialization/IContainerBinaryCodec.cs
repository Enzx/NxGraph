using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

public interface IContainerBinaryCodec : IContainerCodec<ReadOnlyMemory<byte>>;
