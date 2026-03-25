using System.CommandLine;
using System.Text.Json;
using SkillValidator.Shared;

namespace SkillValidator.Check;

public static class CheckCommand
{
    public static Command Create()
    {
        var pluginOpt = new Option<string[]>("--plugin") { Description = "Plugin directories to check (discovers skills, agents, plugin.json)", AllowMultipleArgumentsPerToken = true };
        var skillsOpt = new Option<string[]>("--skills") { Description = "Skill directories to check (skills only)", AllowMultipleArgumentsPerToken = true };
        var agentsOpt = new Option<string[]>("--agents") { Description = "Agent directories to check (agents only)", AllowMultipleArgumentsPerToken = true };
        var allowedExternalDepsOpt = new Option<string?>("--allowed-external-deps") { Description = "Path to allowed-external-deps.txt allow list file" };
        var knownDomainsOpt = new Option<string?>("--known-domains") { Description = "Path to known-domains.txt for reference scanning" };
        var verboseOpt = new Option<bool>("--verbose") { Description = "Show detailed output" };

        var command = new Command("check", "Run static analysis checks on skills, plugins, and agents (no LLM required). Use --plugin to check an entire plugin directory (recommended).")
        {
            pluginOpt,
            skillsOpt,
            agentsOpt,
            allowedExternalDepsOpt,
            knownDomainsOpt,
            verboseOpt,
        };

        command.SetAction(async (parseResult, _) =>
        {
            var pluginPaths = parseResult.GetValue(pluginOpt) ?? [];
            var skillPaths = parseResult.GetValue(skillsOpt) ?? [];
            var agentPaths = parseResult.GetValue(agentsOpt) ?? [];

            int modeCount = (pluginPaths.Length > 0 ? 1 : 0) + (skillPaths.Length > 0 ? 1 : 0) + (agentPaths.Length > 0 ? 1 : 0);
            if (modeCount == 0)
            {
                Console.Error.WriteLine("Specify one of --plugin, --skills, or --agents. Use --plugin to check an entire plugin directory.");
                return 1;
            }
            if (modeCount > 1)
            {
                Console.Error.WriteLine("Only one of --plugin, --skills, or --agents can be used at a time.");
                return 1;
            }

            var config = new CheckConfig
            {
                PluginPaths = pluginPaths,
                SkillPaths = skillPaths,
                AgentPaths = agentPaths,
                AllowedExternalDepsFile = parseResult.GetValue(allowedExternalDepsOpt),
                KnownDomainsFile = parseResult.GetValue(knownDomainsOpt),
                Verbose = parseResult.GetValue(verboseOpt),
            };
            return await Run(config);
        });

        return command;
    }

    public static async Task<int> Run(CheckConfig config)
    {
        if (config.PluginPaths.Count > 0)
            return await RunPluginCheck(config);

        if (config.SkillPaths.Count > 0)
            return await RunSkillsCheck(config);

        if (config.AgentPaths.Count > 0)
            return await RunAgentsCheck(config);

        throw new ArgumentException("No paths specified to check.");
    }

    private static async Task<int> RunPluginCheck(CheckConfig config)
    {
        var allPlugins = new List<PluginInfo>();
        // Track skills per plugin for aggregate checks
        var pluginSkills = new Dictionary<string, List<SkillInfo>>();
        var allSkillsList = new List<SkillInfo>();
        var agentDirs = new List<string>();

        foreach (var pluginDir in config.PluginPaths)
        {
            var fullPath = Path.GetFullPath(pluginDir);
            var pluginName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            // Parse plugin.json
            var pluginJsonPath = Path.Combine(fullPath, "plugin.json");
            if (!File.Exists(pluginJsonPath))
            {
                Console.Error.WriteLine($"{Ansi.Red}❌ No plugin.json found in '{pluginDir}'{Ansi.Reset}");
                return 1;
            }

            PluginInfo? plugin;
            try
            {
                plugin = PluginDiscovery.ParsePluginJson(pluginJsonPath);
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"{Ansi.Red}❌ Malformed plugin.json in '{pluginDir}': {ex.Message}{Ansi.Reset}");
                return 1;
            }

            if (plugin is null)
            {
                Console.Error.WriteLine($"{Ansi.Red}❌ Failed to parse plugin.json in '{pluginDir}'{Ansi.Reset}");
                return 1;
            }

            allPlugins.Add(plugin);

            // Resolve skills path from plugin.json and discover skills per plugin
            var skillsDirs = new List<string>();
            if (plugin is not null)
            {
                foreach (var sp in plugin.SkillPaths)
                {
                    if (PluginDiscovery.TryGetSafeSubdirectory(fullPath, sp, out var dir, out _)
                        && Directory.Exists(dir))
                        skillsDirs.Add(dir!);
                }
            }

            foreach (var dir in skillsDirs)
            {
                var skills = await SkillDiscovery.DiscoverSkills(dir);
                pluginSkills[pluginName] = new List<SkillInfo>(skills);
                allSkillsList.AddRange(skills);
            }

            // Collect agent directories
            var agents = await AgentDiscovery.DiscoverAgentsInPlugin(fullPath);
            foreach (var dir in agents.Select(a => Path.GetDirectoryName(a.Path)).Where(d => d is not null).Distinct())
                agentDirs.Add(dir!);
        }

