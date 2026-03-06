using System.Text.RegularExpressions;
using SkillValidator.Models;

namespace SkillValidator.Services;

public static partial class AgentProfiler
{
    private const int MaxBodyLines = 500;

    public static AgentProfile AnalyzeAgent(AgentInfo agent)
    {
        var content = agent.AgentMdContent;
        var errors = new List<string>();
        var warnings = new List<string>();

        bool hasFrontmatter = FrontmatterRegex().IsMatch(content);
        if (!hasFrontmatter)
        {
            errors.Add("Agent file has no YAML frontmatter — agents require frontmatter for IDE discovery.");
            return new AgentProfile(agent.Name, agent.FileName, errors, warnings);
        }

        // Name validation
        if (string.IsNullOrWhiteSpace(agent.Name))
        {
            errors.Add("Agent frontmatter has no 'name' field — required for agent identification.");
        }
        else
        {
            // Expected filename: {name}.agent.md
            var expectedFileName = agent.Name + ".agent.md";
            if (!string.Equals(expectedFileName, agent.FileName, StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Agent name '{agent.Name}' does not match filename '{agent.FileName}' (expected '{expectedFileName}').");

            SkillProfiler.ValidateName(agent.Name, agent.Name, warnings);
        }

        // Description validation
        if (string.IsNullOrWhiteSpace(agent.Description))
        {
            errors.Add("Agent frontmatter has no 'description' field — required for agent discovery.");
        }
        else if (agent.Description.Length > SkillProfiler.MaxDescriptionLength)
        {
            errors.Add($"Agent description is {agent.Description.Length:N0} characters — maximum is {SkillProfiler.MaxDescriptionLength:N0}.");
        }

        // Body line count
        var body = FrontmatterStripRegex().Replace(content, "");
        var trimmedBody = body.TrimEnd('\r', '\n');
        int bodyLineCount = trimmedBody.Length == 0 ? 0 : trimmedBody.Split('\n').Length;
        if (bodyLineCount > MaxBodyLines)
        {
            errors.Add($"Agent body is {bodyLineCount} lines — maximum is {MaxBodyLines}. Keep agent instructions concise.");
        }

        return new AgentProfile(agent.Name, agent.FileName, errors, warnings);
    }

    [GeneratedRegex(@"^---\r?\n[\s\S]*?\r?\n---", RegexOptions.Multiline)]
    private static partial Regex FrontmatterRegex();

    [GeneratedRegex(@"^---\r?\n[\s\S]*?\r?\n---\r?\n?")]
    private static partial Regex FrontmatterStripRegex();
}
