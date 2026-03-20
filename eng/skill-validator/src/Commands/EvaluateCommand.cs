using System.CommandLine;
using System.Text.Json;
using SkillValidator.Models;
using SkillValidator.Services;
using SkillValidator.Utilities;
using GitHub.Copilot.SDK;

namespace SkillValidator.Commands;

public static class EvaluateCommand
{
    public static Command Create()
    {
        var pathsArg = new Argument<string[]>("paths") { Description = "Paths to skill directories or parent directories", Arity = ArgumentArity.OneOrMore };
        var minImprovementOpt = new Option<double>("--min-improvement") { Description = "Minimum improvement score to pass (0-1)", DefaultValueFactory = _ => 0.1 };
        var requireCompletionOpt = new Option<bool>("--require-completion") { Description = "Fail if skill regresses task completion", DefaultValueFactory = _ => true };
        var verdictWarnOnlyOpt = new Option<bool>("--verdict-warn-only") { Description = "Treat verdict failures as warnings (exit 0). Execution errors still fail." };
        var verboseOpt = new Option<bool>("--verbose") { Description = "Show detailed per-scenario breakdowns" };
        var modelOpt = new Option<string>("--model") { Description = "Model to use for agent runs", DefaultValueFactory = _ => "claude-opus-4.6" };
        var judgeModelOpt = new Option<string?>("--judge-model") { Description = "Model to use for judging (defaults to --model)" };
        var judgeModeOpt = new Option<string>("--judge-mode") { Description = "Judge mode: pairwise, independent, or both", DefaultValueFactory = _ => "pairwise" }
            .AcceptOnlyFromAmong("pairwise", "independent", "both");
        var runsOpt = new Option<int>("--runs") { Description = "Number of runs per scenario for averaging", DefaultValueFactory = _ => 5 };
        var parallelSkillsOpt = new Option<int>("--parallel-skills") { Description = "Max concurrent skills to evaluate", DefaultValueFactory = _ => 3 };
        var parallelScenariosOpt = new Option<int>("--parallel-scenarios") { Description = "Max concurrent scenarios per skill", DefaultValueFactory = _ => 3 };
        var parallelRunsOpt = new Option<int>("--parallel-runs") { Description = "Max concurrent runs per scenario", DefaultValueFactory = _ => 3 };
        var judgeTimeoutOpt = new Option<int>("--judge-timeout") { Description = "Judge timeout in seconds", DefaultValueFactory = _ => 300 };
        var confidenceLevelOpt = new Option<double>("--confidence-level") { Description = "Confidence level for statistical intervals (0-1)", DefaultValueFactory = _ => 0.95 };
        var resultsDirOpt = new Option<string>("--results-dir") { Description = "Directory to save results to", DefaultValueFactory = _ => ".skill-validator-results" };
        var testsDirOpt = new Option<string>("--tests-dir") { Description = "Directory containing test subdirectories", Required = true };
        var reporterOpt = new Option<string[]>("--reporter") { Description = "Reporter (console, json, junit, markdown). Can be repeated.", AllowMultipleArgumentsPerToken = true };
        var noOverfittingCheckOpt = new Option<bool>("--no-overfitting-check") { Description = "Disable LLM-based overfitting analysis (on by default)" };
        var overfittingFixOpt = new Option<bool>("--overfitting-fix") { Description = "Generate a fixed eval.yaml with improved rubric items/assertions" };
        var keepSessionsOpt = new Option<bool>("--keep-sessions") { Description = "Preserve agent session data in the results directory for later rejudging" };
        var noiseSkillsDirOpt = new Option<string?>("--noise-skills-dir") { Description = "Directory containing skills to load as noise. Enables the noise test: re-runs scenarios with all noise skills loaded and measures degradation." };
        var noiseMaxDegradationOpt = new Option<double>("--noise-max-degradation") { Description = "Maximum acceptable average quality degradation (0-1) in noise test (only positive degradations count)", DefaultValueFactory = _ => 0.2 };
        var noiseMaxScenarioDegradationOpt = new Option<double>("--noise-max-scenario-degradation") { Description = "Maximum acceptable quality degradation (0-1) for any single noise-test scenario", DefaultValueFactory = _ => 0.4 };

        var command = new Command("evaluate", "Evaluate agent skills via LLM-based testing")
        {
            pathsArg,
            minImprovementOpt,
            requireCompletionOpt,
            verdictWarnOnlyOpt,
            verboseOpt,
            modelOpt,
            judgeModelOpt,
            judgeModeOpt,
            runsOpt,
            parallelSkillsOpt,
            parallelScenariosOpt,
            parallelRunsOpt,
            judgeTimeoutOpt,
            confidenceLevelOpt,
            resultsDirOpt,
            testsDirOpt,
            reporterOpt,
            noOverfittingCheckOpt,
            overfittingFixOpt,
            keepSessionsOpt,
            noiseSkillsDirOpt,
            noiseMaxDegradationOpt,
            noiseMaxScenarioDegradationOpt,
        };

        command.SetAction(async (parseResult, _) =>
        {
            var paths = parseResult.GetValue(pathsArg) ?? [];
            var reporterValues = parseResult.GetValue(reporterOpt) ?? [];

            var reporters = reporterValues.Length > 0
                ? reporterValues.Select(ParseReporter).ToList()
                : new List<ReporterSpec>
                {
                    new(ReporterType.Console),
                    new(ReporterType.Json),
                    new(ReporterType.Markdown),
                };

            var judgeMode = parseResult.GetValue(judgeModeOpt) switch
            {
                "independent" => JudgeMode.Independent,
                "both" => JudgeMode.Both,
                _ => JudgeMode.Pairwise,
            };

            var config = new ValidatorConfig
            {
                MinImprovement = parseResult.GetValue(minImprovementOpt),
                RequireCompletion = parseResult.GetValue(requireCompletionOpt),
                Verbose = parseResult.GetValue(verboseOpt),
                Model = parseResult.GetValue(modelOpt) ?? "claude-opus-4.6",
                JudgeModel = parseResult.GetValue(judgeModelOpt) ?? parseResult.GetValue(modelOpt) ?? "claude-opus-4.6",
                JudgeMode = judgeMode,
                Runs = Math.Max(1, parseResult.GetValue(runsOpt)),
                ParallelSkills = Math.Max(1, parseResult.GetValue(parallelSkillsOpt)),
                ParallelScenarios = Math.Max(1, parseResult.GetValue(parallelScenariosOpt)),
                ParallelRuns = Math.Max(1, parseResult.GetValue(parallelRunsOpt)),
                JudgeTimeout = parseResult.GetValue(judgeTimeoutOpt) * 1000,
                ConfidenceLevel = parseResult.GetValue(confidenceLevelOpt),
                VerdictWarnOnly = parseResult.GetValue(verdictWarnOnlyOpt),
                Reporters = reporters,
                SkillPaths = paths,
                ResultsDir = parseResult.GetValue(resultsDirOpt),
                TestsDir = parseResult.GetValue(testsDirOpt)!,
                OverfittingCheck = !parseResult.GetValue(noOverfittingCheckOpt),
                OverfittingFix = parseResult.GetValue(overfittingFixOpt),
                KeepSessions = parseResult.GetValue(keepSessionsOpt),
                NoiseSkillsDir = parseResult.GetValue(noiseSkillsDirOpt),
                NoiseDegradationLimit = parseResult.GetValue(noiseMaxDegradationOpt),
                NoiseMaxScenarioDegradation = parseResult.GetValue(noiseMaxScenarioDegradationOpt),
            };

            return await Run(config);
        });

        return command;
    }

