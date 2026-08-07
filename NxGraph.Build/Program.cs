using System.Text;
using static Bullseye.Targets;
using static SimpleExec.Command;
using static NxGraph.Build.BuildHelpers;

namespace NxGraph.Build;

public static class Program
{
    // Resolved once in Main and reused by every RunDotNet invocation — DotNetLocator.Locate
    // probes the filesystem/registry, which is wasteful per call and could even pick a
    // different SDK mid-run if the environment changes.
    private static DotNetLocator.Result? _dotnet;

    // Source staging is an explicit allowlist: a folder that is not named here is silently
    // absent from the staged package, and source mode then fails to compile at the consumer.
    // Keep it in step with the core project's folders — every folder under NxGraph/ that holds
    // compiled code belongs here (Docs holds markdown only).
    private static readonly string[] DirectoriesToCopy =
    [
        Path.Combine("Authoring"),
        Path.Combine("Behaviors"),
        Path.Combine("Blackboards"),
        Path.Combine("Compatibility"),
        Path.Combine("Conditions"),
        Path.Combine("Diagnostics", "Export"),
        Path.Combine("Diagnostics", "Replay"),
        Path.Combine("Diagnostics", "Validations"),
        Path.Combine("Fsm"),
        Path.Combine("Graphs"),
        Path.Combine("Shims"),
        Path.Combine("Tokens"),
    ];

    private static readonly string[] FilesToCopy =
    [
        "Result.cs",
        "ResultHelpers.cs",
        // The sync/async report bridge State.Log and the behavior composites both call.
        "ValueTaskSync.cs",
    ];

    private static readonly string[] ExcludedFiles =
    [
        Path.Combine("Fsm", "TracingObserver.cs"),
    ];

    // Assemblies the core package owns. They are present in the serialization project's
    // netstandard2.1 output too (it references them), and staging them into both packages
    // would give Unity two copies of the same types.
    private static readonly string[] CoreAssemblies =
    [
        "NxGraph",
        "NxGraph.Serialization.Abstraction",
    ];

    // Sidecars worth shipping next to a staged assembly, in staging order.
    private static readonly string[] AssemblySidecars = [".dll", ".pdb", ".xml"];

    public static async Task Main(string[] args)
    {
        var repoRoot = FindRepoRoot();

        var dotnet = DotNetLocator.Locate(preferMajor: 8);
        _dotnet = dotnet;

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
            // Line-coverage floor per instrumented module (each test project scopes its own
            // Include filter in its csproj). Raise this as new features land with tests;
            // never lower it without a recorded decision.
            //
            // Coverage runs on the coverlet.msbuild driver alone (/p:CollectCoverage +
            // /p:Threshold is the gate; lcov coverage.info is the report). The VSTest
            // collector (--collect:"XPlat Code Coverage") was removed deliberately: running
            // both drivers at once is unsupported by coverlet and obscured which one gated.
            // See NxGraph.Build/README.md § Coverage.
            var threshold = OptionalEnv("COVERAGE_THRESHOLD") ?? "70";
            await RunDotNet(repoRoot,
                $"test --no-build --configuration {config} --verbosity normal " +
                $"/p:CollectCoverage=true /p:CoverletOutputFormat=lcov /p:Threshold={threshold} " +
                $"/p:ThresholdType=line /p:ThresholdStat=minimum");
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

            // The API key must ride the command line: `dotnet nuget push` reads it from no
            // environment variable, and a NuGet.Config `apikeys` entry would persist the
            // secret to disk — worse. So the push invocations run with echo suppressed and a
            // redacted command line is printed instead; GitHub CI additionally masks the
            // secret, but local `publish` runs used to print it in cleartext.
            string PushArgs(string pattern, string key) =>
                $"nuget push \"{Path.Combine(artifactsDir, pattern)}\" " +
                $"--api-key \"{key}\" --source \"{source}\" --skip-duplicate";

            Console.WriteLine("\n→ Pushing .nupkg...");
            await RunDotNet(repoRoot, PushArgs("*.nupkg", apiKey),
                redactedArgs: PushArgs("*.nupkg", "***"));

            var snupkgs = Directory.GetFiles(artifactsDir, "*.snupkg");
            if (snupkgs.Length > 0)
            {
                Console.WriteLine("\n→ Pushing .snupkg...");
                await RunDotNet(repoRoot, PushArgs("*.snupkg", apiKey),
                    redactedArgs: PushArgs("*.snupkg", "***"));
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

            PatchPackageJsonVersion(PackageJsonPath(repoRoot), version);

            // The serialization package rides the same version and pins the core it was built
            // against — the two are staged from one build and are not independently versioned.
            PatchPackageJsonVersion(SerializationPackageJsonPath(repoRoot), version,
                pinDependency: CorePackageDir);
        });

