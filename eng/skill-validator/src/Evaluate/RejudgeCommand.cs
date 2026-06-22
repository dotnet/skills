using System.CommandLine;
using System.Text.Json;
using SkillValidator.Shared;

namespace SkillValidator.Evaluate;

public static class RejudgeCommand
{
    public static Command Create()
    {
        var resultsDirArg = new Argument<string>("results-dir") { Description = "Path to a timestamped results directory containing sessions.db" };
        var judgeModelOpt = new Option<string?>("--judge-model") { Description = "Model to use for judging (defaults to the persisted judge model when available)" };
        var baselineDirOpt = new Option<string?>("--baseline-dir") { Description = "Path to a separate results directory whose sessions.db holds the baseline runs to pair against (cross-directory judging)" };
        var judgeModeOpt = new Option<string>("--judge-mode") { Description = "Judge mode: pairwise, independent, or both", DefaultValueFactory = _ => "pairwise" }
            .AcceptOnlyFromAmong("pairwise", "independent", "both");
        var judgeTimeoutOpt = new Option<int>("--judge-timeout") { Description = "Judge timeout in seconds", DefaultValueFactory = _ => 300 };
        var verboseOpt = new Option<bool>("--verbose") { Description = "Show detailed output" };
        var minImprovementOpt = new Option<double>("--min-improvement") { Description = "Minimum improvement score to pass (0-1)", DefaultValueFactory = _ => 0.1 };
        var requireCompletionOpt = new Option<bool>("--require-completion") { Description = "Fail if skill regresses task completion", DefaultValueFactory = _ => true };
        var confidenceLevelOpt = new Option<double>("--confidence-level") { Description = "Confidence level for statistical intervals (0-1)", DefaultValueFactory = _ => 0.95 };

        var command = new Command("rejudge", "Re-run judges on saved sessions without re-running agents")
        {
            resultsDirArg,
            judgeModelOpt,
            baselineDirOpt,
            judgeModeOpt,
            judgeTimeoutOpt,
            verboseOpt,
            minImprovementOpt,
            requireCompletionOpt,
            confidenceLevelOpt,
        };

        command.SetAction(async (parseResult, _) =>
        {
            var resultsDir = parseResult.GetValue(resultsDirArg)!;
            var judgeModel = parseResult.GetValue(judgeModelOpt);
            var baselineDir = parseResult.GetValue(baselineDirOpt);
            var verbose = parseResult.GetValue(verboseOpt);
            var judgeTimeout = parseResult.GetValue(judgeTimeoutOpt) * 1000;
            var minImprovement = parseResult.GetValue(minImprovementOpt);
            var requireCompletion = parseResult.GetValue(requireCompletionOpt);
            var confidenceLevel = parseResult.GetValue(confidenceLevelOpt);

            var judgeMode = parseResult.GetValue(judgeModeOpt) switch
            {
                "independent" => JudgeMode.Independent,
                "both" => JudgeMode.Both,
                _ => JudgeMode.Pairwise,
            };

            if (!string.IsNullOrWhiteSpace(baselineDir))
            {
                return await RunCrossDir(resultsDir, baselineDir, judgeModel, judgeMode, judgeTimeout,
                    verbose, minImprovement, requireCompletion, confidenceLevel);
            }

            return await Run(resultsDir, judgeModel, judgeMode, judgeTimeout, verbose,
                minImprovement, requireCompletion, confidenceLevel);
        });

        return command;
    }

