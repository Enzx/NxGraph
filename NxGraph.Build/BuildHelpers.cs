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

    public static string PackageRoot(string repoRoot) =>
        Path.Combine(repoRoot, "upm", "com.enzx.nxgraph");

    public static string StagedSourceRoot(string repoRoot) =>
        Path.Combine(PackageRoot(repoRoot), "Runtime", "NxGraph");

    public static string PluginsRoot(string repoRoot) =>
        Path.Combine(PackageRoot(repoRoot), "Runtime", "Plugins");

    public static string BuildOutput(string repoRoot) =>
        Path.Combine(repoRoot, "NxGraph", "bin", "Release", "netstandard2.1");

    public static string ArtifactsDir(string repoRoot) =>
        Path.Combine(repoRoot, OptionalEnv("ARTIFACTS_DIR") ?? "artifacts");

    public static string PackageJsonPath(string repoRoot)
    {
        var upmDir = OptionalEnv("UPM_PACKAGE_DIR") ?? Path.Combine("upm", "com.enzx.nxgraph");
        return Path.Combine(repoRoot, upmDir, "package.json");
    }

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
            "--no-build",
            "-o", artifactsDir,
            "-p:ContinuousIntegrationBuild=true",
            "-p:Deterministic=true",
            "-p:IncludeSymbols=true",
            "-p:SymbolPackageFormat=snupkg",
            "-p:DebugType=portable",
            $"-p:PackageVersion={version}",
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

    public static void PatchPackageJsonVersion(string packageJsonPath, string version)
    {
        if (!File.Exists(packageJsonPath))
            throw new FileNotFoundException($"package.json not found at {packageJsonPath}");

        var json = File.ReadAllText(packageJsonPath);
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })
            ?? throw new InvalidOperationException("Failed to parse package.json");

        node["version"] = version;

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(packageJsonPath, node.ToJsonString(options) + Environment.NewLine);

        Console.WriteLine($"Updated package.json version to {version}");
    }

    // ── Tarball creation ───────────────────────────────────────────────

    public static string CreateTarball(string repoRoot, string version)
    {
        var upmRelDir = OptionalEnv("UPM_PACKAGE_DIR") ?? Path.Combine("upm", "com.enzx.nxgraph");
        var upmAbsDir = Path.Combine(repoRoot, upmRelDir);
        var tarballName = $"com.enzx.nxgraph-{version}.tgz";
        var tarballPath = Path.Combine(repoRoot, tarballName);

        if (!Directory.Exists(upmAbsDir))
            throw new DirectoryNotFoundException($"UPM package directory not found: {upmAbsDir}");

        // We need to create a .tar.gz with entries rooted at "com.enzx.nxgraph/"
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

    public static void ClearStagedPlugins(string repoRoot)
    {
        var dir = PluginsRoot(repoRoot);
        Directory.CreateDirectory(dir);

        foreach (var file in Directory.GetFiles(dir))
        {
            if (Path.GetFileName(file) == ".gitkeep") continue;
            File.Delete(file);
        }
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
                    _ when refName.StartsWith("serialization-abstraction/v") =>
                        ("serialization-abstraction", refName["serialization-abstraction/v".Length..]),
                    _ when refName.StartsWith("serialization/v") =>
                        ("serialization", refName["serialization/v".Length..]),
                    _ when refName.StartsWith("nxgraph/v") =>
                        ("nxgraph", refName["nxgraph/v".Length..]),
                    _ when refName.StartsWith("v") =>
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

            if (refType == "tag" && refName is not null && refName.StartsWith("upm/v"))
                version = refName["upm/v".Length..];
        }

        version = ValidateSemVer(version);

        Console.WriteLine($"Resolved: version={version}, mode={mode}");
        return (version, mode);
    }
}

