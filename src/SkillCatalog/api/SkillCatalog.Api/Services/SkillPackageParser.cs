using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SkillCatalog.Api.Models;
using SkillCatalog.Api.Options;

namespace SkillCatalog.Api.Services;

public sealed partial class SkillPackageParser
{
    private readonly SkillSubmissionOptions _limits;
    private readonly SubmissionRuleProvider _rules;
    private readonly UploadedSkillValidator _validator;
    public SkillPackageParser(SkillSubmissionOptions limits, SubmissionRuleProvider rules, UploadedSkillValidator validator) { _limits = limits; _rules = rules; _validator = validator; }

    public async Task<(UploadInspection Inspection, IReadOnlyDictionary<string, byte[]> Files)> InspectAsync(IFormFile upload, CancellationToken cancellationToken)
    {
        var findings = new List<ValidationFinding>();
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (upload.Length is 0 or > 10_000_000) Error("upload.size", "upload", "The selected file is empty or too large.", "Choose a file within the published limit.");
        else if (upload.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            files["SKILL.md"] = await ReadBounded(upload.OpenReadStream(), _limits.MaxResourceBytes, cancellationToken);
        else if (upload.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            await ReadArchive(upload, files, findings, cancellationToken);
        else Error("upload.type", "upload", "Unsupported file type.", "Upload a .zip package or SKILL.md.");

        var skillPaths = files.Keys.Where(x => x.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase) || SkillPath().IsMatch(x)).ToArray();
        if (skillPaths.Length != 1) Error("package.skill-count", "package", $"Expected exactly one skill but found {skillPaths.Length}.", "Upload one skill package.");
        var skillPath = skillPaths.SingleOrDefault();
        var markdown = skillPath is null ? "" : Decode(files[skillPath], findings, skillPath);
        var match = skillPath is null ? Match.Empty : SkillPath().Match(skillPath);
        var plugin = match.Success ? match.Groups["plugin"].Value : null;
        var pathName = match.Success ? match.Groups["name"].Value : null;
        var frontmatterName = FrontmatterName().Match(markdown).Groups[1].Value.Trim();
        var description = FrontmatterDescription().Match(markdown).Groups[1].Value.Trim().Trim('"');
        var name = string.IsNullOrWhiteSpace(pathName) ? frontmatterName : pathName;
        if (string.IsNullOrWhiteSpace(frontmatterName)) Error("skill.name.required", skillPath ?? "SKILL.md", "Frontmatter name is required.", "Add a name field.");
        if (string.IsNullOrWhiteSpace(description)) Error("skill.description.required", skillPath ?? "SKILL.md", "Frontmatter description is required.", "Add a description field.");
        if (match.Success && !string.Equals(frontmatterName, pathName, StringComparison.OrdinalIgnoreCase)) Error("skill.identity.mismatch", skillPath!, "Folder and frontmatter names differ.", "Make the identities match.");
        if (!markdown.Contains("## Workflow", StringComparison.OrdinalIgnoreCase)) Error("skill.workflow.required", skillPath ?? "SKILL.md", "Workflow guidance is missing.", "Add a Workflow section.");
        if (!markdown.Contains("## Validation", StringComparison.OrdinalIgnoreCase)) Error("skill.validation.required", skillPath ?? "SKILL.md", "Validation guidance is missing.", "Add a Validation section.");
        var disposition = plugin is not null && name is not null && _rules.SkillExists(plugin, name) ? "update" : "new";
        var revision = Convert.ToHexString(SHA256.HashData(files.OrderBy(x => x.Key).SelectMany(x => x.Value).ToArray())).ToLowerInvariant();
        var entries = files.Select(x => new UploadedEntry(x.Key, x.Value.Length, Kind(x.Key))).OrderBy(x => x.Path).ToArray();
        var evaluationCount = files.Keys.Count(x => x.EndsWith("/eval.yaml", StringComparison.OrdinalIgnoreCase) || x.Equals("eval.yaml", StringComparison.OrdinalIgnoreCase));
        var ownerPattern = plugin is null || name is null ? null : $"plugins/{plugin}/skills/{name}/";
        var ownershipCovered = ownerPattern is not null && files.Where(x => x.Key.EndsWith("CODEOWNERS", StringComparison.OrdinalIgnoreCase)).Any(x => Encoding.UTF8.GetString(x.Value).Contains(ownerPattern, StringComparison.OrdinalIgnoreCase));
        var preview = new UploadedSkillPreview(plugin, name, description, markdown, disposition, entries, evaluationCount, ownershipCovered);
        findings.AddRange(_validator.Validate(files, skillPath, preview));
        findings = findings.OrderBy(x => x.Severity == "error" ? 0 : 1).ThenBy(x => x.Field, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal).ToList();
        return (new(revision, findings.All(x => x.Severity != "error"), findings, preview, entries.Select(x => new PackageEntry(x.Path, x.Size)).ToArray()), files);
        void Error(string code, string location, string message, string guidance) => findings.Add(new(code, "error", location, message, guidance));
    }

    private async Task ReadArchive(IFormFile upload, Dictionary<string, byte[]> files, List<ValidationFinding> findings, CancellationToken token)
    {
        using var archive = new ZipArchive(upload.OpenReadStream(), ZipArchiveMode.Read, false);
        if (archive.Entries.Count > 100) { findings.Add(new("archive.entries", "error", "package", "Archive has too many entries.", "Use at most 100 entries.")); return; }
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            var path = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (path.StartsWith('/') || Path.IsPathRooted(path) || path.Split('/').Any(x => x is "." or "..") || path.Any(char.IsControl)) { findings.Add(new("archive.path.unsafe", "error", path, "Unsafe archive path.", "Use normalized relative paths.")); continue; }
            if (files.ContainsKey(path)) { findings.Add(new("archive.path.duplicate", "error", path, "Duplicate normalized path.", "Keep one unique entry.")); continue; }
            if (entry.Length > _limits.MaxResourceBytes || expanded + entry.Length > _limits.MaxPackageBytes || (entry.CompressedLength > 0 && entry.Length / Math.Max(1, entry.CompressedLength) > 100)) { findings.Add(new("archive.expansion", "error", path, "Archive entry exceeds safety limits.", "Reduce file size or compression ratio.")); continue; }
            expanded += entry.Length;
            files[path] = await ReadBounded(entry.Open(), _limits.MaxResourceBytes, token);
        }
    }

