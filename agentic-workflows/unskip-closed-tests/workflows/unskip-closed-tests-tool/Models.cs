using System.Text.Json.Serialization;

namespace UnskipClosedTests.Tool;

internal sealed class ToolConfig
{
    public string SchemaVersion { get; set; } = "";
    public List<string> SourceRoots { get; set; } = [];
    public List<string> ExcludedGlobs { get; set; } = [];
    public List<string> GeneratedGlobs { get; set; } = [];
    public List<string> IgnoreAttributeNames { get; set; } = [];
    public List<string> TestAttributeNames { get; set; } = [];
    public List<string> VerificationCommand { get; set; } = [];
    public int VerificationTimeoutSeconds { get; set; }
}

internal sealed class Manifest
{
    public string SchemaVersion { get; set; } = "1";
    public string Repository { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string GitObjectFormat { get; set; } = "";
    public string ConfigDigest { get; set; } = "";
    public string ManifestDigest { get; set; } = "";
    public int CandidateCount { get; set; }
    public List<Candidate> Candidates { get; set; } = [];
}

internal sealed class Candidate
{
    public string CandidateId { get; set; } = "";
    public string StableOwnerId { get; set; } = "";
    public string Path { get; set; } = "";
    public string BlobOid { get; set; } = "";
    public string SourceSha256 { get; set; } = "";
    public SourceSpan AttributeSpan { get; set; } = new();
    public string AttributeTextSha256 { get; set; } = "";
    public OwnerIdentity Owner { get; set; } = new();
    public List<IssueReference> CanonicalIssueReferences { get; set; } = [];
    public CandidateDecision Decision { get; set; } = new();
}

internal sealed class SourceSpan
{
    public int Start { get; set; }
    public int Length { get; set; }
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
}

internal sealed class OwnerIdentity
{
    public string Kind { get; set; } = "";
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; set; } = [];
    public string TypeFqn { get; set; } = "";
    public string DeclarationId { get; set; } = "";
    public string MethodName { get; set; } = "";
    public string MethodSignature { get; set; } = "";
    public List<string> TestFqns { get; set; } = [];
}

internal sealed class IssueReference
{
    public string Kind { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Repo { get; set; } = "";
    public int Number { get; set; }
    public string Canonical { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Eligibility { get; set; }
    public string State { get; set; } = "";
    public string StateReason { get; set; } = "";
    public string? MergedAt { get; set; }
}

internal sealed class CandidateDecision
{
    public bool Eligible { get; set; }
    public List<string> Deferrals { get; set; } = [];
}

internal sealed class EvidenceFile
{
    public string SchemaVersion { get; set; } = "";
    public List<EvidenceReference> References { get; set; } = [];
}

internal sealed class EvidenceReference
{
    public string Canonical { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool Accessible { get; set; } = true;
    public string State { get; set; } = "";
    public string StateReason { get; set; } = "";
    public string? MergedAt { get; set; }
}

internal sealed class ApplyRequest
{
    public string SchemaVersion { get; set; } = "1";
    public VerificationCandidateRequest Candidate { get; set; } = new();
    public string Repository { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public List<VerificationTest> Tests { get; set; } = [];
}

internal sealed class VerificationCandidateRequest
{
    public string CandidateId { get; set; } = "";
}

internal sealed class VerificationTest
{
    public string Fqn { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string ResultFile { get; set; } = "";
}

internal sealed class ApplyResult
{
    public string SchemaVersion { get; set; } = "1";
    public string SourceCommit { get; set; } = "";
    public string ManifestDigest { get; set; } = "";
    public List<RetainedCandidateResult> RetainedCandidates { get; set; } = [];
    public List<RevertedCandidateResult> RevertedCandidates { get; set; } = [];
    public List<string> ChangedPaths { get; set; } = [];
    public bool HasChanges { get; set; }
    public string PrTitle { get; set; } = "";
    public string PrBody { get; set; } = "";
}

internal sealed class RetainedCandidateResult
{
    public string CandidateId { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> TestFqns { get; set; } = [];
}

internal sealed class RevertedCandidateResult
{
    public string CandidateId { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> TestFqns { get; set; } = [];
    public string Reason { get; set; } = "";
}

internal sealed class CliOptions
{
    public string Command { get; init; } = "";
    public Dictionary<string, string> Values { get; init; } = new(StringComparer.Ordinal);

    public string Required(string name) =>
        Values.TryGetValue(name, out string? value) && value.Length > 0
            ? value
            : throw new ContractException($"Missing required option --{name}.");

    public string? Optional(string name) => Values.GetValueOrDefault(name);
}

internal sealed class ContractException(string message) : Exception(message);
internal sealed class InfrastructureException(string message, Exception? inner = null) : Exception(message, inner);

internal static class ExitCodes
{
    public const int Success = 0;
    public const int CleanNoOp = 10;
    public const int Invalid = 20;
    public const int Infrastructure = 30;
}
