namespace NxGraph.Compatibility;

internal static class Guard
{
    public static T NotNull<T>(T value, string paramName)
    {
        return value ?? throw new ArgumentNullException(paramName);
    }
}

