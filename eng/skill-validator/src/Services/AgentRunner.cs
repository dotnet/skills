using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkillValidator.Models;
using SkillValidator.Utilities;
using GitHub.Copilot.SDK;

namespace SkillValidator.Services;

public sealed record RunOptions(
    EvalScenario Scenario,
    SkillInfo? Skill,
    string? EvalPath,
    string Model,
    bool Verbose,
    Action<string>? Log = null,
    IReadOnlyList<SkillInfo>? AdditionalSkills = null);

public static class AgentRunner
{
    private static CopilotClient? _sharedClient;
    private static readonly SemaphoreSlim _clientLock = new(1, 1);
    private static readonly ConcurrentBag<string> _workDirs = [];

    /// <summary>
    /// Returns the shared <see cref="CopilotClient"/>, creating it on first call.
    /// Must be called before executing any untrusted workloads (eval scenarios,
    /// setup commands).
    /// </summary>
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
            {
                options.GitHubToken = githubToken;
                // Clear the token from the environment so child processes
                // (e.g. LLM-generated code, eval shell commands) cannot read it.
                Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
            }

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

    public static bool CheckPermission(PermissionRequest request, string workDir, string? skillPath, Action<string>? log)
    {
        string? reqPath = null;
        if (request.ExtensionData is { } data)
        {
            if (data.TryGetValue("path", out var pathVal) && pathVal is JsonElement pathEl && pathEl.ValueKind == JsonValueKind.String)
                reqPath = pathEl.GetString() ?? "";
            else if (data.TryGetValue("fileName", out var fileVal) && fileVal is JsonElement fileEl && fileEl.ValueKind == JsonValueKind.String)
                reqPath = fileEl.GetString() ?? "";
            else if (data.TryGetValue("fullCommandText", out var cmdVal) && cmdVal is JsonElement cmdEl && cmdEl.ValueKind == JsonValueKind.String)
                reqPath = cmdEl.GetString() ?? "";
        }

        // Deny-by-default: if no path/command can be extracted, deny the request.
        if (string.IsNullOrEmpty(reqPath))
        {
            log?.Invoke($"      ❌ Denying permission request with no path/command json entry: "
                + string.Join(", ", request.ExtensionData?.Select(kv => $"{kv.Key}={kv.Value}") ?? []));
            return false;
        }

        if (!Path.EndsInDirectorySeparator(workDir))
            workDir += Path.DirectorySeparatorChar;

        string resolved = Path.GetFullPath(reqPath, workDir);
        if (!Path.EndsInDirectorySeparator(resolved))
            resolved += Path.DirectorySeparatorChar;

        var allowedDirs = new List<string> { Path.GetFullPath(workDir) };
        if (skillPath is not null)
        {
            string skillsPathAbsolute = Path.GetFullPath(skillPath);
            if (!Path.EndsInDirectorySeparator(skillsPathAbsolute))
                skillsPathAbsolute = skillsPathAbsolute + Path.DirectorySeparatorChar;

            allowedDirs.Add(skillsPathAbsolute);
        }

        // Use case-sensitive comparison on Linux/macOS, case-insensitive on Windows.
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        bool anyAllowed = allowedDirs.Any(dir =>
        {
            string normalizedDir = Path.EndsInDirectorySeparator(dir)
                ? dir
                : dir + Path.DirectorySeparatorChar;
            return resolved.Equals(normalizedDir, comparison) ||
                   resolved.StartsWith(normalizedDir, comparison);
        });

        if (!anyAllowed)
        {
            log?.Invoke($"      ❌ Denying permission request for path/command: {resolved} (allowed: {string.Join(", ", allowedDirs)})");
        }

        return anyAllowed;
    }