    private static ReporterSpec ParseReporter(string value) => value switch
    {
        "console" => new ReporterSpec(ReporterType.Console),
        "json" => new ReporterSpec(ReporterType.Json),
        "junit" => new ReporterSpec(ReporterType.Junit),
        "markdown" => new ReporterSpec(ReporterType.Markdown),
        _ => throw new ArgumentException($"Unknown reporter type: {value}"),
    };

    public static async Task<int> Run(ValidatorConfig config)
    {
        // Validate model early
        try
        {
            var client = await AgentRunner.GetSharedClient(config.Verbose);
            var models = await RetryHelper.ExecuteWithRetry(
                async _ => await client.ListModelsAsync(),
                label: "ListModels",
                maxRetries: 3,
                baseDelayMs: 2_000,
                totalTimeoutMs: 60_000);
            var modelIds = models.Select(m => m.Id).ToList();
            var modelsToValidate = new List<string> { config.Model };
            if (config.JudgeModel != config.Model) modelsToValidate.Add(config.JudgeModel);

            foreach (var m in modelsToValidate)
            {
                if (!modelIds.Contains(m))
                {
                    Console.Error.WriteLine($"Invalid model: \"{m}\"\nAvailable models: {string.Join(", ", modelIds)}");
                    return 1;
                }
            }

            Console.WriteLine($"Using model: {config.Model}" +
                (config.JudgeModel != config.Model ? $", judge: {config.JudgeModel}" : "") +
                $", judge-mode: {config.JudgeMode}");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Failed to validate model: {error}");
            return 1;
        }

        if (config.Verbose)
            Console.WriteLine($"Results dir: {config.ResultsDir}");

        // Discover skills
        var discoveredSkills = new List<SkillInfo>();
        foreach (var path in config.SkillPaths)
        {
            var skills = await SkillDiscovery.DiscoverSkills(path);
            discoveredSkills.AddRange(skills);
        }

        if (discoveredSkills.Count == 0)
        {
            var searched = string.Join(", ", config.SkillPaths.Select(p => $"\"{Path.GetFullPath(p)}\""));
            Console.Error.WriteLine($"No skills found in the specified paths: {searched}");
            return 1;
        }

        Console.WriteLine($"Found {discoveredSkills.Count} skill(s)\n");

        // Discover noise skills when --noise-skills-dir is provided
        var noiseEvalSkills = new List<EvalSkillInfo>();
        if (config.NoiseSkillsDir is not null)
        {
            var noiseSkills = await SkillDiscovery.DiscoverSkillsRecursive(config.NoiseSkillsDir);
            noiseEvalSkills.AddRange(await SkillDiscovery.LoadEvalData(noiseSkills, config.TestsDir));
            Console.WriteLine($"Noise test enabled: discovered {noiseEvalSkills.Count} noise skill(s) from {config.NoiseSkillsDir}");
        }

        // Group skills by their plugin — standalone skills are errors
        var (_, pluginErrors) = SkillDiscovery.GroupSkillsByPlugin(discoveredSkills);
        foreach (var error in pluginErrors)
            Console.Error.WriteLine($"\x1b[31m❌ {error}\x1b[0m");
        if (pluginErrors.Count > 0)
        {
            if (discoveredSkills.Count == pluginErrors.Count)
            {
                Console.Error.WriteLine("\x1b[31mAll skills are standalone (no valid plugin.json found) — nothing to evaluate.\x1b[0m");
                return 1;
            }
            discoveredSkills = discoveredSkills.Where(s => SkillDiscovery.FindPluginRoot(s.Path) is not null).ToList();
        }

        // Load eval data (eval.yaml, MCP servers)
        var allSkills = await SkillDiscovery.LoadEvalData(discoveredSkills, config.TestsDir);

        if (config.Runs < 5)
            Console.WriteLine($"\x1b[33m⚠  Running with {config.Runs} run(s). For statistically significant results, use --runs 5 or higher.\x1b[0m");

        bool usePairwise = config.JudgeMode is JudgeMode.Pairwise or JudgeMode.Both;
        bool effectiveKeepSessions = config.KeepSessions && config.ResultsDir is not null;

        string? sessionsDir = null;
        SessionDatabase? sessionDb = null;
        string? timestampedResultsDir = null;
        if (effectiveKeepSessions)
        {
            timestampedResultsDir = Path.Combine(config.ResultsDir!, Reporter.FormatTimestamp(DateTime.Now));
            Directory.CreateDirectory(timestampedResultsDir);
            sessionsDir = Path.Combine(timestampedResultsDir, "sessions");
            Directory.CreateDirectory(sessionsDir);
            sessionDb = new SessionDatabase(Path.Combine(timestampedResultsDir, "sessions.db"));
            sessionDb.SetSchemaInfo("judge_model", config.JudgeModel);
            Console.WriteLine($"Session persistence enabled: {timestampedResultsDir}");
        }
        else if (config.KeepSessions)
        {
            Console.WriteLine("\x1b[33m⚠  --keep-sessions was set without --results-dir; sessions will not be persisted.\x1b[0m");
        }

        using var spinner = new Spinner();
        using var skillLimit = new ConcurrencyLimiter(config.ParallelSkills);

        // Evaluate skills
        spinner.Start($"Evaluating {allSkills.Count} skill(s)...");
        var skillTasks = allSkills.Select(evalSkill =>
            skillLimit.RunAsync(() => EvaluateSkill(evalSkill, config, usePairwise, spinner, noiseEvalSkills, sessionsDir, sessionDb)));
        var settled = await Task.WhenAll(skillTasks.Select(async t =>
        {
            try { return (Result: await t, Error: (Exception?)null); }
            catch (Exception ex) { return (Result: (SkillVerdict?)null, Error: ex); }
        }));
        spinner.Stop();

        var verdicts = new List<SkillVerdict>();
        var rejectionMessages = new List<string>();
        foreach (var (result, error) in settled)
        {
            if (result is not null)
            {
                verdicts.Add(result);
            }
            else if (error is not null)
            {
                rejectionMessages.Add(error.Message);
            }
        }

        await Reporter.ReportResults(verdicts, config.Reporters, config.Verbose,
            config.Model, config.JudgeModel, config.ResultsDir, timestampedResultsDir,
            rejectedCount: rejectionMessages.Count);

        if (rejectionMessages.Count > 0)
        {
            Console.Error.WriteLine($"\x1b[31m❗ {rejectionMessages.Count} skill(s) failed with execution errors:\x1b[0m");
            foreach (var msg in rejectionMessages)
                Console.Error.WriteLine($"\x1b[31m   • {msg}\x1b[0m");
            Console.Error.WriteLine();
        }

        await AgentRunner.StopAllClients();
        await AgentRunner.CleanupWorkDirs(effectiveKeepSessions);
        sessionDb?.Dispose();

        // Always fail on execution errors, even in --verdict-warn-only mode
        if (rejectionMessages.Count > 0) return 1;

        var allPassed = verdicts.All(v => v.Passed);
        if (config.VerdictWarnOnly && !allPassed)
        {
            // In --verdict-warn-only mode, suppress verdict failures.
            // Execution errors are already fatal (above).
            return 0;
        }

        return allPassed ? 0 : 1;
    }

