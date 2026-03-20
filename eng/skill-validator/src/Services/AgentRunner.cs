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
    string? PluginRoot = null,
    Action<string>? Log = null,
    IReadOnlyList<SkillInfo>? AdditionalSkills = null,
    IReadOnlyDictionary<string, MCPServerDef>? McpServers = null,
    string? SessionsDir = null,
    string? SessionId = null);

public static class AgentRunner
{
    private static readonly ConcurrentDictionary<string, CopilotClient> _pluginClients = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim _clientLock = new(1, 1);
    private static readonly ConcurrentBag<string> _workDirs = [];
    private static readonly ConcurrentBag<string> _configDirs = [];
    private static string? _capturedGitHubToken;
    private static bool _tokenCaptured;

    /// <summary>
    /// Capture GITHUB_TOKEN once at startup so multiple clients can share it
    /// and the env var is cleared from child processes.
    /// </summary>
    public static void CaptureGitHubToken()
    {
        if (_tokenCaptured) return;
        _capturedGitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(_capturedGitHubToken))
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
        _tokenCaptured = true;
    }

    /// <summary>
    /// Returns a shared CopilotClient, keyed by plugin root for future
    /// per-plugin configuration. Currently all clients share the same
    /// options because --plugin-dir is NOT honored by the SDK; plugin
    /// skills are loaded via SkillDirectories in BuildSessionConfig.
    /// </summary>
    public static async Task<CopilotClient> GetPluginClient(
        string? pluginRoot, bool verbose)
    {
        var key = pluginRoot ?? "";

        if (_pluginClients.TryGetValue(key, out var existing))
            return existing;

        await _clientLock.WaitAsync();
        try
        {
            if (_pluginClients.TryGetValue(key, out existing))
                return existing;

            CaptureGitHubToken();

            var options = new CopilotClientOptions
            {
                LogLevel = verbose ? "info" : "none",
            };

            if (!string.IsNullOrEmpty(_capturedGitHubToken))
                options.GitHubToken = _capturedGitHubToken;

            var client = new CopilotClient(options);
            await client.StartAsync();
            _pluginClients[key] = client;
            return client;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>
    /// Backward-compatible alias — returns the no-plugin client.
    /// Used by judge sessions that don't need plugin loading.
    /// </summary>
    public static Task<CopilotClient> GetSharedClient(bool verbose)
        => GetPluginClient(null, verbose);

    /// <summary>Stop all plugin clients (including the no-plugin client).</summary>
    public static async Task StopAllClients()
    {
        foreach (var (key, client) in _pluginClients)
        {
            try { await client.StopAsync(); }
            catch (Exception ex) { Console.Error.WriteLine($"Warning: failed to stop client '{key}': {ex.Message}"); }
        }
        _pluginClients.Clear();
    }

    /// <summary>Remove all temporary working directories created during runs.</summary>
    public static Task CleanupWorkDirs(bool keepSessions = false)
    {
        var dirs = _workDirs.ToArray();
        _workDirs.Clear();

        var configDirsToClean = keepSessions ? [] : _configDirs.ToArray();
        _configDirs.Clear();

        var allDirs = dirs.Concat(configDirsToClean);
        return Task.WhenAll(allDirs.Select(dir =>
        {
            try { Directory.Delete(dir, true); } catch { }
            return Task.CompletedTask;
        }));
    }

    public static bool CheckPermission(PermissionRequest request, string workDir, string? skillPath, Action<string>? log, string? runLabel = null, string? pluginRoot = null, IReadOnlyList<string>? additionalAllowedDirs = null)
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

        var labelSuffix = runLabel is not null ? $" ({runLabel})" : "";

        // Allow-by-default: if no path/fileName/fullCommandText can be extracted, allow the request.
        // The above fields cover file system access and are best effort.
        // Deny-by-default isn't feasible as we would need to whitelist all kinds of tool calls.
        if (string.IsNullOrEmpty(reqPath))
        {
            log?.Invoke($"      Allowing permission request with no path/command json entry{labelSuffix}: "
                + string.Join(", ", request.ExtensionData?.Select(kv => $"{kv.Key}={kv.Value}") ?? []));
            return true;
        }

        if (!Path.EndsInDirectorySeparator(workDir))
            workDir += Path.DirectorySeparatorChar;

        // All relative paths are resolved against the workDir, which is the SDK's current
        // working directory for the session.
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
        if (pluginRoot is not null)
        {
            string pluginRootAbsolute = Path.GetFullPath(pluginRoot);
            if (!Path.EndsInDirectorySeparator(pluginRootAbsolute))
                pluginRootAbsolute = pluginRootAbsolute + Path.DirectorySeparatorChar;

            allowedDirs.Add(pluginRootAbsolute);
        }

        if (additionalAllowedDirs is not null)
        {
            foreach (var dir in additionalAllowedDirs)
            {
                string abs = Path.GetFullPath(dir);
                if (!Path.EndsInDirectorySeparator(abs))
                    abs += Path.DirectorySeparatorChar;
                allowedDirs.Add(abs);
            }
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
            log?.Invoke($"      ❌ Denying permission request for path/command{labelSuffix}: {resolved} (allowed: {string.Join(", ", allowedDirs)})");
        }

        return anyAllowed;
    }

    internal static SessionConfig BuildSessionConfig(
        SkillInfo? skill,
        string? pluginRoot,
        string model,
        string workDir,
        IReadOnlyDictionary<string, MCPServerDef>? mcpServers = null,
        IReadOnlyList<SkillInfo>? additionalSkills = null,
        Action<string>? log = null,
        bool verbose = false,
        string? sessionsDir = null,
        string? sessionId = null)
    {
        // The SDK expects SkillDirectories entries to be parent directories that
        // it scans for child folders containing SKILL.md.
        var skillPath = skill is not null ? Path.GetDirectoryName(skill.Path) : null;

        string configDir;
        if (sessionsDir is not null)
        {
            // Persistent session dir — use sessionId as folder name for DB linkage
            var dirName = sessionId ?? Guid.NewGuid().ToString("N");
            configDir = Path.Combine(sessionsDir, dirName);
            Directory.CreateDirectory(configDir);
            _configDirs.Add(configDir);
        }
        else
        {
            configDir = Path.Combine(Path.GetTempPath(), $"sv-cfg-{Guid.NewGuid():N}");
            Directory.CreateDirectory(configDir);
            _configDirs.Add(configDir);
        }
        if (verbose)
            log?.Invoke($"      📂 Config dir: {configDir} ({(skill is not null ? "skilled" : "baseline")})");

        // Build additional noise skill directories when noise testing is active.
        // For additional skills we stage a temp directory with copies of each
        // skill's content so the SDK discovers exactly those skills — not
        // every sibling that happens to share the same parent directory.
        // We copy the full directory tree (references/, scripts/, etc.) so that
        // relative links inside SKILL.md continue to resolve.
        var noiseDirs = new List<string>();
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
                CopyDirectory(s.Path, stagedSkillDir);
            }

            noiseDirs.Add(stageDir);
        }

        // Convert MCPServerDef records to the SDK's Dictionary<string, object> shape
        // Security hardening: validate commands, sanitize args/env, drop custom cwd.
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

        // Three run types:
        // 1. Baseline (skill == null, pluginRoot == null): no skills, no MCP.
        // 2. Skilled-isolated (skill != null, pluginRoot == null): ONLY the target skill
        //    is loaded — we stage it into a temp directory so the SDK doesn't
        //    discover sibling skills from the same parent.
        // 3. Skilled-plugin (skill != null, pluginRoot != null): entire plugin loaded
        //    via SkillDirectories (--plugin-dir is NOT honored by SDK).
        //
        // For skilled-plugin, we enumerate all skill directories from plugin.json
        // so that all sibling skills are loaded, matching production behavior.
        string[] skillDirs;
        if (pluginRoot is not null)
        {
            skillDirs = ResolvePluginSkillDirectories(pluginRoot);
        }
        else if (skill is not null)
        {
            // Stage the single skill into a temp directory so the SDK discovers
            // only this skill — not every sibling that shares the same parent.
            // Copy the full directory tree (references/, scripts/, etc.) so that
            // relative links inside SKILL.md continue to resolve.
            var isoStageDir = Path.Combine(Path.GetTempPath(), $"sv-iso-{Guid.NewGuid():N}");
            Directory.CreateDirectory(isoStageDir);
            _workDirs.Add(isoStageDir);

            var stagedSkillDir = Path.Combine(isoStageDir, Path.GetFileName(skill.Path));
            if (Directory.Exists(skill.Path))
                CopyDirectory(skill.Path, stagedSkillDir);
            else
                Directory.CreateDirectory(stagedSkillDir);

            // Always write SKILL.md from the in-memory content (may differ from
            // the on-disk version when the validator applies transformations).
            File.WriteAllText(Path.Combine(stagedSkillDir, "SKILL.md"), skill.SkillMdContent);

            skillDirs = [isoStageDir];
        }
        else
        {
            skillDirs = [];
        }

        // Precompute the additional allowed directories once so we don't
        // allocate on every permission request (the set is fixed per session).
        var additionalAllowedDirs = skillDirs
            .Concat(noiseDirs)
            .Where(d => !string.IsNullOrEmpty(d))
            .ToList();

        // In isolated runs the agent should only access the staged copies, not
        // the original skill tree (which includes sibling skills).  Pass null
        // for skillPath so the original location is NOT in the allowlist.
        // In plugin mode, validate that skillPath is under pluginRoot before
        // allowlisting it; otherwise fall back to pluginRoot coverage alone.
        string? effectiveSkillPath = null;
        if (pluginRoot is not null && skillPath is not null)
        {
            var normalizedSkill = Path.GetFullPath(skillPath);
            var normalizedPlugin = Path.GetFullPath(pluginRoot);
            if (!Path.EndsInDirectorySeparator(normalizedPlugin))
                normalizedPlugin += Path.DirectorySeparatorChar;
            var cmp = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (normalizedSkill.StartsWith(normalizedPlugin, cmp))
                effectiveSkillPath = skillPath;
        }

        return new SessionConfig
        {
            Model = model,
            Streaming = true,
            WorkingDirectory = workDir,
            SkillDirectories = [..skillDirs, ..noiseDirs],
            ConfigDir = configDir,
            McpServers = sdkMcp,
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            OnPermissionRequest = (request, _) =>
            {
                var runLabel = skill is not null ? "skilled" : "baseline";
                var result = CheckPermission(request, workDir, effectiveSkillPath, verbose ? log : null, runLabel, pluginRoot, additionalAllowedDirs);
                return Task.FromResult(new PermissionRequestResult
                {
                    Kind = result ? PermissionRequestResultKind.Approved : PermissionRequestResultKind.DeniedByRules,
                });
            },
        };
    }

    /// <summary>
    /// Resolves the skill directories for a plugin by reading its plugin.json
    /// and returning the resolved skills path. The SDK scans this directory
    /// for subdirectories containing SKILL.md files.
    /// </summary>
    internal static string[] ResolvePluginSkillDirectories(string pluginRoot)
    {
        var pluginJsonPath = Path.Combine(pluginRoot, "plugin.json");
        PluginInfo? pluginInfo;
        try
        {
            pluginInfo = PluginValidator.ParsePluginJson(pluginJsonPath);
        }
        catch (JsonException)
        {
            // Malformed plugin.json — return empty so the session is created
            // without extra skill directories; validation surfaces the real error.
            return [];
        }
        if (pluginInfo is null || pluginInfo.SkillPaths.Count == 0) return [];

        var dirs = new List<string>();
        foreach (var relativePath in pluginInfo.SkillPaths)
        {
            if (!PluginValidator.TryGetSafeSubdirectory(pluginRoot, relativePath, out var fullPath, out _))
                continue;
            if (Directory.Exists(fullPath!))
                dirs.Add(fullPath!);
        }
        return dirs.ToArray();
    }

    public static async Task<RunMetrics> RunAgent(RunOptions options)
    {
        var runType = options.Skill is null ? "baseline"
            : options.PluginRoot is not null ? "skilled-plugin"
            : "skilled-isolated";
        return await RetryHelper.ExecuteWithRetry(
            async ct => await RunAgentCore(options, ct),
            label: $"RunAgent({options.Scenario.Name}, {runType})",
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
            // All runs use the same client — plugin skills are loaded manually
            // via SkillDirectories (--plugin-dir is not honored by SDK).
            var client = await GetPluginClient(options.PluginRoot, options.Verbose);

            await using var session = await client.CreateSessionAsync(
                BuildSessionConfig(options.Skill, options.PluginRoot, options.Model, workDir, options.McpServers, options.AdditionalSkills, options.Log, options.Verbose, options.SessionsDir, options.SessionId));

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
                        agentEvent.Data["cacheReadTokens"] = JsonValue.Create(usage.Data.CacheReadTokens);
                        agentEvent.Data["cacheWriteTokens"] = JsonValue.Create(usage.Data.CacheWriteTokens);
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
                else if (file.Source is not null)
                {
                    var resolvedSource = ResolveSourcePath(file.Source, evalPath, skillPath);
                    if (resolvedSource is null)
                    {
                        continue;
                    }
                    File.Copy(resolvedSource, targetPath, true);
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

    /// <summary>
    /// Resolves a setup file source path relative to the eval directory (preferred) or skill directory.
    /// Returns the resolved absolute path, or null if resolution fails (no base directory, or path traversal detected).
    /// </summary>
    internal static string? ResolveSourcePath(string source, string? evalPath, string? skillPath)
    {
        var baseDir = evalPath is not null ? Path.GetDirectoryName(evalPath)! : skillPath;
        if (baseDir is null)
        {
            Console.Error.WriteLine($"Setup file source '{source}' specified but no eval or skill directory is available, skipping.");
            return null;
        }

        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var canonicalBaseDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDir));
        var sourcePath = Path.GetFullPath(Path.Combine(baseDir, source));
        // Prevent path traversal: source must stay inside the base directory
        if (!sourcePath.StartsWith(canonicalBaseDir + Path.DirectorySeparatorChar, pathComparison)
            && !sourcePath.Equals(canonicalBaseDir, pathComparison))
        {
            Console.Error.WriteLine($"Setup file source escapes base directory, skipping: {source}");
            return null;
        }

        return sourcePath;
    }

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

    /// <summary>
    /// Recursively copies a directory tree, skipping symlinks and reparse
    /// points so that staging cannot pull in content from outside the
    /// source root.
    /// </summary>
    private static void CopyDirectory(string source, string destination)
    {
        var sourceRoot = Path.GetFullPath(source);
        if (!Path.EndsInDirectorySeparator(sourceRoot))
            sourceRoot += Path.DirectorySeparatorChar;
        CopyDirectoryCore(source, destination, sourceRoot);
    }

    private static void CopyDirectoryCore(string source, string destination, string sourceRoot)
    {
        Directory.CreateDirectory(destination);

        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);

            // Skip symlinks / reparse points to avoid copying data outside the skill tree.
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            var name = Path.GetFileName(entry);
            var destPath = Path.Combine(destination, name);

            if ((attributes & FileAttributes.Directory) != 0)
            {
                var entryFull = Path.GetFullPath(entry);
                if (!Path.EndsInDirectorySeparator(entryFull))
                    entryFull += Path.DirectorySeparatorChar;

                // Guard against junctions that resolve outside the source root.
                var pathComparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (!entryFull.StartsWith(sourceRoot, pathComparison))
                    continue;

                CopyDirectoryCore(entryFull.TrimEnd(Path.DirectorySeparatorChar), destPath, sourceRoot);
            }
            else
            {
                File.Copy(entry, destPath, overwrite: true);
            }
        }
    }
}
