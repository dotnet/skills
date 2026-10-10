using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Assessment;

internal sealed class ComponentReportScope
{
    private readonly Sha256Digest _definitionDigest;
    private readonly Regex _excludedIds;
    private readonly string[] _excludedRequirements;

    private ComponentReportScope(RubricContract rubric)
    {
        _definitionDigest = rubric.CrosswalkDigest ??
            throw new DeterministicValidationException("Component selection requires the frozen bundled basis.");
        Requirements = rubric.CoreRequirements.Where(row => row.Scope == "component-specific").ToArray();
        if (Requirements.Count != 52 || Requirements.Any(row => row.Basis is not { RequiresPolicyApproval: false }))
        {
            throw new DeterministicValidationException("The component requirement selection has drifted.");
        }

        var selected = Requirements.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var excluded = rubric.CoreRequirements.Concat(rubric.Extensions ?? [])
            .Where(row => !selected.Contains(row.Id)).ToArray();
        string[] retired = ["CI-02", "CI-03", "CI-04", "CI-09", "CI-10", "PERF-07", "PERF-08", "PERF-09", "PERF-10"];
        _excludedIds = new Regex(
            @"(?<![A-Z0-9])(?:" + string.Join("|", excluded.Select(row => row.Id).Concat(retired).Select(Regex.Escape)) + @")(?![A-Z0-9])",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        _excludedRequirements = excluded.Select(row => row.Requirement).ToArray();
    }

    internal IReadOnlyList<RubricRequirement> Requirements { get; }

    internal string Declaration =>
        $"**Requirement basis:** bundled `{RubricLoader.CurrentVersion}` (SHA-256 `{_definitionDigest.Value}`).\n" +
        "**Component scope:** exactly 52 component-specific checks. Package assessment is separate, not a prerequisite.";

    internal const string ExportNotice =
        "This reader is NOT a self-contained validation bundle. Re-verification requires the retained internal workspace. " +
        "Raw inputs and attachments, policy documents, package/source archives, raw captures, and historical " +
        "ledgers/reports are not exported. Their registration and digest references do not mean their bytes were delivered. " +
        "Exact canonical copies are labeled separately from any constructed selected-only evidence companion; " +
        "the reader explicitly declares whether that companion is included or omitted.";

    internal static void RejectRetiredInput(string kind, string basename)
    {
        if (kind.StartsWith("scoped-component-profile", StringComparison.OrdinalIgnoreCase) ||
            kind.StartsWith("scoped-package-context", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(basename, "scoped-package.validation.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new DeterministicValidationException(
                "Scoped-component profiles and package contexts are retired. Use a standalone component assessment.");
        }
    }

    internal static void RejectRetiredInputs(InputManifest input)
    {
        foreach (var item in input.EvidenceInputs)
        {
            RejectRetiredInput(item.Kind, item.Basename);
        }
    }

    internal static ComponentReportScope? For(ReadinessAssessment assessment)
    {
        var rubric = AssessmentService.RequireCurrentContract(assessment);
        return assessment.AssessmentKind == "component" ? new ComponentReportScope(rubric) : null;
    }

    internal void ValidateSelection(ReadinessAssessment assessment)
    {
        AssessmentService.ValidateSelection(assessment, Requirements);
    }

    internal void ValidateEvidence(EvidenceBundle evidence)
    {
        var selected = evidence.Selection.Select(item => item.EvidenceId).ToHashSet(StringComparer.Ordinal);
        var rubric = RubricLoader.Load();
        if (evidence.SourceLedgers.SelectMany(source => source.Ledger.Records)
            .Any(record => selected.Contains(record.StableId) &&
                (record.Provenance.ContentDigest == _definitionDigest ||
                 record.Provenance.ContentDigest == rubric.RubricDigest)))
        {
            throw new DeterministicValidationException(
                "Requirement definitions do not establish component behavior or product evidence.");
        }
    }

    internal EvidenceBundle? BuildSelectedCompanion(EvidenceBundle evidence)
    {
        EvidenceLedgerValidator.ValidateBundle(evidence);
        var selected = evidence.Selection.Select(item => item.EvidenceId).ToHashSet(StringComparer.Ordinal);
        if (evidence.SourceLedgers.SelectMany(source => source.Ledger.Records)
            .Any(record => selected.Contains(record.StableId) &&
                record.Supersedes.Any(predecessor => !selected.Contains(predecessor))))
        {
            return null;
        }

        var ledgers = new List<EvidenceSourceLedger>();
        foreach (var source in evidence.SourceLedgers)
        {
            var records = source.Ledger.Records.Where(record => selected.Contains(record.StableId)).ToArray();
            if (records.Length == 0)
            {
                continue;
            }

            var drafts = records.Select(record => new EvidenceRecordDraft(
                record.Claim, record.Applicability, record.Provenance, record.Supersedes));
            var ledger = source.Ledger.LedgerKind == "component"
                ? EvidenceLedgerBuilder.BuildComponentLedger(source.Ledger.ComponentSubject!, drafts)
                : EvidenceLedgerBuilder.BuildRepositoryLedger(source.Ledger.RepositorySubject!, drafts);
            if (!ledger.Records.Select(record => record.StableId).SequenceEqual(
                    records.Select(record => record.StableId), StringComparer.Ordinal))
            {
                throw new DeterministicValidationException("Selected evidence companion changed a source record identity.");
            }

            ledgers.Add(ledger);
        }

        return EvidenceLedgerBuilder.BuildBundle(evidence.Assessment, ledgers,
            evidence.Selection.OrderBy(item => item.DisplayOrder).Select(item => item.EvidenceId).ToArray());
    }

    internal void RejectDisclosure(byte[] bytes, bool json = false)
    {
        RejectDisclosure(Encoding.UTF8.GetString(bytes));
        if (!json)
        {
            return;
        }

        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "component export");
        Visit(document.RootElement);
        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                RejectDisclosure(element.GetString()!);
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    RejectDisclosure(property.Name);
                    Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    Visit(item);
                }
            }
        }
    }

    internal void RejectDisclosure(string text)
    {
        var decoded = text;
        for (var index = 0; index < 3; index++)
        {
            decoded = WebUtility.HtmlDecode(decoded);
        }

        decoded = Regex.Replace(decoded, @"\\u([0-9a-fA-F]{4})",
            match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
        decoded = decoded.Replace('\u2010', '-').Replace('\u2011', '-').Replace('\u2013', '-');
        var markdownUnescaped = Regex.Replace(decoded, @"\\([!-~])", match =>
        {
            var escaped = match.Groups[1].Value[0];
            return escaped is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~'
                ? match.Groups[1].Value : match.Value;
        });
        if (ContainsExcludedMaterial(decoded) || ContainsExcludedMaterial(markdownUnescaped))
        {
            throw new DeterministicValidationException(
                "Component output contains excluded requirement identifiers or material. Preserve internal artifacts; do not fall back to a broader export.");
        }

        bool ContainsExcludedMaterial(string value) =>
            _excludedIds.IsMatch(value) ||
            value.Contains("versioned extension", StringComparison.OrdinalIgnoreCase) ||
            _excludedRequirements.Any(requirement => value.Contains(requirement, StringComparison.OrdinalIgnoreCase)) ||
            Regex.IsMatch(value, @"\b(?:60|61|112|121)\s*[- ]\s*(?:canonical\s+)?(?:package\s+|component\s+)?(?:rows?|checks?|requirements?|findings?)\b",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }
}
