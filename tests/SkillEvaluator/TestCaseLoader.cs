using System.Text.Json;
using System.Text.RegularExpressions;

namespace SkillEvaluator;

/// <summary>
/// Loads test-case files and rubrics from the tests/ directory structure.
/// </summary>
public static class TestCaseLoader
{
    /// <summary>
    /// Load README.md, good.md, and bad.md for a given skill name.
    /// </summary>
    public static (string Readme, string Good, string Bad) LoadTestCase(string testsRoot, string skillName)
    {
        var dir = Path.Combine(testsRoot, skillName);
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Test directory not found: {dir}");

        return (
            File.ReadAllText(Path.Combine(dir, "README.md")),
            File.ReadAllText(Path.Combine(dir, "good.md")),
            File.ReadAllText(Path.Combine(dir, "bad.md"))
        );
    }

    /// <summary>
    /// Extract the input prompt from a README.md that has an "## Input prompt" section.
    /// </summary>
    public static string ExtractPrompt(string readmeText)
    {
        var match = Regex.Match(readmeText, @"## Input prompt\s*\r?\n\r?\n(.+)$", RegexOptions.Singleline);
        if (!match.Success)
            throw new InvalidOperationException("Could not find '## Input prompt' section in README.md");
        return match.Groups[1].Value.Trim();
    }

    /// <summary>
    /// Load a skill-specific rubric.json if it exists, otherwise fall back to universal-rubric.json.
    /// </summary>
    public static Rubric LoadRubric(string testsRoot, string skillName)
    {
        var skillRubric = Path.Combine(testsRoot, skillName, "rubric.json");
        var universalRubric = Path.Combine(testsRoot, "universal-rubric.json");

        var path = File.Exists(skillRubric) ? skillRubric : universalRubric;
        if (!File.Exists(path))
            throw new FileNotFoundException($"No rubric found. Create {universalRubric}");

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Rubric>(json)
            ?? throw new InvalidOperationException("Failed to deserialize rubric");
    }

    /// <summary>
    /// Load the prompt quality rubric from tests/prompt-quality-rubric.json.
    /// </summary>
    public static Rubric LoadPromptQualityRubric(string testsRoot)
    {
        var path = Path.Combine(testsRoot, "prompt-quality-rubric.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Prompt quality rubric not found: {path}");

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Rubric>(json)
            ?? throw new InvalidOperationException("Failed to deserialize prompt quality rubric");
    }

    /// <summary>
    /// Load the SKILL.md file for a given skill from the skills/ directory (sibling of tests/).
    /// </summary>
    public static string? LoadSkillPrompt(string testsRoot, string skillName)
    {
        // skills/ is a sibling directory of tests/
        var repoRoot = Path.GetDirectoryName(testsRoot)!;
        var skillMd = Path.Combine(repoRoot, "skills", skillName, "SKILL.md");
        return File.Exists(skillMd) ? File.ReadAllText(skillMd) : null;
    }

    /// <summary>
    /// Discover all skill names that have a tests/ directory with README.md + good.md + bad.md.
    /// </summary>
    public static IEnumerable<string> DiscoverSkills(string testsRoot)
    {
        foreach (var dir in Directory.GetDirectories(testsRoot))
        {
            var name = Path.GetFileName(dir);
            if (name == "SkillEvaluator") continue; // skip self

            if (File.Exists(Path.Combine(dir, "README.md")) &&
                File.Exists(Path.Combine(dir, "good.md")) &&
                File.Exists(Path.Combine(dir, "bad.md")))
            {
                yield return name;
            }
        }
    }
}
