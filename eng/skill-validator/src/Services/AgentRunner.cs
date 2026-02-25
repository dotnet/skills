using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using SkillValidator.Models;
using GitHub.Copilot.SDK;

namespace SkillValidator.Services;

public sealed record RunOptions(
    EvalScenario Scenario,
    SkillInfo? Skill,
    string? EvalPath,
    string Model,
    bool Verbose,
    Action<string>? Log = null);

public static class AgentRunner
{
    private static CopilotClient? _sharedClient;
    private static readonly SemaphoreSlim _clientLock = new(1, 1);
    private static readonly ConcurrentBag<string> _workDirs = [];

    public static async Task<CopilotClient> GetSharedClient(bool verbose)
    {
        if (_sharedClient is not null) return _sharedClient;

        await _clientLock.WaitAsync();
        try
        {
            if (_sharedClient is not null) return _sharedClient;

            var options = new CopilotClientOptions
            {
                LogLevel = verbose ? "info" : "none",
            };

            var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrEmpty(githubToken))
                options.GitHubToken = githubToken;

            _sharedClient = new CopilotClient(options);
            await _sharedClient.StartAsync();
            return _sharedClient;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    public static async Task StopSharedClient()
    {
        if (_sharedClient is not null)
        {
            await _sharedClient.StopAsync();
            _sharedClient = null;
        }
    }

    /// <summary>Remove all temporary working directories created during runs.</summary>
    public static Task CleanupWorkDirs()
    {
        var dirs = _workDirs.ToArray();
        _workDirs.Clear();
        return Task.WhenAll(dirs.Select(dir =>
        {
            try { Directory.Delete(dir, true); } catch { }
            return Task.CompletedTask;
        }));
    }

    public static bool CheckPermission(PermissionRequest request, string workDir, string? skillPath)
    {
        string? reqPath = null;
        if (request.ExtensionData is { } data)
        {
            if (data.TryGetValue("path", out var pathVal) && pathVal is JsonElement pathEl && pathEl.ValueKind == JsonValueKind.String)
                reqPath = pathEl.GetString() ?? "";
            else if (data.TryGetValue("command", out var cmdVal) && cmdVal is JsonElement cmdEl && cmdEl.ValueKind == JsonValueKind.String)
                reqPath = cmdEl.GetString() ?? "";
        }

        if (string.IsNullOrEmpty(reqPath)) return true;

        var resolved = Path.GetFullPath(reqPath);
        var allowedDirs = new List<string> { Path.GetFullPath(workDir) };
        if (skillPath is not null) allowedDirs.Add(Path.GetFullPath(skillPath));

        return allowedDirs.Any(dir =>
            resolved.Equals(dir, StringComparison.OrdinalIgnoreCase) ||
            resolved.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    internal static SessionConfig BuildSessionConfig(
        SkillInfo? skill, string model, string workDir,
        IReadOnlyDictionary<string, MCPServerDef>? mcpServers = null)
    {
        var skillPath = skill is not null ? Path.GetDirectoryName(skill.Path) : null;

        // Create a unique temporary config directory for this session to not share any data
        var configDir = Path.Combine(Path.GetTempPath(), $"skill-validator-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDir);
        _workDirs.Add(configDir);

        // Convert MCPServerDef records to the SDK's Dictionary<string, object> shape
        Dictionary<string, object>? sdkMcp = null;
        if (mcpServers is { Count: > 0 })
        {
            sdkMcp = new Dictionary<string, object>();
            foreach (var (name, def) in mcpServers)
            {
                var entry = new Dictionary<string, object>
                {
                    ["type"] = def.Type ?? "stdio",
                    ["command"] = def.Command,
                    ["args"] = def.Args,
                    ["tools"] = def.Tools ?? ["*"],
                };
                if (def.Env is not null) entry["env"] = def.Env;
                if (def.Cwd is not null) entry["cwd"] = def.Cwd;
                sdkMcp[name] = entry;
            }
        }

        return new SessionConfig
        {
            Model = model,
            Streaming = true,
            WorkingDirectory = workDir,
            SkillDirectories = skill is not null ? [skillPath!] : [],
            ConfigDir = configDir,
            McpServers = sdkMcp,
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            OnPermissionRequest = (request, _) =>
            {
                var result = CheckPermission(request, workDir, skillPath);
                return Task.FromResult(new PermissionRequestResult
                {
                    Kind = result ? "approved" : "denied-by-rules",
                });
            },
        };
    }

    public static async Task<RunMetrics> RunAgent(RunOptions options)
    {
        var workDir = await SetupWorkDir(options.Scenario, options.Skill?.Path, options.EvalPath);
        if (options.Verbose)
        {
            var write = options.Log ?? (msg => Console.Error.WriteLine(msg));
            write($"      📂 Work dir: {workDir} ({(options.Skill is not null ? "skilled" : "baseline")})");
        }

        var events = new List<AgentEvent>();
        string agentOutput = "";
        var startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        try
        {
            var client = await GetSharedClient(options.Verbose);

            await using var session = await client.CreateSessionAsync(
                BuildSessionConfig(options.Skill, options.Model, workDir, options.Skill?.McpServers));

            var done = new TaskCompletionSource();
            var effectiveTimeout = options.Scenario.Timeout;
            using var cts = new CancellationTokenSource(effectiveTimeout * 1000);
            cts.Token.Register(() =>
                done.TrySetException(new TimeoutException($"Scenario timed out after {effectiveTimeout}s")));

            session.On(evt =>
            {
                var agentEvent = new AgentEvent(
                    evt.Type,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    []);

                // Copy known event data
                switch (evt)
                {
                    case AssistantMessageDeltaEvent delta:
                        agentEvent.Data["deltaContent"] = delta.Data.DeltaContent;
                        agentOutput += delta.Data.DeltaContent ?? "";
                        break;
                    case AssistantMessageEvent msg:
                        agentEvent.Data["content"] = msg.Data.Content;
                        if (!string.IsNullOrEmpty(msg.Data.Content))
                            agentOutput = msg.Data.Content;
                        break;
                    case ToolExecutionStartEvent toolStart:
                        agentEvent.Data["toolName"] = toolStart.Data.ToolName;
                        agentEvent.Data["arguments"] = toolStart.Data.Arguments?.ToString();
                        if (options.Verbose)
                        {
                            var write = options.Log ?? (m => Console.Error.WriteLine(m));
                            write($"      🔧 {toolStart.Data.ToolName}");
                        }
                        break;
                    case ToolExecutionCompleteEvent toolComplete:
                        agentEvent.Data["success"] = toolComplete.Data.Success.ToString();
                        agentEvent.Data["result"] = toolComplete.Data.Result?.Content ?? toolComplete.Data.Error?.Message ?? "";
                        break;
                    case SkillInvokedEvent skillInvoked:
                        agentEvent.Data["name"] = skillInvoked.Data.Name;
                        agentEvent.Data["path"] = skillInvoked.Data.Path;
                        if (skillInvoked.Data.AllowedTools is { } allowedTools)
                            agentEvent.Data["allowedTools"] = allowedTools;
                        if (options.Verbose)
                        {
                            var write = options.Log ?? (m => Console.Error.WriteLine(m));
                            write($"      📘 Skill invoked: {skillInvoked.Data.Name}");
                        }
                        break;
                    case AssistantUsageEvent usage:
                        agentEvent.Data["inputTokens"] = usage.Data.InputTokens;
                        agentEvent.Data["outputTokens"] = usage.Data.OutputTokens;
                        agentEvent.Data["model"] = usage.Data.Model;
                        break;
                    case UserMessageEvent userMsg:
                        agentEvent.Data["content"] = userMsg.Data.Content;
                        break;
                    case SessionIdleEvent:
                        done.TrySetResult();
                        break;
                    case SessionErrorEvent err:
                        agentEvent.Data["message"] = err.Data.Message;
                        done.TrySetException(new InvalidOperationException(err.Data.Message ?? "Session error"));
                        break;
                }

                events.Add(agentEvent);
            });

            await session.SendAsync(new MessageOptions { Prompt = options.Scenario.Prompt });
            await done.Task;
        }
        catch (Exception error)
        {
            var msg = error.ToString();
            if (error is TimeoutException || error.InnerException is TimeoutException
                || msg.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                // Timeout: record a dedicated event (the timer fired, no session.error exists)
                events.Add(new AgentEvent(
                    "runner.timeout",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    new Dictionary<string, object?> { ["message"] = msg }));
            }
            else if (!events.Any(e => e.Type == "session.error"))
            {
                // Only add runner.error when there isn't already a session.error event
                events.Add(new AgentEvent(
                    "runner.error",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    new Dictionary<string, object?> { ["message"] = msg }));
            }
        }

        var wallTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startTime;
        return MetricsCollector.CollectMetrics(events, agentOutput, wallTimeMs, workDir);
    }

    private static async Task<string> SetupWorkDir(EvalScenario scenario, string? skillPath, string? evalPath)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"skill-validator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        _workDirs.Add(workDir);

        // Copy all sibling files from the eval directory when opted in
        if (evalPath is not null && scenario.Setup?.CopyTestFiles == true)
        {
            var evalDir = Path.GetDirectoryName(evalPath)!;
            foreach (var entry in new DirectoryInfo(evalDir).EnumerateFileSystemInfos())
            {
                if (entry.Name == "eval.yaml") continue;
                var dest = Path.Combine(workDir, entry.Name);
                if (entry is DirectoryInfo dir)
                    CopyDirectory(dir.FullName, dest);
                else if (entry is FileInfo file)
                    file.CopyTo(dest, true);
            }
        }

        // Explicit setup files override/supplement auto-copied files
        if (scenario.Setup?.Files is { } files)
        {
            foreach (var file in files)
            {
                var targetPath = Path.Combine(workDir, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                if (file.Content is not null)
                {
                    await File.WriteAllTextAsync(targetPath, file.Content);
                }
                else if (file.Source is not null && skillPath is not null)
                {
                    var sourcePath = Path.Combine(skillPath, file.Source);
                    File.Copy(sourcePath, targetPath, true);
                }
            }
        }

        // Run setup commands (e.g. build to produce a binlog, then strip sources)
        if (scenario.Setup?.Commands is { } commands)
        {
            foreach (var cmd in commands)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                        Arguments = OperatingSystem.IsWindows() ? $"/c {cmd}" : $"-c \"{cmd.Replace("\"", "\\\"")}\"",
                        WorkingDirectory = workDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    };
                    using var proc = Process.Start(psi);
                    if (proc is not null)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                        await proc.WaitForExitAsync(cts.Token);
                    }
                }
                catch
                {
                    // Setup commands may return non-zero exit codes
                    // (e.g. building a broken project to produce a binlog)
                }
            }
        }

        return workDir;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
