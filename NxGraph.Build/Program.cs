using static Bullseye.Targets;
using static SimpleExec.Command;
using static NxGraph.Build.BuildHelpers;

namespace NxGraph.Build;

public static class Program
{
    private static readonly string[] DirectoriesToCopy =
    [
        Path.Combine("Authoring"),
        Path.Combine("Compatibility"),
        Path.Combine("Diagnostics", "Export"),
        Path.Combine("Diagnostics", "Replay"),
        Path.Combine("Diagnostics", "Validations"),
        Path.Combine("Fsm"),
        Path.Combine("Graphs"),
        Path.Combine("Shims"),
    ];

    private static readonly string[] FilesToCopy =
    [
        "Result.cs",
        "ResultHelpers.cs",
    ];

    private static readonly string[] ExcludedFiles =
    [
        Path.Combine("Fsm", "TracingObserver.cs"),
    ];

    public static async Task Main(string[] args)
    {
        var repoRoot = FindRepoRoot();

        // ── Core build targets ──────────────────────────────────────

        Target("clean", () =>
        {
            CleanStaged(repoRoot);
        });

        Target("restore", async () =>
        {
            await RunAsync("dotnet", "restore", workingDirectory: repoRoot);
        });

        Target("build", DependsOn("restore"), async () =>
        {
            var config = OptionalEnv("CONFIGURATION") ?? "Release";
            await RunAsync("dotnet", $"build --no-restore --configuration {config}", workingDirectory: repoRoot);
        });

        Target("test", DependsOn("build"), async () =>
        {
            var config = OptionalEnv("CONFIGURATION") ?? "Release";
            var threshold = OptionalEnv("COVERAGE_THRESHOLD") ?? "0";
            await RunAsync("dotnet",
                $"test --no-build --configuration {config} --verbosity normal " +
                $"--collect:\"XPlat Code Coverage\" " +
                $"/p:CollectCoverage=true /p:CoverletOutputFormat=lcov /p:Threshold={threshold}",
                workingDirectory: repoRoot);
        });

        // ci = restore → build → test (via dependency chain)
        Target("ci", DependsOn("test"));

        // ── NuGet pack & push ───────────────────────────────────────

        Target("pack", DependsOn("build"), async () =>
        {
            var (target, version) = ResolvePublishTarget();
            var artifactsDir = ArtifactsDir(repoRoot);
            Directory.CreateDirectory(artifactsDir);

            var repoUrl = OptionalEnv("REPO_URL");
            var repoBranch = OptionalEnv("REPO_BRANCH");
            var repoCommit = OptionalEnv("REPO_COMMIT");

            var packages = new List<(string label, string project)>();

            if (target is "all" or "nxgraph")
                packages.Add(("NxGraph", "NxGraph/NxGraph.csproj"));

            if (target is "all" or "serialization")
                packages.Add(("NxGraph.Serialization", "NxGraph.Serialization/NxGraph.Serialization.csproj"));

            if (target is "all" or "serialization-abstraction")
                packages.Add(("NxGraph.Serialization.Abstraction",
                    "NxGraph.Serialization.Abstraction/NxGraph.Serialization.Abstraction.csproj"));

            if (packages.Count == 0)
                throw new InvalidOperationException($"No packages matched target '{target}'.");

            foreach (var (label, project) in packages)
            {
                Console.WriteLine($"\n→ Packing {label}...");
                var packArgs = PackArgs(project, version, artifactsDir,
                    repoUrl: repoUrl, repoBranch: repoBranch, repoCommit: repoCommit);
                await RunAsync("dotnet", string.Join(" ", packArgs), workingDirectory: repoRoot);
            }

            Console.WriteLine($"\nArtifacts in {artifactsDir}:");
            foreach (var file in Directory.GetFiles(artifactsDir))
                Console.WriteLine($"  {Path.GetFileName(file)}");
        });

        Target("push", DependsOn("pack"), async () =>
        {
            var artifactsDir = ArtifactsDir(repoRoot);
            var apiKey = Env("NUGET_API_KEY");
            var source = OptionalEnv("NUGET_SOURCE") ?? "https://api.nuget.org/v3/index.json";

            var nupkgs = Directory.GetFiles(artifactsDir, "*.nupkg");
            if (nupkgs.Length == 0)
                throw new InvalidOperationException($"No .nupkg files found in {artifactsDir}");

            Console.WriteLine("\n→ Pushing .nupkg...");
            await RunAsync("dotnet",
                $"nuget push \"{Path.Combine(artifactsDir, "*.nupkg")}\" " +
                $"--api-key \"{apiKey}\" --source \"{source}\" --skip-duplicate",
                workingDirectory: repoRoot);

            var snupkgs = Directory.GetFiles(artifactsDir, "*.snupkg");
            if (snupkgs.Length > 0)
            {
                Console.WriteLine("\n→ Pushing .snupkg...");
                await RunAsync("dotnet",
                    $"nuget push \"{Path.Combine(artifactsDir, "*.snupkg")}\" " +
                    $"--api-key \"{apiKey}\" --source \"{source}\" --skip-duplicate",
                    workingDirectory: repoRoot);
            }
        });

        // publish = ci + pack + push (full pipeline)
        Target("publish", DependsOn("ci", "push"));

        // ── UPM staging ─────────────────────────────────────────────

        Target("stage-source", () =>
        {
            StageSource(repoRoot);
        });

        Target("stage-binary", async () =>
        {
            await StageBinary(repoRoot);
        });

        Target("upm-patch-version", () =>
        {
            var version = OptionalEnv("VERSION");
            version = ValidateSemVer(version);
            var path = PackageJsonPath(repoRoot);
            PatchPackageJsonVersion(path, version);
        });

        Target("upm-tarball", DependsOn("upm-patch-version"), () =>
        {
            var version = OptionalEnv("VERSION");
            version = ValidateSemVer(version);
            CreateTarball(repoRoot, version);
        });

        await RunTargetsAndExitAsync(args);
    }

