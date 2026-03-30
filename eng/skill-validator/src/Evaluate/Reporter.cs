using System.Text;
using System.Text.Json;
using SkillValidator.Shared;

namespace SkillValidator.Evaluate;

public static class Reporter
{
    public static async Task ReportResults(
        IReadOnlyList<SkillVerdict> verdicts,
        IReadOnlyList<ReporterSpec> reporters,
        bool verbose,
        string? model = null,
        string? judgeModel = null,
        string? resultsDir = null,
        string? timestampedResultsDir = null,
        int rejectedCount = 0)
    {
        bool needsResultsDir = reporters.Any(r =>
            r.Type is ReporterType.Json or ReporterType.Junit or ReporterType.Markdown);
        string? effectiveResultsDir = timestampedResultsDir
            ?? (resultsDir is not null && needsResultsDir
                ? Path.Combine(resultsDir, FormatTimestamp(DateTime.Now))
                : null);

        if (effectiveResultsDir is not null)
            Directory.CreateDirectory(effectiveResultsDir);

        foreach (var reporter in reporters)
        {
            switch (reporter.Type)
            {
                case ReporterType.Console:
                    ReportConsole(verdicts, verbose, rejectedCount);
                    break;
                case ReporterType.Json:
                    if (effectiveResultsDir is null)
                        throw new InvalidOperationException("--results-dir is required for the json reporter");
                    await ReportJson(verdicts, effectiveResultsDir, model, judgeModel);
                    break;
                case ReporterType.Junit:
                    if (effectiveResultsDir is null)
                        throw new InvalidOperationException("--results-dir is required for the junit reporter");
                    await ReportJunit(verdicts, effectiveResultsDir);
                    break;
                case ReporterType.Markdown:
                    if (effectiveResultsDir is null)
                        throw new InvalidOperationException("--results-dir is required for the markdown reporter");
                    await ReportMarkdown(verdicts, effectiveResultsDir, model, judgeModel);
                    break;
            }
        }
    }

    // --- Console reporter ---

    private static void ReportConsole(IReadOnlyList<SkillVerdict> verdicts, bool verbose, int rejectedCount = 0)
    {
        Console.WriteLine();
        Console.WriteLine("{Ansi.Bold}═══ Skill Validation Results ═══{Ansi.Reset}");
        Console.WriteLine();

        foreach (var verdict in verdicts)
        {
            var icon = verdict.Passed ? "{Ansi.Green}✓{Ansi.Reset}" : "{Ansi.Red}✗{Ansi.Reset}";
            var name = $"{Ansi.Bold}{verdict.SkillName}{Ansi.Reset}";
            var score = FormatScore(verdict.OverallImprovementScore);

            var scoreLine = $"{icon} {name}  {score}";
            if (verdict.ConfidenceInterval is { } ci)
            {
                var ciStr = $"[{FormatPct(ci.Low)}, {FormatPct(ci.High)}]";
                var sigStr = verdict.IsSignificant == true
                    ? "{Ansi.Green}significant{Ansi.Reset}"
                    : "{Ansi.Yellow}not significant{Ansi.Reset}";
                scoreLine += $"  {Ansi.Dim}{ciStr}{Ansi.Reset} {sigStr}";
            }
            if (verdict.NormalizedGain is { } ng)
                scoreLine += $"  {Ansi.Dim}(g={FormatPct(ng)}){Ansi.Reset}";

            Console.WriteLine(scoreLine);
            Console.WriteLine($"  {Ansi.Dim}{verdict.Reason}{Ansi.Reset}");

            if (verdict.SkillNotActivated)
            {
                Console.WriteLine();
                Console.WriteLine("  {Ansi.BoldRed}⚠️  SKILL NOT ACTIVATED{Ansi.Reset} — the tested skill was not loaded or invoked by the agent");
            }
            if (verdict.OverfittingResult is { } overfitResult)
            {
                Console.WriteLine();
                var overfitIcon = overfitResult.Severity switch
                {
                    OverfittingSeverity.Low => "✅",
                    OverfittingSeverity.Moderate => "🟡",
                    OverfittingSeverity.High => "🔴",
                    _ => "—",
                };
                var severityColor = overfitResult.Severity switch
                {
                    OverfittingSeverity.Low => "{Ansi.Green}",
                    OverfittingSeverity.Moderate => "{Ansi.Yellow}",
                    OverfittingSeverity.High => "{Ansi.Red}",
                    _ => "{Ansi.Dim}",
                };
                Console.WriteLine($"  🔍 Overfitting: {severityColor}{overfitResult.Score:F2} ({overfitResult.Severity.ToString().ToLowerInvariant()}){Ansi.Reset} {overfitIcon}");

                // For moderate/high, show top signals
                if (overfitResult.Severity is OverfittingSeverity.Moderate or OverfittingSeverity.High)
                {
                    // Show prompt-level issues first (most severe)
                    foreach (var item in overfitResult.PromptAssessments)
                        Console.WriteLine($"    {Ansi.Dim}•{Ansi.Reset} [{item.Issue}] {Ansi.Dim}scenario \"{item.Scenario}\"{Ansi.Reset}\n      {Ansi.Dim}— {item.Reasoning}{Ansi.Reset}");

                    var topRubric = overfitResult.RubricAssessments
                        .Where(a => a.Classification != "outcome")
                        .OrderByDescending(a => a.Confidence)
                        .Take(3);
                    foreach (var item in topRubric)
                        Console.WriteLine($"    {Ansi.Dim}•{Ansi.Reset} [{item.Classification}] {Ansi.Dim}\"{item.Criterion}\"{Ansi.Reset}\n      {Ansi.Dim}— {item.Reasoning}{Ansi.Reset}");

                    var topAssert = overfitResult.AssertionAssessments
                        .Where(a => a.Classification != "broad")
                        .OrderByDescending(a => a.Confidence)
                        .Take(2);
                    foreach (var item in topAssert)
                        Console.WriteLine($"    {Ansi.Dim}•{Ansi.Reset} [{item.Classification}] {Ansi.Dim}{item.AssertionSummary}{Ansi.Reset}\n      {Ansi.Dim}— {item.Reasoning}{Ansi.Reset}");
                }
            }

            // Noise test results
            if (verdict.NoiseTestResult is { } noiseResult)
            {
                Console.WriteLine();
                var noiseIcon = noiseResult.Passed ? "✅" : "⚠️";
                var noiseColor = noiseResult.Passed ? "{Ansi.Green}" : "{Ansi.Yellow}";
                Console.WriteLine($"  🔊 Noise test ({noiseResult.TotalSkillsLoaded} skills loaded): {noiseColor}{noiseResult.OverallDegradation * 100:F1}% avg degradation{Ansi.Reset} {noiseIcon}");
                Console.WriteLine($"  {Ansi.Dim}{noiseResult.Reason}{Ansi.Reset}");

                foreach (var ns in noiseResult.Scenarios)
                {
                    var nsIcon = ns.DegradationScore <= 0 ? "{Ansi.Green}↑{Ansi.Reset}" : "{Ansi.Yellow}↓{Ansi.Reset}";
                    var activated = ns.SkillActivation?.Activated == true ? "✅" : "⚠️ not activated";
                    Console.WriteLine($"    {nsIcon} {ns.ScenarioName}  degradation: {ns.DegradationScore * 100:F1}%  target skill: {activated}");
                    Console.WriteLine($"      {Ansi.Dim}skill-only: {ns.WithSkillOnly.JudgeResult.OverallScore:F1}/5 → all-skills: {ns.WithAllSkills.JudgeResult.OverallScore:F1}/5{Ansi.Reset}");
                }
            }

            if (verdict.Scenarios.Count > 0)
            {
                Console.WriteLine();
                foreach (var scenario in verdict.Scenarios)
                    ReportScenarioDetail(scenario, verbose);
            }
            Console.WriteLine();
        }

        int passed = verdicts.Count(v => v.Passed);
        int total = verdicts.Count + rejectedCount;
        var summaryColor = (passed == total) ? "{Ansi.Green}" : "{Ansi.Red}";
        var summaryText = $"{passed}/{total} skills passed validation";
        if (rejectedCount > 0)
            summaryText += $" ({rejectedCount} rejected due to execution errors)";
        Console.WriteLine($"{summaryColor}{summaryText}{Ansi.Reset}");

        bool anyTimeout = verdicts.Any(v => v.Scenarios.Any(s =>
            s.Baseline.Metrics.TimedOut || s.SkilledIsolated.Metrics.TimedOut || (s.SkilledPlugin?.Metrics.TimedOut == true)));
        if (anyTimeout)
        {
            // TimeoutSeconds is null for comparator/rejudge code paths that don't have eval.yaml config;
            // skip those and only show known timeout values in the parenthetical.
            var timeoutValues = verdicts.SelectMany(v => v.Scenarios)
                .Where(s => s.Baseline.Metrics.TimedOut || s.SkilledIsolated.Metrics.TimedOut || (s.SkilledPlugin?.Metrics.TimedOut == true))
                .Select(s => s.TimeoutSeconds).OfType<int>().Distinct().OrderBy(t => t)
                .Select(t => $"{t}s").ToList();
            var limitClause = timeoutValues.Count > 0
                ? $"({string.Join(", ", timeoutValues)}) "
                : "";
            Console.WriteLine();
            Console.WriteLine($"{Ansi.Yellow}⏰ timeout — run(s) hit the {limitClause}scenario timeout limit; scoring may be impacted by aborting model execution before it could produce its full output (increase via 'timeout' in eval.yaml){Ansi.Reset}");
        }

        Console.WriteLine();
    }