    internal static SessionConfig BuildSessionConfig(
        SkillInfo? skill,
        string model,
        string workDir,
        IReadOnlyDictionary<string, MCPServerDef>? mcpServers = null,
        IReadOnlyList<SkillInfo>? additionalSkills = null,
        Action<string>? log = null,
        bool verbose = false)
    {
        // The SDK expects SkillDirectories entries to be parent directories that
        // it scans for child folders containing SKILL.md.
        var skillPath = skill is not null ? Path.GetDirectoryName(skill.Path) : null;

        // Create a unique temporary config directory for this session to not share any data
        var configDir = Path.Combine(Path.GetTempPath(), $"sv-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDir);
        _workDirs.Add(configDir);
        if (verbose)
            log?.Invoke($"      📂 Config dir: {configDir} ({(skill is not null ? "skilled" : "baseline")})");

        // Build skill directories list: primary skill + any additional skills.
        // For additional skills we stage a temp directory with copies of each
        // skill's SKILL.md so the SDK discovers exactly those skills — not
        // every sibling that happens to share the same parent directory.
        var skillDirs = new List<string>();
        if (skillPath is not null) skillDirs.Add(skillPath);
        if (additionalSkills is { Count: > 0 })
        {
            var stageDir = Path.Combine(Path.GetTempPath(), $"sv-noise-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stageDir);
            _workDirs.Add(stageDir);

            foreach (var s in additionalSkills)
            {
                var skillMdPath = Path.Combine(s.Path, "SKILL.md");
                if (!File.Exists(skillMdPath))
                    continue;

                var stagedSkillDir = Path.Combine(stageDir, Path.GetFileName(s.Path));
                Directory.CreateDirectory(stagedSkillDir);
                File.Copy(skillMdPath, Path.Combine(stagedSkillDir, "SKILL.md"));
            }

            skillDirs.Add(stageDir);
        }

        // Convert MCPServerDef records to the SDK's Dictionary<string, object> shape
        Dictionary<string, object>? sdkMcp = null;
        if (mcpServers is { Count: > 0 })
        {
            sdkMcp = new Dictionary<string, object>();
            foreach (var (name, def) in mcpServers)
            {
                if (!IsAllowedMcpCommand(def.Command))
                {
                    Console.Error.WriteLine(
                        $"Skipping MCP server '{name}': command '{def.Command}' is not in the allowlist");
                    continue;
                }

                var sanitizedArgs = SanitizeMcpArgs(def.Command, def.Args);
                if (sanitizedArgs is null)
                {
                    Console.Error.WriteLine(
                        $"Skipping MCP server '{name}': args contain dangerous eval/exec flags");
                    continue;
                }

                var entry = new Dictionary<string, object>
                {
                    ["type"] = def.Type ?? "stdio",
                    ["command"] = def.Command,
                    ["args"] = sanitizedArgs,
                    ["tools"] = def.Tools ?? ["*"],
                };

                // Sanitize env: strip dangerous keys that could hijack the process.
                var sanitizedEnv = SanitizeMcpEnv(def.Env);
                if (sanitizedEnv is not null) entry["env"] = sanitizedEnv;

                // Drop custom cwd — MCP servers run in workDir, not attacker-chosen dirs.
                sdkMcp[name] = entry;
            }

            // If all servers were filtered out, treat as no MCP servers
            if (sdkMcp.Count == 0) sdkMcp = null;
        }

        return new SessionConfig
        {
            Model = model,
            Streaming = true,
            WorkingDirectory = workDir,
            SkillDirectories = skillDirs,
            ConfigDir = configDir,
            McpServers = sdkMcp,
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            OnPermissionRequest = (request, _) =>
            {
                var result = CheckPermission(request, workDir, skillPath, verbose ? log : null);
                return Task.FromResult(new PermissionRequestResult
                {
                    Kind = result ? PermissionRequestResultKind.Approved : PermissionRequestResultKind.DeniedByRules,
                });
            },
        };
    }

    public static async Task<RunMetrics> RunAgent(RunOptions options)
    {
        return await RetryHelper.ExecuteWithRetry(
            async ct => await RunAgentCore(options, ct),
            label: $"RunAgent({options.Scenario.Name}, {(options.Skill is not null ? "skilled" : "baseline")})",
            maxRetries: 2,
            baseDelayMs: 5_000,
            totalTimeoutMs: (options.Scenario.Timeout + 60) * 1000);
    }

    private static async Task<RunMetrics> RunAgentCore(RunOptions options, CancellationToken cancellationToken)
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
        bool timedOut = false;

        try
        {
            var client = await GetSharedClient(options.Verbose);

            await using var session = await client.CreateSessionAsync(
                BuildSessionConfig(options.Skill, options.Model, workDir, options.Skill?.McpServers, options.AdditionalSkills, options.Log, options.Verbose));

            var done = new TaskCompletionSource();
            var effectiveTimeout = options.Scenario.Timeout;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveTimeout * 1000);
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
                        agentEvent.Data["deltaContent"] = JsonValue.Create(delta.Data.DeltaContent);
                        agentOutput += delta.Data.DeltaContent ?? "";
                        break;
                    case AssistantMessageEvent msg:
                        agentEvent.Data["content"] = JsonValue.Create(msg.Data.Content);
                        if (!string.IsNullOrEmpty(msg.Data.Content))
                            agentOutput = msg.Data.Content;
                        break;
                    case ToolExecutionStartEvent toolStart:
                        agentEvent.Data["toolName"] = JsonValue.Create(toolStart.Data.ToolName);
                        agentEvent.Data["arguments"] = JsonValue.Create(toolStart.Data.Arguments?.ToString());
                        if (options.Verbose)
                        {
                            var write = options.Log ?? (m => Console.Error.WriteLine(m));
                            write($"      🔧 {toolStart.Data.ToolName}");
                        }
                        break;
                    case ToolExecutionCompleteEvent toolComplete:
                        agentEvent.Data["success"] = JsonValue.Create(toolComplete.Data.Success.ToString());
                        agentEvent.Data["result"] = JsonValue.Create(toolComplete.Data.Result?.Content ?? toolComplete.Data.Error?.Message ?? "");
                        break;
                    case SkillInvokedEvent skillInvoked:
                        agentEvent.Data["name"] = JsonValue.Create(skillInvoked.Data.Name);
                        agentEvent.Data["path"] = JsonValue.Create(skillInvoked.Data.Path);
                        if (skillInvoked.Data.AllowedTools is { } allowedTools)
                        {
                            var arr = new JsonArray();
                            foreach (var tool in allowedTools)
                                arr.Add((JsonNode?)JsonValue.Create(tool));
                            agentEvent.Data["allowedTools"] = arr;
                        }
                        if (options.Verbose)
                        {
                            var write = options.Log ?? (m => Console.Error.WriteLine(m));
                            write($"      📘 Skill invoked: {skillInvoked.Data.Name}");
                        }
                        break;
                    case AssistantUsageEvent usage:
                        agentEvent.Data["inputTokens"] = JsonValue.Create(usage.Data.InputTokens);
                        agentEvent.Data["outputTokens"] = JsonValue.Create(usage.Data.OutputTokens);
                        agentEvent.Data["model"] = JsonValue.Create(usage.Data.Model);
                        break;
                    case UserMessageEvent userMsg:
                        agentEvent.Data["content"] = JsonValue.Create(userMsg.Data.Content);
                        break;
                    case SessionIdleEvent:
                        done.TrySetResult();
                        break;
                    case SessionErrorEvent err:
                        agentEvent.Data["message"] = JsonValue.Create(err.Data.Message);
                        done.TrySetException(new InvalidOperationException(err.Data.Message ?? "Session error"));
                        break;
                }

                events.Add(agentEvent);
            });

            await session.SendAsync(new MessageOptions { Prompt = options.Scenario.Prompt });
            await done.Task;
        }
        catch (TimeoutException te)
        {
            timedOut = true;
            events.Add(new AgentEvent(
                "runner.error",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                new Dictionary<string, JsonNode?> { ["message"] = JsonValue.Create(te.ToString()) }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Budget exhausted — let RetryHelper handle it.
        }
        catch (Exception error)
        {
            var msg = error.ToString();

            // Re-throw rate-limit (429) errors so RetryHelper can retry them.
            if (msg.Contains("429", StringComparison.Ordinal)
                || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            {
                throw;
            }

            if (error is TimeoutException || error.InnerException is TimeoutException
                || msg.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                // Timeout: record a dedicated event (the timer fired, no session.error exists)
                events.Add(new AgentEvent(
                    "runner.timeout",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    new Dictionary<string, JsonNode?> { ["message"] = JsonValue.Create(msg) }));
            }
            else if (!events.Any(e => e.Type == "session.error"))
            {
                // Only add runner.error when there isn't already a session.error event
                events.Add(new AgentEvent(
                    "runner.error",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    new Dictionary<string, JsonNode?> { ["message"] = JsonValue.Create(msg) }));
            }
        }

        var wallTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startTime;
        var metrics = MetricsCollector.CollectMetrics(events, agentOutput, wallTimeMs, workDir);
        metrics.TimedOut = timedOut;
        return metrics;
    }

    private static async Task<string> SetupWorkDir(EvalScenario scenario, string? skillPath, string? evalPath)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"sv-{Guid.NewGuid():N}");
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
            var canonicalWorkDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workDir));
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            foreach (var file in files)
            {
                var targetPath = Path.GetFullPath(Path.Combine(workDir, file.Path));
                // Prevent path traversal: target must stay inside workDir
                if (!targetPath.StartsWith(canonicalWorkDir + Path.DirectorySeparatorChar, pathComparison)
                    && !targetPath.Equals(canonicalWorkDir, pathComparison))
                {
                    Console.Error.WriteLine($"Setup file target escapes work directory, skipping: {file.Path}");
                    continue;
                }

                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                if (file.Content is not null)
                {
                    await File.WriteAllTextAsync(targetPath, file.Content);
                }
                else if (file.Source is not null && skillPath is not null)
                {
                    var canonicalSkillPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(skillPath));
                    var sourcePath = Path.GetFullPath(Path.Combine(skillPath, file.Source));
                    // Prevent path traversal: source must stay inside skillPath
                    if (!sourcePath.StartsWith(canonicalSkillPath + Path.DirectorySeparatorChar, pathComparison)
                        && !sourcePath.Equals(canonicalSkillPath, pathComparison))
                    {
                        Console.Error.WriteLine($"Setup file source escapes skill directory, skipping: {file.Source}");
                        continue;
                    }
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

                    // Scrub sensitive environment variables from child processes.
                    // ProcessStartInfo.Environment is pre-populated with the current
                    // process's environment on first access; removing keys prevents
                    // them from being inherited by the child.
                    ScrubSensitiveEnvironment(psi);

                    using var proc = Process.Start(psi);
                    if (proc is not null)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                        try
                        {
                            await proc.WaitForExitAsync(cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // Process timed out — kill the orphan
                            try { proc.Kill(true); } catch { }
                            Console.Error.WriteLine($"Setup command timed out and was killed");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Setup commands may return non-zero exit codes
                    // (e.g. building a broken project to produce a binlog)
                    Console.Error.WriteLine($"Setup command failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        return workDir;
    }

    // --- Security: environment scrubbing for child processes ---

    private static readonly string[] SensitiveEnvKeys =
    [
        "GITHUB_TOKEN",
        "ACTIONS_RUNTIME_TOKEN",
        "ACTIONS_ID_TOKEN_REQUEST_URL",
        "ACTIONS_ID_TOKEN_REQUEST_TOKEN",
        "ACTIONS_CACHE_URL",
        "ACTIONS_RESULTS_URL",
        "GITHUB_STEP_SUMMARY",
        "GITHUB_OUTPUT",
        "GITHUB_ENV",
        "GITHUB_PATH",
        "GITHUB_STATE",
        "NODE_AUTH_TOKEN",
        "NPM_TOKEN",
        "NUGET_API_KEY",
    ];

    private static readonly string[] SensitiveEnvPrefixes =
    [
        "COPILOT_",
        "GH_AW_",
    ];

    internal static void ScrubSensitiveEnvironment(ProcessStartInfo psi)
    {
        foreach (var key in SensitiveEnvKeys)
        {
            psi.Environment.Remove(key);
        }

        var prefixedKeys = psi.Environment.Keys
            .Where(k => SensitiveEnvPrefixes.Any(p =>
                k.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var key in prefixedKeys)
        {
            psi.Environment.Remove(key);
        }
    }

    // --- Security: MCP server command allowlist ---

    private static readonly HashSet<string> AllowedMcpCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet", "dnx", "node", "npx", "python", "python3", "uvx",
    };

    internal static bool IsAllowedMcpCommand(string command)
    {
        // Only allow bare command names (resolved via PATH), not paths.
        if (command.Contains(Path.DirectorySeparatorChar) ||
            command.Contains(Path.AltDirectorySeparatorChar) ||
            command.Contains(".."))
        {
            return false;
        }

        var fileName = Path.GetFileName(command);
        // On Windows, also allow the .exe extension (e.g., "dotnet.exe").
        if (OperatingSystem.IsWindows() &&
            fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName);
        }
        return AllowedMcpCommands.Contains(fileName);
    }

    // Dangerous env var keys that could hijack MCP server processes.
    private static readonly HashSet<string> DangerousMcpEnvKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "LD_PRELOAD", "LD_LIBRARY_PATH", "DYLD_INSERT_LIBRARIES",
        "DYLD_LIBRARY_PATH", "NODE_OPTIONS", "PYTHONSTARTUP", "PYTHONPATH",
        "PERL5OPT", "RUBYOPT", "JAVA_TOOL_OPTIONS", "DOTNET_STARTUP_HOOKS",
        "COMSPEC", "ComSpec",
    };

    internal static Dictionary<string, string>? SanitizeMcpEnv(
        Dictionary<string, string>? env)
    {
        if (env is null or { Count: 0 }) return null;

        var sanitized = new Dictionary<string, string>(env.Count);
        foreach (var (key, value) in env)
        {
            if (DangerousMcpEnvKeys.Contains(key))
            {
                Console.Error.WriteLine(
                    $"Stripping dangerous env var '{key}' from MCP server definition");
                continue;
            }
            sanitized[key] = value;
        }

        return sanitized.Count > 0 ? sanitized : null;
    }

    // Per-runtime dangerous arg patterns that enable arbitrary code execution.
    private static readonly Dictionary<string, HashSet<string>> DangerousMcpArgs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["node"] = new(StringComparer.Ordinal) { "-e", "--eval", "-p", "--print", "--input-type" },
            ["python"] = new(StringComparer.Ordinal) { "-c", "-m" },
            ["python3"] = new(StringComparer.Ordinal) { "-c", "-m" },
            ["npx"] = new(StringComparer.Ordinal) { "-y", "--yes" },
            ["uvx"] = new(StringComparer.Ordinal) { "--from" },
        };

    internal static string[]? SanitizeMcpArgs(string command, string[] args)
    {
        var cmdName = Path.GetFileNameWithoutExtension(command);
        if (!DangerousMcpArgs.TryGetValue(cmdName, out var blocked))
            return args;

        foreach (var arg in args)
        {
            foreach (var flag in blocked)
            {
                // Exact match: -e, --eval
                if (arg.Equals(flag, StringComparison.Ordinal))
                    return null;
                // Combined form: -econsole.log(1), --eval=...
                if (flag.StartsWith("--") && arg.StartsWith(flag + "=", StringComparison.Ordinal))
                    return null;
                if (flag.StartsWith("-") && !flag.StartsWith("--") && arg.StartsWith(flag, StringComparison.Ordinal) && arg.Length > flag.Length)
                    return null;
            }
        }

        return args;
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
