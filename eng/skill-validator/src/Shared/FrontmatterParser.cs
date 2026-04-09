using System.Text.RegularExpressions;

namespace SkillValidator.Shared;

/// <summary>
/// Shared frontmatter parsing for SKILL.md and .agent.md files.
/// </summary>
public static partial class FrontmatterParser
{
    [GeneratedRegex(@"^---\r?\n([\s\S]*?)\r?\n---\r?\n([\s\S]*)$")]
    private static partial Regex FrontmatterRegex();

    /// <summary>
    /// Splits markdown content into frontmatter YAML and body.
    /// Returns null yaml when no frontmatter is present.
    /// </summary>
    public static (string? Yaml, string Body) SplitFrontmatter(string content)
    {
        var match = FrontmatterRegex().Match(content);
        if (!match.Success)
            return (null, content);

        return (match.Groups[1].Value, match.Groups[2].Value);
    }
}
