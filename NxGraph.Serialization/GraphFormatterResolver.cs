using MessagePack;
using MessagePack.Formatters;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

public sealed class GraphFormatterResolver : IFormatterResolver
{
    public static readonly GraphFormatterResolver Instance = new();

    public IMessagePackFormatter<T>? GetFormatter<T>()
    {
        return Cache<T>.Formatter;
    }

    private static class Cache<T>
    {
        public static readonly IMessagePackFormatter<T>? Formatter;

        static Cache()
        {
            if (typeof(T) == typeof(GraphDto))
            {
                Formatter = (IMessagePackFormatter<T>)(object)GraphDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(INodeDto))
            {
                Formatter = (IMessagePackFormatter<T>)(object)NodeDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(TransitionDto))
            {
                Formatter = (IMessagePackFormatter<T>)(object)TransitionDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(INodeDto[]))
            {
                Formatter = (IMessagePackFormatter<T>)(object)NodeDtoArrayFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(TransitionDto[]))
            {
                Formatter = (IMessagePackFormatter<T>)(object)TransitionArrayDtoFormatter.Instance;
                return;
            }

            Formatter = null;
        }
    }
}