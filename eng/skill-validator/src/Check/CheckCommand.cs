using System.CommandLine;
using System.Text.Json;
using SkillValidator.Shared;

namespace SkillValidator.Check;

public static class CheckCommand
{
    private static readonly StringComparison s_pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static readonly StringComparer s_pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    // A skill with `disable-model-invocation: true` in its frontmatter is
    // dropped from the Copilot CLI's model-facing skill menu and therefore does
    // not consume the skill-menu character budget tracked by
    // SkillProfiler.MaxRenderedSkillMenuLength. The flag is parsed once during
    // discovery and surfaced on SkillInfo.DisableModelInvocation.

    public static Command Create()
    {
        var pluginOpt = new Option<string[]>("--plugin") { Description = "Plugin directories to check (discovers skills, agents, plugin.json)", AllowMultipleArgumentsPerToken = true };
        var skillsOpt = new Option<string[]>("--skills") { Description = "Skill directories to check (skills only)", AllowMultipleArgumentsPerToken = true };
        var agentsOpt = new Option<string[]>("--agents") { Description = "Agent directories to check (agents only)", AllowMultipleArgumentsPerToken = true };
        var allowedExternalDepsOpt = new Option<string?>("--allowed-external-deps") { Description = "Path to allowed-external-deps.txt allow list file" };
        var knownDomainsOpt = new Option<string?>("--known-domains") { Description = "Path to known-domains.txt for reference scanning" };
        var verboseOpt = new Option<bool>("--verbose") { Description = "Show detailed output" };
        var allowRepoTraversalOpt = new Option<bool>("--allow-repo-traversal") { Description = "Allow file references that point outside the skill directory: parent-directory paths (../…) and absolute repo-rooted paths (/src/…). Use for skills shipped inside a repo, not standalone." };
        var jsonOutputOpt = new Option<bool>("--json") { Description = "Write machine-readable JSON report to stdout", DefaultValueFactory = (_) => false };

        var command = new Command("check", "Run static analysis checks on skills, plugins, and agents (no LLM required). Use --plugin to check an entire plugin directory (recommended).")
        {
            pluginOpt,
            skillsOpt,
            agentsOpt,
            allowedExternalDepsOpt,
            knownDomainsOpt,
            verboseOpt,
            allowRepoTraversalOpt,
            jsonOutputOpt,
        };

        command.SetAction(async (parseResult, _) =>
        {
            var config = new CheckConfig
            {
                PluginPaths = parseResult.GetValue(pluginOpt) ?? [],
                SkillPaths = parseResult.GetValue(skillsOpt) ?? [],
                AgentPaths = parseResult.GetValue(agentsOpt) ?? [],
                AllowedExternalDepsFile = parseResult.GetValue(allowedExternalDepsOpt),
                KnownDomainsFile = parseResult.GetValue(knownDomainsOpt),
                Verbose = parseResult.GetValue(verboseOpt),
                CheckOptions = new CheckOptions
                {
                    AllowRepoTraversal = parseResult.GetValue(allowRepoTraversalOpt),
                },
                OutputMode = parseResult.GetValue(jsonOutputOpt) ? CheckOutputMode.Json : CheckOutputMode.Console,
            };

            return await Run(config);
        });

        return command;
    }

    public static async Task<int> Run(CheckConfig config)
    {
        var report = await BuildReport(config);
        RenderReport(report);
        return report.ExitCode;
    }

    private static async Task<CheckReport> BuildReport(CheckConfig config)
    {
        var builder = new CheckReportBuilder(config, DetermineScope(config));

        if (!ValidateConfig(config, builder))
            return builder.Build(1);

        int exitCode = config.PluginPaths.Count > 0
            ? await RunPluginCheck(config, builder)
            : config.SkillPaths.Count > 0 && config.AgentPaths.Count > 0
                ? await RunSkillsAndAgentsCheck(config, builder)
                : config.SkillPaths.Count > 0
                    ? await RunSkillsCheck(config, builder)
                    : await RunAgentsCheck(config, builder);

        return builder.Build(exitCode);
    }