    // ── UPM staging implementations ────────────────────────────────

    private static void StageSource(string repoRoot)
    {
        var sourceRoot = SourceRoot(repoRoot);
        var stagedRoot = StagedSourceRoot(repoRoot);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Staging NxGraph source for the Unity package...");
        Console.ResetColor();
        Console.WriteLine($"Source:  {sourceRoot}");
        Console.WriteLine($"Package: {PackageRoot(repoRoot)}");

        if (!Directory.Exists(sourceRoot))
            throw new InvalidOperationException($"Source root not found: {sourceRoot}");

        Directory.CreateDirectory(stagedRoot);
        ClearStagedSource(repoRoot);
        ClearStagedPlugins(repoRoot);

        // Copy directories
        foreach (var relDir in DirectoriesToCopy)
        {
            var src = Path.Combine(sourceRoot, relDir);
            var dst = Path.Combine(stagedRoot, relDir);

            if (!Directory.Exists(src))
            {
                Console.WriteLine($"  Skipping (not found): {relDir}");
                continue;
            }

            CopyDirectory(src, dst);
        }

        // Copy individual files
        foreach (var relFile in FilesToCopy)
        {
            var src = Path.Combine(sourceRoot, relFile);
            var dst = Path.Combine(stagedRoot, relFile);

            if (!File.Exists(src))
                throw new InvalidOperationException($"Expected source file not found: {src}");

            File.Copy(src, dst, overwrite: true);
        }

        // Remove excluded files from staged output
        foreach (var excluded in ExcludedFiles)
        {
            var path = Path.Combine(stagedRoot, excluded);
            if (File.Exists(path))
                File.Delete(path);
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Unity package source staged successfully.");
        Console.ResetColor();

        foreach (var file in Directory.GetFiles(stagedRoot, "*", SearchOption.AllDirectories))
        {
            Console.WriteLine($"  {Path.GetRelativePath(repoRoot, file)}");
        }
    }

    private static async Task StageBinary(string repoRoot)
    {
        var projectPath = Path.Combine(repoRoot, "NxGraph", "NxGraph.csproj");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Building NxGraph binary for Unity package staging...");
        Console.ResetColor();
        Console.WriteLine($"Project: {projectPath}");
        Console.WriteLine($"Package: {PackageRoot(repoRoot)}");

        if (!File.Exists(projectPath))
            throw new InvalidOperationException($"Project not found: {projectPath}");

        ClearStagedSource(repoRoot);
        ClearStagedPlugins(repoRoot);

        // Build via SimpleExec
        await RunAsync("dotnet", $"build \"{projectPath}\" -c Release -f netstandard2.1",
            workingDirectory: repoRoot);

        // Copy outputs
        var buildDir = BuildOutput(repoRoot);
        var pluginsDir = PluginsRoot(repoRoot);

        var dllPath = Path.Combine(buildDir, "NxGraph.dll");
        if (!File.Exists(dllPath))
            throw new InvalidOperationException($"Build completed but NxGraph.dll was not found at {dllPath}");

        File.Copy(dllPath, Path.Combine(pluginsDir, "NxGraph.dll"), overwrite: true);

        var pdbPath = Path.Combine(buildDir, "NxGraph.pdb");
        if (File.Exists(pdbPath))
            File.Copy(pdbPath, Path.Combine(pluginsDir, "NxGraph.pdb"), overwrite: true);

        var xmlPath = Path.Combine(buildDir, "NxGraph.xml");
        if (File.Exists(xmlPath))
            File.Copy(xmlPath, Path.Combine(pluginsDir, "NxGraph.xml"), overwrite: true);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Binary staged successfully.");
        Console.ResetColor();

        foreach (var file in Directory.GetFiles(pluginsDir))
        {
            var info = new FileInfo(file);
            Console.WriteLine($"  {info.Name}  ({info.Length:N0} bytes)");
        }
    }

    private static void CleanStaged(string repoRoot)
    {
        var stagedRoot = StagedSourceRoot(repoRoot);
        var pluginsDir = PluginsRoot(repoRoot);

        if (Directory.Exists(stagedRoot))
        {
            ClearStagedSource(repoRoot);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Cleaned staged Unity package sources in {stagedRoot}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Nothing to clean: {stagedRoot} does not exist.");
            Console.ResetColor();
        }

        if (Directory.Exists(pluginsDir))
        {
            ClearStagedPlugins(repoRoot);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Cleaned staged Unity package binaries in {pluginsDir}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Nothing to clean: {pluginsDir} does not exist.");
            Console.ResetColor();
        }
    }
}
