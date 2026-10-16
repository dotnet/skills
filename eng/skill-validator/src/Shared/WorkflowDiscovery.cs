using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace SkillValidator.Shared;

public sealed record WorkflowInfo(
    string Name,
    string ManifestPath,
    AgentInfo Agent,
    IReadOnlyList<AgentInfo> Agents,
    IReadOnlyDictionary<string, byte[]> Resources,
    string ContentSha);

public static partial class WorkflowDiscovery
{
    public sealed record Manifest
    {
        [YamlMember(Alias = "manifest-version", ApplyNamingConventions = false)]
        public string? ManifestVersion { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<string>? Includes { get; set; }
    }

    public sealed record Frontmatter
    {
        public List<string>? Imports { get; set; }
        public Dictionary<string, Grader>? Graders { get; set; }
    }

    public sealed record Grader
    {
        public string? Run { get; set; }
    }

    [GeneratedRegex(@"(?m)^on:\s")]
    private static partial Regex TriggerRegex();

    [GeneratedRegex(@"\$\{\{\s*(.*?)\s*\}\}")]
    private static partial Regex ExpressionRegex();

    public static async Task<WorkflowInfo> Load(string manifestPath)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        var root = Path.GetDirectoryName(manifestPath)!;
        if (PathSafety.ContainsReparsePoint(root, manifestPath))
            throw new InvalidOperationException($"Unsafe workflow manifest: {manifestPath}");
        var manifest = SkillValidatorYamlContext.UnderscoredDeserializer.Deserialize<Manifest>(
            await File.ReadAllTextAsync(manifestPath));
        if (manifest?.ManifestVersion != "1")
            throw new InvalidOperationException($"Unsupported workflow manifest version: {manifest?.ManifestVersion ?? "missing"}");
        if (manifest?.Includes is not { Count: > 0 })
            throw new InvalidOperationException($"Workflow package has no includes: {manifestPath}");

        var resources = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var include in manifest.Includes)
        {
            var source = ResolveFile(root, include);
            var destination = InstalledPath(include);
            var bytes = await File.ReadAllBytesAsync(source);
            if (!resources.TryAdd(destination, bytes))
                throw new InvalidOperationException($"Duplicate workflow resource: {destination}");
        }

        var workflowFiles = resources.Keys.Where(path =>
            path.StartsWith(".github/workflows/", StringComparison.Ordinal)
            && path.EndsWith(".md", StringComparison.Ordinal)).ToList();
        var entries = workflowFiles.Where(path =>
        {
            var (yaml, _) = FrontmatterParser.SplitFrontmatter(Encoding.UTF8.GetString(resources[path]));
            return yaml is not null && TriggerRegex().IsMatch(yaml);
        }).ToList();
        if (entries.Count != 1)
            throw new InvalidOperationException(
                $"Workflow evaluation requires exactly one entry workflow, found {entries.Count}: {manifestPath}");

