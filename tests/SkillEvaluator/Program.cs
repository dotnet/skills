using SkillEvaluator;

// ── Parse CLI arguments ────────────────────────────────────────────────

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
var command = cliArgs.FirstOrDefault() ?? "help";

// Resolve the tests/ root relative to the executing assembly.
var testsRoot = FindTestsRoot();
var model = GetArg(cliArgs, "--model") ?? "claude-opus-4.6";

switch (command)
{
    case "evaluate":
        await RunEvaluateAsync(cliArgs, testsRoot, model);
        break;

    case "calibrate":
        await RunCalibrateAsync(cliArgs, testsRoot, model);
        break;

    case "evaluate-all":
        await RunEvaluateAllAsync(cliArgs, testsRoot, model);
        break;

    case "calibrate-all":
        await RunCalibrateAllAsync(testsRoot, model);
        break;

    case "list":
        RunList(testsRoot);
        break;

    default:
        PrintUsage();
        break;
}

// ── Commands ──────────────────────────────────────────────────────────

async Task RunEvaluateAsync(string[] cliArgs, string root, string judgeModel)
{
    var skill = GetArg(cliArgs, "--skill") ?? throw new ArgumentException("--skill is required");
    var transcript = GetArg(cliArgs, "--transcript") ?? throw new ArgumentException("--transcript is required");

    var (readme, good, bad) = TestCaseLoader.LoadTestCase(root, skill);
    var prompt = TestCaseLoader.ExtractPrompt(readme);
    var rubric = TestCaseLoader.LoadRubric(root, skill);
    var candidateText = File.ReadAllText(transcript);

    await using var judge = new JudgeEvaluator(judgeModel);
    var result = await judge.EvaluateAsync(prompt, rubric, good, bad, candidateText);

    PrintResult(skill, result);

    var output = GetArg(cliArgs, "--output");
    if (output is not null)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(output, json);
        Console.WriteLine($"\nResults written to {output}");
    }

    Environment.ExitCode = result.Pass ? 0 : 1;
}

async Task RunCalibrateAsync(string[] cliArgs, string root, string judgeModel)
{
    var skill = GetArg(cliArgs, "--skill") ?? throw new ArgumentException("--skill is required");

    var (readme, good, bad) = TestCaseLoader.LoadTestCase(root, skill);
    var prompt = TestCaseLoader.ExtractPrompt(readme);
    var rubric = TestCaseLoader.LoadRubric(root, skill);

    Console.WriteLine($"Calibrating rubric for: {skill}");
    Console.WriteLine(new string('=', 60));

    await using var judge = new JudgeEvaluator(judgeModel);
    var (goodResult, badResult) = await judge.CalibrateAsync(prompt, rubric, good, bad);

    Console.WriteLine($"\nGOOD transcript score: {goodResult.WeightedScore:F2} (expect >= 0.85)");
    foreach (var s in goodResult.Scores)
        Console.WriteLine($"  {s.Criterion}: {s.Score:F1} — {s.Justification}");

    Console.WriteLine($"\nBAD transcript score: {badResult.WeightedScore:F2} (expect <= 0.40)");
    foreach (var s in badResult.Scores)
        Console.WriteLine($"  {s.Criterion}: {s.Score:F1} — {s.Justification}");

    var goodOk = goodResult.WeightedScore >= 0.85;
    var badOk = badResult.WeightedScore <= 0.40;
    var gap = goodResult.WeightedScore - badResult.WeightedScore;

    Console.WriteLine($"\n{new string('=', 60)}");
    Console.WriteLine($"Good >= 0.85: {(goodOk ? "PASS" : "FAIL")}");
    Console.WriteLine($"Bad  <= 0.40: {(badOk ? "PASS" : "FAIL")}");
    Console.WriteLine($"Score gap:    {gap:F2} (larger is better, want >= 0.45)");
    Console.WriteLine($"Calibration:  {(goodOk && badOk ? "PASS" : "NEEDS ADJUSTMENT")}");

    // Prompt quality grading
    var skillMd = TestCaseLoader.LoadSkillPrompt(root, skill);
    if (skillMd is not null)
    {
        Console.WriteLine($"\n{new string('-', 60)}");
        Console.WriteLine("PROMPT QUALITY (SKILL.md)");
        Console.WriteLine(new string('-', 60));

        var promptRubric = TestCaseLoader.LoadPromptQualityRubric(root);
        Console.WriteLine("  Grading SKILL.md...");
        var promptGrade = await judge.GradePromptAsync(skillMd, promptRubric);
        var promptOk = promptGrade.Pass;

        Console.WriteLine($"\n  Prompt quality score: {promptGrade.WeightedScore:F2} (threshold >= 0.75)");
        foreach (var s in promptGrade.Scores)
            Console.WriteLine($"    {s.Criterion}: {s.Score:F1} — {s.Justification}");
        Console.WriteLine($"\n  Prompt quality: {(promptOk ? "PASS" : "NEEDS IMPROVEMENT")}");
        Console.WriteLine($"  Summary: {promptGrade.Summary}");
    }

    Environment.ExitCode = (goodOk && badOk) ? 0 : 1;
}

