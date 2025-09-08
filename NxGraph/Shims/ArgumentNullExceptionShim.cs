// ReSharper disable once CheckNamespace
namespace System
{
    public static class ArgumentNullExceptionShim
    {
        public static void ThrowIfNull(object? argument, string? paramName = null)
        {
            if (argument is null)
                throw new ArgumentNullException(paramName);
        }
    }
}