        // Validate plugins first
        bool hasPluginErrors = false;
        foreach (var plugin in allPlugins)
        {
            var result = PluginProfiler.ValidatePlugin(plugin);
            foreach (var warning in result.Warnings)
                Console.WriteLine($"{Ansi.Yellow}⚠  [plugin:{result.Name}] {warning}{Ansi.Reset}");
            foreach (var error in result.Errors)
            {
                Console.Error.WriteLine($"{Ansi.Red}❌ [plugin:{result.Name}] {error}{Ansi.Reset}");
                hasPluginErrors = true;
            }
        }
        Console.WriteLine($"Validated {allPlugins.Count} plugin(s)");
        if (hasPluginErrors)
        {
            Console.Error.WriteLine("{Ansi.Red}Plugin spec conformance failures — fix the errors above.{Ansi.Reset}");
            return 1;
        }

        // Validate discovered skills (profiles, etc.)
        int skillResult = 0;
        if (allSkillsList.Count > 0)
        {
            Console.WriteLine($"Found {allSkillsList.Count} skill(s)");
            if (ValidateSkillProfiles(allSkillsList, config.Verbose))
                skillResult = 1;
        }

        // Validate agents
        var (allAgents, agentResult) = await RunAgentsCheckCore(agentDirs.Distinct().ToList());

        if (allSkillsList.Count == 0 && allAgents.Count == 0)
        {
            Console.Error.WriteLine("No skills or agents found in the specified plugin(s).");
            return 1;
        }

        // Aggregate description limits apply per plugin
        foreach (var (pluginName, skills) in pluginSkills)
        {
            int totalChars = skills.Sum(s => s.Description.Length);
            if (totalChars > SkillProfiler.MaxAggregateDescriptionLength)
            {
                Console.Error.WriteLine(
                    $"{Ansi.Red}❌ Plugin '{pluginName}' aggregate description size is {totalChars:N0} characters — " +
                    $"maximum is {SkillProfiler.MaxAggregateDescriptionLength:N0}.{Ansi.Reset}");
                return 1;
            }
        }

        // Check for external dependencies (plugin-level check includes all three)
        CheckExternalDeps(config.AllowedExternalDepsFile, allSkillsList, allAgents, allPlugins);

        // Run reference scanner if known-domains file is provided
        if (RunReferenceScanner(config.KnownDomainsFile, config.PluginPaths))
            return 1;

        if (skillResult != 0 || agentResult != 0)
            return 1;

