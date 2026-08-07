using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NxGraph.Build;

/// <summary>
/// Shared helpers used by build targets.
/// </summary>
public static partial class BuildHelpers
{
    // ── SemVer ─────────────────────────────────────────────────────────

    [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.\-]+)?$")]
    private static partial Regex SemVerPattern();

    public static string ValidateSemVer(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("VERSION is not set.");

        if (!SemVerPattern().IsMatch(version))
            throw new InvalidOperationException($"Version '{version}' is not valid SemVer.");

        return version;
    }

    public static bool IsPreRelease(string version) => version.Contains('-');

    // ── Env helpers ────────────────────────────────────────────────────

    public static string Env(string name, string? fallback = null) =>
        Environment.GetEnvironmentVariable(name) ?? fallback
        ?? throw new InvalidOperationException($"Required environment variable '{name}' is not set.");

    public static string? OptionalEnv(string name) =>
        Environment.GetEnvironmentVariable(name);

    // ── Repo root ──────────────────────────────────────────────────────

    public static string FindRepoRoot()
    {
        // Walk up from the executable directory
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "NxGraph.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        // Fallback: walk up from CWD
        dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "NxGraph.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root (NxGraph.sln).");
    }

    // ── Path helpers ───────────────────────────────────────────────────

    public static string SourceRoot(string repoRoot) =>
        Path.Combine(repoRoot, "NxGraph");

    /// <summary>Directory name of the core UPM package.</summary>
    public const string CorePackageDir = "com.enzx.nxgraph";

    /// <summary>Directory name of the optional serialization UPM package.</summary>
    public const string SerializationPackageDir = "com.enzx.nxgraph.serialization";

    public static string PackageRoot(string repoRoot) =>
        Path.Combine(repoRoot, "upm", CorePackageDir);

    public static string SerializationPackageRoot(string repoRoot) =>
        Path.Combine(repoRoot, "upm", SerializationPackageDir);

    public static string StagedSourceRoot(string repoRoot) =>
        Path.Combine(PackageRoot(repoRoot), "Runtime", "NxGraph");

    public static string PluginsRoot(string repoRoot) =>
        Path.Combine(PackageRoot(repoRoot), "Runtime", "Plugins");

    public static string SerializationPluginsRoot(string repoRoot) =>
        Path.Combine(SerializationPackageRoot(repoRoot), "Runtime", "Plugins");

    public static string BuildOutput(string repoRoot) =>
        Path.Combine(repoRoot, "NxGraph", "bin", "Release", "netstandard2.1");

    /// <summary>
    /// The serialization project's netstandard2.1 output. Because that project sets
    /// <c>CopyLocalLockFileAssemblies</c> on this TFM, the directory holds the whole
    /// dependency closure — which is exactly what the Unity package bundles.
    /// </summary>
    public static string SerializationBuildOutput(string repoRoot) =>
        Path.Combine(repoRoot, "NxGraph.Serialization", "bin", "Release", "netstandard2.1");

    public static string ArtifactsDir(string repoRoot) =>
        Path.Combine(repoRoot, OptionalEnv("ARTIFACTS_DIR") ?? "artifacts");

    /// <summary>
    /// The core package's package.json. <c>UPM_PACKAGE_DIR</c> still overrides it, which is how
    /// the release workflow points at a relocated layout.
    /// </summary>
    public static string PackageJsonPath(string repoRoot)
    {
        var upmDir = OptionalEnv("UPM_PACKAGE_DIR") ?? Path.Combine("upm", CorePackageDir);
        return Path.Combine(repoRoot, upmDir, "package.json");
    }

    /// <summary>
    /// The serialization package's package.json. Deliberately not overridable by
    /// <c>UPM_PACKAGE_DIR</c> — that variable names one directory, and the two packages are
    /// versioned and released together.
    /// </summary>
    public static string SerializationPackageJsonPath(string repoRoot) =>
        Path.Combine(SerializationPackageRoot(repoRoot), "package.json");

    // ── Pack helper (replaces the 3× duplicated dotnet pack blocks) ───

    public static IReadOnlyList<string> PackArgs(
        string projectRelPath,
        string version,
        string artifactsDir,
        string configuration = "Release",
        string? repoUrl = null,
        string? repoBranch = null,
        string? repoCommit = null)
    {
        var args = new List<string>
        {
            "pack", projectRelPath,
            "--configuration", configuration,
            // No --no-build: ContinuousIntegrationBuild/Deterministic/Version only take
            // effect at compile time. Packing a --no-build output produced assemblies
            // stamped 1.0.0.0 with none of the determinism flags applied.
            "-o", artifactsDir,
            "-p:ContinuousIntegrationBuild=true",
            "-p:Deterministic=true",
            "-p:IncludeSymbols=true",
            "-p:SymbolPackageFormat=snupkg",
            "-p:DebugType=portable",
            $"-p:PackageVersion={version}",
            $"-p:Version={version}",
        };

        if (!string.IsNullOrEmpty(repoUrl))
        {
            args.Add("-p:PublishRepositoryUrl=true");
            args.Add($"-p:RepositoryUrl={repoUrl}");
        }

        if (!string.IsNullOrEmpty(repoBranch))
            args.Add($"-p:RepositoryBranch={repoBranch}");

        if (!string.IsNullOrEmpty(repoCommit))
        {
            args.Add($"-p:RepositoryCommit={repoCommit}");
            args.Add("-p:EmbedUntrackedSources=true");
        }

        return args;
    }

    // ── package.json version patching ──────────────────────────────────

    /// <param name="pinDependency">
    /// When set, the named entry under <c>dependencies</c> is pinned to the same version. The
    /// two UPM packages ship as a unit, so the serialization package always depends on the
    /// exact core version released alongside it.
    /// </param>
    public static void PatchPackageJsonVersion(string packageJsonPath, string version,
        string? pinDependency = null)
    {
        if (!File.Exists(packageJsonPath))
            throw new FileNotFoundException($"package.json not found at {packageJsonPath}");

        var json = File.ReadAllText(packageJsonPath);
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })
            ?? throw new InvalidOperationException("Failed to parse package.json");

        node["version"] = version;

        if (pinDependency is not null)
        {
            if (node["dependencies"] is not JsonObject dependencies)
                throw new InvalidOperationException(
                    $"{packageJsonPath} has no 'dependencies' object to pin '{pinDependency}' in.");

            if (!dependencies.ContainsKey(pinDependency))
                throw new InvalidOperationException(
                    $"{packageJsonPath} declares no dependency on '{pinDependency}'.");

            dependencies[pinDependency] = version;
            Console.WriteLine($"Pinned dependency {pinDependency} to {version}");
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(packageJsonPath, node.ToJsonString(options) + Environment.NewLine);

        Console.WriteLine($"Updated {Path.GetFileName(Path.GetDirectoryName(packageJsonPath))} version to {version}");
    }

    // ── Tarball creation ───────────────────────────────────────────────

    public static string CreateTarball(string repoRoot, string version)
    {
        var upmRelDir = OptionalEnv("UPM_PACKAGE_DIR") ?? Path.Combine("upm", CorePackageDir);
        return CreateTarball(repoRoot, version, Path.Combine(repoRoot, upmRelDir), CorePackageDir);
    }

    /// <summary>Tarballs one package directory as <c>{packageName}-{version}.tgz</c>.</summary>
    public static string CreateTarball(string repoRoot, string version, string upmAbsDir, string packageName)
    {
        var tarballName = $"{packageName}-{version}.tgz";
        var tarballPath = Path.Combine(repoRoot, tarballName);

        if (!Directory.Exists(upmAbsDir))
            throw new DirectoryNotFoundException($"UPM package directory not found: {upmAbsDir}");

        // We need to create a .tar.gz with entries rooted at "{packageName}/"
        using var fileStream = File.Create(tarballPath);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
        TarFile.CreateFromDirectory(
            sourceDirectoryName: upmAbsDir,
            destination: gzipStream,
            includeBaseDirectory: true);

        var info = new FileInfo(tarballPath);
        Console.WriteLine($"Created {tarballName} ({info.Length:N0} bytes)");

        return tarballPath;
    }

    // ── File / directory helpers ───────────────────────────────────────

    public static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }

    public static void ClearStagedSource(string repoRoot)
    {
        var dir = StagedSourceRoot(repoRoot);
        if (!Directory.Exists(dir)) return;

        foreach (var entry in Directory.GetFileSystemEntries(dir))
        {
            if (File.Exists(entry))
                File.Delete(entry);
            else if (Directory.Exists(entry))
                Directory.Delete(entry, recursive: true);
        }
    }

    public static void ClearStagedPlugins(string repoRoot) => ClearPlugins(PluginsRoot(repoRoot));

    /// <summary>
    /// Removes staged binaries from a Plugins folder while preserving the tracked sidecars.
    /// <para>
    /// The <c>.meta</c> files must survive: they carry the plugin GUIDs Unity uses as reference
    /// identity, they are committed (the binaries themselves are gitignored and staged on
    /// demand), and regenerating them would hand every consumer project a new GUID for the same
    /// assembly. They double as the reviewed allowlist of what may be staged — see
    /// <see cref="StagedPluginNames"/>.
    /// </para>
    /// </summary>
    public static void ClearPlugins(string pluginsDir)
    {
        Directory.CreateDirectory(pluginsDir);

        foreach (var file in Directory.GetFiles(pluginsDir))
        {
            var name = Path.GetFileName(file);
            if (name == ".gitkeep" || name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            File.Delete(file);
        }
    }

    /// <summary>
    /// The file names a Plugins folder is allowed to contain, derived from the committed
    /// <c>.meta</c> sidecars. Staging compares against this so that a new transitive dependency
    /// can neither be silently bundled (unreviewed binary in a shipped package) nor silently
    /// dropped (a TypeLoadException at the consumer) — it fails the build until someone adds
    /// the matching <c>.meta</c>.
    /// </summary>
    public static HashSet<string> StagedPluginNames(string pluginsDir)
    {
        Directory.CreateDirectory(pluginsDir);

        return Directory.GetFiles(pluginsDir, "*.meta")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // ── Target resolution (tag-based) ──────────────────────────────────

    public static (string target, string version) ResolvePublishTarget()
    {
        var target = OptionalEnv("TARGET");
        var version = OptionalEnv("VERSION");

        // If running from a tag in CI, parse it
        if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(version))
        {
            var refType = OptionalEnv("GITHUB_REF_TYPE");
            var refName = OptionalEnv("GITHUB_REF_NAME");

            if (refType == "tag" && !string.IsNullOrEmpty(refName))
            {
                (target, version) = refName switch
                {
                    _ when refName.StartsWith("serialization-abstraction/v", StringComparison.Ordinal) =>
                        ("serialization-abstraction", refName["serialization-abstraction/v".Length..]),
                    _ when refName.StartsWith("serialization/v", StringComparison.Ordinal) =>
                        ("serialization", refName["serialization/v".Length..]),
                    _ when refName.StartsWith("nxgraph/v", StringComparison.Ordinal) =>
                        ("nxgraph", refName["nxgraph/v".Length..]),
                    _ when refName.StartsWith('v') =>
                        ("all", refName[1..]),
                    _ => throw new InvalidOperationException($"Unsupported tag format '{refName}'.")
                };
            }
        }

        if (string.IsNullOrEmpty(target))
            throw new InvalidOperationException("TARGET is not set. Set TARGET env var or push a recognized tag.");

        version = ValidateSemVer(version);

        Console.WriteLine($"Resolved: target={target}, version={version}");
        return (target, version);
    }

    public static (string version, string mode) ResolveUpmMeta()
    {
        var version = OptionalEnv("VERSION");
        var mode = OptionalEnv("UPM_MODE") ?? "binary";

        if (string.IsNullOrEmpty(version))
        {
            var refType = OptionalEnv("GITHUB_REF_TYPE");
            var refName = OptionalEnv("GITHUB_REF_NAME");

            if (refType == "tag" && refName is not null && refName.StartsWith("upm/v", StringComparison.Ordinal))
                version = refName["upm/v".Length..];
        }

        version = ValidateSemVer(version);

        Console.WriteLine($"Resolved: version={version}, mode={mode}");
        return (version, mode);
    }
}

