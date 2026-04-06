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

        var dotnet = DotNetLocator.Locate(preferMajor: 8);

        // Preflight: print which dotnet is being used and basic SDK info.
        // This helps diagnose Windows-specific failures where a process fails to start
        // (e.g. bad PATH, broken .NET install, antivirus interference) and manifests as
        // a negative exit code inside SimpleExec.
        Target("info", async () =>
        {
            Console.WriteLine($"NxGraph.Build: Working directory: {repoRoot}");
            try
            {
                Console.WriteLine($"NxGraph.Build: Using dotnet: {dotnet.ExecutablePath}");
                if (!string.IsNullOrWhiteSpace(dotnet.Why))
                    Console.WriteLine($"NxGraph.Build: dotnet selection: {dotnet.Why}");
                if (dotnet.Candidates.Count > 0)
                {
                    Console.WriteLine("NxGraph.Build: dotnet candidates:");
                    foreach (var c in dotnet.Candidates.Distinct(StringComparer.OrdinalIgnoreCase))
                        Console.WriteLine($"  - {c}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"NxGraph.Build: Failed to resolve dotnet on PATH: {e.Message}");
            }

            // Don't fail the build if --info fails; it's diagnostic only.
            try
            {
                await RunAsync(dotnet.ExecutablePath, "--info", workingDirectory: repoRoot);
            }
            catch (Exception e)
            {
                Console.WriteLine($"NxGraph.Build: dotnet --info failed: {e.Message}");
            }
        });

        // ── Core build targets ──────────────────────────────────────

        Target("clean", () =>
        {
            CleanStaged(repoRoot);
        });

        Target("restore", async () =>
        {
            await RunDotNet(repoRoot, "restore");
        });

        Target("build", DependsOn("restore"), async () =>
        {
            var config = OptionalEnv("CONFIGURATION") ?? "Release";
            await RunDotNet(repoRoot, $"build --no-restore --configuration {config}");
        });

        Target("test", DependsOn("build"), async () =>
        {
            var config = OptionalEnv("CONFIGURATION") ?? "Release";
            var threshold = OptionalEnv("COVERAGE_THRESHOLD") ?? "0";
            await RunDotNet(repoRoot,
                $"test --no-build --configuration {config} --verbosity normal " +
                $"--collect:\"XPlat Code Coverage\" " +
                $"/p:CollectCoverage=true /p:CoverletOutputFormat=lcov /p:Threshold={threshold}");
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
                await RunDotNet(repoRoot, string.Join(" ", packArgs));
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
            await RunDotNet(repoRoot,
                $"nuget push \"{Path.Combine(artifactsDir, "*.nupkg")}\" " +
                $"--api-key \"{apiKey}\" --source \"{source}\" --skip-duplicate");

            var snupkgs = Directory.GetFiles(artifactsDir, "*.snupkg");
            if (snupkgs.Length > 0)
            {
                Console.WriteLine("\n→ Pushing .snupkg...");
                await RunDotNet(repoRoot,
                    $"nuget push \"{Path.Combine(artifactsDir, "*.snupkg")}\" " +
                    $"--api-key \"{apiKey}\" --source \"{source}\" --skip-duplicate");
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

    private static async Task RunDotNet(string workingDirectory, string args)
    {
        try
        {
            var dotnet = DotNetLocator.Locate(preferMajor: 8);
            await RunAsync(dotnet.ExecutablePath, args, workingDirectory: workingDirectory);
        }
        catch (SimpleExec.ExitCodeException e)
        {
            // SimpleExec sometimes surfaces Windows process start failures or hard crashes as
            // unusual negative exit codes. Provide actionable hints.
            var dotnetPath = SafeResolveExecutablePath("dotnet");
            throw new InvalidOperationException(
                $"dotnet {args} failed with exit code {e.ExitCode}. " +
                $"WorkingDirectory='{workingDirectory}'. dotnet='{dotnetPath ?? "<not found>"}'. " +
                "Try running the same command manually with higher verbosity: 'dotnet " + args + " -v diag'. " +
                "On Windows, negative exit codes often indicate the process failed to start (broken install/PATH) or was terminated by policy (AV/AppLocker).",
                e);
        }
    }

    private static class DotNetLocator
    {
        internal sealed record Result(string ExecutablePath, string? Why, List<string> Candidates);

        public static Result Locate(int preferMajor)
        {
            var candidates = new List<string>();

            void AddCandidate(string? path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                candidates.Add(path);
            }

            // 1) DOTNET_ROOT (and DOTNET_ROOT(x86)) are the most explicit ways to point to an install.
            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrWhiteSpace(dotnetRoot))
                AddCandidate(Path.Combine(dotnetRoot, "dotnet.exe"));

            var dotnetRootX86 = Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)");
            if (!string.IsNullOrWhiteSpace(dotnetRootX86))
                AddCandidate(Path.Combine(dotnetRootX86, "dotnet.exe"));

            // 2) PATH resolution.
            AddCandidate(ResolveExecutablePath("dotnet"));

            // 3) Default install locations on Windows.
            if (OperatingSystem.IsWindows())
            {
                AddCandidate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"));
                AddCandidate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "dotnet.exe"));
                AddCandidate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet.exe"));

                // 4) Registry install location (best-effort).
                try
                {
                    using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
                    using var key = baseKey.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64");
                    var installLocation = key?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(installLocation))
                        AddCandidate(Path.Combine(installLocation, "dotnet.exe"));
                }
                catch
                {
                    // ignore
                }
            }

            // De-dup + keep only existing files.
            var existing = candidates
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .ToList();

            // Prefer one that has SDK folder with the requested major version.
            foreach (var dotnetExe in existing)
            {
                var root = Path.GetDirectoryName(dotnetExe);
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                var sdkDir = Path.Combine(root, "sdk");
                if (!Directory.Exists(sdkDir))
                    continue;

                try
                {
                    var hasPreferredMajor = Directory.GetDirectories(sdkDir)
                        .Select(Path.GetFileName)
                        .OfType<string>()
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Any(n => n.StartsWith(preferMajor + ".", StringComparison.OrdinalIgnoreCase));

                    if (hasPreferredMajor)
                        return new Result(dotnetExe, $"Found SDK {preferMajor}.x under '{sdkDir}'", existing);
                }
                catch
                {
                    Console.WriteLine($"Warning: Failed to inspect SDK directory '{sdkDir}' for dotnet at '{dotnetExe}'. Skipping SDK version check for this candidate.");
                }
            }

            // Otherwise just use the first existing candidate.
            if (existing.Count > 0)
                return new Result(existing[0], "Fell back to first discovered dotnet.exe", existing);

            // Last resort: let CreateProcess resolve it (will likely fail, but with clearer message from our wrapper).
            return new Result("dotnet", "No dotnet.exe found in common locations; falling back to PATH resolution", candidates);
        }
    }

    private static string? SafeResolveExecutablePath(string name)
    {
        try { return ResolveExecutablePath(name); }
        catch { return null; }
    }

    private static string? ResolveExecutablePath(string name)
    {
        // Cross-platform-ish resolution for the most common case: look on PATH.
        // We intentionally keep this lightweight and purely diagnostic.
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            candidates.Add(name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe");
            candidates.Add(name);
        }
        else
        {
            candidates.Add(name);
        }

        foreach (var dir in paths)
        {
            foreach (var file in candidates)
            {
                var full = Path.Combine(dir, file);
                if (File.Exists(full))
                    return full;
            }
        }

        return null;
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

        await RunDotNet(repoRoot, $"build \"{projectPath}\" -c Release -f netstandard2.1");

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