    public static byte[] Normalize(IReadOnlyDictionary<string, byte[]> files)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var file in files.OrderBy(x => x.Key, StringComparer.Ordinal))
            { var entry = zip.CreateEntry(file.Key, CompressionLevel.Optimal); entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero); using var stream = entry.Open(); stream.Write(file.Value); }
        return output.ToArray();
    }
    private static async Task<byte[]> ReadBounded(Stream input, int limit, CancellationToken token) { using var output = new MemoryStream(); var buffer = new byte[81920]; int read; while ((read = await input.ReadAsync(buffer, token)) > 0) { if (output.Length + read > limit) throw new InvalidDataException("File exceeds limit."); await output.WriteAsync(buffer.AsMemory(0, read), token); } return output.ToArray(); }
    private static string Decode(byte[] bytes, List<ValidationFinding> findings, string path) { try { return new UTF8Encoding(false, true).GetString(bytes); } catch { findings.Add(new("file.encoding", "error", path, "File is not valid UTF-8.", "Save text files as UTF-8.")); return ""; } }
    private static string Kind(string path) => path.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase) ? "skill" : path.EndsWith("eval.yaml", StringComparison.OrdinalIgnoreCase) ? "evaluation" : path.Contains("/scripts/", StringComparison.OrdinalIgnoreCase) ? "script" : "resource";
    [GeneratedRegex(@"^plugins/(?<plugin>[^/]+)/skills/(?<name>[^/]+)/SKILL\.md$", RegexOptions.IgnoreCase)] private static partial Regex SkillPath();
    [GeneratedRegex(@"(?m)^name:\s*(.+)$")] private static partial Regex FrontmatterName();
    [GeneratedRegex(@"(?m)^description:\s*(.+)$")] private static partial Regex FrontmatterDescription();
}