    private static bool ValidateConfig(CheckConfig config, CheckReportBuilder builder)
    {
        bool hasPlugin = config.PluginPaths.Count > 0;
        bool hasSkills = config.SkillPaths.Count > 0;
        bool hasAgents = config.AgentPaths.Count > 0;

        if (!hasPlugin && !hasSkills && !hasAgents)
        {
            builder.AddGeneralError("Specify one of --plugin, --skills, or --agents. Use --plugin to check an entire plugin directory.");
            return false;
        }

        if (hasPlugin && (hasSkills || hasAgents))
        {
            builder.AddGeneralError("--plugin cannot be combined with --skills or --agents. Use --plugin alone to check an entire plugin directory.");
            return false;
        }

        if (config.CheckOptions.AllowRepoTraversal && hasPlugin)
        {
            builder.AddGeneralError("--allow-repo-traversal cannot be used with --plugin. Plugins must be portable — use --skills or --agents instead.");
            return false;
        }

        return true;
    }

    private static string DetermineScope(CheckConfig config)
    {
        if (config.PluginPaths.Count > 0)
            return "plugin";
        if (config.SkillPaths.Count > 0 && config.AgentPaths.Count > 0)
            return "skillsAndAgents";
        if (config.SkillPaths.Count > 0)
            return "skills";
        if (config.AgentPaths.Count > 0)
            return "agents";

        return "invalid";
    }