        // Graders are auto-installed by gh-aw even when not listed in includes.
        foreach (var path in workflowFiles)
        {
            var metadata = Metadata(resources[path]);
            foreach (var grader in metadata.Graders?.Values.AsEnumerable() ?? [])
            {
                if (grader.Run is not { Length: > 0 } evaluator)
                    continue;
                var local = evaluator.StartsWith("./", StringComparison.Ordinal);
                if (!local && !evaluator.StartsWith(".github/graders/", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Unsupported repository grader location: {evaluator}");
                var repositoryRoot = Directory.GetParent(root)?.Name == "agentic-workflows"
                    ? Directory.GetParent(root)!.Parent!.FullName
                    : throw new InvalidOperationException("Repository-relative graders require an agentic-workflows package");
                var sourceRoot = local
                    ? Path.Combine(root, Path.GetDirectoryName(path[".github/".Length..])!)
                    : repositoryRoot;
                var source = ResolveFile(sourceRoot, local ? evaluator[2..] : evaluator);
                var destination = local
                    ? Path.Combine(Path.GetDirectoryName(path)!, evaluator[2..]).Replace('\\', '/')
                    : evaluator;
                ValidateRelativePath(destination);
                var bytes = await File.ReadAllBytesAsync(source);
                if (resources.TryGetValue(destination, out var existing)
                    && !existing.AsSpan().SequenceEqual(bytes))
                    throw new InvalidOperationException($"Conflicting workflow grader: {destination}");
                resources[destination] = bytes;
            }
        }

        var body = ExpandPrompt(entries[0], resources, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var agents = new List<AgentInfo>();
        foreach (var resource in resources.Where(item => item.Key.StartsWith(".github/agents/", StringComparison.Ordinal)
            && item.Key.EndsWith(".agent.md", StringComparison.Ordinal)))
        {
            var content = Encoding.UTF8.GetString(resource.Value);
            var (metadata, _) = AgentDiscovery.ParseAgentFrontmatter(content);
            if (string.IsNullOrWhiteSpace(metadata.Name))
                throw new InvalidOperationException($"Packaged agent has no name: {resource.Key}");
            agents.Add(new AgentInfo(metadata.Name, metadata.Description ?? "", resource.Key, content,
                Path.GetFileName(resource.Key), metadata.Tools, metadata.Agents));
        }
        if (agents.Select(agent => agent.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != agents.Count)
            throw new InvalidOperationException("Workflow package contains duplicate agent names");

        var name = Path.GetFileName(root);
        var agent = new AgentInfo($"workflow.{name}", manifest.Description ?? manifest.Name ?? name,
            manifestPath, body, $"workflow.{name}.agent.md");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(await File.ReadAllBytesAsync(manifestPath));
        foreach (var resource in resources.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(resource.Key));
            hash.AppendData([0]);
            hash.AppendData(SHA256.HashData(resource.Value));
        }
        return new WorkflowInfo(name, manifestPath, agent, agents, resources,
            Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static Frontmatter Metadata(byte[] bytes)
    {
        var (yaml, _) = FrontmatterParser.SplitFrontmatter(Encoding.UTF8.GetString(bytes));
        return yaml is null ? new Frontmatter()
            : SkillValidatorYamlContext.UnderscoredDeserializer.Deserialize<Frontmatter>(yaml) ?? new Frontmatter();
    }

    private static string ExpandPrompt(
        string path, IReadOnlyDictionary<string, byte[]> resources, HashSet<string> visiting)
    {
        if (!visiting.Add(path))
            throw new InvalidOperationException($"Workflow import cycle: {path}");
        if (!resources.TryGetValue(path, out var bytes))
            throw new InvalidOperationException($"Workflow import is not declared by the package: {path}");
        var body = new StringBuilder();
        foreach (var import in Metadata(bytes).Imports ?? [])
        {
            ValidateRelativePath(import);
            var relative = Path.GetRelativePath(".",
                Path.Combine(Path.GetDirectoryName(path)!, import)).Replace('\\', '/');
            body.AppendLine(ExpandPrompt(relative, resources, visiting));
        }
        body.AppendLine(FrontmatterParser.SplitFrontmatter(Encoding.UTF8.GetString(bytes)).Body);
        visiting.Remove(path);
        return body.ToString();
    }

    internal static string RenderPrompt(string prompt, string workDir)
    {
        var expressions = ExpressionRegex().Matches(prompt);
        if (expressions.Count == 0)
            return prompt;
        var contextPath = Path.Combine(workDir, "workflow-context.json");
        if (PathSafety.ContainsReparsePoint(workDir, contextPath))
            throw new InvalidOperationException("Workflow prompt expressions require a safe workflow-context.json fixture");
        using var context = JsonDocument.Parse(File.ReadAllText(contextPath));
        return ExpressionRegex().Replace(prompt, match =>
        {
            var key = match.Groups[1].Value.Trim();
            if (context.RootElement.ValueKind != JsonValueKind.Object
                || !context.RootElement.TryGetProperty(key, out var value)
                || value.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException($"Missing string workflow context for expression: {key}");
            return value.GetString()!;
        });
    }

    internal static void StageResources(WorkflowInfo workflow, string workDir)
    {
        foreach (var resource in workflow.Resources)
        {
            ValidateRelativePath(resource.Key);
            var destination = Path.Combine(workDir, resource.Key.Replace('/', Path.DirectorySeparatorChar));
            if (PathSafety.ContainsReparsePoint(workDir, destination, missingPathIsUnsafe: false))
                throw new InvalidOperationException($"Unsafe staged workflow resource: {resource.Key}");
            if (File.Exists(destination))
                throw new InvalidOperationException($"Fixture conflicts with workflow resource: {resource.Key}");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, resource.Value);
        }
    }

    private static string InstalledPath(string include)
    {
        ValidateRelativePath(include);
        return include.StartsWith("workflows/", StringComparison.Ordinal)
            || include.StartsWith("agents/", StringComparison.Ordinal)
            ? $".github/{include}" : include;
    }

    private static string ResolveFile(string root, string relative)
    {
        ValidateRelativePath(relative);
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (PathSafety.ContainsReparsePoint(root, path) || !File.Exists(path))
            throw new InvalidOperationException($"Missing or unsafe workflow resource: {relative}");
        return path;
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)
            || path.Contains('\\') || path.Contains(':')
            || path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidOperationException($"Invalid workflow resource path: {path}");
    }
}
