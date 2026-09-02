using System.CommandLine;
using System.Text.Json;
using SkillValidator.Shared;

namespace SkillValidator.Evaluate;

/// <summary>
/// Standalone overfitting analysis command. The overfitting judge is
/// engine-independent — it needs only SKILL.md + eval.yaml + one LLM call per
/// skill — so it is retained as its own component even though the rest of the
/// evaluation pipeline moved to the external Vally harness. The emitted JSON is
/// merged back into the Vally adapter's per-skill results via
/// <c>adapt.mjs --overfitting</c>.
/// </summary>
public static class OverfittingCommand
{
    public sealed record OverfittingEntry(string Plugin, string Skill, OverfittingResult OverfittingResult);

    public static Command Create()
    {
        var pathsArg = new Argument<string[]>("paths") { Description = "Paths to skill directories or parent directories", Arity = ArgumentArity.OneOrMore };
        var testsDirOpt = new Option<string>("--tests-dir") { Description = "Directory containing test subdirectories (nested layout tests/<plugin>/<skill>/eval.yaml)", Required = true };
        var modelOpt = new Option<string>("--model") { Description = "Model to use for the overfitting judge", DefaultValueFactory = _ => "claude-opus-4.6" };
        var judgeTimeoutOpt = new Option<int>("--judge-timeout") { Description = "Judge timeout in seconds", DefaultValueFactory = _ => 300 };
        var outputOpt = new Option<string>("--output") { Description = "Path to write the JSON result array", Required = true };
        var verboseOpt = new Option<bool>("--verbose") { Description = "Show detailed output" };

        var command = new Command("overfitting", "Run LLM-based overfitting analysis on skills (no agent runs required). Writes a JSON array of per-skill overfitting results.")
        {
            pathsArg,
            testsDirOpt,
            modelOpt,
            judgeTimeoutOpt,
            outputOpt,
            verboseOpt,
        };

        command.SetAction(async (parseResult, ct) =>
        {
            var paths = parseResult.GetValue(pathsArg) ?? [];
            var testsDir = parseResult.GetValue(testsDirOpt)!;
            var model = parseResult.GetValue(modelOpt) ?? "claude-opus-4.6";
            var judgeTimeoutSeconds = Math.Max(1, parseResult.GetValue(judgeTimeoutOpt));
            var output = parseResult.GetValue(outputOpt)!;
            var verbose = parseResult.GetValue(verboseOpt);

            return await Run(paths, testsDir, model, judgeTimeoutSeconds, output, verbose, ct);
        });

        return command;
    }

    public static async Task<int> Run(
        string[] paths,
        string testsDir,
        string model,
        int judgeTimeoutSeconds,
        string output,
        bool verbose,
        CancellationToken ct = default)
    {
        // Discover skills across all paths (skills-only; agents have no overfitting concept).
        var discoveredSkills = new List<SkillInfo>();
        foreach (var path in paths)
        {
            if (path.EndsWith(".agent.md", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Directory.Exists(path) && Directory.GetFiles(path, "*.agent.md").Length > 0 && !File.Exists(Path.Combine(path, "SKILL.md")))
                continue;

            discoveredSkills.AddRange(await SkillDiscovery.DiscoverSkills(path));
        }

        if (discoveredSkills.Count == 0)
        {
            var searched = string.Join(", ", paths.Select(p => $"\"{Path.GetFullPath(p)}\""));
            Console.Error.WriteLine($"No skills found in the specified paths: {searched}");
            return 1;
        }

        Console.WriteLine($"Found {discoveredSkills.Count} skill(s)");

        // Resolve + parse each skill's eval.yaml independently so a single
        // malformed or unrecognized eval is skipped with a warning instead of
        // throwing and crashing the whole command. Overfitting is an
        // informational feature — it must never break CI. ParseEvalConfigFlexible
        // reads the current Vally-native format (stimuli/graders) as well as the
        // legacy scenarios format.
        var evalSkills = new List<EvalSkillInfo>();
        foreach (var skill in discoveredSkills)
        {
            var evalPath = EvaluateCommand.ResolveEvalPath(skill.Path, testsDir);
            if (evalPath is null || !File.Exists(evalPath))
                continue;

            try
            {
                var content = await File.ReadAllTextAsync(evalPath, ct);
                var cfg = EvalSchema.ParseEvalConfigFlexible(content);
                if (cfg is null)
                {
                    Console.Error.WriteLine($"⚠️  {skill.Name}: skipping — eval.yaml has no recognizable stimuli/scenarios");
                    continue;
                }

                evalSkills.Add(new EvalSkillInfo(skill, evalPath, cfg));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"⚠️  {skill.Name}: skipping — failed to load eval data: {ex.Message}");
            }
        }

        var workDir = Path.GetTempPath();
        var options = new OverfittingJudgeOptions(model, verbose, judgeTimeoutSeconds * 1000, workDir);

        var entries = new List<OverfittingEntry>();
        var gate = new object();

        using var limiter = new SemaphoreSlim(3);
        var tasks = evalSkills
            .Where(es => es.EvalConfig is not null && es.EvalPath is not null)
            .Select(async evalSkill =>
            {
                await limiter.WaitAsync(ct);
                try
                {
                    var (plugin, skill) = DeriveIdentity(evalSkill.EvalPath!);
                    try
                    {
                        var result = await OverfittingJudge.Analyze(evalSkill, options, ct);
                        if (result is not null)
                        {
                            lock (gate)
                                entries.Add(new OverfittingEntry(plugin, skill, result));
                            Console.WriteLine($"🔍 {plugin}/{skill}: {result.Score:F2} ({result.Severity})");
                        }
                        else if (verbose)
                        {
                            Console.WriteLine($"🔍 {plugin}/{skill}: no result (judge returned null)");
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        Console.Error.WriteLine($"⚠️  {plugin}/{skill}: overfitting analysis failed: {ex.Message}");
                    }
                }
                finally
                {
                    limiter.Release();
                }
            });

        await Task.WhenAll(tasks);

        var outputDir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        var json = JsonSerializer.Serialize(entries, SkillValidatorJsonContext.Default.ListOverfittingEntry);
        await File.WriteAllTextAsync(output, json, ct);

        Console.WriteLine($"Wrote {entries.Count} overfitting result(s) to {output}");

        // Informational feature — never break CI on a per-skill failure or empty result.
        return 0;
    }

    /// <summary>
    /// Derive (plugin, skill) from an eval path of the form
    /// <c>tests/&lt;plugin&gt;/&lt;skill&gt;/eval.yaml</c>. Must exactly match the
    /// Vally adapter's <c>evalIdentity()</c> convention so results merge correctly.
    /// </summary>
    internal static (string Plugin, string Skill) DeriveIdentity(string evalPath)
    {
        var skillDir = Path.GetDirectoryName(evalPath);
        var skill = Path.GetFileName(skillDir) ?? string.Empty;
        var pluginDir = Path.GetDirectoryName(skillDir);
        var plugin = Path.GetFileName(pluginDir) ?? string.Empty;
        return (plugin, skill);
    }
}