    private static async Task<SkillVerdict?> EvaluateSkill(
        EvalSkillInfo evalSkill,
        ValidatorConfig config,
        bool usePairwise,
        Spinner spinner,
        IReadOnlyList<EvalSkillInfo> noiseSkills,
        string? sessionsDir,
        SessionDatabase? sessionDb)
    {
        var skill = evalSkill.Skill;
        var prefix = $"[{skill.Name}]";
        var log = (string msg) => spinner.Log($"{prefix} {msg}");

        if (evalSkill.EvalConfig is null)
        {
            log("⏭  Skipping (no tests/eval.yaml)");
            return null;
        }

        if (evalSkill.EvalConfig.Scenarios.Count == 0)
        {
            log("⏭  Skipping (eval.yaml has no scenarios)");
            return null;
        }

        log("🔍 Evaluating...");

        var profile = SkillProfiler.AnalyzeSkill(skill, evalSkill.EvalConfig);
        log($"📊 {SkillProfiler.FormatProfileLine(profile)}");
        foreach (var error in profile.Errors)
            log($"   ❌ {error}");
        foreach (var warning in SkillProfiler.FormatProfileWarnings(profile))
            log(warning);

        if (profile.Errors.Count > 0)
        {
            return new SkillVerdict
            {
                SkillName = skill.Name,
                SkillPath = skill.Path,
                Passed = false,
                Scenarios = [],
                OverallImprovementScore = 0,
                Reason = string.Join(" ", profile.Errors),
                FailureKind = "spec_conformance_failure",
            };
        }

        // --- Noise-only path: skip normal baseline-vs-skill eval, run only skill-only vs all-skills ---
        if (config.NoiseSkillsDir is not null && noiseSkills.Count > 0)
        {
            return await EvaluateSkillNoise(evalSkill, noiseSkills, config, profile, spinner);
        }

        // Launch overfitting check in parallel with scenario execution
        var workDir = Path.GetTempPath();
        Task<OverfittingResult?> overfittingTask = Task.FromResult<OverfittingResult?>(null);
        if (config.OverfittingCheck && evalSkill.EvalConfig is not null)
        {
            log("🔍 Running overfitting check (parallel)...");
            overfittingTask = Services.OverfittingJudge.Analyze(evalSkill, new OverfittingJudgeOptions(
                config.JudgeModel, config.Verbose, config.JudgeTimeout, workDir));
        }

        var skillSha = sessionDb is not null ? SessionDatabase.ComputeDirectorySha(skill.Path) : null;
        bool singleScenario = evalSkill.EvalConfig!.Scenarios.Count == 1;

        var effectiveParallelScenarios = evalSkill.EvalConfig.MaxParallelScenarios.HasValue
            ? Math.Min(config.ParallelScenarios, evalSkill.EvalConfig.MaxParallelScenarios.Value)
            : config.ParallelScenarios;

        using var scenarioLimit = new ConcurrencyLimiter(effectiveParallelScenarios);

        var scenarioTasks = evalSkill.EvalConfig.Scenarios.Select(scenario =>
            scenarioLimit.RunAsync(() => ExecuteScenario(scenario, evalSkill, config, usePairwise, singleScenario, spinner, sessionsDir, sessionDb, skillSha)));
        var comparisons = (await Task.WhenAll(scenarioTasks)).ToList();

        // Await overfitting result (non-fatal — never blocks an otherwise-successful evaluation)
        OverfittingResult? overfittingResult = null;
        try
        {
            overfittingResult = await overfittingTask;
            if (overfittingResult is not null)
                log($"🔍 Overfitting: {overfittingResult.Score:F2} ({overfittingResult.Severity})");
        }
        catch (Exception ex)
        {
            log($"⚠️ Overfitting check failed: {ex.Message}");
        }

        var verdict = Comparator.ComputeVerdict(skill, comparisons, config.MinImprovement, config.RequireCompletion, config.ConfidenceLevel);
        verdict.ProfileWarnings = profile.Warnings;
        verdict.OverfittingResult = overfittingResult;

        // Optional: generate fixed eval.yaml
        if (config.OverfittingFix && overfittingResult is { Severity: not OverfittingSeverity.Low })
        {
            try
            {
                await Services.OverfittingJudge.GenerateFix(evalSkill, overfittingResult, new OverfittingJudgeOptions(
                    config.JudgeModel, config.Verbose, config.JudgeTimeout, workDir));
                log("📝 Generated eval.fixed.yaml with suggested improvements");
            }
            catch (Exception ex)
            {
                log($"⚠️ Failed to generate overfitting fix: {ex.Message}");
            }
        }

        var notActivatedIsolated = comparisons.Where(c => c.SkillActivationIsolated is { Activated: false } && c.ExpectActivation).ToList();
        var notActivatedPlugin = comparisons.Where(c => c.SkillActivationPlugin is { Activated: false } && c.ExpectActivation).ToList();
        var expectedNotActivated = comparisons.Where(c =>
            (c.SkillActivationIsolated is { Activated: false } || c.SkillActivationPlugin is { Activated: false }) && !c.ExpectActivation).ToList();

        if (expectedNotActivated.Count > 0)
        {
            var names = string.Join(", ", expectedNotActivated.Select(c => c.ScenarioName));
            log($"\x1b[36mℹ️  Skill correctly NOT activated in negative-test scenario(s): {names}\x1b[0m");
        }

        if (notActivatedIsolated.Count > 0)
        {
            var names = string.Join(", ", notActivatedIsolated.Select(c => c.ScenarioName));
            log($"\x1b[33m⚠️  Skill NOT activated (isolated) in: {names}\x1b[0m");
            verdict.SkillNotActivated = true;
            verdict.Passed = false;
            verdict.FailureKind = "skill_not_activated";
            verdict.Reason += $" [NOT ACTIVATED (isolated) in {notActivatedIsolated.Count} scenario(s)]";
        }
        if (notActivatedPlugin.Count > 0)
        {
            var names = string.Join(", ", notActivatedPlugin.Select(c => c.ScenarioName));
            log($"\x1b[33m⚠️  Skill NOT activated (plugin) in: {names}\x1b[0m");
            verdict.SkillNotActivated = true;
            verdict.Passed = false;
            verdict.FailureKind = "skill_not_activated";
            verdict.Reason += $" [NOT ACTIVATED (plugin) in {notActivatedPlugin.Count} scenario(s)]";
        }

        var timedOutScenarios = comparisons.Where(c => c.TimedOut).ToList();
        if (timedOutScenarios.Count > 0)
        {
            var names = string.Join(", ", timedOutScenarios.Select(c => c.ScenarioName));
            log($"\x1b[33m⏰ Execution timed out in scenario(s): {names}\x1b[0m");
        }

        log($"{(verdict.Passed ? "✅" : "❌")} Done (score: {verdict.OverallImprovementScore * 100:F1}%)");
        return verdict;
    }