    private static void ReportScenarioDetail(ScenarioComparison scenario, bool verbose)
    {
        var icon = scenario.ImprovementScore >= 0 ? "{Ansi.Green}↑{Ansi.Reset}" : "{Ansi.Red}↓{Ansi.Reset}";
        Console.WriteLine($"    {icon} {scenario.ScenarioName}  {FormatScore(scenario.ImprovementScore)}");

        var b = scenario.Baseline.Metrics;
        var s = scenario.SkilledIsolated.Metrics;
        var p = scenario.SkilledPlugin?.Metrics;

        double bRubric = AvgRubricScore(scenario.Baseline.JudgeResult.RubricScores);
        double sRubric = AvgRubricScore(scenario.SkilledIsolated.JudgeResult.RubricScores);
        double? pRubric = scenario.SkilledPlugin is { } sp ? AvgRubricScore(sp.JudgeResult.RubricScores) : null;

        // Build 3-column metric rows: baseline: X  isolated: Y (delta%)  plugin: Z (delta%)
        var metricRows = new (string Label, string Baseline, string Isolated, string? Plugin)[]
        {
            ("Tokens",
             $"{b.TokenEstimate}",
             FormatMetricWithDelta(s.TokenEstimate, b.TokenEstimate, true),
             p is not null ? FormatMetricWithDelta(p.TokenEstimate, b.TokenEstimate, true) : null),
            ("Tool calls",
             $"{b.ToolCallCount}",
             FormatMetricWithDelta(s.ToolCallCount, b.ToolCallCount, true),
             p is not null ? FormatMetricWithDelta(p.ToolCallCount, b.ToolCallCount, true) : null),
            ("Task completion",
             FmtBool(b.TaskCompleted),
             FmtBool(s.TaskCompleted),
             p is not null ? FmtBool(p.TaskCompleted) : null),
            ("Time",
             $"{FmtMs(b.WallTimeMs)}{(b.TimedOut ? " ⏰" : "")}",
             $"{FmtMs(s.WallTimeMs)}{(s.TimedOut ? " ⏰" : "")}{FormatPctDelta(s.WallTimeMs, b.WallTimeMs, true)}",
             p is not null ? $"{FmtMs(p.WallTimeMs)}{(p.TimedOut ? " ⏰" : "")}{FormatPctDelta(p.WallTimeMs, b.WallTimeMs, true)}" : null),
            ("Quality (rubric)",
             $"{bRubric:F1}/5",
             $"{sRubric:F1}/5{FormatPctDelta(sRubric, bRubric, false)}",
             pRubric is not null ? $"{pRubric:F1}/5{FormatPctDelta(pRubric.Value, bRubric, false)}" : null),
            ("Quality (overall)",
             $"{scenario.Baseline.JudgeResult.OverallScore:F1}/5",
             $"{scenario.SkilledIsolated.JudgeResult.OverallScore:F1}/5{FormatPctDelta(scenario.SkilledIsolated.JudgeResult.OverallScore, scenario.Baseline.JudgeResult.OverallScore, false)}",
             scenario.SkilledPlugin is not null ? $"{scenario.SkilledPlugin.JudgeResult.OverallScore:F1}/5{FormatPctDelta(scenario.SkilledPlugin.JudgeResult.OverallScore, scenario.Baseline.JudgeResult.OverallScore, false)}" : null),
            ("Errors",
             $"{b.ErrorCount}",
             FormatMetricWithDelta(s.ErrorCount, b.ErrorCount, true),
             p is not null ? FormatMetricWithDelta(p.ErrorCount, b.ErrorCount, true) : null),
        };

        bool hasPlugin = scenario.SkilledPlugin is not null;

        // Show timeout warnings prominently before the metrics table
        if (b.TimedOut || s.TimedOut || p?.TimedOut == true)
        {
            var parts = new List<string>();
            if (b.TimedOut) parts.Add("baseline");
            if (s.TimedOut) parts.Add("isolated");
            if (p?.TimedOut == true) parts.Add("plugin");
            Console.WriteLine($"      {Ansi.BoldRed}⏰ TIMEOUT{Ansi.Reset} — {string.Join(" and ", parts)} run(s) hit the {(scenario.TimeoutSeconds.HasValue ? $"{scenario.TimeoutSeconds}s " : "")}scenario timeout limit (increase via 'timeout' in eval.yaml)");
        }

        foreach (var (label, baseline, isolated, plugin) in metricRows)
        {
            var line = $"      {Ansi.Dim}{label,-20}{Ansi.Reset} baseline: {Ansi.Dim}{baseline,-12}{Ansi.Reset} isolated: {isolated,-20}";
            if (hasPlugin)
                line += $" plugin: {plugin ?? "—"}";
            Console.WriteLine(line);
        }

        // Effective score line (when plugin run exists, show min)
        if (hasPlugin)
        {
            var isoScore = scenario.IsolatedImprovementScore;
            var plugScore = scenario.PluginImprovementScore;
            Console.WriteLine($"      {Ansi.Bold}Effective score:{Ansi.Reset} min(isolated={FormatPct(isoScore)}, plugin={FormatPct(plugScore)}) = {FormatPct(scenario.ImprovementScore)}");
        }

        // Skill activation info — isolated
        if (scenario.SkillActivationIsolated is { } saIso)
        {
            Console.WriteLine();
            ReportActivation(saIso, "Isolated", scenario.ExpectActivation);
        }

        // Skill activation info — plugin
        if (scenario.SkillActivationPlugin is { } saPlug)
        {
            ReportActivation(saPlug, "Plugin", scenario.ExpectActivation);
        }

        // Subagent (custom agent) activation info
        ReportSubagentActivation(scenario.SubagentActivationIsolated, "Isolated");
        ReportSubagentActivation(scenario.SubagentActivationPlugin, "Plugin");

        Console.WriteLine();

        var bj = scenario.Baseline.JudgeResult;
        var sj = scenario.SkilledIsolated.JudgeResult;
        var pj = scenario.SkilledPlugin?.JudgeResult;
        double scoreDeltaIso = sj.OverallScore - bj.OverallScore;
        var deltaStrIso = FormatColorDelta(scoreDeltaIso);

        var bTimeout = b.TimedOut ? " {Ansi.Red}⏰ timeout{Ansi.Reset}" : "";
        var sTimeout = s.TimedOut ? " {Ansi.Red}⏰ timeout{Ansi.Reset}" : "";
        var overallLine = $"      {Ansi.Bold}Overall:{Ansi.Reset} {bj.OverallScore:F1}{bTimeout} → isolated: {sj.OverallScore:F1}{sTimeout} ({deltaStrIso})";
        if (pj is not null)
        {
            double scoreDeltaPlug = pj.OverallScore - bj.OverallScore;
            var pTimeout = p!.TimedOut ? " {Ansi.Red}⏰ timeout{Ansi.Reset}" : "";
            overallLine += $"  plugin: {pj.OverallScore:F1}{pTimeout} ({FormatColorDelta(scoreDeltaPlug)})";
        }
        Console.WriteLine(overallLine);
        Console.WriteLine();

        // Baseline judge
        Console.WriteLine($"      {Ansi.Cyan}─── Baseline Judge{Ansi.Reset} \x1b[36;1m{bj.OverallScore:F1}/5{Ansi.Reset}{bTimeout} {Ansi.Cyan}───{Ansi.Reset}");
        Console.WriteLine($"      {Ansi.Dim}{bj.OverallReasoning}{Ansi.Reset}");
        if (bj.RubricScores.Count > 0)
        {
            Console.WriteLine();
            foreach (var rs in bj.RubricScores)
            {
                var scoreColor = rs.Score >= 4 ? "{Ansi.Green}" : rs.Score >= 3 ? "{Ansi.Yellow}" : "{Ansi.Red}";
                Console.WriteLine($"        {scoreColor}{Ansi.Bold}{rs.Score}/5{Ansi.Reset}  {Ansi.Bold}{rs.Criterion}{Ansi.Reset}");
                if (!string.IsNullOrEmpty(rs.Reasoning))
                    Console.WriteLine($"              {Ansi.Dim}{rs.Reasoning}{Ansi.Reset}");
            }
        }

        Console.WriteLine();

        // With-skill judge (Isolated)
        Console.WriteLine($"      \x1b[35m─── With-Skill Judge (Isolated){Ansi.Reset} \x1b[35;1m{sj.OverallScore:F1}/5{Ansi.Reset}{sTimeout} \x1b[35m───{Ansi.Reset}");
        Console.WriteLine($"      {Ansi.Dim}{sj.OverallReasoning}{Ansi.Reset}");
        if (sj.RubricScores.Count > 0)
        {
            Console.WriteLine();
            foreach (var rs in sj.RubricScores)
            {
                var scoreColor = rs.Score >= 4 ? "{Ansi.Green}" : rs.Score >= 3 ? "{Ansi.Yellow}" : "{Ansi.Red}";
                var baselineRs = bj.RubricScores.FirstOrDefault(b =>
                    string.Equals(b.Criterion, rs.Criterion, StringComparison.OrdinalIgnoreCase));
                var comparison = baselineRs is not null ? $"{Ansi.Dim} (was {baselineRs.Score}/5){Ansi.Reset}" : "";
                Console.WriteLine($"        {scoreColor}{Ansi.Bold}{rs.Score}/5{Ansi.Reset}{comparison}  {Ansi.Bold}{rs.Criterion}{Ansi.Reset}");
                if (!string.IsNullOrEmpty(rs.Reasoning))
                    Console.WriteLine($"              {Ansi.Dim}{rs.Reasoning}{Ansi.Reset}");
            }
        }
        Console.WriteLine();

        // With-skill judge (Plugin) — only if plugin run exists
        if (pj is not null)
        {
            var pTimeout = p!.TimedOut ? " {Ansi.Red}⏰ timeout{Ansi.Reset}" : "";
            Console.WriteLine($"      {Ansi.Green}─── With-Skill Judge (Plugin){Ansi.Reset} \x1b[32;1m{pj.OverallScore:F1}/5{Ansi.Reset}{pTimeout} {Ansi.Green}───{Ansi.Reset}");
            Console.WriteLine($"      {Ansi.Dim}{pj.OverallReasoning}{Ansi.Reset}");
            if (pj.RubricScores.Count > 0)
            {
                Console.WriteLine();
                foreach (var rs in pj.RubricScores)
                {
                    var scoreColor = rs.Score >= 4 ? "{Ansi.Green}" : rs.Score >= 3 ? "{Ansi.Yellow}" : "{Ansi.Red}";
                    var baselineRs = bj.RubricScores.FirstOrDefault(b =>
                        string.Equals(b.Criterion, rs.Criterion, StringComparison.OrdinalIgnoreCase));
                    var comparison = baselineRs is not null ? $"{Ansi.Dim} (was {baselineRs.Score}/5){Ansi.Reset}" : "";
                    Console.WriteLine($"        {scoreColor}{Ansi.Bold}{rs.Score}/5{Ansi.Reset}{comparison}  {Ansi.Bold}{rs.Criterion}{Ansi.Reset}");
                    if (!string.IsNullOrEmpty(rs.Reasoning))
                        Console.WriteLine($"              {Ansi.Dim}{rs.Reasoning}{Ansi.Reset}");
                }
            }
            Console.WriteLine();
        }

        // Pairwise judge results
        if (scenario.PairwiseResult is { } pw)
        {
            var consistencyIcon = pw.PositionSwapConsistent
                ? "{Ansi.Green}✓ consistent{Ansi.Reset}"
                : "{Ansi.Yellow}⚠ inconsistent{Ansi.Reset}";
            var winnerColor = pw.OverallWinner == "skill" ? "{Ansi.Green}" : pw.OverallWinner == "baseline" ? "{Ansi.Red}" : "{Ansi.Dim}";
            Console.WriteLine($"      {Ansi.Bold}─── Pairwise Comparison{Ansi.Reset} {consistencyIcon} {Ansi.Bold}───{Ansi.Reset}");
            Console.WriteLine($"      Winner: {winnerColor}{pw.OverallWinner}{Ansi.Reset} ({pw.OverallMagnitude})");
            Console.WriteLine($"      {Ansi.Dim}{pw.OverallReasoning}{Ansi.Reset}");
            if (pw.RubricResults.Count > 0)
            {
                Console.WriteLine();
                foreach (var pr in pw.RubricResults)
                {
                    var prColor = pr.Winner == "skill" ? "{Ansi.Green}" : pr.Winner == "baseline" ? "{Ansi.Red}" : "{Ansi.Dim}";
                    Console.WriteLine($"        {prColor}{Ansi.Bold}{pr.Winner,-8}{Ansi.Reset} ({pr.Magnitude})  {Ansi.Bold}{pr.Criterion}{Ansi.Reset}");
                    if (!string.IsNullOrEmpty(pr.Reasoning))
                        Console.WriteLine($"              {Ansi.Dim}{pr.Reasoning}{Ansi.Reset}");
                }
            }
            Console.WriteLine();
        }

        if (verbose)
        {
            Console.WriteLine();
            Console.WriteLine("      {Ansi.Dim}Baseline output:{Ansi.Reset}");
            Console.WriteLine(IndentBlock(scenario.Baseline.Metrics.AgentOutput.Length > 0 ? scenario.Baseline.Metrics.AgentOutput : "(no output)", 8));
            Console.WriteLine("      {Ansi.Dim}With-skill output (isolated):{Ansi.Reset}");
            Console.WriteLine(IndentBlock(scenario.SkilledIsolated.Metrics.AgentOutput.Length > 0 ? scenario.SkilledIsolated.Metrics.AgentOutput : "(no output)", 8));
            if (scenario.SkilledPlugin is { } pluginRun)
            {
                Console.WriteLine("      {Ansi.Dim}With-skill output (plugin):{Ansi.Reset}");
                Console.WriteLine(IndentBlock(pluginRun.Metrics.AgentOutput.Length > 0 ? pluginRun.Metrics.AgentOutput : "(no output)", 8));
            }
        }
    }

