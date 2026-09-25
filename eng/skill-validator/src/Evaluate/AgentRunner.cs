using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SkillValidator.Shared;
using GitHub.Copilot;

namespace SkillValidator.Evaluate;

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
    string? SessionId = null,
    AgentInfo? Agent = null,
    IReadOnlyList<AgentInfo>? AdditionalAgents = null,
    bool SelectAgentAsPrimary = true);

internal sealed class RunEventBuffer
{
    private readonly Lock _sync = new();
    private readonly List<AgentEvent> _events = [];
    private readonly StringBuilder _agentOutput = new();

    internal void Record(string type, Action<AgentEvent, StringBuilder> populate)
    {
        lock (_sync)
        {
            var agentEvent = new AgentEvent(
                type,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                []);
            populate(agentEvent, _agentOutput);
            _events.Add(agentEvent);
        }
    }

    internal void Add(AgentEvent agentEvent)
    {
        lock (_sync)
            _events.Add(agentEvent);
    }

    internal bool ContainsType(string type)
    {
        lock (_sync)
            return _events.Any(agentEvent => agentEvent.Type == type);
    }

    internal (List<AgentEvent> Events, string AgentOutput) Snapshot()
    {
        lock (_sync)
            return ([.. _events], _agentOutput.ToString());
    }
}

