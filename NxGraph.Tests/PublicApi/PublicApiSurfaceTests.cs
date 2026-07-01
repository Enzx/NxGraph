using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using NxGraph.Graphs;
using NxGraph.Serialization.Abstraction;

namespace NxGraph.Tests.PublicApi;

/// <summary>
/// Guards the public API surface of the shipped assemblies. Any addition, removal, or
/// signature change of a public/protected symbol fails this fixture until the matching
/// baseline file is updated — making every API change an explicit, reviewed diff.
/// <para>
/// To accept an intentional change, set the environment variable
/// <c>NXGRAPH_UPDATE_PUBLIC_API=1</c> and re-run the tests: the baselines under
/// <c>NxGraph.Tests/PublicApi/</c> are rewritten in place. Commit the resulting diff.
/// </para>
/// <para>
/// The surface is captured for net8.0 (the richest target); the netstandard2.1 surface
/// is compiled from the same sources minus <c>NET8_0_OR_GREATER</c> members.
/// </para>
/// </summary>
[TestFixture]
public class PublicApiSurfaceTests
{
    private static readonly (string BaselineFile, Assembly Assembly)[] Assemblies =
    [
        ("NxGraph.approved.txt", typeof(Graph).Assembly),
        ("NxGraph.Serialization.approved.txt", typeof(Serialization.GraphSerializer).Assembly),
        ("NxGraph.Serialization.Abstraction.approved.txt", typeof(ILogicCodec).Assembly),
    ];

    [Test]
    public void Public_api_matches_approved_baseline(
        [Range(0, 2)] int assemblyIndex)
    {
        (string baselineFile, Assembly assembly) = Assemblies[assemblyIndex];
        string actual = DescribePublicApi(assembly);
        string baselinePath = Path.Combine(BaselineDirectory(), baselineFile);

        if (Environment.GetEnvironmentVariable("NXGRAPH_UPDATE_PUBLIC_API") == "1")
        {
            File.WriteAllText(baselinePath, actual);
            Assert.Pass($"Baseline updated: {baselinePath}");
        }

        if (!File.Exists(baselinePath))
        {
            Assert.Fail(
                $"Missing public API baseline '{baselineFile}'. " +
                "Run the tests once with NXGRAPH_UPDATE_PUBLIC_API=1 to create it, then commit it.");
        }

        string expected = File.ReadAllText(baselinePath);
        if (Normalize(expected) == Normalize(actual))
        {
            Assert.Pass();
        }

        Assert.Fail(
            $"Public API of {assembly.GetName().Name} differs from the approved baseline.\n" +
            Diff(Normalize(expected), Normalize(actual)) +
            "\nIf this change is intentional, re-run with NXGRAPH_UPDATE_PUBLIC_API=1 and commit the baseline diff.");
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd('\n');

    private static string Diff(string expected, string actual)
    {
        HashSet<string> expectedLines = [..expected.Split('\n')];
        HashSet<string> actualLines = [..actual.Split('\n')];

        StringBuilder sb = new();
        foreach (string line in expectedLines.Except(actualLines).Order())
        {
            sb.Append("  - ").AppendLine(line);
        }

        foreach (string line in actualLines.Except(expectedLines).Order())
        {
            sb.Append("  + ").AppendLine(line);
        }

        return sb.ToString();
    }

    private static string BaselineDirectory([CallerFilePath] string thisFile = "")
        => Path.GetDirectoryName(thisFile)!;

    private static string DescribePublicApi(Assembly assembly)
    {
        StringBuilder sb = new();
        IOrderedEnumerable<Type> types = assembly.GetExportedTypes()
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (Type type in types)
        {
            sb.Append(TypeKind(type)).Append(' ').AppendLine(TypeDisplay(type));

            foreach (string member in DescribeMembers(type).Order(StringComparer.Ordinal))
            {
                sb.Append("  ").AppendLine(member);
            }
        }

        return sb.ToString();
    }

    private static string TypeKind(Type type)
    {
        if (type.IsEnum) return "enum";
        if (type.IsValueType) return "struct";
        if (type.IsInterface) return "interface";
        if (typeof(Delegate).IsAssignableFrom(type)) return "delegate";
        if (type is { IsAbstract: true, IsSealed: true }) return "static class";
        if (type.IsAbstract) return "abstract class";
        if (type.IsSealed) return "sealed class";
        return "class";
    }

    private static string TypeDisplay(Type type)
    {
        StringBuilder sb = new(type.FullName);
        List<string> bases = [];
        if (type.BaseType is not null &&
            type.BaseType != typeof(object) &&
            type.BaseType != typeof(ValueType) &&
            type.BaseType != typeof(Enum) &&
            type.BaseType != typeof(MulticastDelegate))
        {
            bases.Add(type.BaseType.FullName ?? type.BaseType.Name);
        }

        bases.AddRange(type.GetInterfaces()
            .Select(i => i.FullName ?? i.Name)
            .Order(StringComparer.Ordinal));

        if (bases.Count > 0)
        {
            sb.Append(" : ").Append(string.Join(", ", bases));
        }

        return sb.ToString();
    }

    private static IEnumerable<string> DescribeMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.DeclaredOnly;

        if (type.IsEnum)
        {
            foreach (string name in Enum.GetNames(type))
            {
                yield return $"{name} = {Convert.ToInt64(Enum.Parse(type, name))}";
            }

            yield break;
        }

        foreach (MemberInfo member in type.GetMembers(flags))
        {
            if (!IsVisible(member))
            {
                continue;
            }

            switch (member)
            {
                case MethodInfo { IsSpecialName: true }:
                    // Property/event accessors and operators surface via their owners;
                    // operators are IsSpecialName too, so re-admit them explicitly.
                    if (member is MethodInfo op && op.Name.StartsWith("op_", StringComparison.Ordinal))
                    {
                        yield return $"operator {op}";
                    }

                    break;
                case MethodInfo method:
                    yield return $"method {method}";
                    break;
                case ConstructorInfo ctor:
                    yield return $"ctor {ctor}";
                    break;
                case PropertyInfo property:
                    yield return $"property {property} {{ {AccessorList(property)} }}";
                    break;
                case FieldInfo field:
                    yield return $"field {(field.IsStatic ? "static " : "")}{(field.IsInitOnly ? "readonly " : "")}{field.FieldType.FullName ?? field.FieldType.Name} {field.Name}";
                    break;
                case EventInfo evt:
                    yield return $"event {evt}";
                    break;
            }
        }
    }

    private static string AccessorList(PropertyInfo property)
    {
        List<string> parts = [];
        if (property.GetMethod is { } get && (get.IsPublic || get.IsFamily || get.IsFamilyOrAssembly))
        {
            parts.Add("get;");
        }

        if (property.SetMethod is { } set && (set.IsPublic || set.IsFamily || set.IsFamilyOrAssembly))
        {
            parts.Add("set;");
        }

        return string.Join(" ", parts);
    }

    private static bool IsVisible(MemberInfo member) => member switch
    {
        MethodBase method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly,
        FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
        PropertyInfo property => (property.GetMethod ?? property.SetMethod) is { } accessor && IsVisible(accessor),
        EventInfo evt => evt.AddMethod is { } add && IsVisible(add),
        _ => false,
    };
}
