using System.CommandLine;
using System.Text.Json;
using SkillValidator.Models;
using SkillValidator.Services;
using SkillValidator.Utilities;

namespace SkillValidator.Commands;

public static class RejudgeCommand
{
    public static Command Create()
    {
        var resultsDirArg = new Argument<string>("results-dir") { Description = "Path to a timestamped results directory containing sessions.db" };
        var judgeModelOpt = new Option<string?>("--judge-model") { Description = "Model to use for judging (defaults to original model)" };
        var judgeModeOpt = new Option<string>("--judge-mode") { Description = "Judge mode: pairwise, independent, or both", DefaultValueFactory = _ => "pairwise" };
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

        // Determine judge model from sessions if not specified
        var effectiveJudgeModel = judgeModel ?? sessions[0].Model;

        // Validate model
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
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Group sessions into run pairs: (skill, scenario, run_index) → (baseline, with-skill)
        var runPairs = sessions
            .GroupBy(s => (s.SkillName, s.ScenarioName, s.RunIndex))
            .Where(g => g.Any(s => s.Role == "baseline") && g.Any(s => s.Role == "with-skill"))
            .ToList();

        if (runPairs.Count == 0)
        {
            Console.Error.WriteLine("No complete run pairs (baseline + with-skill) found.");
            return 1;
        }

        Console.WriteLine($"Found {runPairs.Count} run pair(s) across {runPairs.Select(g => g.Key.SkillName).Distinct().Count()} skill(s)\n");

        // Group by skill → scenario for verdict computation
        var skillGroups = runPairs.GroupBy(g => g.Key.SkillName);
        var verdicts = new List<SkillVerdict>();

        foreach (var skillGroup in skillGroups)
        {
            var skillName = skillGroup.Key;
            var firstSession = skillGroup.First().First();
            Console.WriteLine($"[{skillName}] Rejudging...");

            var scenarioGroups = skillGroup.GroupBy(g => g.Key.ScenarioName);
            var comparisons = new List<ScenarioComparison>();

            foreach (var scenarioGroup in scenarioGroups)
            {
                var scenarioName = scenarioGroup.Key;
                var perRunScores = new List<double>();
                RunResult? lastBaseline = null;
                RunResult? lastWithSkill = null;
                PairwiseJudgeResult? lastPairwise = null;

                foreach (var runGroup in scenarioGroup)
                {
                    var baselineSess = runGroup.First(s => s.Role == "baseline");
                    var skillSess = runGroup.First(s => s.Role == "with-skill");

                    var baselineMetrics = JsonSerializer.Deserialize<RunMetrics>(baselineSess.MetricsJson!, jsonOpts)!;
                    var withSkillMetrics = JsonSerializer.Deserialize<RunMetrics>(skillSess.MetricsJson!, jsonOpts)!;

                    // Reconstruct scenario for judge (we need rubric)
                    // For now, create a minimal scenario from the saved data
                    var scenario = new EvalScenario(scenarioName, "");

                    // Re-judge
                    var judgeOpts = new JudgeOptions(effectiveJudgeModel, verbose, judgeTimeout, baselineMetrics.WorkDir, firstSession.SkillPath);
                    var judgeTasks = await Task.WhenAll(
                        Judge.JudgeRun(scenario, baselineMetrics, judgeOpts),
                        Judge.JudgeRun(scenario, withSkillMetrics, judgeOpts with { WorkDir = withSkillMetrics.WorkDir }));

                    var baselineResult = new RunResult(baselineMetrics, judgeTasks[0]);
                    var withSkillResult = new RunResult(withSkillMetrics, judgeTasks[1]);

                    // Update judge results in DB
                    sessionDb.SaveJudgeResult(baselineSess.Id, JsonSerializer.Serialize(judgeTasks[0]));
                    sessionDb.SaveJudgeResult(skillSess.Id, JsonSerializer.Serialize(judgeTasks[1]));

                    // Pairwise
                    PairwiseJudgeResult? pairwise = null;
                    if (usePairwise)
                    {
                        try
                        {
                            pairwise = await PairwiseJudge.Judge(
                                scenario, baselineMetrics, withSkillMetrics,
                                new PairwiseJudgeOptions(effectiveJudgeModel, verbose, judgeTimeout, baselineMetrics.WorkDir, firstSession.SkillPath));
                            sessionDb.SavePairwiseResult(baselineSess.Id, JsonSerializer.Serialize(pairwise));
                        }
                        catch (Exception error)
                        {
                            Console.Error.WriteLine($"  ⚠️  Pairwise judge failed: {error.Message}");
                        }
                    }

                    var runComparison = Comparator.CompareScenario(scenarioName, baselineResult, withSkillResult, pairwise);
                    perRunScores.Add(runComparison.ImprovementScore);

                    lastBaseline = baselineResult;
                    lastWithSkill = withSkillResult;
                    lastPairwise = pairwise;
                }

                if (lastBaseline is not null && lastWithSkill is not null)
                {
                    var comparison = Comparator.CompareScenario(scenarioName, lastBaseline, lastWithSkill, lastPairwise);
                    comparison.PerRunScores = perRunScores;
                    comparisons.Add(comparison);
                }
            }

            if (comparisons.Count > 0)
            {
                var skill = new SkillInfo(skillName, "", firstSession.SkillPath, firstSession.SkillPath, "", null, null);
                var verdict = Comparator.ComputeVerdict(skill, comparisons, minImprovement, requireCompletion, confidenceLevel);
                Console.WriteLine($"[{skillName}] {(verdict.Passed ? "✅" : "❌")} Score: {verdict.OverallImprovementScore * 100:F1}%");
                verdicts.Add(verdict);
            }
        }

        // Write new results
        var reporters = new List<ReporterSpec>
        {
            new(ReporterType.Console),
            new(ReporterType.Json),
            new(ReporterType.Markdown),
        };
        await Reporter.ReportResults(verdicts, reporters, verbose,
            effectiveJudgeModel, effectiveJudgeModel, resultsDir);

        await AgentRunner.StopSharedClient();

        return verdicts.All(v => v.Passed) ? 0 : 1;
    }
}