    private static async Task<ScenarioComparison> ExecuteScenario(
        EvalScenario scenario,
        EvalSkillInfo evalSkill,
        ValidatorConfig config,
        bool usePairwise,
        bool singleScenario,
        Spinner spinner,
        string? sessionsDir,
        SessionDatabase? sessionDb,
        string? skillSha)
    {
        var skill = evalSkill.Skill;
        var tag = singleScenario ? $"[{skill.Name}]" : $"[{skill.Name}/{scenario.Name}]";
        var scenarioLog = (string msg) => spinner.Log($"{tag} {msg}");

        var effectiveParallelRuns = evalSkill.EvalConfig?.MaxParallelRuns.HasValue == true
            ? Math.Min(config.ParallelRuns, evalSkill.EvalConfig.MaxParallelRuns.Value)
            : config.ParallelRuns;
        using var runLimit = new ConcurrencyLimiter(effectiveParallelRuns);

        if (!singleScenario)
            scenarioLog("📋 Starting scenario");

        var runTasks = Enumerable.Range(0, config.Runs).Select(i =>
            runLimit.RunAsync(() => ExecuteRun(i, scenario, evalSkill, config, usePairwise, singleScenario, spinner, sessionsDir, sessionDb, skillSha)));
        var runResults = await Task.WhenAll(runTasks);

        scenarioLog($"✓ All {config.Runs} run(s) complete");

        var baselineRuns = runResults.Select(r => r.Baseline).ToList();
        var isolatedRuns = runResults.Select(r => r.SkilledIsolated).ToList();
        var pluginRuns = runResults.Select(r => r.SkilledPlugin).ToList();
        var perRunPairwise = runResults.Select(r => r.Pairwise).ToList();

        // Per-run improvement scores — effective score is min(isolated, plugin)
        // Pairwise result is generated against the worse-scoring run (isolated
        // or plugin).  Apply it only to that matching comparison so it does not
        // skew the other one.
        var perRunIsolatedScores = new List<double>();
        var perRunPluginScores = new List<double>();
        for (int i = 0; i < baselineRuns.Count; i++)
        {
            var pw = perRunPairwise[i];
            bool pairwiseFromPlugin = runResults[i].PairwiseFromPlugin;
            var isoComp = Comparator.CompareScenario(scenario.Name, baselineRuns[i], isolatedRuns[i],
                pairwiseFromPlugin ? null : pw);
            var plgComp = Comparator.CompareScenario(scenario.Name, baselineRuns[i], pluginRuns[i],
                pairwiseFromPlugin ? pw : null);
            perRunIsolatedScores.Add(isoComp.ImprovementScore);
            perRunPluginScores.Add(plgComp.ImprovementScore);
        }

        var perRunScores = perRunIsolatedScores
            .Zip(perRunPluginScores, (iso, plg) => Math.Min(iso, plg))
            .ToList();

        var avgBaseline = AverageResults(baselineRuns);
        var avgIsolated = AverageResults(isolatedRuns);
        var avgPlugin = AverageResults(pluginRuns);
        // Select the best pairwise result and track which run it came from
        int bestPairwiseIdx = -1;
        for (int i = 0; i < perRunPairwise.Count; i++)
        {
            if (perRunPairwise[i]?.PositionSwapConsistent == true) { bestPairwiseIdx = i; break; }
        }
        if (bestPairwiseIdx < 0)
        {
            for (int i = 0; i < perRunPairwise.Count; i++)
            {
                if (perRunPairwise[i] is not null) { bestPairwiseIdx = i; break; }
            }
        }
        var bestPairwise = bestPairwiseIdx >= 0 ? perRunPairwise[bestPairwiseIdx] : null;

        // Two comparisons — apply pairwise only to the matching one,
        // using the source run's flag (not any-run) to avoid misattribution.
        bool aggPairwiseFromPlugin = bestPairwiseIdx >= 0 && runResults[bestPairwiseIdx].PairwiseFromPlugin;
        var isoComparison = Comparator.CompareScenario(scenario.Name, avgBaseline, avgIsolated,
            aggPairwiseFromPlugin ? null : bestPairwise);
        var plgComparison = Comparator.CompareScenario(scenario.Name, avgBaseline, avgPlugin,
            aggPairwiseFromPlugin ? bestPairwise : null);

        // Build the combined ScenarioComparison
        var comparison = new ScenarioComparison
        {
            ScenarioName = scenario.Name,
            Baseline = avgBaseline,
            SkilledIsolated = avgIsolated,
            SkilledPlugin = avgPlugin,
            ImprovementScore = Math.Min(isoComparison.ImprovementScore, plgComparison.ImprovementScore),
            IsolatedImprovementScore = isoComparison.ImprovementScore,
            PluginImprovementScore = plgComparison.ImprovementScore,
            Breakdown = isoComparison.ImprovementScore <= plgComparison.ImprovementScore
                ? isoComparison.Breakdown : plgComparison.Breakdown,
            IsolatedBreakdown = isoComparison.Breakdown,
            PluginBreakdown = plgComparison.Breakdown,
            PairwiseResult = bestPairwise,
        };
        comparison.PerRunScores = perRunScores;

        // Aggregate skill activation — BOTH skilled runs independently
        var allIsoActivations = runResults.Select(r => r.SkillActivationIsolated).ToList();
        var allPlgActivations = runResults.Select(r => r.SkillActivationPlugin).ToList();

        comparison.SkillActivationIsolated = new SkillActivationInfo(
            Activated: allIsoActivations.Any(a => a.Activated),
            DetectedSkills: allIsoActivations.SelectMany(a => a.DetectedSkills).Distinct().ToList(),
            ExtraTools: allIsoActivations.SelectMany(a => a.ExtraTools).Distinct().ToList(),
            SkillEventCount: allIsoActivations.Sum(a => a.SkillEventCount));

        comparison.SkillActivationPlugin = new SkillActivationInfo(
            Activated: allPlgActivations.Any(a => a.Activated),
            DetectedSkills: allPlgActivations.SelectMany(a => a.DetectedSkills).Distinct().ToList(),
            ExtraTools: allPlgActivations.SelectMany(a => a.ExtraTools).Distinct().ToList(),
            SkillEventCount: allPlgActivations.Sum(a => a.SkillEventCount));

        // Propagate timeout info from any run
        comparison.TimedOut = runResults.Any(r =>
            r.Baseline.Metrics.TimedOut ||
            r.SkilledIsolated.Metrics.TimedOut ||
            r.SkilledPlugin.Metrics.TimedOut);

        // Propagate expect_activation from scenario config
        comparison.ExpectActivation = scenario.ExpectActivation;

        return comparison;
    }

