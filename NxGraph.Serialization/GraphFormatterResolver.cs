using MessagePack;
using MessagePack.Formatters;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Serialization;

internal sealed class GraphFormatterResolver : IFormatterResolver
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

            if (typeof(T) == typeof(RetryPolicyDto))
            {
                Formatter = (IMessagePackFormatter<T>)(object)RetryPolicyDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(RetryPolicyDto[]))
            {
                Formatter = (IMessagePackFormatter<T>)(object)RetryPolicyArrayDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(OutcomeCodeDto))
            {
                Formatter = (IMessagePackFormatter<T>)(object)OutcomeCodeDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(OutcomeCodeDto[]))
            {
                Formatter = (IMessagePackFormatter<T>)(object)OutcomeCodeArrayDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(OutcomeNameDto))
            {
                Formatter = (IMessagePackFormatter<T>)(object)OutcomeNameDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(OutcomeNameDto[]))
            {
                Formatter = (IMessagePackFormatter<T>)(object)OutcomeNameArrayDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(SubGraphDto))
            {
                Formatter = (IMessagePackFormatter<T>)(object)SubGraphDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(SubGraphDto[]))
            {
                Formatter = (IMessagePackFormatter<T>)(object)SubgraphArrayDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(CompositeDto))
            {
                Formatter = (IMessagePackFormatter<T>)(object)CompositeDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(CompositeDto[]))
            {
                Formatter = (IMessagePackFormatter<T>)(object)CompositeArrayDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(UidDto))
            {
                Formatter = (IMessagePackFormatter<T>)(object)UidDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(UidDto[]))
            {
                Formatter = (IMessagePackFormatter<T>)(object)UidArrayDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(ForkDto))
            {
                Formatter = (IMessagePackFormatter<T>)(object)ForkDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(ForkDto[]))
            {
                Formatter = (IMessagePackFormatter<T>)(object)ForkArrayDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(JoinDto))
            {
                Formatter = (IMessagePackFormatter<T>)(object)JoinDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(JoinDto[]))
            {
                Formatter = (IMessagePackFormatter<T>)(object)JoinArrayDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(ContainerDto))
            {
                Formatter = (IMessagePackFormatter<T>)(object)ContainerDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(ContainerDto[]))
            {
                Formatter = (IMessagePackFormatter<T>)(object)ContainerArrayDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(EventEntryDto))
            {
                Formatter = (IMessagePackFormatter<T>)(object)EventEntryDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(EventEntryDto[]))
            {
                Formatter = (IMessagePackFormatter<T>)(object)EventEntryArrayDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(BehaviorDto))
            {
                Formatter = (IMessagePackFormatter<T>)(object)BehaviorDtoFormatter.Instance;
                return;
            }

            if (typeof(T) == typeof(BehaviorDto[]))
            {
                Formatter = (IMessagePackFormatter<T>)(object)BehaviorArrayDtoFormatter.Instance;
                return;
            }

            Formatter = null;
        }
    }
}