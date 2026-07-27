using System.Text;
using System.Text.RegularExpressions;
using SkillCatalog.Api.Models;
using YamlDotNet.RepresentationModel;

namespace SkillCatalog.Api.Services;

public sealed partial class UploadedSkillValidator
{
    private readonly SubmissionRuleProvider _rules;
    public UploadedSkillValidator(SubmissionRuleProvider rules) => _rules = rules;

    public IReadOnlyList<ValidationFinding> Validate(IReadOnlyDictionary<string, byte[]> files, string? skillPath, UploadedSkillPreview preview)
    {
        var findings = new List<ValidationFinding>();
        var basePath = skillPath is null || skillPath == "SKILL.md" ? "" : skillPath[..^"SKILL.md".Length];
        foreach (Match match in MarkdownReference().Matches(preview.Markdown))
        {
            var reference = match.Groups[1].Value.Split('#')[0];
            if (string.IsNullOrWhiteSpace(reference) || Uri.TryCreate(reference, UriKind.Absolute, out _)) continue;
            var target = Normalize(basePath + reference);
            if (target is null || !files.ContainsKey(target)) Error("reference.missing", skillPath ?? "SKILL.md", $"Referenced file `{reference}` was not found.", "Include the referenced file using a safe relative path.");
        }

        foreach (var file in files.Where(x => IsText(x.Key)))
        {
            string text;
            try { text = new UTF8Encoding(false, true).GetString(file.Value); } catch { continue; }
            if (text.Contains("-----BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase)) Error("security.private-key", file.Key, "Private-key material is prohibited.", "Remove and rotate the key.");
            if (Credential().IsMatch(text) && !Placeholder().IsMatch(text)) Error("security.credential", file.Key, "Possible credential or token detected.", "Use a documented placeholder.");
            if (PipeShell().IsMatch(text)) Error("security.pipe-shell", file.Key, "Pipe-to-shell commands are prohibited.", "Download, verify, and invoke separately.");
            foreach (Match url in Url().Matches(text))
                if (Uri.TryCreate(url.Value, UriKind.Absolute, out var uri))
                {
                    if (uri.Scheme == "http") Error("security.insecure-url", file.Key, "Insecure HTTP reference detected.", "Use HTTPS.");
                    if (!_rules.AllowedDomains().Contains(uri.Host)) Error("security.domain", file.Key, $"Domain `{uri.Host}` is not approved.", "Use a repository-approved domain.");
                }
            if (ExternalScript().IsMatch(text) && !Integrity().IsMatch(text)) Error("security.script-integrity", file.Key, "External scripts require integrity metadata.", "Add integrity metadata or remove the script.");
        }

        var evalPaths = files.Keys.Where(x => x.EndsWith("/eval.yaml", StringComparison.OrdinalIgnoreCase) || x.Equals("eval.yaml", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var path in evalPaths) ValidateEvaluation(path, files[path], preview.Name, findings);
        if (evalPaths.Length == 0) findings.Add(new("evaluation.missing", "warning", "eval.yaml", "No evaluation file was found.", "Add repository-format evaluations before submission."));

        var ownerPattern = preview.Plugin is null || preview.Name is null ? null : $"plugins/{preview.Plugin}/skills/{preview.Name}/";
        var hasOwnership = ownerPattern is not null && files.Where(x => x.Key.EndsWith("CODEOWNERS", StringComparison.OrdinalIgnoreCase)).Any(x => Encoding.UTF8.GetString(x.Value).Contains(ownerPattern, StringComparison.OrdinalIgnoreCase));
        if (!hasOwnership) findings.Add(new("ownership.missing", "warning", "CODEOWNERS", "Ownership coverage was not found in the upload.", "Ensure the contribution adds owners for the skill and evaluation paths."));
        return findings;
        void Error(string code, string location, string message, string guidance) => findings.Add(new(code, "error", location, message, guidance));
    }

    private static void ValidateEvaluation(string path, byte[] bytes, string? skillName, List<ValidationFinding> findings)
    {
        try
        {
            var yaml = new YamlStream(); using var reader = new StringReader(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF')); yaml.Load(reader);
            var root = (YamlMappingNode)yaml.Documents[0].RootNode;
            if (!TrySequence(root, "scenarios", out var scenarios) || scenarios.Children.Count == 0) { Add("evaluation.scenarios", "error", path, "Evaluation scenarios are missing.", "Add at least one scenario."); return; }
            var positive = false;
            for (var i = 0; i < scenarios.Children.Count; i++)
            {
                if (scenarios.Children[i] is not YamlMappingNode scenario) continue;
                var prompt = Scalar(scenario, "prompt");
                var activation = Scalar(scenario, "expect_activation");
                positive |= !string.Equals(activation, "false", StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(skillName) && prompt?.Contains(skillName, StringComparison.OrdinalIgnoreCase) == true) Add("evaluation.skill-name-leakage", "warning", $"{path}:scenarios[{i}].prompt", "Prompt names the skill and may overfit activation.", "Describe the user need without naming the skill.");
                if (!TrySequence(scenario, "graders", out var graders) || graders.Children.Count == 0) Add("evaluation.graders", "error", $"{path}:scenarios[{i}].graders", "A deterministic grader is required.", "Add a supported grader.");
                if (!TrySequence(scenario, "rubric", out var rubric) || rubric.Children.Count == 0) Add("evaluation.rubric", "error", $"{path}:scenarios[{i}].rubric", "Outcome criteria are required.", "Add observable outcome criteria.");
            }
            if (!positive) Add("evaluation.positive-required", "error", path, "At least one positive activation scenario is required.", "Add a scenario where activation is expected.");
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or DecoderFallbackException or InvalidCastException) { Add("evaluation.yaml", "error", path, "Evaluation YAML is malformed.", "Correct the YAML structure."); }
        void Add(string code, string severity, string location, string message, string guidance) => findings.Add(new(code, severity, location, message, guidance));
    }
    private static bool TrySequence(YamlMappingNode node, string key, out YamlSequenceNode sequence) { if (node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlSequenceNode found) { sequence = found; return true; } sequence = new(); return false; }
    private static string? Scalar(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? (value as YamlScalarNode)?.Value : null;
    private static string? Normalize(string value) { var parts = new List<string>(); foreach (var part in value.Replace('\\', '/').Split('/')) { if (part is "" or ".") continue; if (part == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); } else parts.Add(part); } return string.Join('/', parts); }
    private static bool IsText(string path) => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sh", StringComparison.OrdinalIgnoreCase);
    [GeneratedRegex(@"!?\[[^\]]*\]\(([^)\s]+)")] private static partial Regex MarkdownReference();
    [GeneratedRegex(@"(?im)\b(api[_-]?key|secret|token|password)\b\s*[:=]\s*['""]?[A-Za-z0-9_\-/+=]{12,}")] private static partial Regex Credential();
    [GeneratedRegex(@"(?i)(example|placeholder|your[_-]|changeme|redacted)")] private static partial Regex Placeholder();
    [GeneratedRegex(@"(?i)(curl|wget)[^\r\n|]*\|\s*(sh|bash|zsh|pwsh|powershell)")] private static partial Regex PipeShell();
    [GeneratedRegex(@"https?://[^\s)\]}>""']+")] private static partial Regex Url();
    [GeneratedRegex(@"(?i)<script[^>]+src=")] private static partial Regex ExternalScript();
    [GeneratedRegex(@"(?i)\bintegrity\s*=")] private static partial Regex Integrity();
}