    private sealed record RunExecutionResult(
        RunResult Baseline,
        RunResult SkilledIsolated,
        RunResult SkilledPlugin,
        PairwiseJudgeResult? Pairwise,
        bool PairwiseFromPlugin,
        SkillActivationInfo SkillActivationIsolated,
        SkillActivationInfo SkillActivationPlugin);

    private static async Task<RunExecutionResult> ExecuteRun(
        int runIndex,
        EvalScenario scenario,
        EvalSkillInfo evalSkill,
        ValidatorConfig config,
        bool usePairwise,
        bool singleScenario,
        Spinner spinner,
        string? sessionsDir,
        SessionDatabase? sessionDb,
        string? skillSha)
    {
        var skill = evalSkill.Skill;
        var runTag = config.Runs > 1
            ? (singleScenario ? $"[{skill.Name}/{runIndex + 1}]" : $"[{skill.Name}/{scenario.Name}/{runIndex + 1}]")
            : (singleScenario ? $"[{skill.Name}]" : $"[{skill.Name}/{scenario.Name}]");
        var runLog = (string msg) => spinner.Log($"{runTag} {msg}");

        if (config.Verbose)
            runLog("running agents...");

        var pluginRoot = SkillDiscovery.FindPluginRoot(skill.Path);
        var baselineSessionId = Guid.NewGuid().ToString("N");
        var isolatedSessionId = Guid.NewGuid().ToString("N");
        var pluginSessionId = Guid.NewGuid().ToString("N");

        var baselineConfigDir = sessionsDir is not null ? Path.Combine("sessions", baselineSessionId) : null;
        var isolatedConfigDir = sessionsDir is not null ? Path.Combine("sessions", isolatedSessionId) : null;
        var pluginConfigDir = sessionsDir is not null ? Path.Combine("sessions", pluginSessionId) : null;
        var rubricJson = JsonSerializer.Serialize(scenario.Rubric?.ToArray() ?? [], SkillValidatorJsonContext.Default.StringArray);

        sessionDb?.RegisterSession(baselineSessionId, skill.Name, skill.Path, scenario.Name, runIndex,
            "baseline", config.Model, baselineConfigDir, null, scenario.Prompt, skillSha, rubricJson);
        sessionDb?.RegisterSession(isolatedSessionId, skill.Name, skill.Path, scenario.Name, runIndex,
            "with-skill-isolated", config.Model, isolatedConfigDir, null, scenario.Prompt, skillSha, rubricJson);
        sessionDb?.RegisterSession(pluginSessionId, skill.Name, skill.Path, scenario.Name, runIndex,
            "with-skill-plugin", config.Model, pluginConfigDir, null, scenario.Prompt, skillSha, rubricJson);

        var agentTasks = await Task.WhenAll(
            // 1. Baseline: no plugin, no skills — vanilla agent
            AgentRunner.RunAgent(new RunOptions(scenario, null, evalSkill.EvalPath, config.Model, config.Verbose,
                PluginRoot: null, Log: runLog, SessionsDir: sessionsDir, SessionId: baselineSessionId)),
            // 2. Skilled-isolated: single skill only (current behavior)
            AgentRunner.RunAgent(new RunOptions(scenario, skill, evalSkill.EvalPath, config.Model, config.Verbose,
                PluginRoot: null, Log: runLog, McpServers: evalSkill.McpServers, SessionsDir: sessionsDir, SessionId: isolatedSessionId)),
            // 3. Skilled-plugin: load entire plugin from plugin root directory
            AgentRunner.RunAgent(new RunOptions(scenario, skill, evalSkill.EvalPath, config.Model, config.Verbose,
                PluginRoot: pluginRoot, Log: runLog, McpServers: evalSkill.McpServers, SessionsDir: sessionsDir, SessionId: pluginSessionId)));
        var baselineMetrics = agentTasks[0];
        var isolatedMetrics = agentTasks[1];
        var pluginMetrics = agentTasks[2];

        if (sessionDb is not null)
        {
            var baselineStatus = baselineMetrics.TimedOut ? "timed_out" : "completed";
            var isolatedStatus = isolatedMetrics.TimedOut ? "timed_out" : "completed";
            var pluginStatus = pluginMetrics.TimedOut ? "timed_out" : "completed";
            sessionDb.CompleteSession(baselineSessionId, baselineStatus, JsonSerializer.Serialize(baselineMetrics, SkillValidatorJsonContext.Default.RunMetrics));
            sessionDb.CompleteSession(isolatedSessionId, isolatedStatus, JsonSerializer.Serialize(isolatedMetrics, SkillValidatorJsonContext.Default.RunMetrics));
            sessionDb.CompleteSession(pluginSessionId, pluginStatus, JsonSerializer.Serialize(pluginMetrics, SkillValidatorJsonContext.Default.RunMetrics));
        }

        // Evaluate assertions on all three runs
        if (scenario.Assertions is { Count: > 0 })
        {
            baselineMetrics.AssertionResults = await AssertionEvaluator.EvaluateAssertions(scenario.Assertions, baselineMetrics.AgentOutput, baselineMetrics.WorkDir);
            isolatedMetrics.AssertionResults = await AssertionEvaluator.EvaluateAssertions(scenario.Assertions, isolatedMetrics.AgentOutput, isolatedMetrics.WorkDir);
            pluginMetrics.AssertionResults = await AssertionEvaluator.EvaluateAssertions(scenario.Assertions, pluginMetrics.AgentOutput, pluginMetrics.WorkDir);
        }

        // Evaluate constraints on all three runs
        var baselineConstraints = AssertionEvaluator.EvaluateConstraints(scenario, baselineMetrics);
        var isolatedConstraints = AssertionEvaluator.EvaluateConstraints(scenario, isolatedMetrics);
        var pluginConstraints = AssertionEvaluator.EvaluateConstraints(scenario, pluginMetrics);
        baselineMetrics.AssertionResults = [..baselineMetrics.AssertionResults, ..baselineConstraints];
        isolatedMetrics.AssertionResults = [..isolatedMetrics.AssertionResults, ..isolatedConstraints];
        pluginMetrics.AssertionResults = [..pluginMetrics.AssertionResults, ..pluginConstraints];

        // Task completion for all three
        if (scenario.Assertions is { Count: > 0 } || baselineConstraints.Count > 0)
        {
            baselineMetrics.TaskCompleted = baselineMetrics.AssertionResults.All(a => a.Passed);
            isolatedMetrics.TaskCompleted = isolatedMetrics.AssertionResults.All(a => a.Passed);
            pluginMetrics.TaskCompleted = pluginMetrics.AssertionResults.All(a => a.Passed);
        }
        else
        {
            baselineMetrics.TaskCompleted = baselineMetrics.ErrorCount == 0;
            isolatedMetrics.TaskCompleted = isolatedMetrics.ErrorCount == 0;
            pluginMetrics.TaskCompleted = pluginMetrics.ErrorCount == 0;
        }

        // Judge all three runs independently (failures are non-fatal)
        var judgeOpts = new JudgeOptions(config.JudgeModel, config.Verbose, config.JudgeTimeout, baselineMetrics.WorkDir, skill.Path);

        var baselineJudgeTask = Services.Judge.JudgeRun(scenario, baselineMetrics, judgeOpts, runLog);
        var isolatedJudgeTask = Services.Judge.JudgeRun(
            scenario, isolatedMetrics, judgeOpts with { WorkDir = isolatedMetrics.WorkDir }, runLog);
        var pluginJudgeTask = Services.Judge.JudgeRun(
            scenario, pluginMetrics, judgeOpts with { WorkDir = pluginMetrics.WorkDir }, runLog);

        var (baselineJudge, baselineJudgeTokens) = await SafeJudge(baselineJudgeTask, "baseline", runLog);
        var (isolatedJudge, isolatedJudgeTokens) = await SafeJudge(isolatedJudgeTask, "isolated", runLog);
        var (pluginJudge, pluginJudgeTokens) = await SafeJudge(pluginJudgeTask, "plugin", runLog);

        // Accumulate judge tokens into each run's metrics
        AccumulateJudgeTokens(baselineMetrics, baselineJudgeTokens);
        AccumulateJudgeTokens(isolatedMetrics, isolatedJudgeTokens);
        AccumulateJudgeTokens(pluginMetrics, pluginJudgeTokens);

        if (sessionDb is not null)
        {
            sessionDb.SaveJudgeResult(baselineSessionId, JsonSerializer.Serialize(baselineJudge, SkillValidatorJsonContext.Default.JudgeResult));
            sessionDb.SaveJudgeResult(isolatedSessionId, JsonSerializer.Serialize(isolatedJudge, SkillValidatorJsonContext.Default.JudgeResult));
            sessionDb.SaveJudgeResult(pluginSessionId, JsonSerializer.Serialize(pluginJudge, SkillValidatorJsonContext.Default.JudgeResult));
        }

        var baselineResult = new RunResult(baselineMetrics, baselineJudge);
        var isolatedResult = new RunResult(isolatedMetrics, isolatedJudge);
        var pluginResult = new RunResult(pluginMetrics, pluginJudge);

        // Pairwise judging — compare baseline vs worse-scoring skilled run
        // Track which run the pairwise result corresponds to.
        PairwiseJudgeResult? pairwise = null;
        bool pairwiseFromPlugin = false;
        if (usePairwise)
        {
            pairwiseFromPlugin = pluginJudge.OverallScore < isolatedJudge.OverallScore;
            var worseSkilled = pairwiseFromPlugin
                ? pluginMetrics : isolatedMetrics;
            try
            {
                var (pairwiseResult, pairwiseTokens) = await Services.PairwiseJudge.Judge(
                    scenario, baselineMetrics, worseSkilled,
                    new PairwiseJudgeOptions(config.JudgeModel, config.Verbose, config.JudgeTimeout, baselineMetrics.WorkDir, skill.Path, worseSkilled.WorkDir),
                    runLog);
                pairwise = pairwiseResult;
                // Attribute pairwise judge tokens to both the baseline and the compared run
                AccumulateJudgeTokens(baselineMetrics, pairwiseTokens);
                AccumulateJudgeTokens(worseSkilled, pairwiseTokens);
                if (sessionDb is not null && pairwise is not null)
                {
                    sessionDb.SavePairwiseResult(baselineSessionId, JsonSerializer.Serialize(pairwise, SkillValidatorJsonContext.Default.PairwiseJudgeResult));
                }
            }
            catch (Exception error)
            {
                runLog($"⚠️  Pairwise judge failed: {error}");
            }
        }

        // Skill activation — check both skilled runs independently
        // Pass the target skill name so that in plugin runs only the skill under
        // test counts towards activation (prevents sibling-skill false positives).
        var isolatedActivation = MetricsCollector.ExtractSkillActivation(
            isolatedMetrics.Events, baselineMetrics.ToolCallBreakdown, skill.Name);
        var pluginActivation = MetricsCollector.ExtractSkillActivation(
            pluginMetrics.Events, baselineMetrics.ToolCallBreakdown, skill.Name);

        runLog(isolatedActivation.Activated
            ? $"🔌 Skill activated (isolated): skills={string.Join(", ", isolatedActivation.DetectedSkills)}"
            : "⚠️  Skill NOT activated (isolated)");
        runLog(pluginActivation.Activated
            ? $"🔌 Skill activated (plugin): skills={string.Join(", ", pluginActivation.DetectedSkills)}"
            : "⚠️  Skill NOT activated (plugin)");

        if (config.Verbose)
            runLog("✓ complete");

        return new RunExecutionResult(baselineResult, isolatedResult, pluginResult, pairwise,
            pairwiseFromPlugin, isolatedActivation, pluginActivation);
    }

