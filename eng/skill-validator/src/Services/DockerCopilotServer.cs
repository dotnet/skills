using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using GitHub.Copilot.SDK;
using SkillValidator.Models;

namespace SkillValidator.Services;

public partial class DockerCopilotServer
{
    record ContainerState(int HostPort);

    public static DockerCopilotServer? Instance { get; private set; }

    public static void Initialize(bool verbose, IReadOnlyList<SkillInfo> skills)
    {
        Instance = Create(verbose, skills);
    }

    internal static DockerCopilotServer Create(bool verbose, IReadOnlyList<SkillInfo> skills)
    {
        return new DockerCopilotServer(verbose, BuildSkillMounts(skills));
    }

    private const int InternalPort = 4321;
    private const string ImageBaseName = "skill-validator-base";

    private readonly string _invocationId = Guid.NewGuid().ToString("N")[..8];
    private readonly bool _verbose;
    private readonly Lazy<Task<ContainerState>> _lazyStartTask;

    /// <summary>Host skill directory → container mount point (e.g. "/skills/dotnet").</summary>
    private readonly Dictionary<string, string> _skillMounts;

    private ContainerState? _containerState;
    private EventHandler? _processExitHandler;
    private ConsoleCancelEventHandler? _cancelKeyPressHandler;

    private DockerCopilotServer(bool verbose, Dictionary<string, string> skillMounts)
    {
        _verbose = verbose;
        _skillMounts = skillMounts;
        _lazyStartTask = new Lazy<Task<ContainerState>>(() => StartAsync());
    }

    internal static Dictionary<string, string> BuildSkillMounts(IReadOnlyList<SkillInfo> skills)
    {
        var mounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Mount the grandparent directory of each SKILL.md (i.e. the parent of skill.Path)
        foreach (var skill in skills)
        {
            var fullPath = Path.GetFullPath(Path.GetDirectoryName(skill.Path)!);
            if (mounts.ContainsKey(fullPath))
                continue;
            var name = Path.GetFileName(fullPath);
            if (usedNames.TryGetValue(name, out var count))
            {
                usedNames[name] = count + 1;
                name = $"{name}-{count}";
            }
            else
            {
                usedNames[name] = 1;
            }
            mounts[fullPath] = $"/skills/{name}";
        }
        return mounts;
    }

    public string GetHostDir() => Path.Combine(Path.GetTempPath(), $"sv-container-{_invocationId}");

    private string GetContainerName() => $"skill-validator-{_invocationId}";

    public async Task<string> GetCliUrlAsync(CancellationToken ct = default)
    {
        var state = await GetOrStartContainerAsync(ct).ConfigureAwait(false);
        return $"localhost:{state.HostPort}";
    }