    private static void ReportActivation(SkillActivationInfo sa, string label, bool expectActivation)
    {
        if (sa.Activated)
        {
            var parts = new List<string>();
            if (sa.DetectedSkills.Count > 0) parts.Add(string.Join(", ", sa.DetectedSkills));
            if (sa.ExtraTools.Count > 0) parts.Add("extra tools: " + string.Join(", ", sa.ExtraTools));
            Console.WriteLine($"      {Ansi.Dim}Skill activated ({label}):{Ansi.Reset} {Ansi.Green}{(parts.Count > 0 ? string.Join("; ", parts) : "yes")}{Ansi.Reset}");
        }
        else
        {
            if (!expectActivation)
                Console.WriteLine($"      {Ansi.Cyan}ℹ️  Skill correctly NOT activated ({label}, negative test){Ansi.Reset}");
            else
                Console.WriteLine($"      {Ansi.Yellow}⚠️  Skill was NOT activated ({label}){Ansi.Reset}");
        }
    }

    private static void ReportSubagentActivation(SubagentActivationInfo? sa, string label)
    {
        if (sa is null || sa.InvokedAgents.Count == 0)
            return;
        Console.WriteLine($"      \x1b[2mAgents invoked ({label}):\x1b[0m \x1b[34m{string.Join(", ", sa.InvokedAgents)}\x1b[0m ({sa.SubagentEventCount} event(s))");
    }