    private static async Task<int> RunPluginCheck(CheckConfig config, CheckReportBuilder builder)
    {
        var allPlugins = new List<PluginInfo>();
        var pluginSkills = new Dictionary<string, List<SkillInfo>>(s_pathComparer);
        var allSkillsList = new List<SkillInfo>();
        var agentDirs = new List<string>();

        foreach (var pluginDir in config.PluginPaths)
        {
            var fullPath = Path.GetFullPath(pluginDir);
            var pluginJsonPath = Path.Combine(fullPath, "plugin.json");

            if (!File.Exists(pluginJsonPath))
            {
                builder.AddGeneralError($"No plugin.json found in '{pluginDir}'");
                return 1;
            }

            PluginInfo? plugin;
            try
            {
                plugin = PluginDiscovery.ParsePluginJson(pluginJsonPath);
            }
            catch (JsonException ex)
            {
                builder.AddGeneralError($"Malformed plugin.json in '{pluginDir}': {ex.Message}");
                return 1;
            }

            if (plugin is null)
            {
                builder.AddGeneralError($"Failed to parse plugin.json in '{pluginDir}'");
                return 1;
            }

            allPlugins.Add(plugin);

            foreach (var skillPath in plugin.SkillPaths)
            {
                if (!PluginDiscovery.TryGetSafeSubdirectory(fullPath, skillPath, out var dir, out _) || !Directory.Exists(dir))
                    continue;

                var skills = await SkillDiscovery.DiscoverSkills(dir!);
                if (!pluginSkills.TryGetValue(plugin.DirectoryPath, out var pluginSkillList))
                {
                    pluginSkillList = [];
                    pluginSkills[plugin.DirectoryPath] = pluginSkillList;
                }

                pluginSkillList.AddRange(skills);
                allSkillsList.AddRange(skills);
            }

            var agents = await AgentDiscovery.DiscoverAgentsInPlugin(fullPath);
            foreach (var dir in agents.Select(a => Path.GetDirectoryName(a.Path)).Where(d => d is not null).Distinct())
                agentDirs.Add(dir!);
        }

        bool hasPluginErrors = false;
        foreach (var plugin in allPlugins)
        {
            var result = PluginProfiler.ValidatePlugin(plugin);
            builder.Plugins.Add(result);

            foreach (var error in result.Errors)
                hasPluginErrors = true;
        }

        if (hasPluginErrors)
        {
            return 1;
        }

        int skillResult = 0;
        if (allSkillsList.Count > 0)
        {
            if (ValidateSkillProfiles(builder, allSkillsList, config.Verbose, config.CheckOptions))
                skillResult = 1;

            if (CheckDuplicateSkillNames(builder, builder.Skills))
                skillResult = 1;
        }

        var (allAgents, discoveredAgents, agentResult) = await RunAgentsCheckCore(builder, agentDirs.Distinct().ToList());

        if (allSkillsList.Count == 0 && allAgents.Count == 0)
        {
            builder.AddGeneralError("No skills or agents found in the specified plugin(s).");
            return 1;
        }

        foreach (var (pluginDirectoryPath, skills) in pluginSkills)
        {
            // Sum each model-invocable skill's RENDERED menu cost — the full
            // <skill> block the Copilot CLI emits (name + description + location
            // + markup), via SkillProfiler.RenderedSkillMenuCost — so this
            // mirrors the real SKILL_CHAR_BUDGET rather than just the raw
            // description length.
            //
            // Skills hidden from the model-facing skill menu via
            // `disable-model-invocation: true` do not consume that budget, so
            // they are excluded from the aggregate (see
            // SkillProfiler.MaxRenderedSkillMenuLength).
            int totalChars = skills
                .Where(s => !s.DisableModelInvocation)
                .Sum(SkillProfiler.RenderedSkillMenuCost);
            if (totalChars <= SkillProfiler.MaxRenderedSkillMenuLength)
                continue;

            var pluginResult = builder.Plugins.FirstOrDefault(p => string.Equals(p.DirectoryPath, pluginDirectoryPath, s_pathComparison));
            var pluginLabel = pluginResult?.Name
                ?? Path.GetFileName(pluginDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var message = $"Plugin '{pluginLabel}' rendered skill-menu size is {totalChars:N0} characters — maximum is {SkillProfiler.MaxRenderedSkillMenuLength:N0}.";
            if (pluginResult is not null)
                pluginResult.Errors.Add(message);
            else
                builder.AddGeneralError(message);

            return 1;
        }

        CheckExternalDeps(builder, config.AllowedExternalDepsFile, allSkillsList, discoveredAgents, allPlugins);

        if (RunReferenceScanner(builder, config.KnownDomainsFile, config.PluginPaths))
            return 1;

        if (skillResult != 0 || agentResult != 0)
            return 1;
        return 0;
    }

    private static async Task<int> RunSkillsCheck(CheckConfig config, CheckReportBuilder builder)
    {
        var (skills, result) = await RunSkillsCheckCore(builder, config.SkillPaths, config.Verbose, config.CheckOptions);

        if (skills.Count == 0)
            return 1;
        if (result != 0)
            return result;

        if (RunReferenceScanner(builder, config.KnownDomainsFile, config.SkillPaths))
            return 1;

        return 0;
    }

    private static async Task<int> RunAgentsCheck(CheckConfig config, CheckReportBuilder builder)
    {
        var (agents, _, result) = await RunAgentsCheckCore(builder, config.AgentPaths);

        if (agents.Count == 0)
            return 1;
        if (result != 0)
            return result;

        if (RunReferenceScanner(builder, config.KnownDomainsFile, config.AgentPaths))
            return 1;

        return 0;
    }

    private static async Task<int> RunSkillsAndAgentsCheck(CheckConfig config, CheckReportBuilder builder)
    {
        var (skills, skillResult) = await RunSkillsCheckCore(builder, config.SkillPaths, config.Verbose, config.CheckOptions);
        var (agents, _, agentResult) = await RunAgentsCheckCore(builder, config.AgentPaths);

        if (skills.Count == 0 && agents.Count == 0)
            return 1;

        if (skillResult != 0 || agentResult != 0)
            return 1;

        var allDirs = config.SkillPaths.Concat(config.AgentPaths).ToList();
        if (RunReferenceScanner(builder, config.KnownDomainsFile, allDirs))
            return 1;

        return 0;
    }

    private static async Task<(IReadOnlyList<SkillCheckResult> Skills, int Result)> RunSkillsCheckCore(CheckReportBuilder builder, IReadOnlyList<string> skillPaths, bool verbose, CheckOptions? checkOptions = null)
    {
        var allSkills = new List<SkillInfo>();
        foreach (var path in skillPaths)
        {
            var skills = await SkillDiscovery.DiscoverSkills(path);
            allSkills.AddRange(skills);
        }

        if (allSkills.Count == 0)
        {
            if (skillPaths.Count > 0)
            {
                var searched = string.Join(", ", skillPaths.Select(p => $"\"{Path.GetFullPath(p)}\""));
                builder.AddGeneralError($"No skills found in the specified paths: {searched}");
                return ([], 1);
            }

            return ([], 0);
        }

        bool hasErrors = ValidateSkillProfiles(builder, allSkills, verbose, checkOptions);

        if (CheckDuplicateSkillNames(builder, builder.Skills))
            hasErrors = true;

        return (builder.Skills, hasErrors ? 1 : 0);
    }

    private static async Task<(IReadOnlyList<AgentCheckResult> Agents, IReadOnlyList<AgentInfo> DiscoveredAgents, int Result)> RunAgentsCheckCore(CheckReportBuilder builder, IReadOnlyList<string> agentPaths)
    {
        var allAgents = new List<AgentInfo>();
        foreach (var path in agentPaths)
        {
            var agents = await AgentDiscovery.DiscoverAgentsInDirectory(path);
            allAgents.AddRange(agents);
        }

        if (allAgents.Count == 0)
        {
            if (agentPaths.Count > 0)
            {
                var searched = string.Join(", ", agentPaths.Select(p => $"\"{Path.GetFullPath(p)}\""));
                builder.AddGeneralError($"No agents found in the specified paths: {searched}");
                return ([], [], 1);
            }

            return ([], [], 0);
        }

        bool hasErrors = false;
        foreach (var agent in allAgents)
        {
            var profile = AgentProfiler.AnalyzeAgent(agent);
            var result = new AgentCheckResult
            {
                Name = profile.Name,
                FileName = profile.FileName,
                Path = agent.Path,
            };
            result.Warnings.AddRange(profile.Warnings);
            result.Errors.AddRange(profile.Errors);
            builder.Agents.Add(result);

            foreach (var error in profile.Errors)
                hasErrors = true;
        }

        if (hasErrors)
            return (builder.Agents, allAgents, 1);

        return (builder.Agents, allAgents, 0);
    }

    private static bool CheckDuplicateSkillNames(CheckReportBuilder builder, IReadOnlyList<SkillCheckResult> skills)
    {
        var seenNames = new Dictionary<string, string>(StringComparer.Ordinal);
        bool hasDuplicates = false;

        foreach (var skill in skills)
        {
            if (seenNames.TryGetValue(skill.Name, out var firstPath))
            {
                var message = $"Duplicate skill name '{skill.Name}' found in '{skill.Path}' (first seen in '{firstPath}')";
                skill.Errors.Add(message);
                hasDuplicates = true;
            }
            else
            {
                seenNames[skill.Name] = skill.Path;
            }
        }

        return hasDuplicates;
    }

    private static bool ValidateSkillProfiles(CheckReportBuilder builder, IReadOnlyList<SkillInfo> skills, bool verbose, CheckOptions? checkOptions = null)
    {
        bool hasErrors = false;
        foreach (var skill in skills)
        {
            var profile = SkillProfiler.AnalyzeSkill(skill, checkOptions);
            var result = new SkillCheckResult
            {
                Name = skill.Name,
                Path = skill.Path,
                SkillMdPath = skill.SkillMdPath,
                Profile = profile,
                ProfileLine = verbose ? SkillProfiler.FormatProfileLine(profile) : null,
            };

            result.Errors.AddRange(profile.Errors);
            result.Warnings.AddRange(profile.Warnings);
            builder.Skills.Add(result);

            foreach (var error in profile.Errors)
                hasErrors = true;
        }

        return hasErrors;
    }

    private static void CheckExternalDeps(CheckReportBuilder builder, string? allowedExternalDepsFile, IReadOnlyList<SkillInfo> skills, IReadOnlyList<AgentInfo> agents, IReadOnlyList<PluginInfo> plugins)
    {
        if (allowedExternalDepsFile is null)
            return;

        var allowed = ExternalDependencyChecker.LoadAllowList(allowedExternalDepsFile);

        foreach (var skill in skills)
        {
            foreach (var warning in ExternalDependencyChecker.CheckSkill(skill, allowed))
                builder.ExternalDependencies.Add(new ExternalDependencyResult(ExternalDependencyKind.Skill, skill.Name, skill.SkillMdPath, warning));
        }

        foreach (var agent in agents)
        {
            foreach (var warning in ExternalDependencyChecker.CheckAgent(agent, allowed))
                builder.ExternalDependencies.Add(new ExternalDependencyResult(ExternalDependencyKind.Agent, agent.Name, agent.Path, warning));
        }

        foreach (var plugin in plugins)
        {
            foreach (var warning in ExternalDependencyChecker.CheckPlugin(plugin, allowed))
                builder.ExternalDependencies.Add(new ExternalDependencyResult(ExternalDependencyKind.Plugin, plugin.Name, plugin.DirectoryPath, warning));
        }
    }

    private static bool RunReferenceScanner(CheckReportBuilder builder, string? knownDomainsFile, IReadOnlyList<string> directories)
    {
        if (knownDomainsFile is null)
        {
            builder.ReferenceScan = new ReferenceScanReport(ReferenceScanStatus.Disabled, null, 0, []);
            return false;
        }

        if (!File.Exists(knownDomainsFile))
        {
            builder.ReferenceScan = new ReferenceScanReport(ReferenceScanStatus.MissingKnownDomainsFile, knownDomainsFile, 0, []);
            builder.AddGeneralError($"Known-domains file not found: '{knownDomainsFile}'");
            return true;
        }

        var knownDomains = ReferenceScanner.LoadKnownDomains(knownDomainsFile);
        var files = ReferenceScanner.DiscoverFiles(directories);
        var findings = ReferenceScanner.ScanFiles(files, knownDomains, knownDomainsFile);
        builder.ReferenceScan = new ReferenceScanReport(ReferenceScanStatus.Completed, knownDomainsFile, files.Count, findings);

        if (findings.Count > 0)
            return true;

        return false;
    }

    private static void RenderReport(CheckReport report)
    {
        if (report.Invocation.OutputMode == CheckOutputMode.Json)
        {
            var jsonOutput = CreateJsonOutput(report);
            Console.Out.WriteLine(JsonSerializer.Serialize(jsonOutput, CheckJsonSerializerContext.Default.CheckJsonOutput));
            return;
        }

        RenderPlugins(report);
        RenderSkills(report);
        RenderAgents(report);
        RenderExternalDependencies(report);
        RenderReferenceScan(report);
        RenderGeneralErrors(report);

        if (report.Succeeded)
            Console.WriteLine($"{Ansi.Green}✅ {FormatSuccessSummary(report)}{Ansi.Reset}");
    }

    private static CheckJsonOutput CreateJsonOutput(CheckReport report)
    {
        var plugins = report.Plugins
            .Select(source => new CheckJsonPlugin(
                Name: source.Name,
                DirectoryPath: source.DirectoryPath,
                Errors: [.. source.Errors],
                Warnings: source.Warnings
                    .Select(warning => new CheckJsonWarning(CheckJsonWarningKinds.Validation, warning))
                    .ToList()))
            .ToList();

        var skills = report.Skills
            .Select(source => new CheckJsonSkill(
                Name: source.Name,
                Path: source.Path,
                SkillMdPath: source.SkillMdPath,
                Errors: [.. source.Errors],
                Warnings: source.Warnings
                    .Select(warning => new CheckJsonWarning(CheckJsonWarningKinds.Profile, warning))
                    .ToList(),
                Profile: source.Profile is null ? null : new CheckJsonSkillProfile(
                    Name: source.Profile.Name,
                    Chars4TokenCount: source.Profile.Chars4TokenCount,
                    BpeTokenCount: source.Profile.BpeTokenCount,
                    ComplexityTier: source.Profile.ComplexityTier,
                    SectionCount: source.Profile.SectionCount,
                    CodeBlockCount: source.Profile.CodeBlockCount,
                    NumberedStepCount: source.Profile.NumberedStepCount,
                    BulletCount: source.Profile.BulletCount,
                    HasFrontmatter: source.Profile.HasFrontmatter,
                    HasWhenToUse: source.Profile.HasWhenToUse,
                    HasWhenNotToUse: source.Profile.HasWhenNotToUse),
                ProfileLine: source.ProfileLine))
            .ToList();

        var agents = report.Agents
            .Select(source => new CheckJsonAgent(
                Name: source.Name,
                FileName: source.FileName,
                Path: source.Path,
                Errors: [.. source.Errors],
                Warnings: source.Warnings
                    .Select(warning => new CheckJsonWarning(CheckJsonWarningKinds.Validation, warning))
                    .ToList()))
            .ToList();

        var pluginsByPath = new Dictionary<string, CheckJsonPlugin>(s_pathComparer);
        foreach (var plugin in plugins)
            pluginsByPath.TryAdd(NormalizePathKey(plugin.DirectoryPath), plugin);

        var skillsByPath = new Dictionary<string, CheckJsonSkill>(s_pathComparer);
        foreach (var skill in skills)
            skillsByPath.TryAdd(NormalizePathKey(skill.SkillMdPath), skill);

        var agentsByPath = new Dictionary<string, CheckJsonAgent>(s_pathComparer);
        foreach (var agent in agents)
            agentsByPath.TryAdd(NormalizePathKey(agent.Path), agent);

        foreach (var dependency in report.ExternalDependencies)
        {
            var dependencyPath = NormalizePathKey(dependency.TargetPath);

            switch (dependency.Kind)
            {
                case ExternalDependencyKind.Plugin:
                    if (pluginsByPath.TryGetValue(dependencyPath, out var plugin))
                        plugin.Warnings.Add(new CheckJsonWarning(CheckJsonWarningKinds.ExternalDependency, dependency.Message));
                    break;
                case ExternalDependencyKind.Skill:
                    if (skillsByPath.TryGetValue(dependencyPath, out var skill))
                        skill.Warnings.Add(new CheckJsonWarning(CheckJsonWarningKinds.ExternalDependency, dependency.Message));
                    break;
                case ExternalDependencyKind.Agent:
                    if (agentsByPath.TryGetValue(dependencyPath, out var agent))
                        agent.Warnings.Add(new CheckJsonWarning(CheckJsonWarningKinds.ExternalDependency, dependency.Message));
                    break;
            }
        }

        var referenceTargets = CreateReferenceTargets(skills, agents, plugins);

        var topLevelErrors = report.GeneralErrors.ToList();

        if (report.ReferenceScan is not null)
        {
            foreach (var finding in report.ReferenceScan.Findings)
            {
                var formattedFinding = $"{finding.Path}:{finding.LineNum} [{finding.Code}] {finding.Message}";
                if (TryAttachReferenceFinding(referenceTargets, finding.Path, formattedFinding))
                    continue;

                topLevelErrors.Add(formattedFinding);
            }
        }

        return new CheckJsonOutput(
            Counts: new CheckJsonCounts(
                PluginCount: plugins.Count,
                SkillCount: skills.Count,
                AgentCount: agents.Count),
            Plugins: plugins,
            Skills: skills,
            Agents: agents,
            Errors: topLevelErrors.Count > 0 ? topLevelErrors : null);
    }

    private static void RenderPlugins(CheckReport report)
    {
        if (report.Plugins.Count == 0)
            return;

        foreach (var plugin in report.Plugins)
        {
            foreach (var warning in plugin.Warnings)
                Console.WriteLine($"{Ansi.Yellow}⚠  [plugin:{plugin.Name}] {warning}{Ansi.Reset}");
            foreach (var error in plugin.Errors)
                Console.Error.WriteLine($"{Ansi.Red}❌ [plugin:{plugin.Name}] {error}{Ansi.Reset}");
        }

        Console.WriteLine($"Validated {report.Plugins.Count} plugin(s)");

        if (report.Plugins.Any(plugin => plugin.Errors.Count > 0))
            Console.Error.WriteLine($"{Ansi.Red}Plugin spec conformance failures — fix the errors above.{Ansi.Reset}");
    }

    private static void RenderSkills(CheckReport report)
    {
        if (report.Skills.Count == 0)
            return;

        Console.WriteLine($"Found {report.Skills.Count} skill(s)");

        foreach (var skill in report.Skills)
        {
            if (skill.ProfileLine is not null)
                Console.WriteLine($"[{skill.Name}] 📊 {skill.ProfileLine}");

            foreach (var error in skill.Errors)
                Console.Error.WriteLine($"{Ansi.Red}❌ [{skill.Name}] {error}{Ansi.Reset}");

            var formattedWarnings = skill.Profile is null
                ? skill.Warnings
                : SkillProfiler.FormatProfileWarnings(skill.Profile);

            foreach (var warning in formattedWarnings)
                Console.WriteLine($"[{skill.Name}] {warning}");
        }

        if (report.Skills.Any(skill => skill.Errors.Count > 0))
            Console.Error.WriteLine($"{Ansi.Red}Skill spec conformance failures — fix the errors above.{Ansi.Reset}");
    }

    private static void RenderAgents(CheckReport report)
    {
        if (report.Agents.Count == 0)
            return;

        Console.WriteLine($"Found {report.Agents.Count} agent(s)");

        foreach (var agent in report.Agents)
        {
            foreach (var warning in agent.Warnings)
                Console.WriteLine($"{Ansi.Yellow}⚠  [agent:{agent.Name}] {warning}{Ansi.Reset}");
            foreach (var error in agent.Errors)
                Console.Error.WriteLine($"{Ansi.Red}❌ [agent:{agent.Name}] {error}{Ansi.Reset}");
        }

        Console.WriteLine($"Validated {report.Agents.Count} agent(s)");

        if (report.Agents.Any(agent => agent.Errors.Count > 0))
            Console.Error.WriteLine($"{Ansi.Red}Agent spec conformance failures — fix the errors above.{Ansi.Reset}");
    }

    private static void RenderExternalDependencies(CheckReport report)
    {
        if (report.ExternalDependencies.Count == 0)
            return;

        foreach (var dependency in report.ExternalDependencies)
            Console.WriteLine($"{Ansi.Yellow}⚠  [{dependency.Kind}:{dependency.Name}] {dependency.Message}{Ansi.Reset}");

        Console.WriteLine();
    }

    private static void RenderReferenceScan(CheckReport report)
    {
        if (report.ReferenceScan is null)
            return;

        if (report.ReferenceScan.Status == ReferenceScanStatus.Completed)
        {
            if (report.ReferenceScan.Findings.Count == 0)
            {
                Console.WriteLine($"--- Reference scan: {report.ReferenceScan.FilesScanned} file(s) scanned, 0 error(s) ---");
                return;
            }

            Console.Error.WriteLine($"\n  {report.ReferenceScan.Findings.Count} reference error(s):\n");
            foreach (var finding in report.ReferenceScan.Findings)
                Console.Error.WriteLine($"  {Ansi.Red}❌ {finding.Path}:{finding.LineNum} [{finding.Code}] {finding.Message}{Ansi.Reset}");
            Console.Error.WriteLine();
            Console.Error.WriteLine($"{Ansi.Red}--- Reference scan: {report.ReferenceScan.FilesScanned} file(s) scanned, {report.ReferenceScan.Findings.Count} error(s) ---{Ansi.Reset}");
        }
    }

    private static void RenderGeneralErrors(CheckReport report)
    {
        foreach (var error in report.GeneralErrors)
            Console.Error.WriteLine($"{Ansi.Red}❌ {error}{Ansi.Reset}");
    }

    private static string FormatSuccessSummary(CheckReport report) =>
        report.Scope switch
        {
            "plugin" => $"All checks passed ({report.Counts.SkillCount} skill(s), {report.Counts.AgentCount} agent(s), {report.Counts.PluginCount} plugin(s))",
            "skillsAndAgents" => $"All checks passed ({report.Counts.SkillCount} skill(s), {report.Counts.AgentCount} agent(s))",
            "skills" => $"All checks passed ({report.Counts.SkillCount} skill(s))",
            "agents" => $"All checks passed ({report.Counts.AgentCount} agent(s))",
            _ => "All checks passed",
        };

    private static IReadOnlyList<JsonReferenceTarget> CreateReferenceTargets(
        IReadOnlyList<CheckJsonSkill> skills,
        IReadOnlyList<CheckJsonAgent> agents,
        IReadOnlyList<CheckJsonPlugin> plugins)
    {
        var targets = new List<JsonReferenceTarget>(skills.Count * 2 + agents.Count * 2 + plugins.Count);

        foreach (var skill in skills)
        {
            TryAddReferenceTarget(targets, skill.Path, error => skill.Errors.Add(error));
            TryAddReferenceTarget(targets, skill.SkillMdPath, error => skill.Errors.Add(error));
        }

        foreach (var agent in agents)
        {
            TryAddReferenceTarget(targets, agent.Path, error => agent.Errors.Add(error));
            TryAddReferenceTarget(targets, Path.GetDirectoryName(agent.Path), error => agent.Errors.Add(error));
        }

        foreach (var plugin in plugins)
            TryAddReferenceTarget(targets, plugin.DirectoryPath, error => plugin.Errors.Add(error));

        return targets;
    }

    private static void TryAddReferenceTarget(List<JsonReferenceTarget> targets, string? containerPath, Action<string> addError)
    {
        if (string.IsNullOrWhiteSpace(containerPath))
            return;

        var normalizedPath = NormalizePathKey(containerPath);
        var normalizedDirectoryPath = File.Exists(normalizedPath)
            ? Path.GetDirectoryName(normalizedPath) ?? normalizedPath
            : normalizedPath;
        var normalizedDirectoryPrefix = normalizedDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        targets.Add(new JsonReferenceTarget(normalizedPath, normalizedDirectoryPrefix, addError));
    }

    private static bool TryAttachReferenceFinding(
        IReadOnlyList<JsonReferenceTarget> targets,
        string findingPath,
        string formattedFinding)
    {
        var normalizedFindingPath = NormalizePathKey(findingPath);

        foreach (var target in targets)
        {
            if (string.Equals(normalizedFindingPath, target.ExactPath, s_pathComparison)
                || normalizedFindingPath.StartsWith(target.DirectoryPrefix, s_pathComparison))
            {
                target.AddError(formattedFinding);
                return true;
            }
        }

        return false;
    }

    private static string NormalizePathKey(string path) => Path.GetFullPath(path);

    private sealed record JsonReferenceTarget(
        string ExactPath,
        string DirectoryPrefix,
        Action<string> AddError);

    private sealed class CheckReportBuilder(CheckConfig config, string scope)
    {
        public List<string> GeneralErrors { get; } = [];

        public List<PluginCheckResult> Plugins { get; } = [];

        public List<SkillCheckResult> Skills { get; } = [];

        public List<AgentCheckResult> Agents { get; } = [];

        public List<ExternalDependencyResult> ExternalDependencies { get; } = [];

        public ReferenceScanReport? ReferenceScan { get; set; }

        public void AddGeneralError(string text) => GeneralErrors.Add(text);

        public CheckReport Build(int exitCode)
        {
            var warningCount = Plugins.Sum(plugin => plugin.Warnings.Count)
                + Skills.Sum(skill => skill.Warnings.Count)
                + Agents.Sum(agent => agent.Warnings.Count)
                + ExternalDependencies.Count;

            var errorCount = GeneralErrors.Count
                + Plugins.Sum(plugin => plugin.Errors.Count)
                + Skills.Sum(skill => skill.Errors.Count)
                + Agents.Sum(agent => agent.Errors.Count)
                + (ReferenceScan?.Findings.Count ?? 0);

            var infoCount = (Plugins.Count > 0 ? 1 : 0)
                + (Skills.Count > 0 ? 1 : 0)
                + Skills.Count(skill => skill.ProfileLine is not null)
                + (Agents.Count > 0 ? 2 : 0)
                + (ReferenceScan?.Status == ReferenceScanStatus.Completed && ReferenceScan.Findings.Count == 0 ? 1 : 0)
                + (exitCode == 0 ? 1 : 0);

            var counts = new CheckCounts(
                PluginCount: Plugins.Count,
                SkillCount: Skills.Count,
                AgentCount: Agents.Count,
                ReferenceFileCount: ReferenceScan?.FilesScanned ?? 0,
                InfoCount: infoCount,
                WarningCount: warningCount,
                ErrorCount: errorCount);

            return new CheckReport(
                Scope: scope,
                Invocation: new CheckInvocation(
                    PluginPaths: config.PluginPaths,
                    SkillPaths: config.SkillPaths,
                    AgentPaths: config.AgentPaths,
                    AllowedExternalDepsFile: config.AllowedExternalDepsFile,
                    KnownDomainsFile: config.KnownDomainsFile,
                    Verbose: config.Verbose,
                    AllowRepoTraversal: config.CheckOptions.AllowRepoTraversal,
                    OutputMode: config.OutputMode),
                Succeeded: exitCode == 0,
                ExitCode: exitCode,
                Counts: counts,
                GeneralErrors: GeneralErrors,
                Plugins: Plugins,
                Skills: Skills,
                Agents: Agents,
                ExternalDependencies: ExternalDependencies,
                ReferenceScan: ReferenceScan);
        }
    }
}