async Task RunEvaluateAllAsync(string[] cliArgs, string root, string judgeModel)
{
    var transcriptsDir = GetArg(cliArgs, "--transcripts-dir")
        ?? throw new ArgumentException("--transcripts-dir is required");

    var skills = TestCaseLoader.DiscoverSkills(root).ToList();
    var results = new List<SkillResult>();

    await using var judge = new JudgeEvaluator(judgeModel);

    foreach (var skill in skills.Order())
    {
        var transcriptPath = Path.Combine(transcriptsDir, $"{skill}.md");
        if (!File.Exists(transcriptPath))
        {
            Console.WriteLine($"  [SKIP] {skill}: no transcript at {transcriptPath}");
            continue;
        }

        try
        {
            Console.WriteLine($"  Evaluating {skill}...");
            var (readme, good, bad) = TestCaseLoader.LoadTestCase(root, skill);
            var prompt = TestCaseLoader.ExtractPrompt(readme);
            var rubric = TestCaseLoader.LoadRubric(root, skill);
            var candidateText = File.ReadAllText(transcriptPath);

            var result = await judge.EvaluateAsync(prompt, rubric, good, bad, candidateText);
            results.Add(new SkillResult { SkillName = skill, Result = result });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [ERROR] {skill}: {ex.Message}");
            results.Add(new SkillResult { SkillName = skill, Error = ex.Message });
        }
    }

    PrintSummary(results);

    var output = GetArg(cliArgs, "--output");
    if (output is not null)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(output, json);
        Console.WriteLine($"\nResults written to {output}");
    }

    var allPass = results.All(r => r.Result?.Pass == true);
    Environment.ExitCode = allPass ? 0 : 1;
}