    /// <summary>Formats a metric value with a percentage delta from baseline, e.g. "800 (-33%)".</summary>
    private static string FormatMetricWithDelta(double value, double baseline, bool lowerIsBetter)
    {
        if (baseline == 0) return $"{value}";
        double pctChange = (value - baseline) / baseline * 100;
        var sign = pctChange > 0 ? "+" : "";
        bool isGood = lowerIsBetter ? pctChange < 0 : pctChange > 0;
        var color = isGood ? "{Ansi.Green}" : pctChange == 0 ? "" : "{Ansi.Red}";
        var reset = string.IsNullOrEmpty(color) ? "" : "{Ansi.Reset}";
        return $"{value} {color}({sign}{pctChange:F0}%){reset}";
    }

    /// <summary>Returns a parenthesized percentage delta string, e.g. " (-33%)".</summary>
    private static string FormatPctDelta(double value, double baseline, bool lowerIsBetter)
    {
        if (baseline == 0) return "";
        double pctChange = (value - baseline) / baseline * 100;
        var sign = pctChange > 0 ? "+" : "";
        bool isGood = lowerIsBetter ? pctChange < 0 : pctChange > 0;
        var color = isGood ? "{Ansi.Green}" : pctChange == 0 ? "" : "{Ansi.Red}";
        var reset = string.IsNullOrEmpty(color) ? "" : "{Ansi.Reset}";
        return $" {color}({sign}{pctChange:F0}%){reset}";
    }

    private static string FormatColorDelta(double delta) =>
        delta > 0 ? $"{Ansi.Green}+{delta:F1}{Ansi.Reset}" :
        delta < 0 ? $"{Ansi.Red}{delta:F1}{Ansi.Reset}" : "{Ansi.Dim}±0{Ansi.Reset}";

