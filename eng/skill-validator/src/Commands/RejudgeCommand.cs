using System.CommandLine;
using System.Text.Json;
using SkillValidator.Models;
using SkillValidator.Services;

namespace SkillValidator.Commands;

public static class RejudgeCommand
{
    public static Command Create()
    {
        var resultsDirArg = new Argument<string>("results-dir") { Description = "Path to a timestamped results directory containing sessions.db" };
        var judgeModelOpt = new Option<string?>("--judge-model") { Description = "Model to use for judging (defaults to the persisted judge model when available)" };
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
                            firstSkillSession.SkillPath);
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

                        sessionDb.SaveJudgeResult(baselineSess.Id, JsonSerializer.Serialize(baselineJudge.Result, SkillValidatorJsonContext.Default.JudgeResult));
                        sessionDb.SaveJudgeResult(isolatedSess.Id, JsonSerializer.Serialize(isolatedJudge.Result, SkillValidatorJsonContext.Default.JudgeResult));
                        if (pluginSess is not null && pluginJudge is not null)
                        {
                            sessionDb.SaveJudgeResult(pluginSess.Id, JsonSerializer.Serialize(pluginJudge.Value.Result, SkillValidatorJsonContext.Default.JudgeResult));
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
                                        firstSkillSession.SkillPath,
                                        CreateJudgeWorkDir(judgeWorkRoot, "pairwise-skilled")),
                                    log);
                                pairwise = pairwiseResult;
                                sessionDb.SavePairwiseResult(baselineSess.Id, JsonSerializer.Serialize(pairwise, SkillValidatorJsonContext.Default.PairwiseJudgeResult));
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

                        rejudgedRuns.Add(new RejudgedRun(
                            Baseline: baselineResult,
                            Isolated: isolatedResult,
                            Plugin: pluginResult,
                            Pairwise: pairwise,
                            PairwiseFromPlugin: pairwiseFromPlugin,
                            IsolatedActivation: isolatedActivation,
                            PluginActivation: pluginActivation));
                    }
                    finally
                    {
                        TryDeleteDirectory(judgeWorkRoot);
                    }
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
