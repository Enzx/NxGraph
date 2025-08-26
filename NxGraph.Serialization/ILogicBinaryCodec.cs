using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

public interface ILogicBinaryCodec : ILogicCodec<ReadOnlyMemory<byte>>;