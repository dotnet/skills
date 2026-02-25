using System.Text;
using System.Text.Json;
using System.Xml;
using SkillValidator.Models;

namespace SkillValidator.Services;

public static class Reporter
{
    public static async Task ReportResults(
        IReadOnlyList<SkillVerdict> verdicts,
        IReadOnlyList<ReporterSpec> reporters,
        bool verbose,
        string? model = null,
        string? judgeModel = null,
        string? resultsDir = null)
    {
        bool needsResultsDir = reporters.Any(r =>
            r.Type is ReporterType.Json or ReporterType.Junit or ReporterType.Markdown);
        string? effectiveResultsDir = resultsDir is not null && needsResultsDir
            ? Path.Combine(resultsDir, FormatTimestamp(DateTime.Now))
            : null;

        if (effectiveResultsDir is not null)
            Directory.CreateDirectory(effectiveResultsDir);

        foreach (var reporter in reporters)
        {
            switch (reporter.Type)
            {
                case ReporterType.Console:
                    ReportConsole(verdicts, verbose);
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

    private static void ReportConsole(IReadOnlyList<SkillVerdict> verdicts, bool verbose)
    {
        Console.WriteLine();
        Console.WriteLine("\x1b[1m═══ Skill Validation Results ═══\x1b[0m");
        Console.WriteLine();

        foreach (var verdict in verdicts)
        {
            var icon = verdict.Passed ? "\x1b[32m✓\x1b[0m" : "\x1b[31m✗\x1b[0m";
            var name = $"\x1b[1m{verdict.SkillName}\x1b[0m";
            var score = FormatScore(verdict.OverallImprovementScore);

            var scoreLine = $"{icon} {name}  {score}";
            if (verdict.ConfidenceInterval is { } ci)
            {
                var ciStr = $"[{FormatPct(ci.Low)}, {FormatPct(ci.High)}]";
                var sigStr = verdict.IsSignificant == true
                    ? "\x1b[32msignificant\x1b[0m"
                    : "\x1b[33mnot significant\x1b[0m";
                scoreLine += $"  \x1b[2m{ciStr}\x1b[0m {sigStr}";
            }
            if (verdict.NormalizedGain is { } ng)
                scoreLine += $"  \x1b[2m(g={FormatPct(ng)})\x1b[0m";

            Console.WriteLine(scoreLine);
            Console.WriteLine($"  \x1b[2m{verdict.Reason}\x1b[0m");

            if (!verdict.Passed && verdict.ProfileWarnings is { Count: > 0 })
            {
                Console.WriteLine();
                Console.WriteLine("  \x1b[33mPossible causes from skill analysis:\x1b[0m");
                foreach (var warning in verdict.ProfileWarnings)
                    Console.WriteLine($"    \x1b[2m•\x1b[0m \x1b[2m{warning}\x1b[0m");
            }
            if (verdict.SkillNotActivated)
            {
                Console.WriteLine();
                Console.WriteLine("  \x1b[31;1m⚠️  SKILL NOT ACTIVATED\x1b[0m — the tested skill was not loaded or invoked by the agent");
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
        int total = verdicts.Count;
        var summaryColor = passed == total ? "\x1b[32m" : "\x1b[31m";
        Console.WriteLine($"{summaryColor}{passed}/{total} skills passed validation\x1b[0m");

        bool anyTimeout = verdicts.Any(v => v.Scenarios.Any(s =>
            s.Baseline.Metrics.TimedOut || s.WithSkill.Metrics.TimedOut));
        if (anyTimeout)
        {
            Console.WriteLine();
            Console.WriteLine("\x1b[33m⏰ timeout — run hit the scenario timeout limit; scoring may be impacted by aborting model execution before it could produce its full output\x1b[0m");
        }

        Console.WriteLine();
    }

    private static void ReportScenarioDetail(ScenarioComparison scenario, bool verbose)
    {
        var icon = scenario.ImprovementScore >= 0 ? "\x1b[32m↑\x1b[0m" : "\x1b[31m↓\x1b[0m";
        Console.WriteLine($"    {icon} {scenario.ScenarioName}  {FormatScore(scenario.ImprovementScore)}");

        var b = scenario.Baseline.Metrics;
        var s = scenario.WithSkill.Metrics;
        var bd = scenario.Breakdown;

        double bRubric = AvgRubricScore(scenario.Baseline.JudgeResult.RubricScores);
        double sRubric = AvgRubricScore(scenario.WithSkill.JudgeResult.RubricScores);

        var metrics = new (string Label, double Value, string Absolute, bool LowerIsBetter)[]
        {
            ("Tokens", bd.TokenReduction, $"{b.TokenEstimate} → {s.TokenEstimate}", true),
            ("Tool calls", bd.ToolCallReduction, $"{b.ToolCallCount} → {s.ToolCallCount}", true),
            ("Task completion", bd.TaskCompletionImprovement, $"{FmtBool(b.TaskCompleted)} → {FmtBool(s.TaskCompleted)}", false),
            ("Time", bd.TimeReduction, $"{FmtMs(b.WallTimeMs)}{(b.TimedOut ? " ⏰" : "")} → {FmtMs(s.WallTimeMs)}{(s.TimedOut ? " ⏰" : "")}", true),
            ("Quality (rubric)", bd.QualityImprovement, $"{bRubric:F1}/5 → {sRubric:F1}/5", false),
            ("Quality (overall)", bd.OverallJudgmentImprovement, $"{scenario.Baseline.JudgeResult.OverallScore:F1}/5 → {scenario.WithSkill.JudgeResult.OverallScore:F1}/5", false),
            ("Errors", bd.ErrorReduction, $"{b.ErrorCount} → {s.ErrorCount}", true),
        };

        // Show timeout warnings prominently before the metrics table
        if (b.TimedOut || s.TimedOut)
        {
            var parts = new List<string>();
            if (b.TimedOut) parts.Add("baseline");
            if (s.TimedOut) parts.Add("with-skill");
            Console.WriteLine($"      \x1b[31;1m⏰ TIMEOUT\x1b[0m — {string.Join(" and ", parts)} run(s) hit the scenario timeout limit");
        }

        foreach (var (label, value, absolute, lowerIsBetter) in metrics)
        {
            var color = value > 0 ? "\x1b[32m" : value < 0 ? "\x1b[31m" : "\x1b[2m";
            double displayValue = lowerIsBetter ? -value : value;
            Console.WriteLine($"      \x1b[2m{label,-20}\x1b[0m {color}{FormatDelta(displayValue),-10}\x1b[0m \x1b[2m{absolute}\x1b[0m");
        }

        // Skill activation info
        if (scenario.SkillActivation is { } sa)
        {
            Console.WriteLine();
            if (sa.Activated)
            {
                var parts = new List<string>();
                if (sa.DetectedSkills.Count > 0) parts.Add(string.Join(", ", sa.DetectedSkills));
                if (sa.ExtraTools.Count > 0) parts.Add("extra tools: " + string.Join(", ", sa.ExtraTools));
                Console.WriteLine($"      \x1b[2mSkill activated:\x1b[0m \x1b[32m{(parts.Count > 0 ? string.Join("; ", parts) : "yes")}\x1b[0m");
            }
            else
            {
                Console.WriteLine("      \x1b[33m⚠️  Skill was NOT activated\x1b[0m");
            }
        }

        Console.WriteLine();

        var bj = scenario.Baseline.JudgeResult;
        var sj = scenario.WithSkill.JudgeResult;
        double scoreDelta = sj.OverallScore - bj.OverallScore;
        var deltaStr = scoreDelta > 0 ? $"\x1b[32m+{scoreDelta:F1}\x1b[0m" :
            scoreDelta < 0 ? $"\x1b[31m{scoreDelta:F1}\x1b[0m" : "\x1b[2m±0\x1b[0m";

        var bTimeout = b.TimedOut ? " \x1b[31m⏰ timeout\x1b[0m" : "";
        var sTimeout = s.TimedOut ? " \x1b[31m⏰ timeout\x1b[0m" : "";
        Console.WriteLine($"      \x1b[1mOverall:\x1b[0m {bj.OverallScore:F1}{bTimeout} → {sj.OverallScore:F1}{sTimeout} ({deltaStr})");
        Console.WriteLine();

        // Baseline judge
        Console.WriteLine($"      \x1b[36m─── Baseline Judge\x1b[0m \x1b[36;1m{bj.OverallScore:F1}/5\x1b[0m{bTimeout} \x1b[36m───\x1b[0m");
        Console.WriteLine($"      \x1b[2m{bj.OverallReasoning}\x1b[0m");
        if (bj.RubricScores.Count > 0)
        {
            Console.WriteLine();
            foreach (var rs in bj.RubricScores)
            {
                var scoreColor = rs.Score >= 4 ? "\x1b[32m" : rs.Score >= 3 ? "\x1b[33m" : "\x1b[31m";
                Console.WriteLine($"        {scoreColor}\x1b[1m{rs.Score}/5\x1b[0m  \x1b[1m{rs.Criterion}\x1b[0m");
                if (!string.IsNullOrEmpty(rs.Reasoning))
                    Console.WriteLine($"              \x1b[2m{rs.Reasoning}\x1b[0m");
            }
        }

        Console.WriteLine();

        // With-skill judge
        Console.WriteLine($"      \x1b[35m─── With-Skill Judge\x1b[0m \x1b[35;1m{sj.OverallScore:F1}/5\x1b[0m{sTimeout} \x1b[35m───\x1b[0m");
        Console.WriteLine($"      \x1b[2m{sj.OverallReasoning}\x1b[0m");
        if (sj.RubricScores.Count > 0)
        {
            Console.WriteLine();
            foreach (var rs in sj.RubricScores)
            {
                var scoreColor = rs.Score >= 4 ? "\x1b[32m" : rs.Score >= 3 ? "\x1b[33m" : "\x1b[31m";
                var baselineRs = bj.RubricScores.FirstOrDefault(b =>
                    string.Equals(b.Criterion, rs.Criterion, StringComparison.OrdinalIgnoreCase));
                var comparison = baselineRs is not null ? $"\x1b[2m (was {baselineRs.Score}/5)\x1b[0m" : "";
                Console.WriteLine($"        {scoreColor}\x1b[1m{rs.Score}/5\x1b[0m{comparison}  \x1b[1m{rs.Criterion}\x1b[0m");
                if (!string.IsNullOrEmpty(rs.Reasoning))
                    Console.WriteLine($"              \x1b[2m{rs.Reasoning}\x1b[0m");
            }
        }
        Console.WriteLine();

        // Pairwise judge results
        if (scenario.PairwiseResult is { } pw)
        {
            var consistencyIcon = pw.PositionSwapConsistent
                ? "\x1b[32m✓ consistent\x1b[0m"
                : "\x1b[33m⚠ inconsistent\x1b[0m";
            var winnerColor = pw.OverallWinner == "skill" ? "\x1b[32m" : pw.OverallWinner == "baseline" ? "\x1b[31m" : "\x1b[2m";
            Console.WriteLine($"      \x1b[1m─── Pairwise Comparison\x1b[0m {consistencyIcon} \x1b[1m───\x1b[0m");
            Console.WriteLine($"      Winner: {winnerColor}{pw.OverallWinner}\x1b[0m ({pw.OverallMagnitude})");
            Console.WriteLine($"      \x1b[2m{pw.OverallReasoning}\x1b[0m");
            if (pw.RubricResults.Count > 0)
            {
                Console.WriteLine();
                foreach (var pr in pw.RubricResults)
                {
                    var prColor = pr.Winner == "skill" ? "\x1b[32m" : pr.Winner == "baseline" ? "\x1b[31m" : "\x1b[2m";
                    Console.WriteLine($"        {prColor}\x1b[1m{pr.Winner,-8}\x1b[0m ({pr.Magnitude})  \x1b[1m{pr.Criterion}\x1b[0m");
                    if (!string.IsNullOrEmpty(pr.Reasoning))
                        Console.WriteLine($"              \x1b[2m{pr.Reasoning}\x1b[0m");
                }
            }
            Console.WriteLine();
        }

        if (verbose)
        {
            Console.WriteLine();
            Console.WriteLine("      \x1b[2mBaseline output:\x1b[0m");
            Console.WriteLine(IndentBlock(scenario.Baseline.Metrics.AgentOutput.Length > 0 ? scenario.Baseline.Metrics.AgentOutput : "(no output)", 8));
            Console.WriteLine("      \x1b[2mWith-skill output:\x1b[0m");
            Console.WriteLine(IndentBlock(scenario.WithSkill.Metrics.AgentOutput.Length > 0 ? scenario.WithSkill.Metrics.AgentOutput : "(no output)", 8));
        }
    }

    // --- Markdown reporter ---

    public static string GenerateMarkdownSummary(
        IReadOnlyList<SkillVerdict> verdicts,
        string? model = null,
        string? judgeModel = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Skill Validation Results");
        sb.AppendLine();
        sb.AppendLine("| Skill | Scenario | Baseline | With Skill | Δ | Skills Loaded | Verdict |");
        sb.AppendLine("|-------|----------|----------|------------|---|---------------|--------|");

        foreach (var v in verdicts)
        {
            bool skillNotActivated = v.SkillNotActivated;
            foreach (var s in v.Scenarios)
            {
                var baseScore = s.Baseline?.JudgeResult?.OverallScore;
                var skillScore = s.WithSkill?.JudgeResult?.OverallScore;
                var bTimedOut = s.Baseline?.Metrics?.TimedOut == true;
                var sTimedOut = s.WithSkill?.Metrics?.TimedOut == true;
                var baseStr = (baseScore is { } bs && !double.IsNaN(bs) ? $"{bs:F1}/5" : "—") + (bTimedOut ? " ⏰ timeout" : "");
                var skillStr = (skillScore is { } ss && !double.IsNaN(ss) ? $"{ss:F1}/5" : "—") + (sTimedOut ? " ⏰ timeout" : "");

                string deltaStr = "—";
                if (baseScore is { } b && skillScore is { } sk && !double.IsNaN(b) && !double.IsNaN(sk))
                {
                    double delta = sk - b;
                    if (!double.IsNaN(delta))
                        deltaStr = delta > 0 ? $"+{delta:F1}" : $"{delta:F1}";
                }

                var icon = s.ImprovementScore > 0 ? "✅" : s.ImprovementScore < 0 ? "❌" : "🟡";

                string skillsCol = "—";
                if (s.SkillActivation is { } sa)
                {
                    if (sa.Activated)
                    {
                        var parts = new List<string>();
                        if (sa.DetectedSkills.Count > 0) parts.AddRange(sa.DetectedSkills);
                        if (sa.ExtraTools.Count > 0) parts.Add("tools: " + string.Join(", ", sa.ExtraTools));
                        skillsCol = parts.Count > 0 ? "✅ " + string.Join("; ", parts) : "✅";
                    }
                    else
                    {
                        skillsCol = "⚠️ NOT ACTIVATED";
                    }
                }
                else if (skillNotActivated)
                {
                    skillsCol = "⚠️ NOT ACTIVATED";
                }

                sb.AppendLine($"| {v.SkillName} | {s.ScenarioName} | {baseStr} | {skillStr} | {deltaStr} | {skillsCol} | {icon} |");
            }
        }

        bool anyTimeout = verdicts.Any(v => v.Scenarios.Any(s =>
            (s.Baseline?.Metrics?.TimedOut == true) || (s.WithSkill?.Metrics?.TimedOut == true)));
        if (anyTimeout)
            sb.AppendLine("\n> ⏰ **timeout** — run hit the scenario timeout limit; scoring may be impacted by aborting model execution before it could produce its full output");

        sb.AppendLine($"\nModel: {model ?? "unknown"} | Judge: {judgeModel ?? "unknown"}");
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
                judgeReport.AppendLine("## With-Skill Judge");
                judgeReport.AppendLine($"Overall Score: {scenario.WithSkill.JudgeResult.OverallScore}/5");
                judgeReport.AppendLine($"Reasoning: {scenario.WithSkill.JudgeResult.OverallReasoning}");
                judgeReport.AppendLine();
                foreach (var rs in scenario.WithSkill.JudgeResult.RubricScores)
                    judgeReport.AppendLine($"- **{rs.Criterion}**: {rs.Score}/5 — {rs.Reasoning}");
                judgeReport.AppendLine();
                judgeReport.AppendLine("## Baseline Agent Output");
                judgeReport.AppendLine("```");
                judgeReport.AppendLine(scenario.Baseline.Metrics.AgentOutput.Length > 0 ? scenario.Baseline.Metrics.AgentOutput : "(no output)");
                judgeReport.AppendLine("```");
                judgeReport.AppendLine();
                judgeReport.AppendLine("## With-Skill Agent Output");
                judgeReport.AppendLine("```");
                judgeReport.AppendLine(scenario.WithSkill.Metrics.AgentOutput.Length > 0 ? scenario.WithSkill.Metrics.AgentOutput : "(no output)");
                judgeReport.AppendLine("```");

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
        var output = new
        {
            model = model ?? "unknown",
            judgeModel = judgeModel ?? model ?? "unknown",
            timestamp = DateTime.UtcNow.ToString("o"),
            verdicts,
        };

        var json = JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        await File.WriteAllTextAsync(Path.Combine(resultsDir, "results.json"), json);
        Console.WriteLine($"JSON results written to {Path.Combine(resultsDir, "results.json")}");

        // Write per-skill verdict.json files for downstream consumers (e.g. dashboard)
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        foreach (var verdict in verdicts)
        {
            var skillDir = Path.Combine(resultsDir, SafeDirName(verdict.SkillName));
            Directory.CreateDirectory(skillDir);
            var verdictJson = JsonSerializer.Serialize(verdict, jsonOptions);
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

    private static string FmtBool(bool v) => v ? "✓" : "✗";

    private static string FmtMs(long ms) =>
        ms < 1000 ? $"{ms}ms" : $"{ms / 1000.0:F1}s";

    private static string FormatScore(double score)
    {
        var pct = $"{score * 100:F1}%";
        if (score > 0) return $"\x1b[32m+{pct}\x1b[0m";
        if (score < 0) return $"\x1b[31m{pct}\x1b[0m";
        return $"\x1b[2m{pct}\x1b[0m";
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