    // --- Markdown reporter ---

    public static string GenerateMarkdownSummary(
        IReadOnlyList<SkillVerdict> verdicts,
        string? model = null,
        string? judgeModel = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Skill Validation Results");
        sb.AppendLine();

        // Show validation/spec errors for skills that failed before evaluation ran
        var failedVerdicts = verdicts
            .Where(v => !v.Passed
                        && !string.IsNullOrEmpty(v.FailureKind)
                        && v.Scenarios.Count == 0)
            .ToArray();
        if (failedVerdicts.Length > 0)
        {
            sb.AppendLine("### ❌ Skill validation errors");
            sb.AppendLine();
            foreach (var v in failedVerdicts)
            {
                // Wrap in inline code to prevent markdown injection from PR-controlled content
                var safeName = v.SkillName.Replace("`", "'").Replace("\r", "").Replace("\n", " ");
                var safeReason = v.Reason.Replace("`", "'").Replace("\r", "").Replace("\n", " ");
                sb.AppendLine($"- `{safeName}: {safeReason}`");
            }

            sb.AppendLine();
        }

        var footnotes = new List<string>();
        var tableRows = new List<string>();
        bool anyPluginRun = verdicts.Any(v => v.Scenarios.Any(s => s.SkilledPlugin is not null));
        bool anyAgentsInvoked = verdicts.Any(v => v.Scenarios.Any(s =>
            (s.SubagentActivationIsolated is { } sai && sai.InvokedAgents.Count > 0) ||
            (s.SubagentActivationPlugin is { } sap && sap.InvokedAgents.Count > 0)));

        foreach (var v in verdicts)
        {
            bool skillNotActivated = v.SkillNotActivated;
            foreach (var s in v.Scenarios)
            {
                var baseScore = s.Baseline?.JudgeResult?.OverallScore;
                var isoScore = s.SkilledIsolated?.JudgeResult?.OverallScore;
                var plugScore = s.SkilledPlugin?.JudgeResult?.OverallScore;
                var bTimedOut = s.Baseline?.Metrics?.TimedOut == true;
                var isoTimedOut = s.SkilledIsolated?.Metrics?.TimedOut == true;
                var plugTimedOut = s.SkilledPlugin?.Metrics?.TimedOut == true;

                string isoQualityCol = FormatQualityCell(baseScore, isoScore, bTimedOut, isoTimedOut, out double? isoQualityDelta);
                double? plugQualityDelta = null;
                string plugQualityCol = "";
                if (anyPluginRun)
                {
                    plugQualityCol = FormatQualityCell(baseScore, plugScore, bTimedOut, plugTimedOut, out plugQualityDelta);
                }

                // Use the effective (worse) run's quality delta for footnotes
                bool pluginIsEffective = s.SkilledPlugin is not null && s.PluginImprovementScore < s.IsolatedImprovementScore;
                double? qualityDelta = pluginIsEffective ? plugQualityDelta : isoQualityDelta;
                var icon = s.ImprovementScore > 0 ? "✅" : s.ImprovementScore < 0 ? "❌" : "🟡";

                // Skills loaded column — show both isolated and plugin activation
                string skillsCol = "—";
                if (s.SkillActivationIsolated is { } saIso)
                {
                    skillsCol = FormatActivationCell(saIso, s.ExpectActivation);
                }
                else if (skillNotActivated)
                {
                    skillsCol = "⚠️ NOT ACTIVATED";
                }

                if (anyPluginRun && s.SkillActivationPlugin is { } saPlug)
                {
                    string plugActivation = FormatActivationCell(saPlug, s.ExpectActivation);
                    if (plugActivation != skillsCol)
                        skillsCol += $" / {plugActivation}";
                }

                // Agents invoked column — show subagent activations
                string agentsCol = FormatSubagentCell(s.SubagentActivationIsolated);
                if (anyPluginRun && s.SubagentActivationPlugin is { } saPlugAgent)
                {
                    string plugAgents = FormatSubagentCell(saPlugAgent);
                    if (plugAgents != agentsCol)
                        agentsCol += $" / {plugAgents}";
                }

                var footnote = BuildVerdictFootnote(s, qualityDelta);
                string verdictCol = icon;
                if (footnote is not null)
                {
                    footnotes.Add(footnote);
                    int n = footnotes.Count;
                    verdictCol = $"{icon} <a href=\"#user-content-fn-{n}\" id=\"ref-{n}\">[{n}]</a>";
                }

                string agentsSegment = anyAgentsInvoked ? $" | {agentsCol}" : "";
                var row = anyPluginRun
                    ? $"| {v.SkillName} | {s.ScenarioName} | {isoQualityCol} | {plugQualityCol} | {skillsCol}{agentsSegment} | {FormatOverfitCell(v.OverfittingResult)} | {verdictCol} |"
                    : $"| {v.SkillName} | {s.ScenarioName} | {isoQualityCol} | {skillsCol}{agentsSegment} | {FormatOverfitCell(v.OverfittingResult)} | {verdictCol} |";
                tableRows.Add(row);
            }
        }

        if (tableRows.Count > 0)
        {
            string agentsHeader = anyAgentsInvoked ? " Agents Invoked |" : "";
            string agentsSep = anyAgentsInvoked ? "----------------|" : "";
            if (anyPluginRun)
            {
                sb.AppendLine($"| Skill | Scenario | Quality (Isolated) | Quality (Plugin) | Skills Loaded |{agentsHeader} Overfit | Verdict |");
                sb.AppendLine($"|-------|----------|--------------------|------------------|---------------|{agentsSep}---------|---------|"  );
            }
            else
            {
                sb.AppendLine($"| Skill | Scenario | Quality | Skills Loaded |{agentsHeader} Overfit | Verdict |");
                sb.AppendLine($"|-------|----------|---------|---------------|{agentsSep}---------|---------|"  );
            }
            foreach (var row in tableRows)
                sb.AppendLine(row);
        }
      
        if (footnotes.Count > 0)
        {
            sb.AppendLine();
            for (int i = 0; i < footnotes.Count; i++)
                sb.AppendLine($"<a href=\"#user-content-ref-{i + 1}\" id=\"fn-{i + 1}\"><strong>[{i + 1}]</strong></a> {footnotes[i]}");
        }

        bool anyTimeout = verdicts.Any(v => v.Scenarios.Any(s =>
            (s.Baseline?.Metrics?.TimedOut == true) || (s.SkilledIsolated?.Metrics?.TimedOut == true) || (s.SkilledPlugin?.Metrics?.TimedOut == true)));
        if (anyTimeout)
        {
            // TimeoutSeconds is null for comparator/rejudge code paths that don't have eval.yaml config;
            // skip those and only show known timeout values in the parenthetical.
            var timeoutValues = verdicts.SelectMany(v => v.Scenarios)
                .Where(s => (s.Baseline?.Metrics?.TimedOut == true) || (s.SkilledIsolated?.Metrics?.TimedOut == true) || (s.SkilledPlugin?.Metrics?.TimedOut == true))
                .Select(s => s.TimeoutSeconds).OfType<int>().Distinct().OrderBy(t => t)
                .Select(t => $"{t}s").ToList();
            var limitClause = timeoutValues.Count > 0
                ? $"({string.Join(", ", timeoutValues)}) "
                : "";
            sb.AppendLine($"\n> ⏰ **timeout** — run(s) hit the {limitClause}scenario timeout limit; scoring may be impacted by aborting model execution before it could produce its full output (increase via `timeout` in eval.yaml)");
        }

        // Noise test results
        var withNoise = verdicts.Where(v => v.NoiseTestResult is not null).ToList();
        if (withNoise.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Noise Test (Multi-Skill Loading)");
            sb.AppendLine();
            sb.AppendLine("| Skill | Skills Loaded | Degradation | Verdict |");
            sb.AppendLine("|-------|--------------|-------------|---------|");
            foreach (var v in withNoise)
            {
                var nr = v.NoiseTestResult!;
                var icon = nr.Passed ? "✅" : "⚠️";
                sb.AppendLine($"| {v.SkillName} | {nr.TotalSkillsLoaded} | {nr.OverallDegradation * 100:F1}% | {icon} |");
            }

            foreach (var v in withNoise)
            {
                var nr = v.NoiseTestResult!;
                if (nr.Scenarios.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"**{v.SkillName}** noise scenarios:");
                    sb.AppendLine();
                    sb.AppendLine("| Scenario | Skill-Only | All-Skills | Degradation | Target Activated |");
                    sb.AppendLine("|----------|-----------|------------|-------------|-----------------|");
                    foreach (var ns in nr.Scenarios)
                    {
                        var activated = ns.SkillActivation?.Activated == true ? "✅" : "⚠️";
                        sb.AppendLine($"| {ns.ScenarioName} | {ns.WithSkillOnly.JudgeResult.OverallScore:F1}/5 | {ns.WithAllSkills.JudgeResult.OverallScore:F1}/5 | {ns.DegradationScore * 100:F1}% | {activated} |");
                    }
                }
            }
        }

