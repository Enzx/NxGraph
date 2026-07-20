using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
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
/// <c>NxGraph.Tests/PublicApi/</c> are rewritten in place (the netstandard2.1 baseline
/// included — it goes through the exact same mechanism). Commit the resulting diff.
/// </para>
/// <para>
/// Two flavors of capture: the loaded net8.0 assemblies are described via runtime
/// reflection; the netstandard2.1 build of NxGraph (the Unity-facing surface, compiled
/// with the <c>Shims/**</c> re-includes) is described via a <see cref="MetadataLoadContext"/>
/// walk over <c>NxGraph/bin/{Configuration}/netstandard2.1/NxGraph.dll</c> resolved against
/// the NETStandard.Library.Ref facades, and lands in <c>NxGraph.netstandard2.1.approved.txt</c>.
/// </para>
/// <para>
/// Known limitation (both flavors, unchanged from the original fixture): the walk captures
/// shape only — not nullability annotations, attributes, parameter names, or default values.
/// The two flavors also render open-generic signatures differently (runtime reflection uses
/// short names, MetadataLoadContext full names), so compare each baseline only with itself;
/// the netstandard2.1 rendering is tied to the pinned System.Reflection.MetadataLoadContext
/// package version — a bump may need a baseline regeneration via the same env var.
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
        CompareOrUpdateBaseline(baselineFile, assembly.GetName().Name!, DescribePublicApi(assembly));
    }

    [Test]
    public void Public_api_matches_approved_baseline_for_netstandard21()
    {
        string targetDll = NetStandardAssemblyPath();
        if (!File.Exists(targetDll))
        {
            Assert.Ignore(
                $"netstandard2.1 build output not found at '{targetDll}' — a partial build. " +
                "Build the full solution (dotnet build) to produce it; the solution build always emits both TFMs.");
        }

        string facadeDir = NetStandardFacadeDirectory();
        PathAssemblyResolver resolver = new(
            Directory.EnumerateFiles(facadeDir, "*.dll").Append(targetDll));
        using MetadataLoadContext mlc = new(resolver, coreAssemblyName: "netstandard");

        string actual = DescribePublicApi(mlc.LoadFromAssemblyPath(targetDll));
        CompareOrUpdateBaseline("NxGraph.netstandard2.1.approved.txt", "NxGraph (netstandard2.1)", actual);
    }

    private static void CompareOrUpdateBaseline(string baselineFile, string assemblyDisplayName, string actual)
    {
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
            $"Public API of {assemblyDisplayName} differs from the approved baseline.\n" +
            Diff(Normalize(expected), Normalize(actual)) +
            "\nIf this change is intentional, re-run with NXGRAPH_UPDATE_PUBLIC_API=1 and commit the baseline diff.");
    }

    /// <summary>NxGraph's netstandard2.1 build output for the configuration this test run was built as.</summary>
    private static string NetStandardAssemblyPath()
    {
        // Test bin layout: <repo>/NxGraph.Tests/bin/<Configuration>/net8.0/ — take the live
        // configuration from the test host's own path so a Debug test run checks the Debug
        // netstandard build and Release checks Release.
        string baseDir = AppContext.BaseDirectory
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string configuration = new DirectoryInfo(baseDir).Parent?.Name ?? "Release";

        string repoRoot = Path.GetFullPath(Path.Combine(BaselineDirectory(), "..", ".."));
        return Path.Combine(repoRoot, "NxGraph", "bin", configuration, "netstandard2.1", "NxGraph.dll");
    }

    /// <summary>Reference-facade directory of the restored NETStandard.Library.Ref pack.</summary>
    private static string NetStandardFacadeDirectory()
    {
        string? dir = typeof(PublicApiSurfaceTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "NxGraph.Tests.NetStandardRefDir")?.Value;

        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            Assert.Fail(
                "NETStandard.Library.Ref facade directory not found (assembly metadata " +
                $"'NxGraph.Tests.NetStandardRefDir' resolved to '{dir}'). Check the " +
                "NETStandard.Library.Ref PackageReference in NxGraph.Tests.csproj.");
        }

        return dir!;
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

    // Constructed generic types render their arguments assembly-qualified
    // ("[[NxGraph.Result, NxGraph, Version=1.0.0.0, ...]]"), which would make the
    // baseline depend on the build's version stamp (e.g. VERSION set in a release CI run).
    private static string StripAssemblyQualification(string text) =>
        Regex.Replace(text, @", [\w.]+, Version=[^,\]]+, Culture=[^,\]]+, PublicKeyToken=[^,\]]+", "");

    // Everything below is deliberately MetadataLoadContext-safe: no identity comparisons
    // against runtime typeof(...) (an MLC type never equals a runtime type) and no
    // Enum.GetNames/Enum.Parse (which require runtime types) — name-based checks and raw
    // field constants render identically for both reflection flavors.

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

        return StripAssemblyQualification(sb.ToString());
    }

    private static string TypeKind(Type type)
    {
        if (type.IsEnum) return "enum";
        if (type.IsValueType) return "struct";
        if (type.IsInterface) return "interface";
        if (IsDelegate(type)) return "delegate";
        if (type is { IsAbstract: true, IsSealed: true }) return "static class";
        if (type.IsAbstract) return "abstract class";
        if (type.IsSealed) return "sealed class";
        return "class";
    }

    private static bool IsDelegate(Type type)
    {
        for (Type? baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.FullName is "System.MulticastDelegate" or "System.Delegate")
            {
                return true;
            }
        }

        return false;
    }

    private static string TypeDisplay(Type type)
    {
        StringBuilder sb = new(type.FullName);
        List<string> bases = [];
        if (type.BaseType is { } baseType &&
            baseType.FullName is not ("System.Object" or "System.ValueType" or "System.Enum"
                or "System.MulticastDelegate"))
        {
            bases.Add(baseType.FullName ?? baseType.Name);
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
            // Same ordering Enum.GetNames uses: ascending unsigned binary value.
            foreach (FieldInfo field in type
                         .GetFields(BindingFlags.Public | BindingFlags.Static)
                         .OrderBy(f => unchecked((ulong)Convert.ToInt64(f.GetRawConstantValue(), CultureInfo.InvariantCulture)))
                         .ThenBy(f => f.Name, StringComparer.Ordinal))
            {
                yield return $"{field.Name} = {Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture)}";
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