    private static async Task<(JudgeResult Result, TokenUsage Tokens)> SafeJudge(Task<(JudgeResult Result, TokenUsage Tokens)> task, string label, Action<string> runLog)
    {
        try
        {
            return await task;
        }
        catch (Exception error)
        {
            var shortMsg = SanitizeErrorMessage(error.Message);
            runLog($"\x1b[33m⚠️  Judge ({label}) failed, using fallback scores: {shortMsg}\x1b[0m");
            return (new JudgeResult([], 3, $"Judge failed: {shortMsg}"), TokenUsage.Zero);
        }
    }

    // --- Noise-only evaluation: skill-only vs all-skills (no pure-agent baseline) ---

    private static async Task<SkillVerdict> EvaluateSkillNoise(
        EvalSkillInfo evalSkill,
        IReadOnlyList<EvalSkillInfo> noiseEvalSkills,
        ValidatorConfig config,
        SkillProfile profile,
        Spinner spinner)
    {
        var skill = evalSkill.Skill;
        var prefix = $"[{skill.Name}]";
        var log = (string msg) => spinner.Log($"{prefix} {msg}");

        NoiseTestResult noiseResult;
        try
        {
            noiseResult = await ExecuteNoiseTest(evalSkill, noiseEvalSkills, config, spinner);
        }
        catch (Exception ex)
        {
            log($"\u26a0\ufe0f Noise test failed: {ex.Message}");
            return new SkillVerdict
            {
                SkillName = skill.Name,
                SkillPath = skill.Path,
                Passed = false,
                Scenarios = [],
                OverallImprovementScore = 0,
                Reason = $"Noise test execution failed: {ex.Message}",
                FailureKind = "noise_degradation",
                ProfileWarnings = profile.Warnings,
            };
        }

        var verdict = new SkillVerdict
        {
            SkillName = skill.Name,
            SkillPath = skill.Path,
            Passed = noiseResult.Passed,
            Scenarios = [],
            OverallImprovementScore = 0,
            Reason = noiseResult.Reason,
            FailureKind = noiseResult.Passed ? null : "noise_degradation",
            ProfileWarnings = profile.Warnings,
            NoiseTestResult = noiseResult,
        };

        if (!noiseResult.Passed)
        {
            log($"\x1b[33m\u26a0\ufe0f  Noise test: quality degraded by {noiseResult.OverallDegradation * 100:F1}% with {noiseResult.TotalSkillsLoaded} skills loaded\x1b[0m");
        }
        else
        {
            log($"\u2705 Noise test passed ({noiseResult.TotalSkillsLoaded} skills loaded, degradation: {noiseResult.OverallDegradation * 100:F1}%)");
        }

        var noiseNotActivated = noiseResult.Scenarios.Where(s => s.SkillActivation is { Activated: false }).ToList();
        if (noiseNotActivated.Count > 0)
        {
            var names = string.Join(", ", noiseNotActivated.Select(s => s.ScenarioName));
            log($"\x1b[33m\u26a0\ufe0f  Skills NOT activated in noise scenario(s): {names}\x1b[0m");
        }

        log($"{(verdict.Passed ? "✅" : "❌")} Done (noise degradation: {noiseResult.OverallDegradation * 100:F1}%)");
        return verdict;
    }