    private void RegisterProcessExitHandler()
    {
        if (_processExitHandler is not null || _cancelKeyPressHandler is not null)
            return;

        _processExitHandler = (_, _) =>
        {
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                if (_verbose)
                    Console.Error.WriteLine($"🐳 Failed to stop container on process exit: {ex.Message}");
            }
        };
        _cancelKeyPressHandler = (_, _) =>
        {
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                if (_verbose)
                    Console.Error.WriteLine($"🐳 Failed to stop container on Ctrl+C: {ex.Message}");
            }
        };

        AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
        Console.CancelKeyPress += _cancelKeyPressHandler;
    }

    private void UnregisterProcessExitHandler()
    {
        if (_processExitHandler is null && _cancelKeyPressHandler is null)
            return;

        if (_processExitHandler is not null)
            AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
        if (_cancelKeyPressHandler is not null)
            Console.CancelKeyPress -= _cancelKeyPressHandler;
        _processExitHandler = null;
        _cancelKeyPressHandler = null;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        var containerName = GetContainerName();
        try
        {
            if (_containerState is null)
                return;

            try
            {
                await RunDockerCommandAsync(["stop", containerName], ct);
            }
            catch { /* container may already be stopped */ }

            try
            {
                await RunDockerCommandAsync(["rm", containerName], ct);
            }
            catch { /* container may already be removed */ }

            _containerState = null;

            if (_verbose)
                Console.Error.WriteLine($"🐳 Container {containerName} stopped and removed.");
        }
        finally
        {
            UnregisterProcessExitHandler();
        }
    }

    public string MapHostPathToContainer(string hostPath)
    {
        var fullPath = Path.GetFullPath(hostPath);

        // Check work dir mount
        if (TryMapToContainerMount(fullPath, GetHostDir(), "/work", out var workResult))
            return workResult;

        // Check skill dir mounts
        foreach (var (hostSkillDir, containerMount) in _skillMounts)
        {
            if (TryMapToContainerMount(fullPath, hostSkillDir, containerMount, out var skillResult))
                return skillResult;
        }

        throw new ArgumentException($"Host path is not mapped into the container: {hostPath}");
    }

    public bool TryMapContainerPathToHost(string containerPath, [NotNullWhen(true)] out string? hostPath)
    {
        if (containerPath.StartsWith("/work/") || containerPath == "/work")
        {
            var relativePath = containerPath == "/work" ? "." : containerPath["/work/".Length..];
            hostPath = Path.GetFullPath(Path.Combine(GetHostDir(), relativePath));
            return true;
        }

        foreach (var (hostSkillDir, containerMount) in _skillMounts)
        {
            var prefix = containerMount + "/";
            if (containerPath.StartsWith(prefix) || containerPath == containerMount)
            {
                var relativePath = containerPath == containerMount ? "." : containerPath[prefix.Length..];
                hostPath = Path.GetFullPath(Path.Combine(hostSkillDir, relativePath));
                return true;
            }
        }

        hostPath = null;
        return false;
    }

    public async Task ExecAsync(string workDir, string command, CancellationToken ct = default)
    {
        _ = await GetOrStartContainerAsync(ct);

        await RunDockerCommandAsync(["exec", GetContainerName(), "--workdir", workDir, "/bin/sh", "-c", command], ct);
    }

    private async Task<ContainerState> GetOrStartContainerAsync(CancellationToken ct = default)
    {
        if (_containerState is not null)
            return _containerState;

        _containerState = await _lazyStartTask.Value.WaitAsync(ct).ConfigureAwait(false);
        return _containerState;
    }

    private async Task<ContainerState> StartAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN")))
            throw new InvalidOperationException("GITHUB_TOKEN environment variable is required when running in Docker. You can get it with 'gh auth token'.");

        if (_verbose)
            Console.Error.WriteLine("🐳 Building Docker image ...");

        var sdkVersion = GetCopilotSdkVersion();
        var imageName = $"{ImageBaseName}:{sdkVersion}";
        var dockerFilePath = Path.Combine(AppContext.BaseDirectory, "Docker", "Dockerfile");

        await RunDockerCommandAsync(
            ["build", "-t", imageName, "--build-arg", $"COPILOT_SDK_VERSION={sdkVersion}", "-f", dockerFilePath, Path.GetDirectoryName(dockerFilePath)!], ct);

        if (_verbose)
            Console.Error.WriteLine("🐳 Docker image built successfully.");

        var containerName = GetContainerName();

        if (_verbose)
            Console.Error.WriteLine($"🐳 Starting container {containerName}...");

        var hostDir = GetHostDir();
        Directory.CreateDirectory(hostDir);

        var runArgs = new List<string>
        {
            "run",
            "--name", containerName,
            "-p", $"0:{InternalPort}", // Map internal port to a random host port
            "-e", "GITHUB_TOKEN",
            "-v", $"{hostDir}:/work", // Mount host dir to /work in container
        };

        // Mount skill directories as read-only volumes
        foreach (var (hostSkillDir, containerMount) in _skillMounts)
            runArgs.AddRange(["-v", $"{hostSkillDir}:{containerMount}:ro"]);

        runArgs.AddRange([
            imageName,
            // Start the Copilot server in headless mode, listening on the internal port, and using the GITHUB_TOKEN from env
            "copilot", 
            "--headless", 
            "--port", InternalPort.ToString(), 
            "--auth-token-env", "GITHUB_TOKEN",
            "--no-auto-login",
            "--no-auto-update",
            "--log-level", (_verbose ? "info" : "none")
        ]);

        using var process = StartNonDetached(runArgs);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cts.Token);
                if (line is null)
                {
                    var stderr = await process.StandardError.ReadToEndAsync(ct);
                    throw new InvalidOperationException(
                        $"Container {containerName} exited before becoming ready. stderr: {stderr}");
                }

                var match = ListeningPattern().Match(line);
                if (match.Success)
                    break;
            }

            if (cts.Token.IsCancellationRequested)
                throw new TimeoutException($"Container {containerName} did not become ready within 30s.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync();
            }
        }

        var output = await RunDockerCommandAsync(["port", GetContainerName(), InternalPort.ToString()], ct);
        var portMatch = PortPattern().Match(output);
        if (!portMatch.Success)
            throw new InvalidOperationException($"Could not parse port mapping from: {output}");

        var port = int.Parse(portMatch.Groups[1].Value);

        if (_verbose)
            Console.Error.WriteLine($"🐳 Container {containerName} ready (port {port})");

        RegisterProcessExitHandler();

        return new ContainerState(port);
    }

    private static async Task<string> RunDockerCommandAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        using var proc = StartNonDetached(args);

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (proc.ExitCode != 0)
        {
            var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"docker {args[0]} failed (exit {proc.ExitCode}): {output.Trim()}");
        }

        return stdout.Trim();
    }

    private static Process StartNonDetached(IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo("docker", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        return Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start docker run process");
    }

    private static bool TryMapToContainerMount(
        string fullPath, 
        string hostDir, 
        string containerMount,
        [NotNullWhen(true)] out string? containerPath)
    {
        var relativePath = Path.GetRelativePath(hostDir, fullPath);
        if (!Path.IsPathRooted(relativePath) &&
            !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            relativePath != "..")
        {
            containerPath = Path.Combine(containerMount, relativePath).Replace("\\", "/");
            return true;
        }

        containerPath = null;
        return false;
    }

    internal static string GetCopilotSdkVersion()
    {
        var attr = typeof(CopilotClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var version = attr?.InformationalVersion
            ?? throw new InvalidOperationException("Could not determine GitHub.Copilot.SDK version from assembly.");
        // Strip the commit hash suffix (e.g. "0.1.26+abc123" → "0.1.26")
        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }

    [GeneratedRegex(@"listening on port (\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ListeningPattern();

    [GeneratedRegex(@":(\d+)$", RegexOptions.Multiline)]
    private static partial Regex PortPattern();
}