        Target("upm-tarball", DependsOn("upm-patch-version"), () =>
        {
            var version = OptionalEnv("VERSION");
            version = ValidateSemVer(version);

            CreateTarball(repoRoot, version);

            // Source mode leaves the serialization package unstaged (see StageSource); there is
            // nothing to ship, so no second tarball is produced.
            if (Directory.GetFiles(SerializationPluginsRoot(repoRoot), "*.dll").Length > 0)
            {
                CreateTarball(repoRoot, version, SerializationPackageRoot(repoRoot), SerializationPackageDir);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(
                    $"Skipping {SerializationPackageDir} tarball: no staged assemblies " +
                    "(expected when staging in source mode).");
                Console.ResetColor();
            }
        });

        await RunTargetsAndExitAsync(args);
    }

    /// <param name="workingDirectory">Directory to run in.</param>
    /// <param name="args">The real argument string handed to dotnet.</param>
    /// <param name="redactedArgs">When set, <paramref name="args"/> contains a secret: echo is
    /// suppressed and this redacted rendering is printed (and used in failure messages)
    /// instead, so the secret never reaches the console or an exception text.</param>
    private static async Task RunDotNet(string workingDirectory, string args, string? redactedArgs = null)
    {
        var printableArgs = redactedArgs ?? args;
        try
        {
            var dotnet = _dotnet ??= DotNetLocator.Locate(preferMajor: 8);
            if (redactedArgs is not null)
            {
                Console.WriteLine($"{dotnet.ExecutablePath} {redactedArgs}");
            }

            await RunAsync(dotnet.ExecutablePath, args, workingDirectory: workingDirectory,
                noEcho: redactedArgs is not null);
        }
        catch (SimpleExec.ExitCodeException e)
        {
            // SimpleExec sometimes surfaces Windows process start failures or hard crashes as
            // unusual negative exit codes. Provide actionable hints.
            var dotnetPath = SafeResolveExecutablePath("dotnet");
            throw new InvalidOperationException(
                $"dotnet {printableArgs} failed with exit code {e.ExitCode}. " +
                $"WorkingDirectory='{workingDirectory}'. dotnet='{dotnetPath ?? "<not found>"}'. " +
                "Try running the same command manually with higher verbosity: 'dotnet " + printableArgs + " -v diag'. " +
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

        // Source mode compiles the core into the package's own asmdef, so the resulting Unity
        // assembly is named NxGraph.Unity.Runtime — not NxGraph. A prebuilt
        // NxGraph.Serialization.dll carries an assembly reference to "NxGraph" and cannot bind
        // to it, so the serialization package is only ever staged in binary mode. Clearing it
        // here keeps the two package layouts consistent with each other.
        ClearPlugins(SerializationPluginsRoot(repoRoot));

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
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(
            $"Note: {SerializationPackageDir} is not staged in source mode — a prebuilt " +
            "NxGraph.Serialization.dll cannot bind to a source-compiled core. Use stage-binary " +
            "to produce both packages.");
        Console.ResetColor();

        foreach (var file in Directory.GetFiles(stagedRoot, "*", SearchOption.AllDirectories))
        {
            Console.WriteLine($"  {Path.GetRelativePath(repoRoot, file)}");
        }
    }

    private static async Task StageBinary(string repoRoot)
    {
        // Building the serialization project also builds the core it references, and its
        // netstandard2.1 output carries the full dependency closure
        // (CopyLocalLockFileAssemblies) that the serialization package bundles.
        var serializationProject = Path.Combine(repoRoot, "NxGraph.Serialization", "NxGraph.Serialization.csproj");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Building netstandard2.1 binaries for Unity package staging...");
        Console.ResetColor();
        Console.WriteLine($"Project:  {serializationProject}");
        Console.WriteLine($"Packages: {PackageRoot(repoRoot)}");
        Console.WriteLine($"          {SerializationPackageRoot(repoRoot)}");

        if (!File.Exists(serializationProject))
            throw new InvalidOperationException($"Project not found: {serializationProject}");

        ClearStagedSource(repoRoot);
        ClearStagedPlugins(repoRoot);
        ClearPlugins(SerializationPluginsRoot(repoRoot));

        await RunDotNet(repoRoot, $"build \"{serializationProject}\" -c Release -f netstandard2.1");

        var serializationBuildDir = SerializationBuildOutput(repoRoot);

        // The core package takes NxGraph plus the dependency-free serialization abstraction;
        // both come out of the same build directory.
        StagePlugins(repoRoot, serializationBuildDir, PluginsRoot(repoRoot), CorePackageDir,
            name => CoreAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase));

        // The serialization package takes everything else: the serializer itself and its
        // bundled third-party dependencies.
        StagePlugins(repoRoot, serializationBuildDir, SerializationPluginsRoot(repoRoot),
            SerializationPackageDir,
            name => !CoreAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase));

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Binaries staged successfully.");
        Console.ResetColor();
    }

    /// <summary>
    /// Copies the assemblies selected by <paramref name="include"/> from a build directory into
    /// a package's Plugins folder, and reconciles the result against the committed
    /// <c>.meta</c> sidecars: an assembly with no sidecar, or a sidecar with no assembly, fails
    /// the target. That keeps a shipped package's binary contents a reviewed, tracked decision
    /// rather than whatever NuGet happened to resolve.
    /// </summary>
    private static void StagePlugins(string repoRoot, string buildDir, string pluginsDir,
        string packageName, Func<string, bool> include)
    {
        if (!Directory.Exists(buildDir))
            throw new InvalidOperationException($"Build output not found: {buildDir}");

        var expected = StagedPluginNames(pluginsDir);
        var staged = new List<string>();

        foreach (var dll in Directory.GetFiles(buildDir, "*.dll").Order(StringComparer.Ordinal))
        {
            var assemblyName = Path.GetFileNameWithoutExtension(dll);
            if (!include(assemblyName))
                continue;

            foreach (var extension in AssemblySidecars)
            {
                var source = Path.Combine(buildDir, assemblyName + extension);
                if (!File.Exists(source))
                    continue;

                var fileName = assemblyName + extension;

                // Only files with a committed .meta are staged; the reconciliation below turns
                // anything missing into an actionable error rather than a silent omission.
                if (!expected.Contains(fileName))
                {
                    if (extension == ".dll")
                        staged.Add(fileName);

                    continue;
                }

                File.Copy(source, Path.Combine(pluginsDir, fileName), overwrite: true);
                if (extension == ".dll")
                    staged.Add(fileName);
            }
        }

        var unexpected = staged.Where(f => !expected.Contains(f)).Order(StringComparer.Ordinal).ToList();
        var missing = expected
            .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Where(f => !staged.Contains(f, StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();

        if (unexpected.Count > 0 || missing.Count > 0)
        {
            var message = new StringBuilder();
            message.Append("The staged assemblies for ").Append(packageName)
                .AppendLine(" do not match the package's committed .meta sidecars.");

            if (unexpected.Count > 0)
            {
                message.AppendLine().AppendLine(
                    "Built but not allowed (a dependency was added — review it, then commit a " +
                    ".meta with a fresh GUID for each):");
                foreach (var file in unexpected)
                    message.Append("  + ").AppendLine(file);
            }

            if (missing.Count > 0)
            {
                message.AppendLine().AppendLine(
                    "Allowed but not built (a dependency was dropped — delete the stale .meta):");
                foreach (var file in missing)
                    message.Append("  - ").AppendLine(file);
            }

            message.AppendLine().Append("Plugins folder: ").Append(pluginsDir);
            throw new InvalidOperationException(message.ToString());
        }

        Console.WriteLine();
        Console.WriteLine($"{packageName}:");
        foreach (var file in Directory.GetFiles(pluginsDir).Order(StringComparer.Ordinal))
        {
            if (Path.GetExtension(file).Equals(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            var info = new FileInfo(file);
            Console.WriteLine($"  {info.Name}  ({info.Length:N0} bytes)");
        }
    }

    private static void CleanStaged(string repoRoot)
    {
        var stagedRoot = StagedSourceRoot(repoRoot);

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

        foreach (var pluginsDir in new[] { PluginsRoot(repoRoot), SerializationPluginsRoot(repoRoot) })
        {
            if (Directory.Exists(pluginsDir))
            {
                ClearPlugins(pluginsDir);
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
}