    // --- Noise test: run scenarios with all discovered skills loaded ---

    private static async Task<NoiseTestResult> ExecuteNoiseTest(
        EvalSkillInfo targetEvalSkill,
        IReadOnlyList<EvalSkillInfo> allEvalSkills,
        ValidatorConfig config,
        Spinner spinner)
    {
        var targetSkill = targetEvalSkill.Skill;
        var prefix = $"[{targetSkill.Name}/noise]";
        var log = (string msg) => spinner.Log($"{prefix} {msg}");

        var otherSkills = allEvalSkills
            .Where(s => !string.Equals(s.Skill.Path, targetSkill.Path, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Skill)
            .ToList();
        int totalLoaded = otherSkills.Count + 1; // target + others

        log($"🔊 Running noise test with {totalLoaded} skills loaded...");

        var noiseScenarios = new List<NoiseScenarioResult>();

        var effectiveParallelScenarios = targetEvalSkill.EvalConfig!.MaxParallelScenarios.HasValue
            ? Math.Min(config.ParallelScenarios, targetEvalSkill.EvalConfig.MaxParallelScenarios.Value)
            : config.ParallelScenarios;
        var effectiveParallelRuns = targetEvalSkill.EvalConfig.MaxParallelRuns.HasValue
            ? Math.Min(config.ParallelRuns, targetEvalSkill.EvalConfig.MaxParallelRuns.Value)
            : config.ParallelRuns;

        using var scenarioLimit = new ConcurrencyLimiter(effectiveParallelScenarios);

        var tasks = targetEvalSkill.EvalConfig!.Scenarios
            .Where(s => s.ExpectActivation) // only test positive scenarios
            .Select(scenario => scenarioLimit.RunAsync(async () =>
            {
                var tag = $"[{targetSkill.Name}/noise/{scenario.Name}]";
                var scenarioLog = (string msg) => spinner.Log($"{tag} {msg}");

                scenarioLog($"running skill-only vs all-skills ({config.Runs} run(s))...");

                using var runLimit = new ConcurrencyLimiter(effectiveParallelRuns);

                var runResults = await Task.WhenAll(Enumerable.Range(0, config.Runs).Select(runIndex =>
                    runLimit.RunAsync(async () =>
                    {
                        // Run with target skill only
                        var skillOnlyMetrics = await AgentRunner.RunAgent(new RunOptions(
                            scenario, targetSkill, targetEvalSkill.EvalPath, config.Model, config.Verbose,
                            Log: scenarioLog, McpServers: targetEvalSkill.McpServers));

                        // Run with all skills loaded
                        var allSkillsMetrics = await AgentRunner.RunAgent(new RunOptions(
                            scenario, targetSkill, targetEvalSkill.EvalPath, config.Model, config.Verbose,
                            Log: scenarioLog, AdditionalSkills: otherSkills, McpServers: targetEvalSkill.McpServers));

                        // Evaluate assertions on both
                        if (scenario.Assertions is { Count: > 0 })
                        {
                            skillOnlyMetrics.AssertionResults = await AssertionEvaluator.EvaluateAssertions(
                                scenario.Assertions, skillOnlyMetrics.AgentOutput, skillOnlyMetrics.WorkDir);
                            allSkillsMetrics.AssertionResults = await AssertionEvaluator.EvaluateAssertions(
                                scenario.Assertions, allSkillsMetrics.AgentOutput, allSkillsMetrics.WorkDir);
                        }
                        var soConstraints = AssertionEvaluator.EvaluateConstraints(scenario, skillOnlyMetrics);
                        var asConstraints = AssertionEvaluator.EvaluateConstraints(scenario, allSkillsMetrics);
                        skillOnlyMetrics.AssertionResults = [..skillOnlyMetrics.AssertionResults, ..soConstraints];
                        allSkillsMetrics.AssertionResults = [..allSkillsMetrics.AssertionResults, ..asConstraints];

                        skillOnlyMetrics.TaskCompleted = scenario.Assertions is { Count: > 0 } || soConstraints.Count > 0
                            ? skillOnlyMetrics.AssertionResults.All(a => a.Passed)
                            : skillOnlyMetrics.ErrorCount == 0;
                        allSkillsMetrics.TaskCompleted = scenario.Assertions is { Count: > 0 } || asConstraints.Count > 0
                            ? allSkillsMetrics.AssertionResults.All(a => a.Passed)
                            : allSkillsMetrics.ErrorCount == 0;

                        // Judge both runs
                        var judgeOpts = new JudgeOptions(config.JudgeModel, config.Verbose, config.JudgeTimeout, skillOnlyMetrics.WorkDir, targetSkill.Path);
                        JudgeResult skillOnlyJudge, allSkillsJudge;
                        try
                        {
                            var (result, tokens) = await Services.Judge.JudgeRun(scenario, skillOnlyMetrics, judgeOpts, log);
                            skillOnlyJudge = result;
                            AccumulateJudgeTokens(skillOnlyMetrics, tokens);
                        }
                        catch
                        {
                            skillOnlyJudge = new JudgeResult([], 3, "Judge failed");
                        }
                        try
                        {
                            var (result, tokens) = await Services.Judge.JudgeRun(scenario, allSkillsMetrics,
                                judgeOpts with { WorkDir = allSkillsMetrics.WorkDir }, log);
                            allSkillsJudge = result;
                            AccumulateJudgeTokens(allSkillsMetrics, tokens);
                        }
                        catch
                        {
                            allSkillsJudge = new JudgeResult([], 3, "Judge failed");
                        }

                        var skillOnly = new RunResult(skillOnlyMetrics, skillOnlyJudge);
                        var allSkills = new RunResult(allSkillsMetrics, allSkillsJudge);
                        var activation = MetricsCollector.ExtractSkillActivation(
                            allSkillsMetrics.Events, skillOnlyMetrics.ToolCallBreakdown);

                        return (SkillOnly: skillOnly, AllSkills: allSkills, Activation: activation);
                    })));

                scenarioLog($"✓ All {config.Runs} noise run(s) complete");

                // Average across runs, then compare the averaged results
                var avgSkillOnly = AverageResults(runResults.Select(r => r.SkillOnly).ToList());
                var avgAllSkills = AverageResults(runResults.Select(r => r.AllSkills).ToList());

                // Compare: skill-only is "baseline", all-skills is "with-skill"
                // A positive score means all-skills is *better*, negative means degradation
                var comparison = Comparator.CompareScenario(scenario.Name, avgSkillOnly, avgAllSkills);
                var degradation = -comparison.ImprovementScore; // positive = degradation

                // Aggregate activation info across runs
                var activation = new SkillActivationInfo(
                    Activated: runResults.Any(r => r.Activation.Activated),
                    DetectedSkills: runResults.SelectMany(r => r.Activation.DetectedSkills).Distinct().ToList(),
                    ExtraTools: runResults.SelectMany(r => r.Activation.ExtraTools).Distinct().ToList(),
                    SkillEventCount: runResults.Sum(r => r.Activation.SkillEventCount));

                scenarioLog($"✓ degradation: {degradation * 100:F1}%, target skill activated: {activation.Activated}");

                return new NoiseScenarioResult(
                    scenario.Name,
                    avgSkillOnly,
                    avgAllSkills,
                    degradation,
                    comparison.Breakdown,
                    activation,
                    totalLoaded);
            }));

        noiseScenarios = (await Task.WhenAll(tasks)).ToList();

        // Aggregate only positive (harmful) degradations so that improvements don't mask regressions
        double overallDegradation = noiseScenarios.Count > 0 ? noiseScenarios.Average(s => Math.Max(0, s.DegradationScore)) : 0;

        // Also enforce a per-scenario cap so a single bad scenario can't be hidden by others
        var worstScenario = noiseScenarios.Count > 0 ? noiseScenarios.MaxBy(s => s.DegradationScore) : null;
        double worstDegradation = worstScenario?.DegradationScore ?? 0;

        bool avgPassed = overallDegradation <= config.NoiseDegradationLimit;
        bool worstScenarioPassed = worstDegradation <= config.NoiseMaxScenarioDegradation;
        bool passed = avgPassed && worstScenarioPassed;

        string reason;
        if (!worstScenarioPassed)
        {
            reason = $"Scenario '{worstScenario!.ScenarioName}' degradation {worstDegradation * 100:F1}% exceeds per-scenario threshold of {config.NoiseMaxScenarioDegradation * 100:F1}% ({totalLoaded} skills loaded)";
        }
        else if (!avgPassed)
        {
            reason = $"Average degradation {overallDegradation * 100:F1}% exceeds threshold of {config.NoiseDegradationLimit * 100:F1}% ({totalLoaded} skills loaded)";
        }
        else
        {
            reason = $"Quality degradation {overallDegradation * 100:F1}% within threshold of {config.NoiseDegradationLimit * 100:F1}%, worst scenario {worstDegradation * 100:F1}% within {config.NoiseMaxScenarioDegradation * 100:F1}% ({totalLoaded} skills loaded)";
        }

        return new NoiseTestResult(noiseScenarios, overallDegradation, passed, reason, totalLoaded);
    }

    private static void AccumulateJudgeTokens(RunMetrics metrics, TokenUsage tokens)
    {
        metrics.JudgeInputTokens += tokens.InputTokens;
        metrics.JudgeOutputTokens += tokens.OutputTokens;
        metrics.JudgeCacheReadTokens += tokens.CacheReadTokens;
        metrics.JudgeCacheWriteTokens += tokens.CacheWriteTokens;
    }

    private static RunResult AverageResults(List<RunResult> runs)
    {
        if (runs.Count == 1) return runs[0];

        static double Avg(IEnumerable<double> nums) => nums.Average();
        static int AvgRound(IEnumerable<int> nums) => (int)Math.Round(nums.Average());

        var avgMetrics = new RunMetrics
        {
            TokenEstimate = AvgRound(runs.Select(r => r.Metrics.TokenEstimate)),
            InputTokens = AvgRound(runs.Select(r => r.Metrics.InputTokens)),
            OutputTokens = AvgRound(runs.Select(r => r.Metrics.OutputTokens)),
            CacheReadTokens = AvgRound(runs.Select(r => r.Metrics.CacheReadTokens)),
            CacheWriteTokens = AvgRound(runs.Select(r => r.Metrics.CacheWriteTokens)),
            JudgeInputTokens = AvgRound(runs.Select(r => r.Metrics.JudgeInputTokens)),
            JudgeOutputTokens = AvgRound(runs.Select(r => r.Metrics.JudgeOutputTokens)),
            JudgeCacheReadTokens = AvgRound(runs.Select(r => r.Metrics.JudgeCacheReadTokens)),
            JudgeCacheWriteTokens = AvgRound(runs.Select(r => r.Metrics.JudgeCacheWriteTokens)),
            ToolCallCount = AvgRound(runs.Select(r => r.Metrics.ToolCallCount)),
            ToolCallBreakdown = runs[0].Metrics.ToolCallBreakdown,
            TurnCount = AvgRound(runs.Select(r => r.Metrics.TurnCount)),
            WallTimeMs = (long)Math.Round(runs.Average(r => r.Metrics.WallTimeMs)),
            ErrorCount = AvgRound(runs.Select(r => r.Metrics.ErrorCount)),
            TimedOut = runs.Any(r => r.Metrics.TimedOut),
            AssertionResults = runs[^1].Metrics.AssertionResults,
            TaskCompleted = runs.Any(r => r.Metrics.TaskCompleted),
            AgentOutput = runs[^1].Metrics.AgentOutput,
            Events = runs[^1].Metrics.Events,
            WorkDir = runs[^1].Metrics.WorkDir,
        };

        var avgJudge = new JudgeResult(
            runs[0].JudgeResult.RubricScores.Select((s, i) => new RubricScore(
                s.Criterion,
                Math.Round(Avg(runs.Select(r => i < r.JudgeResult.RubricScores.Count ? r.JudgeResult.RubricScores[i].Score : 3)) * 10) / 10,
                s.Reasoning)).ToList(),
            Math.Round(Avg(runs.Select(r => r.JudgeResult.OverallScore)) * 10) / 10,
            runs[^1].JudgeResult.OverallReasoning);

        return new RunResult(avgMetrics, avgJudge);
    }

    /// <summary>
    /// Collapses multiline error messages to single-line and truncates to a reasonable length
    /// so they don't bloat console/markdown reports.
    /// </summary>
    private static string SanitizeErrorMessage(string? message)
    {
        var raw = message ?? "unknown error";
        var singleLine = raw.ReplaceLineEndings(" ");
        return singleLine.Length > 150 ? singleLine[..150] + "…" : singleLine;
    }
}
