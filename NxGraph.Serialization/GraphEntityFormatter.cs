using MessagePack;
using MessagePack.Formatters;

namespace NxGraph.Serialization;

public abstract class GraphEntityFormatter<TEntity> : IMessagePackFormatter<TEntity> where TEntity : notnull
{
    public abstract void Serialize(ref MessagePackWriter writer, TEntity value, MessagePackSerializerOptions options);

    public abstract TEntity Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options);
}