        sb.AppendLine($"\nModel: {model ?? "unknown"} | Judge: {judgeModel ?? "unknown"}");

        bool anyFailure = verdicts.Any(v => !v.Passed);
        if (anyFailure)
            sb.AppendLine("\n> 📖 See docs/InvestigatingResults.md for how to diagnose failures. Additional debugging guidance may be provided by your workflow.");

        return sb.ToString();
    }

    private static async Task ReportMarkdown(
        IReadOnlyList<SkillVerdict> verdicts,
        string resultsDir,
        string? model,
        string? judgeModel)
    {
        var md = GenerateMarkdownSummary(verdicts, model, judgeModel);
        await File.WriteAllTextAsync(Path.Combine(resultsDir, "summary.md"), md);
        Console.WriteLine($"Markdown summary written to {Path.Combine(resultsDir, "summary.md")}");

        foreach (var verdict in verdicts)
        {
            var skillDir = Path.Combine(resultsDir, SafeDirName(verdict.SkillName));
            Directory.CreateDirectory(skillDir);

            foreach (var scenario in verdict.Scenarios)
            {
                var scenarioSlug = System.Text.RegularExpressions.Regex.Replace(
                    scenario.ScenarioName.ToLowerInvariant(), "[^a-z0-9]+", "-");

                var judgeReport = new StringBuilder();
                judgeReport.AppendLine($"# Judge Report: {scenario.ScenarioName}");
                judgeReport.AppendLine();
                judgeReport.AppendLine("## Baseline Judge");
                judgeReport.AppendLine($"Overall Score: {scenario.Baseline.JudgeResult.OverallScore}/5");
                judgeReport.AppendLine($"Reasoning: {scenario.Baseline.JudgeResult.OverallReasoning}");
                judgeReport.AppendLine();
                foreach (var rs in scenario.Baseline.JudgeResult.RubricScores)
                    judgeReport.AppendLine($"- **{rs.Criterion}**: {rs.Score}/5 — {rs.Reasoning}");
                judgeReport.AppendLine();
                judgeReport.AppendLine("## With-Skill Judge (Isolated)");
                judgeReport.AppendLine($"Overall Score: {scenario.SkilledIsolated.JudgeResult.OverallScore}/5");
                judgeReport.AppendLine($"Reasoning: {scenario.SkilledIsolated.JudgeResult.OverallReasoning}");
                judgeReport.AppendLine();
                foreach (var rs in scenario.SkilledIsolated.JudgeResult.RubricScores)
                    judgeReport.AppendLine($"- **{rs.Criterion}**: {rs.Score}/5 — {rs.Reasoning}");
                judgeReport.AppendLine();

                // Plugin judge section (if plugin run exists)
                if (scenario.SkilledPlugin is { } pluginRun)
                {
                    judgeReport.AppendLine("## With-Skill Judge (Plugin)");
                    judgeReport.AppendLine($"Overall Score: {pluginRun.JudgeResult.OverallScore}/5");
                    judgeReport.AppendLine($"Reasoning: {pluginRun.JudgeResult.OverallReasoning}");
                    judgeReport.AppendLine();
                    foreach (var rs in pluginRun.JudgeResult.RubricScores)
                        judgeReport.AppendLine($"- **{rs.Criterion}**: {rs.Score}/5 — {rs.Reasoning}");
                    judgeReport.AppendLine();
                }

                judgeReport.AppendLine("## Baseline Agent Output");
                judgeReport.AppendLine("```");
                judgeReport.AppendLine(scenario.Baseline.Metrics.AgentOutput.Length > 0 ? scenario.Baseline.Metrics.AgentOutput : "(no output)");
                judgeReport.AppendLine("```");
                judgeReport.AppendLine();
                judgeReport.AppendLine("## With-Skill Agent Output (Isolated)");
                judgeReport.AppendLine("```");
                judgeReport.AppendLine(scenario.SkilledIsolated.Metrics.AgentOutput.Length > 0 ? scenario.SkilledIsolated.Metrics.AgentOutput : "(no output)");
                judgeReport.AppendLine("```");

                // Plugin agent output (if plugin run exists)
                if (scenario.SkilledPlugin is { } pluginRunOutput)
                {
                    judgeReport.AppendLine();
                    judgeReport.AppendLine("## With-Skill Agent Output (Plugin)");
                    judgeReport.AppendLine("```");
                    judgeReport.AppendLine(pluginRunOutput.Metrics.AgentOutput.Length > 0 ? pluginRunOutput.Metrics.AgentOutput : "(no output)");
                    judgeReport.AppendLine("```");
                }

                await File.WriteAllTextAsync(Path.Combine(skillDir, $"{scenarioSlug}.md"), judgeReport.ToString());
            }
        }
    }

    // --- JSON reporter ---

    private static async Task ReportJson(
        IReadOnlyList<SkillVerdict> verdicts,
        string resultsDir,
        string? model,
        string? judgeModel)
    {
        var output = new ResultsOutput
        {
            Model = model ?? "unknown",
            JudgeModel = judgeModel ?? model ?? "unknown",
            Timestamp = DateTime.UtcNow.ToString("o"),
            Verdicts = verdicts,
        };

        var json = JsonSerializer.Serialize(output, SkillValidatorJsonContext.Default.ResultsOutput);

        await File.WriteAllTextAsync(Path.Combine(resultsDir, "results.json"), json);
        Console.WriteLine($"JSON results written to {Path.Combine(resultsDir, "results.json")}");

        // Write per-skill verdict.json files for downstream consumers (e.g. dashboard)
        foreach (var verdict in verdicts)
        {
            var skillDir = Path.Combine(resultsDir, SafeDirName(verdict.SkillName));
            Directory.CreateDirectory(skillDir);
            var verdictJson = JsonSerializer.Serialize(verdict, SkillValidatorJsonContext.Default.SkillVerdict);
            await File.WriteAllTextAsync(Path.Combine(skillDir, "verdict.json"), verdictJson);
        }
    }
    // --- JUnit reporter ---

    private static async Task ReportJunit(IReadOnlyList<SkillVerdict> verdicts, string resultsDir)
    {
        var testcases = new List<string>();
        foreach (var verdict in verdicts)
        {
            if (verdict.Scenarios.Count == 0)
            {
                var status = verdict.Passed ? "" : $"""<failure message="{EscapeXml(verdict.Reason)}" />""";
                testcases.Add($"""    <testcase name="{EscapeXml(verdict.SkillName)}" classname="skill-validator">{status}</testcase>""");
            }
            else
            {
                foreach (var scenario in verdict.Scenarios)
                {
                    var name = $"{verdict.SkillName} / {scenario.ScenarioName}";
                    var status = scenario.ImprovementScore >= 0
                        ? ""
                        : $"""<failure message="Improvement score: {scenario.ImprovementScore * 100:F1}%" />""";
                    testcases.Add($"""    <testcase name="{EscapeXml(name)}" classname="skill-validator">{status}</testcase>""");
                }
            }
        }

        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <testsuites>
              <testsuite name="skill-validator" tests="{testcases.Count}">
            {string.Join("\n", testcases)}
              </testsuite>
            </testsuites>
            """;

        await File.WriteAllTextAsync(Path.Combine(resultsDir, "results.xml"), xml);
        Console.WriteLine($"JUnit results written to {Path.Combine(resultsDir, "results.xml")}");
    }

    // --- Helpers ---

    /// <summary>Sanitize a skill name into a safe single directory segment by slugifying.</summary>
    internal static string SafeDirName(string name)
    {
        var seg = Path.GetFileName(name ?? "");
        if (string.IsNullOrEmpty(seg) || seg == "." || seg == "..")
            throw new ArgumentException($"Invalid skill name for directory use: '{name}'");
        // Replace characters that are unsafe in directory names with hyphens and collapse runs.
        var slugified = System.Text.RegularExpressions.Regex.Replace(seg, "[^a-zA-Z0-9._-]", "-");
        slugified = System.Text.RegularExpressions.Regex.Replace(slugified, "-{2,}", "-");
        slugified = slugified.Trim('-');
        if (string.IsNullOrEmpty(slugified))
            throw new ArgumentException($"Invalid skill name for directory use: '{name}'");
        return slugified;
    }

    private static double AvgRubricScore(IReadOnlyList<RubricScore> scores) =>
        scores.Count == 0 ? 0 : scores.Average(s => s.Score);

    private static string FormatOverfitCell(OverfittingResult? result)
    {
        if (result is null) return "—";
        var icon = result.Severity switch
        {
            OverfittingSeverity.Low => "✅",
            OverfittingSeverity.Moderate => "🟡",
            OverfittingSeverity.High => "🔴",
            _ => "—",
        };
        return $"{icon} {result.Score:F2}";
    }

    /// <summary>
    /// Formats the combined Quality column: "baseline → skill" with the better score bolded
    /// and an emoji indicator.
    /// </summary>
    internal static string FormatQualityCell(
        double? baseScore, double? skillScore,
        bool bTimedOut, bool sTimedOut,
        out double? qualityDelta)
    {
        qualityDelta = null;
        string baseFmt = baseScore is { } b && !double.IsNaN(b) ? $"{b:F1}/5" : "\u2014";
        string skillFmt = skillScore is { } sk && !double.IsNaN(sk) ? $"{sk:F1}/5" : "\u2014";
        if (bTimedOut) baseFmt += " \u23f0";
        if (sTimedOut) skillFmt += " \u23f0";

        if (baseScore is { } bv && skillScore is { } sv && !double.IsNaN(bv) && !double.IsNaN(sv))
        {
            double delta = sv - bv;
            if (!double.IsNaN(delta))
                qualityDelta = delta;

            if (sv > bv)
                return $"{baseFmt} \u2192 **{skillFmt}** \U0001f7e2";
            if (sv < bv)
                return $"**{baseFmt}** \u2192 {skillFmt} \U0001f534";
        }

        return $"{baseFmt} \u2192 {skillFmt}";
    }

    /// <summary>Formats an activation info object into a markdown cell string.</summary>
    internal static string FormatActivationCell(SkillActivationInfo sa, bool expectActivation)
    {
        if (sa.Activated)
        {
            var parts = new List<string>();
            if (sa.DetectedSkills.Count > 0) parts.AddRange(sa.DetectedSkills);
            if (sa.ExtraTools.Count > 0) parts.Add("tools: " + string.Join(", ", sa.ExtraTools));
            return parts.Count > 0 ? "✅ " + string.Join("; ", parts) : "✅";
        }
        return expectActivation ? "⚠️ NOT ACTIVATED" : "ℹ️ not activated (expected)";
    }

    /// <summary>Formats a subagent activation info object into a markdown cell string.</summary>
    /// <remarks>
    /// Agent names may originate from plugin content; we sanitize them to avoid breaking
    /// markdown table formatting or enabling injection via characters like '|' or newlines.
    /// </remarks>
    internal static string FormatSubagentCell(SubagentActivationInfo? sa)
    {
        if (sa is null || sa.InvokedAgents.Count == 0)
            return "—";

        var sanitized = new List<string>(sa.InvokedAgents.Count);
        foreach (var agent in sa.InvokedAgents)
        {
            if (string.IsNullOrEmpty(agent))
                continue;
            sanitized.Add(SanitizeMarkdownTableCell(agent));
        }

        return sanitized.Count > 0 ? string.Join(", ", sanitized) : "—";
    }

    /// <summary>
    /// Sanitizes text for inclusion in a markdown table cell by escaping table delimiters
    /// and replacing line breaks with spaces.
    /// </summary>
    internal static string SanitizeMarkdownTableCell(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\r':
                case '\n':
                    builder.Append(' ');
                    break;
                case '|':
                    builder.Append("\\|");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns a footnote string when the verdict (based on composite ImprovementScore)
    /// disagrees with the quality delta shown in the table, or null if no explanation is needed.
    /// </summary>
    internal static string? BuildVerdictFootnote(ScenarioComparison s, double? qualityDelta)
    {
        // No footnote for neutral verdicts (🟡) or when quality delta is unknown
        if (s.ImprovementScore == 0 || !qualityDelta.HasValue)
            return null;

        bool verdictPositive = s.ImprovementScore > 0;

        // Use the raw quality delta to determine direction, matching the comparison
        // used in FormatQualityCell (which shows arrows based on unrounded scores).
        double delta = qualityDelta.Value;

        bool qualityPositive = delta > 0;
        bool qualityNegative = delta < 0;

        // Verdict agrees with quality direction — no footnote needed
        if (verdictPositive && !qualityNegative) return null;
        if (!verdictPositive && qualityNegative) return null;

        var bd = s.Breakdown;
        var composite = s.ImprovementScore * 100;

        // Map breakdown fields using DefaultWeights to avoid hard-coded weight duplication
        var breakdownByKey = new Dictionary<string, (string label, double raw)>
        {
            ["QualityImprovement"] = ("quality", bd.QualityImprovement),
            ["OverallJudgmentImprovement"] = ("judgment", bd.OverallJudgmentImprovement),
            ["TaskCompletionImprovement"] = ("completion", bd.TaskCompletionImprovement),
            ["TokenReduction"] = ("tokens", bd.TokenReduction),
            ["ErrorReduction"] = ("errors", bd.ErrorReduction),
            ["ToolCallReduction"] = ("tool calls", bd.ToolCallReduction),
            ["TimeReduction"] = ("time", bd.TimeReduction),
        };

        var contributors = DefaultWeights.Values
            .Where(kvp => breakdownByKey.ContainsKey(kvp.Key))
            .Select(kvp =>
            {
                var (label, raw) = breakdownByKey[kvp.Key];
                return (label, raw, weighted: raw * kvp.Value);
            })
            .ToList();

        // Determine which run is the effective (worse) one for raw metric values
        bool pluginIsWorse = s.SkilledPlugin is not null && s.PluginImprovementScore < s.IsolatedImprovementScore;
        string runLabel = pluginIsWorse ? "Plugin" : "Isolated";

        // Format a contributor label with raw metric values when available
        string FormatContributor(string label)
        {
            var bm = s.Baseline?.Metrics;
            // Use the effective (worse) run's metrics for raw values
            var sm = pluginIsWorse
                ? s.SkilledPlugin?.Metrics
                : s.SkilledIsolated?.Metrics;
            if (bm is null || sm is null) return label;

            string? raw = label switch
            {
                "tokens" => $"{bm.TokenEstimate} \u2192 {sm.TokenEstimate}",
                "tool calls" => $"{bm.ToolCallCount} \u2192 {sm.ToolCallCount}",
                "time" => $"{FmtMs(bm.WallTimeMs)} \u2192 {FmtMs(sm.WallTimeMs)}",
                "errors" => $"{bm.ErrorCount} \u2192 {sm.ErrorCount}",
                "completion" => $"{FmtBool(bm.TaskCompleted)} \u2192 {FmtBool(sm.TaskCompleted)}",
                _ => null,
            };

            return raw is not null ? $"{label} ({raw})" : label;
        }

        string compositeStr = $"{(composite >= 0 ? "+" : "")}{composite:F1}%";

        if (!verdictPositive && (qualityPositive || !qualityNegative))
        {
            // Quality improved or unchanged, but composite is negative — show what dragged it down
            var negatives = contributors
                .Where(c => c.weighted < -0.005)
                .OrderBy(c => c.weighted)
                .Select(c => FormatContributor(c.label))
                .ToList();
            string factors = negatives.Count > 0
                ? string.Join(", ", negatives)
                : "efficiency metrics";
            string qualityDesc = qualityPositive ? "Quality improved" : "Quality unchanged";
            return $"({runLabel}) {qualityDesc} but weighted score is {compositeStr} due to: {factors}";
        }

        if (verdictPositive && qualityNegative)
        {
            // Quality dropped, but composite is positive — show what compensated
            var positives = contributors
                .Where(c => c.weighted > 0.005 && c.label is not "quality" and not "judgment")
                .OrderByDescending(c => c.weighted)
                .Select(c => FormatContributor(c.label))
                .ToList();
            string factors = positives.Count > 0
                ? string.Join(", ", positives)
                : "efficiency metrics";
            return $"({runLabel}) Quality dropped but weighted score is {compositeStr} due to: {factors}";
        }

        return null;
    }

    private static string FmtBool(bool v) => v ? "✓" : "✗";

    private static string FmtMs(long ms) =>
        ms < 1000 ? $"{ms}ms" : $"{ms / 1000.0:F1}s";

    private static string FormatScore(double score)
    {
        var pct = $"{score * 100:F1}%";
        if (score > 0) return $"{Ansi.Green}+{pct}{Ansi.Reset}";
        if (score < 0) return $"{Ansi.Red}{pct}{Ansi.Reset}";
        return $"{Ansi.Dim}{pct}{Ansi.Reset}";
    }

    private static string FormatPct(double value)
    {
        var pct = $"{value * 100:F1}%";
        return value > 0 ? $"+{pct}" : pct;
    }

    private static string FormatDelta(double value)
    {
        var pct = $"{value * 100:F1}%";
        if (value > 0) return $"+{pct}";
        if (value < 0) return pct;
        return "0.0%";
    }

    internal static string FormatTimestamp(DateTime date) =>
        date.ToString("yyyyMMdd-HHmmss");

    private static string IndentBlock(string text, int spaces)
    {
        var prefix = new string(' ', spaces);
        return string.Join("\n", text.Split('\n').Select(l => $"{prefix}{l}"));
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;")
         .Replace("<", "&lt;")
         .Replace(">", "&gt;")
         .Replace("\"", "&quot;")
         .Replace("'", "&apos;");
}
