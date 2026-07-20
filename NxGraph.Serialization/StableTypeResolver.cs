using System.Reflection;

namespace NxGraph.Serialization;

/// <summary>
/// Resolves the runtime-stable type names the payloads carry (see
/// <see cref="BlackboardSerializer.StableTypeName"/> for the rendering) back to live
/// <see cref="Type"/>s — cold read-side reflection, the <c>BlackboardSerializer</c>
/// precedent. Simple names probe <see cref="Type.GetType(string)"/> and then every loaded
/// assembly; constructed generics and arrays are parsed recursively, because stable names
/// deliberately omit assembly qualification.
/// </summary>
internal static class StableTypeResolver
{
    internal static bool TryResolve(string stableName, out Type type)
    {
        Type? resolved = Resolve(stableName.Trim());
        type = resolved!;
        return resolved is not null;
    }

    private static Type? Resolve(string name)
    {
        if (name.Length == 0)
        {
            return null;
        }

        if (!name.EndsWith(']'))
        {
            return ResolveSimple(name);
        }

        int open = FindMatchingOpen(name);
        if (open <= 0)
        {
            return null;
        }

        string inner = name.Substring(open + 1, name.Length - open - 2);
        if (IsArraySuffix(inner))
        {
            Type? element = Resolve(name.Substring(0, open));
            if (element is null)
            {
                return null;
            }

            int rank = inner.Length + 1; // "" → 1, "," → 2, ...
            return rank == 1 ? element.MakeArrayType() : element.MakeArrayType(rank);
        }

        Type? definition = ResolveSimple(name.Substring(0, open));
        if (definition is null || !definition.IsGenericTypeDefinition)
        {
            return null;
        }

        string[] parts = SplitTopLevel(inner);
        if (definition.GetGenericArguments().Length != parts.Length)
        {
            return null;
        }

        Type[] arguments = new Type[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            Type? argument = Resolve(parts[i].Trim());
            if (argument is null)
            {
                return null;
            }

            arguments[i] = argument;
        }

        try
        {
            return definition.MakeGenericType(arguments);
        }
        catch (ArgumentException)
        {
            return null; // constraint violation — treat as unresolvable, callers throw targeted errors
        }
    }

    private static Type? ResolveSimple(string name)
    {
        Type? type = Type.GetType(name);
        if (type is not null)
        {
            return type;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(name);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    private static bool IsArraySuffix(string inner)
    {
        foreach (char c in inner)
        {
            if (c != ',')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Index of the '[' matching the final ']' of <paramref name="name"/>, or -1.</summary>
    private static int FindMatchingOpen(string name)
    {
        int depth = 0;
        for (int i = name.Length - 1; i >= 0; i--)
        {
            switch (name[i])
            {
                case ']':
                    depth++;
                    break;
                case '[':
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }

    private static string[] SplitTopLevel(string inner)
    {
        List<string> parts = [];
        int depth = 0;
        int start = 0;
        for (int i = 0; i < inner.Length; i++)
        {
            switch (inner[i])
            {
                case '[':
                    depth++;
                    break;
                case ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(inner.Substring(start, i - start));
                    start = i + 1;
                    break;
            }
        }

        parts.Add(inner.Substring(start));
        return parts.ToArray();
    }
}