        Console.WriteLine($"{Ansi.Green}✅ All checks passed ({allSkillsList.Count} skill(s), {allAgents.Count} agent(s), {allPlugins.Count} plugin(s)){Ansi.Reset}");
        return 0;
    }

    private static async Task<int> RunSkillsCheck(CheckConfig config)
    {
        var (skills, result) = await RunSkillsCheckCore(config.SkillPaths, config.Verbose);

        if (skills.Count == 0)
            return 1; // error already printed
        if (result != 0)
            return result;

        // Run reference scanner on skill directories
        if (RunReferenceScanner(config.KnownDomainsFile, config.SkillPaths))
            return 1;

        Console.WriteLine($"{Ansi.Green}✅ All checks passed ({skills.Count} skill(s)){Ansi.Reset}");
        return 0;
    }

    private static async Task<int> RunAgentsCheck(CheckConfig config)
    {
        var (agents, result) = await RunAgentsCheckCore(config.AgentPaths);

        if (agents.Count == 0)
            return 1; // error already printed
        if (result != 0)
            return result;

        // Run reference scanner on agent directories
        if (RunReferenceScanner(config.KnownDomainsFile, config.AgentPaths))
            return 1;

        Console.WriteLine($"{Ansi.Green}✅ All checks passed ({agents.Count} agent(s)){Ansi.Reset}");
        return 0;
    }

    private static async Task<(IReadOnlyList<SkillInfo> Skills, int Result)> RunSkillsCheckCore(IReadOnlyList<string> skillPaths, bool verbose)
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
                Console.Error.WriteLine($"No skills found in the specified paths: {searched}");
            }
            return (allSkills, 0);
        }

        Console.WriteLine($"Found {allSkills.Count} skill(s)");

        if (ValidateSkillProfiles(allSkills, verbose))
            return (allSkills, 1);

        return (allSkills, 0);
    }

    private static async Task<(IReadOnlyList<AgentInfo> Agents, int Result)> RunAgentsCheckCore(IReadOnlyList<string> agentPaths)
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
                Console.Error.WriteLine($"No agents found in the specified paths: {searched}");
            }
            return (allAgents, 0);
        }

        Console.WriteLine($"Found {allAgents.Count} agent(s)");

        bool hasErrors = false;
        foreach (var agent in allAgents)
        {
            var profile = AgentProfiler.AnalyzeAgent(agent);
            foreach (var warning in profile.Warnings)
                Console.WriteLine($"{Ansi.Yellow}⚠  [agent:{profile.Name}] {warning}{Ansi.Reset}");
            foreach (var error in profile.Errors)
            {
                Console.Error.WriteLine($"{Ansi.Red}❌ [agent:{profile.Name}] {error}{Ansi.Reset}");
                hasErrors = true;
            }
        }
        Console.WriteLine($"Validated {allAgents.Count} agent(s)\n");

        if (hasErrors)
        {
            Console.Error.WriteLine("{Ansi.Red}Agent spec conformance failures — fix the errors above.{Ansi.Reset}");
            return (allAgents, 1);
        }

        return (allAgents, 0);
    }

    private static bool ValidateSkillProfiles(IReadOnlyList<SkillInfo> skills, bool verbose)
    {
        bool hasErrors = false;
        foreach (var skill in skills)
        {
            var profile = SkillProfiler.AnalyzeSkill(skill);

            if (verbose)
                Console.WriteLine($"[{skill.Name}] 📊 {SkillProfiler.FormatProfileLine(profile)}");

            foreach (var error in profile.Errors)
            {
                Console.Error.WriteLine($"{Ansi.Red}❌ [{skill.Name}] {error}{Ansi.Reset}");
                hasErrors = true;
            }
            foreach (var warning in SkillProfiler.FormatProfileWarnings(profile))
                Console.WriteLine($"[{skill.Name}] {warning}");
        }

        if (hasErrors)
            Console.Error.WriteLine("{Ansi.Red}Skill spec conformance failures — fix the errors above.{Ansi.Reset}");

        return hasErrors;
    }

    private static void CheckExternalDeps(string? allowedExternalDepsFile, IReadOnlyList<SkillInfo> skills, IReadOnlyList<AgentInfo> agents, IReadOnlyList<PluginInfo> plugins)
    {
        if (allowedExternalDepsFile is null)
            return;

        bool hasExternalDeps = false;
        var allowed = ExternalDependencyChecker.LoadAllowList(allowedExternalDepsFile);
        foreach (var skill in skills)
        {
            foreach (var warning in ExternalDependencyChecker.CheckSkill(skill, allowed))
            {
                Console.WriteLine($"{Ansi.Yellow}⚠  [skill:{skill.Name}] {warning}{Ansi.Reset}");
                hasExternalDeps = true;
            }
        }
        foreach (var agent in agents)
        {
            foreach (var warning in ExternalDependencyChecker.CheckAgent(agent, allowed))
            {
                Console.WriteLine($"{Ansi.Yellow}⚠  [agent:{agent.Name}] {warning}{Ansi.Reset}");
                hasExternalDeps = true;
            }
        }
        foreach (var plugin in plugins)
        {
            foreach (var warning in ExternalDependencyChecker.CheckPlugin(plugin, allowed))
            {
                Console.WriteLine($"{Ansi.Yellow}⚠  [plugin:{plugin.Name}] {warning}{Ansi.Reset}");
                hasExternalDeps = true;
            }
        }
        if (hasExternalDeps)
            Console.WriteLine();
    }

    /// <summary>
    /// Run the reference scanner on discovered files. Returns true if errors were found.
    /// </summary>
    private static bool RunReferenceScanner(string? knownDomainsFile, IReadOnlyList<string> directories)
    {
        if (knownDomainsFile is null)
            return false;

        if (!File.Exists(knownDomainsFile))
        {
            Console.Error.WriteLine($"{Ansi.Red}❌ Known-domains file not found: '{knownDomainsFile}'{Ansi.Reset}");
            return true;
        }

        var knownDomains = ReferenceScanner.LoadKnownDomains(knownDomainsFile);
        var files = ReferenceScanner.DiscoverFiles(directories);
        var findings = ReferenceScanner.ScanFiles(files, knownDomains, knownDomainsFile);

        if (findings.Count > 0)
        {
            Console.Error.WriteLine($"\n  {findings.Count} reference error(s):\n");
            foreach (var f in findings)
                Console.Error.WriteLine($"  {Ansi.Red}❌ {f.Path}:{f.LineNum} [{f.Code}] {f.Message}{Ansi.Reset}");
            Console.Error.WriteLine();
            Console.Error.WriteLine($"{Ansi.Red}--- Reference scan: {files.Count} file(s) scanned, {findings.Count} error(s) ---{Ansi.Reset}");
            return true;
        }

        Console.WriteLine($"--- Reference scan: {files.Count} file(s) scanned, 0 error(s) ---");
        return false;
    }
}