public static class AgentRunner
{
    private static readonly HashSet<string> EvaluatorOnlySetupEntries =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "eval.yaml",
            "references",
        };
    private static readonly ConcurrentDictionary<string, CopilotClient> _pluginClients = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim _clientLock = new(1, 1);
    private static readonly ConcurrentBag<string> _workDirs = [];
    private static readonly ConcurrentBag<string> _configDirs = [];
    private static readonly Lazy<string> _evaluationRoot = new(() =>
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"skill-validator-{Environment.ProcessId}-{Guid.NewGuid():N}");
        if (OperatingSystem.IsWindows())
        {
            using var currentIdentity = WindowsIdentity.GetCurrent();
            var currentUser = currentIdentity.User
                ?? throw new InvalidOperationException(
                    "Cannot create the private evaluator root without a Windows user identity.");
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetOwner(currentUser);
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            FileSystemAclExtensions.Create(new DirectoryInfo(root), security);
        }
        else
        {
            const UnixFileMode ownerOnly =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Directory.CreateDirectory(root, ownerOnly);
            File.SetUnixFileMode(root, ownerOnly);
        }
        return Path.GetFullPath(root);
    });
    private static string? _capturedGitHubToken;
    private static bool _tokenCaptured;
    private static readonly string[] GitHubTokenEnvKeys = ["GH_TOKEN", "GITHUB_TOKEN"];

    /// <summary>
    /// Capture a GitHub token once at startup so multiple clients can share it
    /// and the supported env aliases are cleared from child processes.
    /// </summary>
    public static void CaptureGitHubToken()
    {
        if (_tokenCaptured) return;
        _capturedGitHubToken = CaptureAndRemoveGitHubTokenAliases(
            Environment.GetEnvironmentVariable,
            key => Environment.SetEnvironmentVariable(key, null));
        _tokenCaptured = true;
    }

    internal static string? CaptureAndRemoveGitHubTokenAliases(
        Func<string, string?> read,
        Action<string> remove)
    {
        string? token = null;
        foreach (var key in GitHubTokenEnvKeys)
        {
            var value = read(key);
            if (string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(value))
                token = value;
            remove(key);
        }
        return token;
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
                LogLevel = verbose ? CopilotLogLevel.Info : CopilotLogLevel.None,
                SessionFs = new SessionFsConfig
                {
                    // The shared client is created during model discovery, before
                    // per-scenario temp workspaces exist. Built-in file tools enforce
                    // this client-level root even when SessionConfig.WorkingDirectory
                    // points at a later sv-* workspace. Keep every fixture and staged
                    // skill under one private evaluator root; per-session hooks below
                    // further restrict each run to its narrow allowlist.
                    InitialWorkingDirectory = GetEvaluationRoot(),
                    SessionStatePath = "session-state",
                    Conventions = OperatingSystem.IsWindows()
                        ? GitHub.Copilot.Rpc.SessionFsSetProviderConventions.Windows
                        : GitHub.Copilot.Rpc.SessionFsSetProviderConventions.Posix,
                },
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

    internal static string GetEvaluationRoot()
    {
        var root = _evaluationRoot.Value;
        Directory.CreateDirectory(root);
        return root;
    }

    internal static string CreatePrivateWorkDir(string prefix)
    {
        var workDir = Path.Combine(
            GetEvaluationRoot(),
            $"sv-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        _workDirs.Add(workDir);
        return workDir;
    }

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

    /// <summary>
    /// Extracts file paths from PreToolUseHookInput.ToolArgs for permission sandboxing.
    /// Checks single- and multi-path file-tool keys. Shell command text is not itself a path; shell
    /// paths are checked from PermissionRequestShell.PossiblePaths instead.
    /// </summary>
    internal static IReadOnlyList<string> ExtractPathsFromToolArgs(PreToolUseHookInput input)
    {
        if (input.ToolArgs is not JsonElement args || args.ValueKind != JsonValueKind.Object)
            return [];

        var paths = new List<string>();
        var keys = new List<string>
        {
            "path", "fileName", "sourcePath", "destinationPath", "oldPath", "newPath",
            "paths",
        };
        if (input.ToolName?.Equals("rename", StringComparison.OrdinalIgnoreCase) == true
            || input.ToolName?.Equals("move", StringComparison.OrdinalIgnoreCase) == true)
            keys.AddRange(["source", "destination", "src", "dest", "from", "to"]);

        foreach (var key in keys)
        {
            if (!args.TryGetProperty(key, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String && value.GetString() is { } path)
            {
                paths.Add(path);
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                paths.AddRange(value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .Where(path => !string.IsNullOrEmpty(path)));
            }
        }

        return paths;
    }

    internal static bool IsShellTool(string? toolName) =>
        toolName is not null &&
        (toolName.Equals("bash", StringComparison.OrdinalIgnoreCase) ||
         toolName.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
         toolName.Equals("local_shell", StringComparison.OrdinalIgnoreCase));

    private static readonly HashSet<string> AllowedPathlessShellCommands = new(
        [
            "dir",
            "dotnet --info",
            "dotnet --version",
            "dotnet build",
            "dotnet test",
            "git diff",
            "git diff --check",
            "git status",
            "git status --short",
            "ls",
            "pwd",
        ],
        StringComparer.OrdinalIgnoreCase);

    internal static bool IsAllowedPathlessShellCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var normalized = string.Join(
            ' ',
            command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return AllowedPathlessShellCommands.Contains(normalized);
    }

    internal static bool CheckPermissions(IEnumerable<string>? reqPaths, string workDir, string? skillPath, Action<string>? log, string? runLabel = null, string? pluginRoot = null, IReadOnlyList<string>? additionalAllowedDirs = null)
    {
        return reqPaths is null || reqPaths.All(path =>
            CheckPermission(path, workDir, skillPath, log, runLabel, pluginRoot, additionalAllowedDirs));
    }

    internal static bool CheckShellPermission(
        PermissionRequestShell request,
        string workDir,
        string? skillPath,
        Action<string>? log,
        string? runLabel = null,
        string? pluginRoot = null,
        IReadOnlyList<string>? additionalAllowedDirs = null)
    {
        var hasUrl = request.PossibleUrls is { Length: > 0 }
            || request.FullCommandText?.Contains("://", StringComparison.OrdinalIgnoreCase) == true;
        if (hasUrl)
        {
            var labelSuffix = runLabel is not null ? $" ({runLabel})" : "";
            log?.Invoke($"      ❌ Denying shell permission request with network URL{labelSuffix}");
            return false;
        }

        if (MayCreateFileSystemLink(request.FullCommandText))
        {
            var labelSuffix = runLabel is not null ? $" ({runLabel})" : "";
            log?.Invoke($"      ❌ Denying shell command that can create a filesystem link{labelSuffix}");
            return false;
        }

        if (request.PossiblePaths is not { Length: > 0 }
            && !IsAllowedPathlessShellCommand(request.FullCommandText))
        {
            var labelSuffix = runLabel is not null ? $" ({runLabel})" : "";
            log?.Invoke($"      ❌ Denying unclassified shell permission request{labelSuffix}");
            return false;
        }

        return CheckPermissions(
            request.PossiblePaths,
            workDir,
            skillPath,
            log,
            runLabel,
            pluginRoot,
            additionalAllowedDirs);
    }

    internal static bool MayCreateFileSystemLink(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        return Regex.IsMatch(
            command,
            """
            (?ix)
            (?:
                \bln\s+(?:-[^\s;|&]*s[^\s;|&]*|--symbolic)\b
              | \bmklink(?:\.exe)?\b
              | \bjunction(?:\.exe)?\b
              | \bfsutil(?:\.exe)?\s+(?:hardlink|reparsepoint)\b
              | \bnew-item\b[^\r\n;|&]*-(?:itemtype|type)\s+(?:symboliclink|junction|hardlink)\b
              | \b(?:directory|file)\.createsymboliclink\s*\(
              | \b(?:file\.)?createhardlink\s*\(
              | \bfs(?:\.promises)?\.symlinksync\s*\(
              | \bfs(?:\.promises)?\.symlink\s*\(
              | \.(?:symlinksync|symlink)\s*\(
              | \bos\.symlink\s*\(
              | \.symlink_to\s*\(
            )
            """,
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    public static bool CheckPermission(string? reqPath, string workDir, string? skillPath, Action<string>? log, string? runLabel = null, string? pluginRoot = null, IReadOnlyList<string>? additionalAllowedDirs = null)
    {
        var labelSuffix = runLabel is not null ? $" ({runLabel})" : "";

        // Allow-by-default: if no path can be extracted, allow the request.
        // Deny-by-default isn't feasible as we would need to whitelist all kinds of tool calls.
        if (string.IsNullOrEmpty(reqPath))
        {
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
            var lexicallyContained = resolved.Equals(normalizedDir, comparison)
                || resolved.StartsWith(normalizedDir, comparison);
            return lexicallyContained
                && !PathSafety.ContainsReparsePoint(
                    Path.TrimEndingDirectorySeparator(dir),
                    Path.TrimEndingDirectorySeparator(resolved),
                    missingPathIsUnsafe: false);
        });

        if (!anyAllowed)
        {
            log?.Invoke($"      ❌ Denying permission request for path/command{labelSuffix}: {resolved} (allowed: {string.Join(", ", allowedDirs)})");
        }

        return anyAllowed;
    }

    internal static async Task<SessionConfig> BuildSessionConfig(
        SkillInfo? skill,
        string? pluginRoot,
        string model,
        string workDir,
        IReadOnlyDictionary<string, MCPServerDef>? mcpServers = null,
        IReadOnlyList<SkillInfo>? additionalSkills = null,
        Action<string>? log = null,
        bool verbose = false,
        string? sessionsDir = null,
        string? sessionId = null,
        AgentInfo? agent = null,
        IReadOnlyList<AgentInfo>? additionalAgents = null)
    {
        // Runtime guard: Skill and Agent are mutually exclusive targets.
        // (additionalSkills/additionalAgents are cross-dependencies and may co-exist with either target.)
        if (skill is not null && agent is not null)
            throw new InvalidOperationException("BuildSessionConfig cannot have both Skill and Agent set.");

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
            configDir = Path.Combine(GetEvaluationRoot(), $"sv-cfg-{Guid.NewGuid():N}");
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
            var stageDir = Path.Combine(GetEvaluationRoot(), $"sv-noise-{Guid.NewGuid():N}");
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

        // Convert MCPServerDef records to the SDK's McpServerConfig shape
        // Security hardening: validate commands, sanitize args/env, drop custom cwd.
        IDictionary<string, McpServerConfig>? sdkMcp = null;
        if (mcpServers is { Count: > 0 })
        {
            sdkMcp = new Dictionary<string, McpServerConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, def) in mcpServers)
            {
                if (!IsAllowedMcpCommand(def.Command))
                {
                    Console.Error.WriteLine(
                        $"Skipping MCP server '{name}': command '{def.Command}' is not in the allowlist");
                    continue;
                }

                // Only stdio servers are supported; reject unknown types early.
                if (def.Type is not null and not "stdio")
                {
                    Console.Error.WriteLine(
                        $"Skipping MCP server '{name}': unsupported type '{def.Type}' (only 'stdio' is supported)");
                    continue;
                }

                var sanitizedArgs = SanitizeMcpArgs(def.Command, def.Args);
                if (sanitizedArgs is null)
                {
                    Console.Error.WriteLine(
                        $"Skipping MCP server '{name}': args contain dangerous eval/exec flags");
                    continue;
                }

                var hardenedLaunch = HardenedBinlogMcpLaunch.Value;
                var entry = new McpStdioServerConfig
                {
                    Command = def.Command,
                    Args = [.. hardenedLaunch.Args],
                    Tools = def.Tools ?? [],
                    Env = new Dictionary<string, string>(hardenedLaunch.Environment),
                };

                if (def.Env is { Count: > 0 })
                {
                    Console.Error.WriteLine(
                        $"Ignoring plugin-supplied environment variables for MCP server '{name}'");
                }

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
            skillDirs = await StagePluginSkillDirectories(pluginRoot);
        }
        else if (skill is not null)
        {
            // Stage the single skill into a temp directory so the SDK discovers
            // only this skill — not every sibling that shares the same parent.
            // Copy the full directory tree (references/, scripts/, etc.) so that
            // relative links inside SKILL.md continue to resolve.
            var isoStageDir = Path.Combine(GetEvaluationRoot(), $"sv-iso-{Guid.NewGuid():N}");
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

        // Agents should access only the staged copies, never the original skill
        // or plugin tree. Custom-agent prompts are already loaded into memory.
        // The staged roots above contain all runtime skill content and are the
        // only skill directories added to the per-session allowlist.

        // Build CustomAgents list for agent evaluation.
        // In plugin mode: register all plugin agents. In isolated mode: register
        // only the target agent (+ additional declared agents). In baseline: none.
        List<CustomAgentConfig>? customAgents = null;
        if (pluginRoot is not null && agent is not null)
        {
            // Plugin run: discover and register all agents in the plugin
            var pluginAgents = await AgentDiscovery.DiscoverAgentsInPlugin(pluginRoot);
            if (pluginAgents.Count > 0)
            {
                customAgents = pluginAgents.Select(a => BuildCustomAgentConfig(a)).ToList();
                if (verbose)
                    log?.Invoke($"      🤖 Registered {customAgents.Count} plugin agent(s): {string.Join(", ", customAgents.Select(a => a.Name))}");
            }
        }
        else if (agent is not null)
        {
            // Isolated agent run: register only the target agent + declared dependencies
            customAgents = new[] { agent }
                .Concat(additionalAgents ?? [])
                .DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(BuildCustomAgentConfig)
                .ToList();
            if (verbose)
                log?.Invoke($"      🤖 Registered agent(s) (isolated): {string.Join(", ", customAgents.Select(a => a.Name))}");
        }
        else if (additionalAgents is { Count: > 0 })
        {
            // Skill isolated run with declared agent dependencies
            customAgents = additionalAgents.Select(a => BuildCustomAgentConfig(a)).ToList();
            if (verbose)
                log?.Invoke($"      🤖 Registered additional agent(s): {string.Join(", ", customAgents.Select(a => a.Name))}");
        }

        var runLabel =
            agent is not null
                ? (pluginRoot is not null ? "agent-plugin" : "agent-isolated")
                : (skill is not null ? "skilled" : "baseline");

        return new SessionConfig
        {
            Model = model,
            Streaming = true,
            WorkingDirectory = workDir,
            SkillDirectories = [..skillDirs, ..noiseDirs],
            ConfigDirectory = configDir,
            McpServers = sdkMcp,
            CustomAgents = customAgents,
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            // The SDK uses one SessionFsProvider for both session-state I/O and
            // built-in file tools. Keep state under configDir, resolve relative
            // tool paths under workDir, and permit absolute paths only within
            // this scenario workspace and its explicitly staged skill roots.
            CreateSessionFsProvider = _ => new LocalSessionFsHandler(
                configDir,
                workDir,
                new[] { workDir }.Concat(additionalAllowedDirs)),
            OnPermissionRequest = (request, _) =>
                Task.FromResult(DecidePermissionRequest(
                    request,
                    workDir,
                    verbose ? log : null,
                    runLabel,
                    additionalAllowedDirs,
                    sdkMcp)),
            Hooks = new SessionHooks
            {
                OnPreToolUse = (input, invocation) =>
                {
                    if (IsShellTool(input.ToolName))
                    {
                        return Task.FromResult<PreToolUseHookOutput?>(new PreToolUseHookOutput
                        {
                            PermissionDecision = "ask",
                            PermissionDecisionReason = "Validate shell command paths",
                        });
                    }

                    var reqPaths = ExtractPathsFromToolArgs(input);
                    if (reqPaths.Any(LocalSessionFsHandler.IsSessionStatePath))
                    {
                        return Task.FromResult<PreToolUseHookOutput?>(new PreToolUseHookOutput
                        {
                            PermissionDecision = "deny",
                            PermissionDecisionReason = "Session state is reserved for the evaluator",
                        });
                    }
                    var allowed = CheckPermissions(
                        reqPaths,
                        workDir,
                        skillPath: null,
                        verbose ? log : null,
                        runLabel,
                        pluginRoot: null,
                        additionalAllowedDirs);
                    return Task.FromResult<PreToolUseHookOutput?>(new PreToolUseHookOutput
                    {
                        PermissionDecision = allowed ? "allow" : "deny",
                        PermissionDecisionReason = allowed ? null : "Path outside allowed directories",
                    });
                },
            },
        };
    }

    internal static GitHub.Copilot.Rpc.PermissionDecision DecidePermissionRequest(
        PermissionRequest request,
        string workDir,
        Action<string>? log,
        string runLabel,
        IReadOnlyList<string> additionalAllowedDirs,
        IDictionary<string, McpServerConfig>? allowedMcpServers)
    {
        GitHub.Copilot.Rpc.PermissionDecision CheckPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)
                || LocalSessionFsHandler.IsSessionStatePath(path)
                || !CheckPermission(
                    path,
                    workDir,
                    skillPath: null,
                    log,
                    runLabel,
                    pluginRoot: null,
                    additionalAllowedDirs))
            {
                return GitHub.Copilot.Rpc.PermissionDecision.Reject(
                    "Path outside allowed directories");
            }

            return GitHub.Copilot.Rpc.PermissionDecision.ApproveOnce();
        }

        return request switch
        {
            PermissionRequestShell shellRequest => CheckShellPermission(
                shellRequest,
                workDir,
                skillPath: null,
                log,
                runLabel,
                pluginRoot: null,
                additionalAllowedDirs)
                    ? GitHub.Copilot.Rpc.PermissionDecision.ApproveOnce()
                    : GitHub.Copilot.Rpc.PermissionDecision.Reject(
                        "Path outside allowed directories, network access requested, or command not allowlisted"),
            PermissionRequestRead readRequest => CheckPath(readRequest.Path),
            PermissionRequestWrite writeRequest => CheckPath(writeRequest.FileName),
            PermissionRequestMcp mcpRequest => IsAllowedMcpPermission(
                mcpRequest,
                allowedMcpServers)
                    ? GitHub.Copilot.Rpc.PermissionDecision.ApproveOnce()
                    : GitHub.Copilot.Rpc.PermissionDecision.Reject(
                        "MCP server or tool is not allowed"),
            PermissionRequestUrl => GitHub.Copilot.Rpc.PermissionDecision.Reject(
                "Network access is not allowed during evaluation"),
            _ => GitHub.Copilot.Rpc.PermissionDecision.Reject(
                "Unsupported permission request during evaluation"),
        };
    }

    private static bool IsAllowedMcpPermission(
        PermissionRequestMcp request,
        IDictionary<string, McpServerConfig>? allowedMcpServers)
    {
        if (allowedMcpServers is null
            || string.IsNullOrWhiteSpace(request.ServerName)
            || string.IsNullOrWhiteSpace(request.ToolName)
            || !allowedMcpServers.TryGetValue(request.ServerName, out var server))
        {
            return false;
        }

        return server.Tools is { Count: > 0 } tools && tools.Any(tool =>
            tool == "*"
            || tool.Equals(request.ToolName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds a CustomAgentConfig from an AgentInfo, stripping frontmatter from the prompt body.
    /// </summary>
    internal static CustomAgentConfig BuildCustomAgentConfig(AgentInfo agent)
    {
        var (_, body) = FrontmatterParser.SplitFrontmatter(agent.AgentMdContent);
        return new CustomAgentConfig
        {
            Name = agent.Name,
            DisplayName = agent.Name,
            Description = agent.Description,
            Prompt = body,
            Tools = agent.Tools?.ToList(),
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
            pluginInfo = PluginDiscovery.ParsePluginJson(pluginJsonPath);
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
            if (!PluginDiscovery.TryGetSafeSubdirectory(pluginRoot, relativePath, out var fullPath, out _))
                continue;
            if (Directory.Exists(fullPath!))
                dirs.Add(fullPath!);
        }
        return dirs.ToArray();
    }

    private static async Task<string[]> StagePluginSkillDirectories(string pluginRoot)
    {
        var stagedRoots = new List<string>();
        foreach (var sourceRoot in ResolvePluginSkillDirectories(pluginRoot))
        {
            var skills = await SkillDiscovery.DiscoverSkills(sourceRoot, pluginRoot);
            if (skills.Count == 0)
                continue;

            var stageDir = Path.Combine(GetEvaluationRoot(), $"sv-plugin-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stageDir);
            _workDirs.Add(stageDir);
            foreach (var discoveredSkill in skills)
            {
                var stagedSkillDir = Path.Combine(stageDir, Path.GetFileName(discoveredSkill.Path));
                CopyDirectory(discoveredSkill.Path, stagedSkillDir);
            }
            stagedRoots.Add(stageDir);
        }
        return stagedRoots.ToArray();
    }

    public static async Task<RunMetrics> RunAgent(RunOptions options, CancellationToken cancellationToken = default)
    {
        // Validate mutual exclusivity
        if (options.Skill is not null && options.Agent is not null)
            throw new InvalidOperationException("RunOptions cannot have both Skill and Agent set.");

        var runType = options.Skill is null && options.Agent is null ? "baseline"
            : options.PluginRoot is not null ? (options.Agent is not null ? "agent-plugin" : "skilled-plugin")
            : options.Agent is not null ? "agent-isolated"
            : "skilled-isolated";
        return await RetryHelper.ExecuteWithRetry(
            async ct => await RunAgentCore(options, ct),
            label: $"RunAgent({options.Scenario.Name}, {runType})",
            maxRetries: 2,
            baseDelayMs: 5_000,
            totalTimeoutMs: (options.Scenario.Timeout + 60) * 1000,
            cancellationToken: cancellationToken);
    }

    private static async Task<RunMetrics> RunAgentCore(RunOptions options, CancellationToken cancellationToken)
    {
        var workDir = await SetupWorkDir(options.Scenario, options.Skill?.Path, options.EvalPath);
        if (options.Verbose)
        {
            var write = options.Log ?? (msg => Console.Error.WriteLine(msg));
            write($"      📂 Work dir: {workDir} ({(options.Skill is not null ? "skilled" : "baseline")})");
        }

        var eventBuffer = new RunEventBuffer();
        var startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool timedOut = false;

        try
        {
            // All runs use the same client — plugin skills are loaded manually
            // via SkillDirectories (--plugin-dir is not honored by SDK).
            var client = await GetPluginClient(options.PluginRoot, options.Verbose);

            await using var session = await client.CreateSessionAsync(
                await BuildSessionConfig(options.Skill, options.PluginRoot, options.Model, workDir, options.McpServers,
                    options.AdditionalSkills, options.Log, options.Verbose, options.SessionsDir, options.SessionId,
                    options.Agent, options.AdditionalAgents));

            var done = new TaskCompletionSource();
            var effectiveTimeout = options.Scenario.Timeout;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveTimeout * 1000);
            cts.Token.Register(() =>
                done.TrySetException(new TimeoutException($"Scenario timed out after {effectiveTimeout}s")));

            // Register event handler BEFORE SelectAsync so SubagentSelectedEvent
            // from the agent selection is captured in the events list.
            session.On<SessionEvent>(evt =>
            {
                eventBuffer.Record(evt.Type, (agentEvent, agentOutput) =>
                {
                    // Copy known event data
                    switch (evt)
                    {
                        case AssistantMessageDeltaEvent delta:
                            agentEvent.Data["deltaContent"] = JsonValue.Create(delta.Data.DeltaContent);
                            agentOutput.Append(delta.Data.DeltaContent);
                            break;
                        case AssistantMessageEvent msg:
                            agentEvent.Data["content"] = JsonValue.Create(msg.Data.Content);
                            if (!string.IsNullOrEmpty(msg.Data.Content))
                            {
                                agentOutput.Clear();
                                agentOutput.Append(msg.Data.Content);
                            }
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
                            agentEvent.Data["success"] = JsonValue.Create(toolComplete.Data.Success);
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
                        case SubagentStartedEvent subagentStarted:
                            agentEvent.Data["agentName"] = JsonValue.Create(subagentStarted.Data.AgentName);
                            agentEvent.Data["agentDisplayName"] = JsonValue.Create(subagentStarted.Data.AgentDisplayName);
                            agentEvent.Data["agentDescription"] = JsonValue.Create(subagentStarted.Data.AgentDescription);
                            agentEvent.Data["toolCallId"] = JsonValue.Create(subagentStarted.Data.ToolCallId);
                            if (options.Verbose)
                            {
                                var write = options.Log ?? (m => Console.Error.WriteLine(m));
                                write($"      🤖 Subagent started: {subagentStarted.Data.AgentName}");
                            }
                            break;
                        case SubagentCompletedEvent subagentCompleted:
                            agentEvent.Data["agentName"] = JsonValue.Create(subagentCompleted.Data.AgentName);
                            agentEvent.Data["agentDisplayName"] = JsonValue.Create(subagentCompleted.Data.AgentDisplayName);
                            agentEvent.Data["toolCallId"] = JsonValue.Create(subagentCompleted.Data.ToolCallId);
                            if (options.Verbose)
                            {
                                var write = options.Log ?? (m => Console.Error.WriteLine(m));
                                write($"      ✅ Subagent completed: {subagentCompleted.Data.AgentName}");
                            }
                            break;
                        case SubagentFailedEvent subagentFailed:
                            agentEvent.Data["agentName"] = JsonValue.Create(subagentFailed.Data.AgentName);
                            agentEvent.Data["agentDisplayName"] = JsonValue.Create(subagentFailed.Data.AgentDisplayName);
                            agentEvent.Data["toolCallId"] = JsonValue.Create(subagentFailed.Data.ToolCallId);
                            agentEvent.Data["error"] = JsonValue.Create(subagentFailed.Data.Error);
                            if (options.Verbose)
                            {
                                var write = options.Log ?? (m => Console.Error.WriteLine(m));
                                write($"      ❌ Subagent failed: {subagentFailed.Data.AgentName}");
                            }
                            break;
                        case SubagentSelectedEvent subagentSelected:
                            agentEvent.Data["agentName"] = JsonValue.Create(subagentSelected.Data.AgentName);
                            agentEvent.Data["agentDisplayName"] = JsonValue.Create(subagentSelected.Data.AgentDisplayName);
                            break;
                        case SubagentDeselectedEvent:
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
                });
            });

            // Expected-active custom-agent evaluation selects the target as the
            // primary persona. A successful SelectAsync is recorded directly for
            // the activation gate; optional SDK subagent events continue to report
            // later delegation and organic routing.
            if (options.Agent is not null && options.SelectAgentAsPrimary)
            {
                await session.Rpc.Agent.SelectAsync(options.Agent.Name);
                eventBuffer.Record("agent.primary_selected", (agentEvent, _) =>
                {
                    agentEvent.Data["agentName"] = JsonValue.Create(options.Agent.Name);
                });
                if (options.Verbose)
                {
                    var write = options.Log ?? (msg => Console.Error.WriteLine(msg));
                    write($"      🤖 Agent selected: {options.Agent.Name}");
                }
            }

            await session.SendAsync(new MessageOptions { Prompt = options.Scenario.Prompt });
            await done.Task;
        }
        catch (TimeoutException te)
        {
            timedOut = true;
            eventBuffer.Add(new AgentEvent(
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
                eventBuffer.Add(new AgentEvent(
                    "runner.timeout",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    new Dictionary<string, JsonNode?> { ["message"] = JsonValue.Create(msg) }));
            }
            else if (!eventBuffer.ContainsType("session.error"))
            {
                // Only add runner.error when there isn't already a session.error event
                eventBuffer.Add(new AgentEvent(
                    "runner.error",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    new Dictionary<string, JsonNode?> { ["message"] = JsonValue.Create(msg) }));
            }
        }

        var wallTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startTime;
        var (events, agentOutput) = eventBuffer.Snapshot();
        var metrics = MetricsCollector.CollectMetrics(events, agentOutput, wallTimeMs, workDir);
        metrics.TimedOut = timedOut;
        return metrics;
    }

    internal static async Task<string> SetupWorkDir(EvalScenario scenario, string? skillPath, string? evalPath)
    {
        var workDir = Path.Combine(GetEvaluationRoot(), $"sv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        _workDirs.Add(workDir);

        // Copy all sibling files from the eval directory when opted in
        if (evalPath is not null && scenario.Setup?.CopyTestFiles == true)
        {
            var evalDir = Path.GetDirectoryName(evalPath)!;
            foreach (var entry in new DirectoryInfo(evalDir).EnumerateFileSystemInfos())
            {
                if (EvaluatorOnlySetupEntries.Contains(entry.Name))
                    continue;
                FileAttributes attributes;
                try
                {
                    attributes = entry.Attributes;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"Unable to inspect setup entry, skipping: {entry.FullName}");
                    continue;
                }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Console.Error.WriteLine($"Setup entry is a symbolic link or reparse point, skipping: {entry.FullName}");
                    continue;
                }
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
                    if (Directory.Exists(resolvedSource))
                    {
                        Directory.CreateDirectory(targetPath);
                        CopyDirectory(resolvedSource, targetPath);
                    }
                    else
                    {
                        File.Copy(resolvedSource, targetPath, true);
                    }
                }
            }
        }

        // Run setup commands (e.g. build to produce a binlog, then strip sources)
        if (scenario.Setup?.Commands is { } commands)
        {
            foreach (var cmd in commands)
            {
                await RunSetupCommand(cmd, workDir);
            }
        }

        return workDir;
    }

    internal static async Task RunSetupCommand(
        string command,
        string workDir,
        TimeSpan? timeout = null)
    {
        var psi = CreateSetupProcessStartInfo(command, workDir);
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Setup command failed to start: {command}");
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(120));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(); } catch { }
            throw new TimeoutException($"Setup command timed out and was killed: {command}");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Setup command exited with code {process.ExitCode}: {command}");
        }
    }

    internal static ProcessStartInfo CreateSetupProcessStartInfo(string command, string workDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            WorkingDirectory = workDir,
            UseShellExecute = false,
        };

        // Setup output is intentionally inherited. Redirecting unused streams
        // can deadlock when a verbose command fills an unread OS pipe.
        ScrubSensitiveEnvironment(psi);
        return psi;
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
        var allowedRoot = evalPath is null
            ? canonicalBaseDir
            : FindRepositoryRoot(canonicalBaseDir) ?? canonicalBaseDir;
        var normalizedAllowedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
        // Vally fixture paths are relative to eval.yaml and may intentionally
        // reference a sibling eval's shared fixtures. Keep them inside the
        // repository root; standalone legacy evals remain confined to their
        // own eval/skill directory when no repository root can be identified.
        if (!sourcePath.StartsWith(normalizedAllowedRoot + Path.DirectorySeparatorChar, pathComparison)
            && !sourcePath.Equals(normalizedAllowedRoot, pathComparison))
        {
            Console.Error.WriteLine($"Setup file source escapes the allowed repository directory, skipping: {source}");
            return null;
        }
        if (PathSafety.ContainsReparsePoint(
            normalizedAllowedRoot,
            sourcePath,
            missingPathIsUnsafe: false))
        {
            Console.Error.WriteLine($"Setup file source contains a symbolic link or reparse point, skipping: {source}");
            return null;
        }

        return sourcePath;
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "plugins"))
                && Directory.Exists(Path.Combine(current.FullName, "tests")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        return null;
    }

    private static readonly string[] SensitiveEnvKeys =
    [
        "GH_TOKEN",
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
        "NUGET_CREDENTIALPROVIDERS_PATH",
        "NUGET_FALLBACK_PACKAGES",
        "NUGET_HTTP_CACHE_PATH",
        "NUGET_NETCORE_PLUGIN_PATHS",
        "NUGET_PACKAGES",
        "NUGET_PLUGIN_PATHS",
        "NUGET_SCRATCH",
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
        "dotnet",
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

    private static readonly string[] AllowedBinlogMcpArgs =
        ["dnx", "Microsoft.AITools.BinlogMcp", "--yes", "--prerelease"];

    private const string BinlogMcpPackage = "Microsoft.AITools.BinlogMcp";
    private const string BinlogMcpVersion = "3.0.2";
    private const string TrustedNugetSource = "https://api.nuget.org/v3/index.json";
    private static readonly Lazy<HardenedMcpLaunch> HardenedBinlogMcpLaunch =
        new(CreateHardenedBinlogMcpLaunch);

    internal static string[]? SanitizeMcpArgs(string command, string[] args)
    {
        var cmdName = Path.GetFileNameWithoutExtension(command);
        if (!cmdName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return null;

        return args.SequenceEqual(AllowedBinlogMcpArgs, StringComparer.Ordinal)
            ? [.. args]
            : null;
    }

    private static HardenedMcpLaunch CreateHardenedBinlogMcpLaunch()
    {
        var root = Path.Combine(GetEvaluationRoot(), $"sv-mcp-{Guid.NewGuid():N}");
        var packages = Path.Combine(root, "packages");
        var httpCache = Path.Combine(root, "http-cache");
        Directory.CreateDirectory(packages);
        Directory.CreateDirectory(httpCache);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        };

        var configPath = Path.Combine(root, "NuGet.config");
        File.WriteAllText(
            configPath,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="nuget.org" value="{TrustedNugetSource}" protocolVersion="3" />
              </packageSources>
              <fallbackPackageFolders>
                <clear />
              </fallbackPackageFolders>
              <packageSourceMapping>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);

        return new HardenedMcpLaunch(
            Args:
            [
                "dnx",
                $"{BinlogMcpPackage}@{BinlogMcpVersion}",
                "--yes",
                "--configfile",
                configPath,
            ],
            Environment: new Dictionary<string, string>
            {
                ["NUGET_PACKAGES"] = packages,
                ["NUGET_HTTP_CACHE_PATH"] = httpCache,
                ["NUGET_FALLBACK_PACKAGES"] = "",
            });
    }

    private sealed record HardenedMcpLaunch(
        string[] Args,
        Dictionary<string, string> Environment);

    /// <summary>
    /// Recursively copies a directory tree, skipping symlinks and reparse
    /// points so that staging cannot pull in content from outside the
    /// source root.
    /// </summary>
    private static void CopyDirectory(string source, string destination)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Unable to inspect source directory '{source}'.", ex);
        }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing to copy symbolic link or reparse-point directory '{source}'.");

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