async Task RunCalibrateAllAsync(string root, string judgeModel)
{
    var skills = TestCaseLoader.DiscoverSkills(root).ToList();
    Console.WriteLine($"Calibrating {skills.Count} skills with model: {judgeModel}");
    Console.WriteLine(new string('=', 60));

    Rubric? promptRubric = null;
    try { promptRubric = TestCaseLoader.LoadPromptQualityRubric(root); }
    catch { /* prompt quality rubric not available */ }

    await using var judge = new JudgeEvaluator(judgeModel);
    var passed = 0;
    var total = 0;
    var promptScores = new List<(string Skill, double Score, bool Pass)>();

    foreach (var skill in skills.Order())
    {
        total++;
        Console.WriteLine($"\n[{total}/{skills.Count}] {skill}");

        try
        {
            var (readme, good, bad) = TestCaseLoader.LoadTestCase(root, skill);
            var prompt = TestCaseLoader.ExtractPrompt(readme);
            var rubric = TestCaseLoader.LoadRubric(root, skill);

            var (goodResult, badResult) = await judge.CalibrateAsync(prompt, rubric, good, bad);
            var goodOk = goodResult.WeightedScore >= 0.85;
            var badOk = badResult.WeightedScore <= 0.40;

            Console.WriteLine($"  Good: {goodResult.WeightedScore:F2} {(goodOk ? "OK" : "LOW")} | Bad: {badResult.WeightedScore:F2} {(badOk ? "OK" : "HIGH")} | Gap: {goodResult.WeightedScore - badResult.WeightedScore:F2}");

            if (goodOk && badOk) passed++;

            // Prompt quality grading
            if (promptRubric is not null)
            {
                var skillMd = TestCaseLoader.LoadSkillPrompt(root, skill);
                if (skillMd is not null)
                {
                    Console.WriteLine("  Grading SKILL.md...");
                    var promptGrade = await judge.GradePromptAsync(skillMd, promptRubric);
                    promptScores.Add((skill, promptGrade.WeightedScore, promptGrade.Pass));
                    Console.WriteLine($"  Prompt quality: {promptGrade.WeightedScore:F2} {(promptGrade.Pass ? "OK" : "LOW")}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ERROR: {ex.Message}");
        }
    }

    Console.WriteLine($"\n{new string('=', 60)}");
    Console.WriteLine($"Calibration: {passed}/{total} skills passed ({(total > 0 ? passed * 100 / total : 0)}%)");

    if (promptScores.Count > 0)
    {
        var promptPassed = promptScores.Count(p => p.Pass);
        Console.WriteLine($"Prompt quality: {promptPassed}/{promptScores.Count} skills passed ({(promptScores.Count > 0 ? promptPassed * 100 / promptScores.Count : 0)}%)");
        Console.WriteLine(new string('-', 60));
        foreach (var (skill, score, ok) in promptScores.OrderBy(p => p.Skill))
            Console.WriteLine($"  {(ok ? "OK" : "LOW")} {skill}: {score:F2}");
    }

    Environment.ExitCode = (passed == total) ? 0 : 1;
}

void RunList(string root)
{
    Console.WriteLine("Available skill test cases:");
    Console.WriteLine();
    foreach (var skill in TestCaseLoader.DiscoverSkills(root).Order())
        Console.WriteLine($"  {skill}");
}

// ── Helpers ────────────────────────────────────────────────────────────

void PrintResult(string skill, EvaluationResult result)
{
    var status = result.Pass ? "PASS" : "FAIL";
    Console.WriteLine($"\n[{status}] {skill}: {result.WeightedScore:F2} ({result.Classification})");
    Console.WriteLine();

    foreach (var s in result.Scores)
    {
        var indicator = s.Score >= 1.0 ? "+" : s.Score >= 0.5 ? "~" : "-";
        Console.WriteLine($"  [{indicator}] {s.Criterion}: {s.Score:F1} — {s.Justification}");
    }

    Console.WriteLine();
    Console.WriteLine($"Summary: {result.Summary}");
}

void PrintSummary(List<SkillResult> results)
{
    var total = results.Count;
    var passed = results.Count(r => r.Result?.Pass == true);

    Console.WriteLine($"\n{new string('=', 60)}");
    Console.WriteLine($"RESULTS: {passed}/{total} skills passed ({(total > 0 ? passed * 100 / total : 0)}%)");
    Console.WriteLine(new string('=', 60));

    foreach (var sr in results.OrderBy(r => r.SkillName))
    {
        if (sr.Error is not null)
        {
            Console.WriteLine($"  [ERROR] {sr.SkillName}: {sr.Error}");
        }
        else
        {
            var status = sr.Result!.Pass ? "PASS" : "FAIL";
            Console.WriteLine($"  [{status}] {sr.SkillName}: {sr.Result.WeightedScore:F2}");
        }
    }
}

string? GetArg(string[] cliArgs, string name)
{
    for (var i = 0; i < cliArgs.Length - 1; i++)
    {
        if (cliArgs[i] == name)
            return cliArgs[i + 1];
    }
    return null;
}

string FindTestsRoot()
{
    // Walk up from the executable to find the tests/ directory.
    var dir = AppContext.BaseDirectory;
    for (var i = 0; i < 10; i++)
    {
        var candidate = Path.Combine(dir, "tests");
        if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "universal-rubric.json")))
            return candidate;

        // Also check if dir itself is the tests root.
        if (File.Exists(Path.Combine(dir, "universal-rubric.json")))
            return dir;

        dir = Path.GetDirectoryName(dir)!;
    }

    // Fallback: assume CWD is the repo root.
    var cwd = Environment.CurrentDirectory;
    var testsDir = Path.Combine(cwd, "tests");
    if (Directory.Exists(testsDir))
        return testsDir;

    return cwd;
}

void PrintUsage()
{
    Console.WriteLine("""
        SkillEvaluator — LLM-as-a-Judge for .NET Agent Skills (powered by GitHub Copilot SDK)

        USAGE:
          SkillEvaluator <command> [options]

        COMMANDS:
          evaluate         Evaluate a single skill transcript against good/bad references
          calibrate        Run calibration to verify a rubric separates good from bad
          evaluate-all     Evaluate transcripts for all skills in a directory
          calibrate-all    Calibrate all discovered skills
          list             List all available skill test cases
          help             Show this help message

        OPTIONS:
          --skill <name>            Skill name (e.g. debugging-memory-leaks)
          --transcript <path>       Path to the transcript file to evaluate
          --transcripts-dir <path>  Directory of transcripts (for evaluate-all)
          --model <model>           Judge model (default: claude-opus-4.6)
          --output <path>           Write JSON results to file

        EXAMPLES:
          # List available skills
          dotnet run -- list

          # Calibrate a single skill
          dotnet run -- calibrate --skill debugging-memory-leaks

          # Calibrate all skills
          dotnet run -- calibrate-all

          # Evaluate a transcript
          dotnet run -- evaluate --skill debugging-memory-leaks --transcript ./candidate.md

          # Evaluate all transcripts with a specific model
          dotnet run -- evaluate-all --transcripts-dir ./transcripts --model claude-sonnet-4.5 --output results.json

        AUTHENTICATION:
          The evaluator uses the GitHub Copilot SDK, which authenticates via the Copilot CLI.
          Ensure you are logged in: `copilot auth login`
          Or use BYOK by setting OPENAI_API_KEY and passing --model with a provider override.
        """);
}
