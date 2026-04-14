using SkillValidator.Shared;

namespace SkillValidator.Check;

/// <summary>
/// Validates .agent.md files against the agent plugin conventions.
/// See: https://code.visualstudio.com/docs/copilot/customization/custom-agents
/// See: https://code.claude.com/docs/en/plugins-reference (Agents section)
/// </summary>
public static class AgentProfiler
{
    public static AgentProfile AnalyzeAgent(AgentInfo agent)
    {
        var content = agent.AgentMdContent;
        var errors = new List<string>();
        var warnings = new List<string>();

        // Use fileName as fallback identifier when name is empty (e.g. missing frontmatter).
        var profileName = !string.IsNullOrWhiteSpace(agent.Name) ? agent.Name : agent.FileName;

        var (yaml, body) = FrontmatterParser.SplitFrontmatter(content);
        if (yaml is null)
        {
            errors.Add("Agent file has no YAML frontmatter — agents require frontmatter for IDE discovery.");
            return new AgentProfile(profileName, agent.FileName, errors, warnings);
        }

        // --- Name validation ---
        if (string.IsNullOrWhiteSpace(agent.Name))
        {
            errors.Add("Agent frontmatter has no 'name' field — required for agent identification.");
        }
        else
        {
            // Agent filename convention: {name}.agent.md
            var expectedFileName = agent.Name + ".agent.md";
            if (!string.Equals(expectedFileName, agent.FileName, StringComparison.Ordinal))
                errors.Add($"Agent name '{agent.Name}' does not match filename '{agent.FileName}' (expected '{expectedFileName}').");

            // Validate name format (lowercase, hyphens, length) per agentskills.io naming rules.
            // Directory-match is not checked — agents use filename convention, not directory naming.
            // Spec uses "Must" for all name constraints, so violations are errors.
            SkillProfiler.ValidateNameFormat(agent.Name, "Agent", errors);
        }

        // --- Description validation (same 1024-char limit as skills) ---
        // https://agentskills.io/specification#description-field
        SkillProfiler.ValidateDescription(agent.Description, "Agent", errors);

        return new AgentProfile(profileName, agent.FileName, errors, warnings);
    }
}
