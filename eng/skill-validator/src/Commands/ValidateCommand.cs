using System.CommandLine;
using SkillValidator.Models;
using SkillValidator.Services;
using SkillValidator.Utilities;
using GitHub.Copilot.SDK;

namespace SkillValidator.Commands;

public static class ValidateCommand
{
    public static RootCommand Create()
    {
        var pathsArg = new Argument<string[]>("paths") { Description = "Paths to skill directories or parent directories" };
        var minImprovementOpt = new Option<double>("--min-improvement") { Description = "Minimum improvement score to pass (0-1)", DefaultValueFactory = _ => 0.1 };
        var requireCompletionOpt = new Option<bool>("--require-completion") { Description = "Fail if skill regresses task completion", DefaultValueFactory = _ => true };
        var requireEvalsOpt = new Option<bool>("--require-evals") { Description = "Fail if skill has no tests/eval.yaml" };
        var verdictWarnOnlyOpt = new Option<bool>("--verdict-warn-only") { Description = "Treat verdict failures as warnings (exit 0). Execution errors and --require-evals still fail." };
        var verboseOpt = new Option<bool>("--verbose") { Description = "Show detailed per-scenario breakdowns" };
        var modelOpt = new Option<string>("--model") { Description = "Model to use for agent runs", DefaultValueFactory = _ => "claude-opus-4.6" };
        var judgeModelOpt = new Option<string?>("--judge-model") { Description = "Model to use for judging (defaults to --model)" };
        var judgeModeOpt = new Option<string>("--judge-mode") { Description = "Judge mode: pairwise, independent, or both", DefaultValueFactory = _ => "pairwise" };
        var runsOpt = new Option<int>("--runs") { Description = "Number of runs per scenario for averaging", DefaultValueFactory = _ => 5 };
        var parallelSkillsOpt = new Option<int>("--parallel-skills") { Description = "Max concurrent skills to evaluate", DefaultValueFactory = _ => 1 };
        var parallelScenariosOpt = new Option<int>("--parallel-scenarios") { Description = "Max concurrent scenarios per skill", DefaultValueFactory = _ => 1 };
        var parallelRunsOpt = new Option<int>("--parallel-runs") { Description = "Max concurrent runs per scenario", DefaultValueFactory = _ => 1 };
        var judgeTimeoutOpt = new Option<int>("--judge-timeout") { Description = "Judge timeout in seconds", DefaultValueFactory = _ => 300 };
        var confidenceLevelOpt = new Option<double>("--confidence-level") { Description = "Confidence level for statistical intervals (0-1)", DefaultValueFactory = _ => 0.95 };
        var resultsDirOpt = new Option<string>("--results-dir") { Description = "Directory to save results to", DefaultValueFactory = _ => ".skill-validator-results" };
        var testsDirOpt = new Option<string?>("--tests-dir") { Description = "Directory containing test subdirectories" };
        var reporterOpt = new Option<string[]>("--reporter") { Description = "Reporter (console, json, junit, markdown). Can be repeated.", AllowMultipleArgumentsPerToken = true };

        var command = new RootCommand("Validate that agent skills meaningfully improve agent performance")
        {
            pathsArg,
            minImprovementOpt,
            requireCompletionOpt,
            requireEvalsOpt,
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
                RequireEvals = parseResult.GetValue(requireEvalsOpt),
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
                TestsDir = parseResult.GetValue(testsDirOpt),
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
            var models = await client.ListModelsAsync();
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
        var allSkills = new List<SkillInfo>();
        foreach (var path in config.SkillPaths)
        {
            var skills = await SkillDiscovery.DiscoverSkills(path, config.TestsDir);
            allSkills.AddRange(skills);
        }

        if (allSkills.Count == 0)
        {
            Console.Error.WriteLine("No skills found in the specified paths.");
            return 1;
        }

        Console.WriteLine($"Found {allSkills.Count} skill(s)\n");

        if (config.Runs < 5)
            Console.WriteLine($"\x1b[33m⚠  Running with {config.Runs} run(s). For statistically significant results, use --runs 5 or higher.\x1b[0m");

        bool usePairwise = config.JudgeMode is JudgeMode.Pairwise or JudgeMode.Both;

        using var spinner = new Spinner();
        using var skillLimit = new ConcurrencyLimiter(config.ParallelSkills);

        // Evaluate skills
        spinner.Start($"Evaluating {allSkills.Count} skill(s)...");
        var skillTasks = allSkills.Select(skill =>
            skillLimit.RunAsync(() => EvaluateSkill(skill, config, usePairwise, spinner)));
        var settled = await Task.WhenAll(skillTasks.Select(async t =>
        {
            try { return (Result: await t, Error: (Exception?)null); }
            catch (Exception ex) { return (Result: (SkillVerdict?)null, Error: ex); }
        }));
        spinner.Stop();

        var verdicts = new List<SkillVerdict>();
        bool hasRejections = false;
        foreach (var (result, error) in settled)
        {
            if (result is not null)
            {
                verdicts.Add(result);
            }
            else if (error is not null)
            {
                hasRejections = true;
                Console.Error.WriteLine($"\x1b[31m❌ Skill evaluation failed: {error.Message}\x1b[0m");
            }
        }

        await Reporter.ReportResults(verdicts, config.Reporters, config.Verbose,
            config.Model, config.JudgeModel, config.ResultsDir);

        await AgentRunner.StopSharedClient();
        await AgentRunner.CleanupWorkDirs();

        // Always fail on execution errors, even in --verdict-warn-only mode
        if (hasRejections) return 1;

        var allPassed = verdicts.All(v => v.Passed);
        if (config.VerdictWarnOnly && !allPassed)
        {
            // In --verdict-warn-only mode, suppress verdict failures except missing_eval
            // (which is controlled by --require-evals and should remain fatal).
            var onlyWarnableFailures = verdicts.All(
                v => v.Passed || v.FailureKind != "missing_eval");
            if (onlyWarnableFailures) return 0;
        }

        return allPassed ? 0 : 1;
    }

    private static async Task<SkillVerdict?> EvaluateSkill(
        SkillInfo skill,
        ValidatorConfig config,
        bool usePairwise,
        Spinner spinner)
    {
        var prefix = $"[{skill.Name}]";
        var log = (string msg) => spinner.Log($"{prefix} {msg}");

        if (skill.EvalConfig is null)
        {
            if (config.RequireEvals)
            {
                return new SkillVerdict
                {
                    SkillName = skill.Name,
                    SkillPath = skill.Path,
                    Passed = false,
                    Scenarios = [],
                    OverallImprovementScore = 0,
                    Reason = "No tests/eval.yaml found (required by --require-evals)",
                    FailureKind = "missing_eval",
                };
            }
            log("⏭  Skipping (no tests/eval.yaml)");
            return null;
        }

        if (skill.EvalConfig.Scenarios.Count == 0)
        {
            log("⏭  Skipping (eval.yaml has no scenarios)");
            return null;
        }

        log("🔍 Evaluating...");

        var profile = SkillProfiler.AnalyzeSkill(skill);
        log($"📊 {SkillProfiler.FormatProfileLine(profile)}");
        foreach (var warning in SkillProfiler.FormatProfileWarnings(profile))
            log(warning);

        bool singleScenario = skill.EvalConfig.Scenarios.Count == 1;
        using var scenarioLimit = new ConcurrencyLimiter(config.ParallelScenarios);

        var scenarioTasks = skill.EvalConfig.Scenarios.Select(scenario =>
            scenarioLimit.RunAsync(() => ExecuteScenario(scenario, skill, config, usePairwise, singleScenario, spinner)));
        var comparisons = (await Task.WhenAll(scenarioTasks)).ToList();

        var verdict = Comparator.ComputeVerdict(skill, comparisons, config.MinImprovement, config.RequireCompletion, config.ConfidenceLevel);
        verdict.ProfileWarnings = profile.Warnings;

        var notActivated = comparisons.Where(c => c.SkillActivation is { Activated: false }).ToList();
        if (notActivated.Count > 0)
        {
            var names = string.Join(", ", notActivated.Select(c => c.ScenarioName));
            log($"\x1b[33m\u26a0\ufe0f  Skill was NOT activated in scenario(s): {names}\x1b[0m");
            verdict.SkillNotActivated = true;
            verdict.Passed = false;
            verdict.FailureKind = "skill_not_activated";
            verdict.Reason += $" [SKILL NOT ACTIVATED in {notActivated.Count} scenario(s): {names}]";
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
        SkillInfo skill,
        ValidatorConfig config,
        bool usePairwise,
        bool singleScenario,
        Spinner spinner)
    {
        var tag = singleScenario ? $"[{skill.Name}]" : $"[{skill.Name}/{scenario.Name}]";
        var scenarioLog = (string msg) => spinner.Log($"{tag} {msg}");
        using var runLimit = new ConcurrencyLimiter(config.ParallelRuns);

        if (!singleScenario)
            scenarioLog("📋 Starting scenario");

        var runTasks = Enumerable.Range(0, config.Runs).Select(i =>
            runLimit.RunAsync(() => ExecuteRun(i, scenario, skill, config, usePairwise, singleScenario, spinner)));
        var runResults = await Task.WhenAll(runTasks);

        scenarioLog($"✓ All {config.Runs} run(s) complete");

        var baselineRuns = runResults.Select(r => r.Baseline).ToList();
        var withSkillRuns = runResults.Select(r => r.WithSkill).ToList();
        var perRunPairwise = runResults.Select(r => r.Pairwise).ToList();

        var perRunScores = new List<double>();
        for (int i = 0; i < baselineRuns.Count; i++)
        {
            var runComparison = Comparator.CompareScenario(scenario.Name, baselineRuns[i], withSkillRuns[i], perRunPairwise[i]);
            perRunScores.Add(runComparison.ImprovementScore);
        }

        var avgBaseline = AverageResults(baselineRuns);
        var avgWithSkill = AverageResults(withSkillRuns);
        var bestPairwise = perRunPairwise.FirstOrDefault(pw => pw?.PositionSwapConsistent == true)
            ?? perRunPairwise.FirstOrDefault();

        var comparison = Comparator.CompareScenario(scenario.Name, avgBaseline, avgWithSkill, bestPairwise);
        comparison.PerRunScores = perRunScores;

        // Aggregate skill activation info
        var allActivations = runResults.Select(r => r.SkillActivation).ToList();
        comparison.SkillActivation = new SkillActivationInfo(
            Activated: allActivations.Any(a => a.Activated),
            DetectedSkills: allActivations.SelectMany(a => a.DetectedSkills).Distinct().ToList(),
            ExtraTools: allActivations.SelectMany(a => a.ExtraTools).Distinct().ToList(),
            SkillEventCount: allActivations.Sum(a => a.SkillEventCount));

        // Propagate timeout info from any run
        comparison.TimedOut = runResults.Any(r => r.WithSkill.Metrics.TimedOut || r.Baseline.Metrics.TimedOut);

        return comparison;
    }

    private sealed record RunExecutionResult(
        RunResult Baseline,
        RunResult WithSkill,
        PairwiseJudgeResult? Pairwise,
        SkillActivationInfo SkillActivation);

    private static async Task<RunExecutionResult> ExecuteRun(
        int runIndex,
        EvalScenario scenario,
        SkillInfo skill,
        ValidatorConfig config,
        bool usePairwise,
        bool singleScenario,
        Spinner spinner)
    {
        var runTag = config.Runs > 1
            ? (singleScenario ? $"[{skill.Name}/{runIndex + 1}]" : $"[{skill.Name}/{scenario.Name}/{runIndex + 1}]")
            : (singleScenario ? $"[{skill.Name}]" : $"[{skill.Name}/{scenario.Name}]");
        var runLog = (string msg) => spinner.Log($"{runTag} {msg}");

        if (config.Verbose)
            runLog("running agents...");

        var agentTasks = await Task.WhenAll(
            AgentRunner.RunAgent(new RunOptions(scenario, null, skill.EvalPath, config.Model, config.Verbose, runLog)),
            AgentRunner.RunAgent(new RunOptions(scenario, skill, skill.EvalPath, config.Model, config.Verbose, runLog)));
        var baselineMetrics = agentTasks[0];
        var withSkillMetrics = agentTasks[1];

        // Evaluate assertions
        if (scenario.Assertions is { Count: > 0 })
        {
            baselineMetrics.AssertionResults = await AssertionEvaluator.EvaluateAssertions(scenario.Assertions, baselineMetrics.AgentOutput, baselineMetrics.WorkDir);
            withSkillMetrics.AssertionResults = await AssertionEvaluator.EvaluateAssertions(scenario.Assertions, withSkillMetrics.AgentOutput, withSkillMetrics.WorkDir);
        }

        // Evaluate constraints
        var baselineConstraints = AssertionEvaluator.EvaluateConstraints(scenario, baselineMetrics);
        var withSkillConstraints = AssertionEvaluator.EvaluateConstraints(scenario, withSkillMetrics);
        baselineMetrics.AssertionResults = [..baselineMetrics.AssertionResults, ..baselineConstraints];
        withSkillMetrics.AssertionResults = [..withSkillMetrics.AssertionResults, ..withSkillConstraints];

        // Task completion
        if (scenario.Assertions is { Count: > 0 } || baselineConstraints.Count > 0)
        {
            baselineMetrics.TaskCompleted = baselineMetrics.AssertionResults.All(a => a.Passed);
            withSkillMetrics.TaskCompleted = withSkillMetrics.AssertionResults.All(a => a.Passed);
        }
        else
        {
            baselineMetrics.TaskCompleted = baselineMetrics.ErrorCount == 0;
            withSkillMetrics.TaskCompleted = withSkillMetrics.ErrorCount == 0;
        }

        // Judge
        var judgeOpts = new JudgeOptions(config.JudgeModel, config.Verbose, config.JudgeTimeout, baselineMetrics.WorkDir, skill.Path);

        var judgeTasks = await Task.WhenAll(
            Services.Judge.JudgeRun(scenario, baselineMetrics, judgeOpts),
            Services.Judge.JudgeRun(scenario, withSkillMetrics, judgeOpts with { WorkDir = withSkillMetrics.WorkDir }));
        var baselineJudge = judgeTasks[0];
        var withSkillJudge = judgeTasks[1];

        var baseline = new RunResult(baselineMetrics, baselineJudge);
        var withSkillResult = new RunResult(withSkillMetrics, withSkillJudge);

        // Pairwise judging
        PairwiseJudgeResult? pairwise = null;
        if (usePairwise)
        {
            try
            {
                pairwise = await Services.PairwiseJudge.Judge(
                    scenario, baselineMetrics, withSkillMetrics,
                    new PairwiseJudgeOptions(config.JudgeModel, config.Verbose, config.JudgeTimeout, baselineMetrics.WorkDir, skill.Path));
            }
            catch (Exception error)
            {
                runLog($"⚠️  Pairwise judge failed: {error}");
            }
        }

        // Skill activation
        var skillActivation = MetricsCollector.ExtractSkillActivation(withSkillMetrics.Events, baselineMetrics.ToolCallBreakdown);

        if (skillActivation.Activated)
        {
            var parts = new List<string>();
            if (skillActivation.DetectedSkills.Count > 0) parts.Add($"skills: {string.Join(", ", skillActivation.DetectedSkills)}");
            if (skillActivation.ExtraTools.Count > 0) parts.Add($"extra tools: {string.Join(", ", skillActivation.ExtraTools)}");
            runLog($"🔌 Skill activated ({string.Join("; ", parts)})");
        }
        else
        {
            runLog("\x1b[33m⚠️  Skill was NOT activated during this run\x1b[0m");
        }

        if (config.Verbose)
            runLog("✓ complete");

        return new RunExecutionResult(baseline, withSkillResult, pairwise, skillActivation);
    }

    private static RunResult AverageResults(List<RunResult> runs)
    {
        if (runs.Count == 1) return runs[0];

        static double Avg(IEnumerable<double> nums) => nums.Average();
        static int AvgRound(IEnumerable<int> nums) => (int)Math.Round(nums.Average());

        var avgMetrics = new RunMetrics
        {
            TokenEstimate = AvgRound(runs.Select(r => r.Metrics.TokenEstimate)),
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
}