    public static async Task<int> Run(
        string resultsDir,
        string? judgeModel,
        JudgeMode judgeMode,
        int judgeTimeout,
        bool verbose,
        double minImprovement,
        bool requireCompletion,
        double confidenceLevel)
    {
        var dbPath = Path.Combine(resultsDir, "sessions.db");
        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"No sessions.db found at {dbPath}");
            Console.Error.WriteLine("Use --keep-sessions during evaluation to enable rejudging.");
            return 1;
        }

        using var sessionDb = new SessionDatabase(dbPath);
        var sessions = sessionDb.GetCompletedSessions();
        if (sessions.Count == 0)
        {
            Console.Error.WriteLine("No completed sessions found in the database.");
            return 1;
        }

        var schemaInfo = sessionDb.GetSchemaInfo();
        var persistedJudgeModel = schemaInfo.GetValueOrDefault("judge_model");
        var effectiveJudgeModel = judgeModel ?? persistedJudgeModel;
        if (string.IsNullOrWhiteSpace(effectiveJudgeModel))
        {
            Console.Error.WriteLine("No persisted judge model found in sessions.db. Re-run with --judge-model to specify the judge explicitly.");
            return 1;
        }

        try
        {
            var client = await AgentRunner.GetSharedClient(verbose);
            var models = await client.ListModelsAsync();
            if (!models.Any(m => m.Id == effectiveJudgeModel))
            {
                Console.Error.WriteLine($"Invalid model: \"{effectiveJudgeModel}\"\nAvailable models: {string.Join(", ", models.Select(m => m.Id))}");
                return 1;
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Failed to validate model: {error}");
            return 1;
        }

        Console.WriteLine($"Rejudging {sessions.Count} sessions with model: {effectiveJudgeModel}, mode: {judgeMode}");

        bool usePairwise = judgeMode is JudgeMode.Pairwise or JudgeMode.Both;
        var runGroups = sessions
            .GroupBy(s => (s.SkillName, s.ScenarioName, s.RunIndex))
            .Where(g => g.Any(s => s.Role == "baseline") &&
                (g.Any(s => s.Role == "with-skill-isolated") || g.Any(s => s.Role == "with-skill")))
            .ToList();

        if (runGroups.Count == 0)
        {
            Console.Error.WriteLine("No complete run groups found.");
            return 1;
        }

        Console.WriteLine($"Found {runGroups.Count} run group(s) across {runGroups.Select(g => g.Key.SkillName).Distinct().Count()} skill(s)\n");

        var firstSession = sessions[0];
        var verdicts = new List<SkillVerdict>();
        foreach (var skillGroup in runGroups.GroupBy(g => g.Key.SkillName))
        {
            var skillName = skillGroup.Key;
            var firstSkillSession = skillGroup.First().First();
            Console.WriteLine($"[{skillName}] Rejudging...");

            var comparisons = new List<ScenarioComparison>();
            foreach (var scenarioGroup in skillGroup.GroupBy(g => g.Key.ScenarioName))
            {
                var scenarioName = scenarioGroup.Key;
                var storedRubric = GetStoredRubric(skillName, scenarioName, scenarioGroup.SelectMany(g => g));
                var rejudgedRuns = new List<RejudgedRun>();

                foreach (var runGroup in scenarioGroup)
                {
                    var baselineSess = runGroup.First(s => s.Role == "baseline");
                    var isolatedSess = runGroup.FirstOrDefault(s => s.Role == "with-skill-isolated")
                        ?? runGroup.FirstOrDefault(s => s.Role == "with-skill");
                    if (isolatedSess is null)
                        continue;

                    var pluginSess = runGroup.FirstOrDefault(s => s.Role == "with-skill-plugin");
                    var prompt = baselineSess.Prompt ?? isolatedSess.Prompt ?? pluginSess?.Prompt ?? "";
                    var scenario = new EvalScenario(scenarioName, prompt, Rubric: storedRubric);
                    Action<string>? log = verbose ? msg => Console.WriteLine($"  [{scenarioName}/{runGroup.Key.RunIndex + 1}] {msg}") : null;

                    rejudgedRuns.Add(await JudgeRunGroup(
                        scenario, skillName, firstSkillSession.SkillPath,
                        baselineSess, isolatedSess, pluginSess,
                        effectiveJudgeModel, verbose, judgeTimeout, usePairwise,
                        baselineSaveDb: sessionDb, treatmentSaveDb: sessionDb, log));
                }

                if (rejudgedRuns.Count == 0)
                    continue;

                comparisons.Add(BuildScenarioComparison(scenarioName, rejudgedRuns));
            }

            if (comparisons.Count == 0)
                continue;

            var skill = new SkillInfo(skillName, "", firstSkillSession.SkillPath, firstSkillSession.SkillPath, "");
            var verdict = Comparator.ComputeVerdict(skill, comparisons, minImprovement, requireCompletion, confidenceLevel);
            Console.WriteLine($"[{skillName}] {(verdict.Passed ? "✅" : "❌")} Score: {verdict.OverallImprovementScore * 100:F1}%");
            verdicts.Add(verdict);
        }

        var reporters = new List<ReporterSpec>
        {
            new(ReporterType.Console),
            new(ReporterType.Json),
            new(ReporterType.Markdown),
        };
        await Reporter.ReportResults(verdicts, reporters, verbose,
            firstSession.Model, effectiveJudgeModel, resultsDir, resultsDir);

        await AgentRunner.StopAllClients();
        return verdicts.All(v => v.Passed) ? 0 : 1;
    }

    /// <summary>
    /// Cross-directory judging: pair treatment runs in <paramref name="treatmentDir"/> with baseline
    /// runs in <paramref name="baselineDir"/> by their baseline key, judge each pair, and apply the
    /// same scoring and pass/fail gates as an inline <c>evaluate</c> run.
    /// </summary>
    public static async Task<int> RunCrossDir(
        string treatmentDir,
        string baselineDir,
        string? judgeModel,
        JudgeMode judgeMode,
        int judgeTimeout,
        bool verbose,
        double minImprovement,
        bool requireCompletion,
        double confidenceLevel)
    {
        var treatmentDbPath = Path.Combine(treatmentDir, "sessions.db");
        var baselineDbPath = Path.Combine(baselineDir, "sessions.db");
        if (!File.Exists(treatmentDbPath))
        {
            Console.Error.WriteLine($"No sessions.db found at {treatmentDbPath}");
            return 1;
        }
        if (!File.Exists(baselineDbPath))
        {
            Console.Error.WriteLine($"No baseline sessions.db found at {baselineDbPath}");
            return 1;
        }

        using var treatmentDb = new SessionDatabase(treatmentDbPath);
        using var baselineDb = new SessionDatabase(baselineDbPath);

        var treatmentSessions = treatmentDb.GetCompletedSessions();
        var baselineSessions = baselineDb.GetCompletedSessions();
        if (treatmentSessions.Count == 0)
        {
            Console.Error.WriteLine("No completed treatment sessions found in the database.");
            return 1;
        }
        if (baselineSessions.Count == 0)
        {
            Console.Error.WriteLine("No completed baseline sessions found in the database.");
            return 1;
        }

        var treatmentModels = treatmentSessions.Select(s => s.Model).Distinct().ToList();
        var baselineModels = baselineSessions.Select(s => s.Model).Distinct().ToList();
        if (treatmentModels.Count > 1)
        {
            Console.Error.WriteLine($"Treatment sessions use multiple models ({string.Join(", ", treatmentModels)}); cross-directory judging requires a single agent model.");
            return 1;
        }
        if (baselineModels.Count > 1)
        {
            Console.Error.WriteLine($"Baseline sessions use multiple models ({string.Join(", ", baselineModels)}); cross-directory judging requires a single agent model.");
            return 1;
        }
        var treatmentModel = treatmentModels[0];
        var baselineModel = baselineModels[0];
        var treatmentJudgeModel = treatmentDb.GetSchemaInfo().GetValueOrDefault("judge_model");
        var baselineJudgeModel = baselineDb.GetSchemaInfo().GetValueOrDefault("judge_model");

        var (compatOk, effectiveJudgeModel, compatError) = ValidateCrossDirCompat(
            baselineModel, treatmentModel, baselineJudgeModel, treatmentJudgeModel, judgeModel);
        if (!compatOk)
        {
            Console.Error.WriteLine(compatError);
            return 1;
        }

        try
        {
            var client = await AgentRunner.GetSharedClient(verbose);
            var models = await client.ListModelsAsync();
            if (!models.Any(m => m.Id == effectiveJudgeModel))
            {
                Console.Error.WriteLine($"Invalid model: \"{effectiveJudgeModel}\"\nAvailable models: {string.Join(", ", models.Select(m => m.Id))}");
                return 1;
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Failed to validate model: {error}");
            return 1;
        }

        var pairing = PairCrossDir(baselineSessions, treatmentSessions);
        foreach (var unmatched in pairing.Unmatched)
            Console.WriteLine($"⚠️  No baseline match for treatment run {unmatched}; skipping.");

        if (pairing.Pairs.Count == 0)
        {
            Console.Error.WriteLine("No treatment runs could be paired with a baseline.");
            return 1;
        }

        Console.WriteLine($"Judging {pairing.Pairs.Count} paired run(s) with model: {effectiveJudgeModel}, mode: {judgeMode}\n");

        bool usePairwise = judgeMode is JudgeMode.Pairwise or JudgeMode.Both;
        var verdicts = new List<SkillVerdict>();
        foreach (var skillGroup in pairing.Pairs.GroupBy(p => p.SkillName))
        {
            var skillName = skillGroup.Key;
            var skillPath = skillGroup.First().Isolated.SkillPath;
            Console.WriteLine($"[{skillName}] Judging...");

            var comparisons = new List<ScenarioComparison>();
            foreach (var scenarioGroup in skillGroup.GroupBy(p => p.ScenarioName))
            {
                var scenarioName = scenarioGroup.Key;
                var storedRubric = GetStoredRubric(skillName, scenarioName,
                    scenarioGroup.SelectMany(p => p.Plugin is null
                        ? new[] { p.Baseline, p.Isolated }
                        : new[] { p.Baseline, p.Isolated, p.Plugin }));
                var rejudgedRuns = new List<RejudgedRun>();

                foreach (var pair in scenarioGroup)
                {
                    var prompt = pair.Baseline.Prompt ?? pair.Isolated.Prompt ?? pair.Plugin?.Prompt ?? "";
                    var scenario = new EvalScenario(scenarioName, prompt, Rubric: storedRubric);
                    Action<string>? log = verbose ? msg => Console.WriteLine($"  [{scenarioName}/{pair.RunIndex + 1}] {msg}") : null;

                    rejudgedRuns.Add(await JudgeRunGroup(
                        scenario, skillName, skillPath,
                        pair.Baseline, pair.Isolated, pair.Plugin,
                        effectiveJudgeModel!, verbose, judgeTimeout, usePairwise,
                        baselineSaveDb: baselineDb, treatmentSaveDb: treatmentDb, log));
                }

                if (rejudgedRuns.Count == 0)
                    continue;

                comparisons.Add(BuildScenarioComparison(scenarioName, rejudgedRuns));
            }

            if (comparisons.Count == 0)
                continue;

            var skill = new SkillInfo(skillName, "", skillPath, skillPath, "");
            var verdict = Comparator.ComputeVerdict(skill, comparisons, minImprovement, requireCompletion, confidenceLevel);
            Console.WriteLine($"[{skillName}] {(verdict.Passed ? "✅" : "❌")} Score: {verdict.OverallImprovementScore * 100:F1}%");
            verdicts.Add(verdict);
        }

        var reporters = new List<ReporterSpec>
        {
            new(ReporterType.Console),
            new(ReporterType.Json),
            new(ReporterType.Markdown),
        };
        await Reporter.ReportResults(verdicts, reporters, verbose,
            treatmentModel, effectiveJudgeModel!, treatmentDir, treatmentDir);

        await AgentRunner.StopAllClients();
        return verdicts.All(v => v.Passed) ? 0 : 1;
    }

    private static readonly string[] CrossDirIsolatedRoles = { "with-skill-isolated", "with-skill", "with-agent-isolated" };
    private static readonly string[] CrossDirPluginRoles = { "with-skill-plugin", "with-agent-plugin" };
    private static readonly string[] CrossDirBaselineRoles = { "baseline", "baseline-reused" };

    /// <summary>
    /// Pure pairing of baseline sessions to treatment sessions by their shared baseline key
    /// (prompt SHA + target SHA). Treatment runs are grouped by skill/scenario/run-index; each
    /// run's baseline is the baseline session sharing its key, preferring the matching run index.
    /// </summary>
    public static CrossDirPairing PairCrossDir(
        IReadOnlyList<SessionRecord> baselineSessions,
        IReadOnlyList<SessionRecord> treatmentSessions)
    {
        var baselineByKey = baselineSessions
            .Where(s => CrossDirBaselineRoles.Contains(s.Role) && !string.IsNullOrEmpty(s.BaselineKey))
            .GroupBy(s => s.BaselineKey!)
            .ToDictionary(g => g.Key, g => g.ToList());

        var pairs = new List<CrossDirPair>();
        var unmatched = new List<string>();

        foreach (var group in treatmentSessions.GroupBy(s => (s.SkillName, s.ScenarioName, s.RunIndex)))
        {
            var isolated = CrossDirIsolatedRoles
                .Select(role => group.FirstOrDefault(s => s.Role == role))
                .FirstOrDefault(s => s is not null);
            if (isolated is null)
                continue;

            var plugin = CrossDirPluginRoles
                .Select(role => group.FirstOrDefault(s => s.Role == role))
                .FirstOrDefault(s => s is not null);

            var key = isolated.BaselineKey;
            if (string.IsNullOrEmpty(key) || !baselineByKey.TryGetValue(key, out var candidates) || candidates.Count == 0)
            {
                unmatched.Add($"{group.Key.SkillName}/{group.Key.ScenarioName}#{group.Key.RunIndex + 1}");
                continue;
            }

            var baseline = candidates.FirstOrDefault(b => b.RunIndex == group.Key.RunIndex) ?? candidates[0];
            pairs.Add(new CrossDirPair(
                group.Key.SkillName,
                group.Key.ScenarioName,
                group.Key.RunIndex,
                baseline,
                isolated,
                plugin));
        }

        return new CrossDirPairing(pairs, unmatched);
    }

    /// <summary>
    /// Pure validation of cross-directory model/judge-model compatibility. Baseline and treatment
    /// must share the same agent model; the effective judge model is the explicit override, then the
    /// treatment's persisted judge model, then the baseline's. A mismatch between the two persisted
    /// judge models (with no override) is rejected.
    /// </summary>
    public static (bool Ok, string? EffectiveJudgeModel, string? Error) ValidateCrossDirCompat(
        string baselineModel,
        string treatmentModel,
        string? baselineJudgeModel,
        string? treatmentJudgeModel,
        string? explicitJudgeModel)
    {
        if (!string.Equals(baselineModel, treatmentModel, StringComparison.Ordinal))
        {
            return (false, null,
                $"Model mismatch: baseline sessions used \"{baselineModel}\" but treatment sessions used \"{treatmentModel}\". Baseline and treatment must share the same --model.");
        }

        if (string.IsNullOrWhiteSpace(explicitJudgeModel)
            && !string.IsNullOrWhiteSpace(baselineJudgeModel)
            && !string.IsNullOrWhiteSpace(treatmentJudgeModel)
            && !string.Equals(baselineJudgeModel, treatmentJudgeModel, StringComparison.Ordinal))
        {
            return (false, null,
                $"Judge-model mismatch: baseline sessions were judged with \"{baselineJudgeModel}\" but treatment sessions with \"{treatmentJudgeModel}\". Pass --judge-model to override.");
        }

        var effective = !string.IsNullOrWhiteSpace(explicitJudgeModel)
            ? explicitJudgeModel
            : !string.IsNullOrWhiteSpace(treatmentJudgeModel)
                ? treatmentJudgeModel
                : baselineJudgeModel;

        if (string.IsNullOrWhiteSpace(effective))
        {
            return (false, null,
                "No persisted judge model found in either sessions.db. Re-run with --judge-model to specify the judge explicitly.");
        }

        return (true, effective, null);
    }

    /// <summary>
    /// Judges a single baseline/treatment run group and persists the judge results.
    /// Baseline judge + pairwise are saved to <paramref name="baselineSaveDb"/>; the
    /// isolated/plugin judge results are saved to <paramref name="treatmentSaveDb"/>.
    /// When judging within a single directory, both arguments are the same database.
    /// </summary>
    private static async Task<RejudgedRun> JudgeRunGroup(
        EvalScenario scenario,
        string skillName,
        string skillPath,
        SessionRecord baselineSess,
        SessionRecord isolatedSess,
        SessionRecord? pluginSess,
        string effectiveJudgeModel,
        bool verbose,
        int judgeTimeout,
        bool usePairwise,
        SessionDatabase baselineSaveDb,
        SessionDatabase treatmentSaveDb,
        Action<string>? log)
    {
        var baselineMetrics = JsonSerializer.Deserialize(baselineSess.MetricsJson!, SkillValidatorJsonContext.Default.RunMetrics)!;
        var isolatedMetrics = JsonSerializer.Deserialize(isolatedSess.MetricsJson!, SkillValidatorJsonContext.Default.RunMetrics)!;
        var pluginMetrics = pluginSess?.MetricsJson is not null
            ? JsonSerializer.Deserialize(pluginSess.MetricsJson, SkillValidatorJsonContext.Default.RunMetrics)
            : null;

        var judgeWorkRoot = CreateJudgeWorkDir("rejudge");
        try
        {
            var judgeOpts = new JudgeOptions(
                effectiveJudgeModel,
                verbose,
                judgeTimeout,
                CreateJudgeWorkDir(judgeWorkRoot, "baseline"),
                skillPath);
            var baselineJudge = await SafeJudge(
                Judge.JudgeRun(scenario, baselineMetrics, judgeOpts, log),
                "baseline",
                log);
            var isolatedJudge = await SafeJudge(
                Judge.JudgeRun(scenario, isolatedMetrics, judgeOpts with { WorkDir = CreateJudgeWorkDir(judgeWorkRoot, "isolated") }, log),
                "isolated",
                log);
            var pluginJudge = pluginMetrics is not null
                ? await SafeJudge(
                    Judge.JudgeRun(scenario, pluginMetrics, judgeOpts with { WorkDir = CreateJudgeWorkDir(judgeWorkRoot, "plugin") }, log),
                    "plugin",
                    log)
                : ((JudgeResult Result, TokenUsage Tokens)?)null;

            // Accumulate judge tokens into metrics
            baselineMetrics.JudgeInputTokens += baselineJudge.Tokens.InputTokens;
            baselineMetrics.JudgeOutputTokens += baselineJudge.Tokens.OutputTokens;
            baselineMetrics.JudgeCacheReadTokens += baselineJudge.Tokens.CacheReadTokens;
            baselineMetrics.JudgeCacheWriteTokens += baselineJudge.Tokens.CacheWriteTokens;
            isolatedMetrics.JudgeInputTokens += isolatedJudge.Tokens.InputTokens;
            isolatedMetrics.JudgeOutputTokens += isolatedJudge.Tokens.OutputTokens;
            isolatedMetrics.JudgeCacheReadTokens += isolatedJudge.Tokens.CacheReadTokens;
            isolatedMetrics.JudgeCacheWriteTokens += isolatedJudge.Tokens.CacheWriteTokens;
            if (pluginMetrics is not null && pluginJudge is not null)
            {
                pluginMetrics.JudgeInputTokens += pluginJudge.Value.Tokens.InputTokens;
                pluginMetrics.JudgeOutputTokens += pluginJudge.Value.Tokens.OutputTokens;
                pluginMetrics.JudgeCacheReadTokens += pluginJudge.Value.Tokens.CacheReadTokens;
                pluginMetrics.JudgeCacheWriteTokens += pluginJudge.Value.Tokens.CacheWriteTokens;
            }

            baselineSaveDb.SaveJudgeResult(baselineSess.Id, JsonSerializer.Serialize(baselineJudge.Result, SkillValidatorJsonContext.Default.JudgeResult));
            treatmentSaveDb.SaveJudgeResult(isolatedSess.Id, JsonSerializer.Serialize(isolatedJudge.Result, SkillValidatorJsonContext.Default.JudgeResult));
            if (pluginSess is not null && pluginJudge is not null)
            {
                treatmentSaveDb.SaveJudgeResult(pluginSess.Id, JsonSerializer.Serialize(pluginJudge.Value.Result, SkillValidatorJsonContext.Default.JudgeResult));
            }

            var baselineResult = new RunResult(baselineMetrics, baselineJudge.Result);
            var isolatedResult = new RunResult(isolatedMetrics, isolatedJudge.Result);
            var pluginResult = pluginMetrics is not null && pluginJudge is not null
                ? new RunResult(pluginMetrics, pluginJudge.Value.Result)
                : null;

            PairwiseJudgeResult? pairwise = null;
            bool pairwiseFromPlugin = false;
            if (usePairwise)
            {
                try
                {
                    var pairwiseTarget = pluginResult is not null && pluginResult.JudgeResult.OverallScore < isolatedResult.JudgeResult.OverallScore
                        ? pluginResult
                        : isolatedResult;
                    pairwiseFromPlugin = ReferenceEquals(pairwiseTarget, pluginResult);
                    var (pairwiseResult, _) = await PairwiseJudge.Judge(
                        scenario,
                        baselineMetrics,
                        pairwiseTarget.Metrics,
                        new PairwiseJudgeOptions(
                            effectiveJudgeModel,
                            verbose,
                            judgeTimeout,
                            CreateJudgeWorkDir(judgeWorkRoot, "pairwise"),
                            skillPath,
                            CreateJudgeWorkDir(judgeWorkRoot, "pairwise-skilled")),
                        log);
                    pairwise = pairwiseResult;
                    baselineSaveDb.SavePairwiseResult(baselineSess.Id, JsonSerializer.Serialize(pairwise, SkillValidatorJsonContext.Default.PairwiseJudgeResult));
                }
                catch (Exception error)
                {
                    log?.Invoke($"⚠️  Pairwise judge failed: {error.Message}");
                }
            }

            var isolatedActivation = MetricsCollector.ExtractSkillActivation(
                isolatedMetrics.Events,
                baselineMetrics.ToolCallBreakdown,
                skillName);
            var pluginActivation = pluginMetrics is not null
                ? MetricsCollector.ExtractSkillActivation(pluginMetrics.Events, baselineMetrics.ToolCallBreakdown, skillName)
                : null;

            return new RejudgedRun(
                Baseline: baselineResult,
                Isolated: isolatedResult,
                Plugin: pluginResult,
                Pairwise: pairwise,
                PairwiseFromPlugin: pairwiseFromPlugin,
                IsolatedActivation: isolatedActivation,
                PluginActivation: pluginActivation);
        }
        finally
        {
            TryDeleteDirectory(judgeWorkRoot);
        }
    }

    private static string CreateJudgeWorkDir(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), $"sv-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateJudgeWorkDir(string root, string name)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    private static ScenarioComparison BuildScenarioComparison(string scenarioName, List<RejudgedRun> runs)
    {
        var baselineRuns = runs.Select(r => r.Baseline).ToList();
        var isolatedRuns = runs.Select(r => r.Isolated).ToList();
        var avgBaseline = AverageResults(baselineRuns);
        var avgIsolated = AverageResults(isolatedRuns);
        var bestPairwise = runs.Select(r => r.Pairwise).FirstOrDefault(p => p?.PositionSwapConsistent == true)
            ?? runs.Select(r => r.Pairwise).FirstOrDefault();

        if (runs.Any(r => r.Plugin is not null))
        {
            var pluginRuns = runs.Where(r => r.Plugin is not null).Select(r => r.Plugin!).ToList();
            var perRunIsolatedScores = new List<double>();
            var perRunPluginScores = new List<double>();

            foreach (var run in runs)
            {
                var isoComp = Comparator.CompareScenario(scenarioName, run.Baseline, run.Isolated,
                    run.PairwiseFromPlugin ? null : run.Pairwise);
                var pluginComp = run.Plugin is not null
                    ? Comparator.CompareScenario(scenarioName, run.Baseline, run.Plugin,
                        run.PairwiseFromPlugin ? run.Pairwise : null)
                    : isoComp;
                perRunIsolatedScores.Add(isoComp.ImprovementScore);
                perRunPluginScores.Add(pluginComp.ImprovementScore);
            }

            var perRunScores = perRunIsolatedScores
                .Zip(perRunPluginScores, (iso, plugin) => Math.Min(iso, plugin))
                .ToList();
            var avgPlugin = AverageResults(pluginRuns);
            int bestPairwiseIdx = runs.FindIndex(r => r.Pairwise?.PositionSwapConsistent == true);
            if (bestPairwiseIdx < 0)
                bestPairwiseIdx = runs.FindIndex(r => r.Pairwise is not null);
            bool pairwiseFromPlugin = bestPairwiseIdx >= 0 && runs[bestPairwiseIdx].PairwiseFromPlugin;

            var isoComparison = Comparator.CompareScenario(scenarioName, avgBaseline, avgIsolated,
                pairwiseFromPlugin ? null : bestPairwise);
            var pluginComparison = Comparator.CompareScenario(scenarioName, avgBaseline, avgPlugin,
                pairwiseFromPlugin ? bestPairwise : null);

            var comparison = new ScenarioComparison
            {
                ScenarioName = scenarioName,
                Baseline = avgBaseline,
                SkilledIsolated = avgIsolated,
                SkilledPlugin = avgPlugin,
                ImprovementScore = Math.Min(isoComparison.ImprovementScore, pluginComparison.ImprovementScore),
                IsolatedImprovementScore = isoComparison.ImprovementScore,
                PluginImprovementScore = pluginComparison.ImprovementScore,
                Breakdown = isoComparison.ImprovementScore <= pluginComparison.ImprovementScore
                    ? isoComparison.Breakdown
                    : pluginComparison.Breakdown,
                IsolatedBreakdown = isoComparison.Breakdown,
                PluginBreakdown = pluginComparison.Breakdown,
                PairwiseResult = bestPairwise,
                PerRunScores = perRunScores,
                SkillActivationIsolated = new SkillActivationInfo(
                    Activated: runs.Any(r => r.IsolatedActivation.Activated),
                    DetectedSkills: runs.SelectMany(r => r.IsolatedActivation.DetectedSkills).Distinct().ToList(),
                    ExtraTools: runs.SelectMany(r => r.IsolatedActivation.ExtraTools).Distinct().ToList(),
                    SkillEventCount: runs.Sum(r => r.IsolatedActivation.SkillEventCount)),
                SkillActivationPlugin = new SkillActivationInfo(
                    Activated: runs.Any(r => r.PluginActivation?.Activated == true),
                    DetectedSkills: runs.SelectMany(r => r.PluginActivation?.DetectedSkills ?? []).Distinct().ToList(),
                    ExtraTools: runs.SelectMany(r => r.PluginActivation?.ExtraTools ?? []).Distinct().ToList(),
                    SkillEventCount: runs.Sum(r => r.PluginActivation?.SkillEventCount ?? 0)),
                TimedOut = runs.Any(r => r.Baseline.Metrics.TimedOut || r.Isolated.Metrics.TimedOut || r.Plugin?.Metrics.TimedOut == true),
            };
            return comparison;
        }

        var comparisonNoPlugin = Comparator.CompareScenario(scenarioName, avgBaseline, avgIsolated, bestPairwise);
        comparisonNoPlugin.PerRunScores = runs.Select(r => Comparator.CompareScenario(scenarioName, r.Baseline, r.Isolated, r.Pairwise).ImprovementScore).ToList();
        comparisonNoPlugin.SkillActivationIsolated = new SkillActivationInfo(
            Activated: runs.Any(r => r.IsolatedActivation.Activated),
            DetectedSkills: runs.SelectMany(r => r.IsolatedActivation.DetectedSkills).Distinct().ToList(),
            ExtraTools: runs.SelectMany(r => r.IsolatedActivation.ExtraTools).Distinct().ToList(),
            SkillEventCount: runs.Sum(r => r.IsolatedActivation.SkillEventCount));
        comparisonNoPlugin.TimedOut = runs.Any(r => r.Baseline.Metrics.TimedOut || r.Isolated.Metrics.TimedOut);
        return comparisonNoPlugin;
    }

    private static string[]? GetStoredRubric(string skillName, string scenarioName, IEnumerable<SessionRecord> sessions)
    {
        var rubricJson = sessions
            .Select(s => s.RubricJson)
            .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));
        if (rubricJson is null)
        {
            Console.WriteLine($"[{skillName}] ⚠️  Scenario '{scenarioName}' has no persisted rubric in sessions.db; falling back to the default judging rubric.");
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(rubricJson, SkillValidatorJsonContext.Default.StringArray) ?? [];
        }
        catch (JsonException error)
        {
            Console.WriteLine($"[{skillName}] ⚠️  Scenario '{scenarioName}' has an unreadable persisted rubric ({error.Message}); falling back to the default judging rubric.");
            return null;
        }
    }

    private static async Task<(JudgeResult Result, TokenUsage Tokens)> SafeJudge(Task<(JudgeResult Result, TokenUsage Tokens)> task, string label, Action<string>? log)
    {
        try
        {
            return await task;
        }
        catch (Exception error)
        {
            log?.Invoke($"⚠️  Judge ({label}) failed, using fallback scores: {error.Message}");
            return (new JudgeResult([], 3, $"Judge failed: {error.Message}"), TokenUsage.Zero);
        }
    }

    private static RunResult AverageResults(List<RunResult> runs)
    {
        if (runs.Count == 1)
            return runs[0];

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
            runs[0].JudgeResult.RubricScores.Select((score, i) => new RubricScore(
                score.Criterion,
                Math.Round(Avg(runs.Select(r => i < r.JudgeResult.RubricScores.Count ? r.JudgeResult.RubricScores[i].Score : 3)) * 10) / 10,
                score.Reasoning)).ToList(),
            Math.Round(Avg(runs.Select(r => r.JudgeResult.OverallScore)) * 10) / 10,
            runs[^1].JudgeResult.OverallReasoning);

        return new RunResult(avgMetrics, avgJudge);
    }

    private sealed record RejudgedRun(
        RunResult Baseline,
        RunResult Isolated,
        RunResult? Plugin,
        PairwiseJudgeResult? Pairwise,
        bool PairwiseFromPlugin,
        SkillActivationInfo IsolatedActivation,
        SkillActivationInfo? PluginActivation);
}

/// <summary>A treatment run paired with its matching baseline run for cross-directory judging.</summary>
public sealed record CrossDirPair(
    string SkillName,
    string ScenarioName,
    int RunIndex,
    SessionRecord Baseline,
    SessionRecord Isolated,
    SessionRecord? Plugin);

/// <summary>Result of pairing treatment runs to baseline runs across two results directories.</summary>
public sealed record CrossDirPairing(
    IReadOnlyList<CrossDirPair> Pairs,
    IReadOnlyList<string> Unmatched);
