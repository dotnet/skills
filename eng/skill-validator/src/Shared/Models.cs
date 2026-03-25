namespace SkillValidator.Shared;

// --- MCP server definition (from plugin.json) ---

public sealed record MCPServerDef(
    string Command,
    string[] Args,
    string? Type = null,
    string[]? Tools = null,
    Dictionary<string, string>? Env = null,
    string? Cwd = null);

// --- Skill info ---

public sealed record SkillInfo(
    string Name,
    string Description,
    string Path,
    string SkillMdPath,
    string SkillMdContent,
    string? Compatibility = null);

// --- Agent info ---

public sealed record AgentInfo(
    string Name,
    string Description,
    string Path,
    string AgentMdContent,
    string FileName,
    IReadOnlyList<string>? Tools = null);

// --- Plugin info ---

public sealed record PluginInfo(
    string Name,
    string? Version,
    string? Description,
    IReadOnlyList<string> SkillPaths,
    IReadOnlyList<string> AgentPaths,
    string DirectoryPath,
    string DirectoryName);

// --- Frontmatter ---

public sealed record SkillFrontmatter
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Compatibility { get; set; }
}

public sealed record AgentFrontmatter
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<string>? Tools { get; set; }
}
