using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.IO;

namespace BlazorComponentReadiness.Validator.Evidence;

public static partial class EvidenceIdentity
{
    public const string VendorPublicDocumentation = "vendor-public-documentation";
    public const string PackageArtifactMetadata = "package-artifact-metadata";
    public const string VendorSourceRepository = "vendor-source-repository";
    public const string ReproducedRuntimeObservation = "reproduced-runtime-observation";
    public const string ReviewerGeneratedAnalysis = "reviewer-generated-analysis";
    public const string OwnerSuppliedInternalEvidence = "owner-supplied-internal-evidence";
    public const string OwnerSuppliedPublicEvidence = "owner-supplied-public-evidence";
    public const string OwnerDeclaredClosedSource = "owner-declared-closed-source";
    public const string OwnerDeclaredUnavailable = "owner-declared-unavailable";

    /// <summary>
    /// Gets the existing provenance kinds accepted by evidence record validation.
    /// </summary>
    public static IReadOnlyList<string> ExistingProvenanceKinds =>
    [
        VendorPublicDocumentation,
        PackageArtifactMetadata,
        VendorSourceRepository,
        ReproducedRuntimeObservation,
        ReviewerGeneratedAnalysis,
        OwnerSuppliedInternalEvidence,
        OwnerSuppliedPublicEvidence,
        OwnerDeclaredClosedSource,
        OwnerDeclaredUnavailable
    ];

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdentifierPattern();

    [GeneratedRegex("^EV1-[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex EvidenceIdentifierPattern();

    [GeneratedRegex("^[A-Za-z0-9 ._\\-:/+=,@()]{1,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommandProbePattern();

    [GeneratedRegex("^\\d+[.)]\\s", RegexOptions.CultureInvariant)]
    private static partial Regex OrderedListPattern();

    public static ExactAssessmentIdentity NormalizeAssessment(ExactAssessmentIdentity assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var (assessmentKind, componentId) = NormalizeAssessmentKind(
            assessment.AssessmentKind,
            assessment.ComponentId);
        ValidateDigest(assessment.InputManifestDigest, "input_manifest_sha256");
        return new ExactAssessmentIdentity(
            assessmentKind,
            NormalizePackage(assessment.Package),
            assessment.InputManifestDigest,
            componentId);
    }

    public static RepositoryLedgerSubject NormalizeRepositorySubject(RepositoryLedgerSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var (assessmentKind, componentId) = NormalizeAssessmentKind(
            subject.AssessmentKind,
            subject.ComponentId);
        ValidateDigest(subject.InputManifestDigest, "input_manifest_sha256");
        return new RepositoryLedgerSubject(
            assessmentKind,
            NormalizePackage(subject.Package),
            subject.InputManifestDigest,
            componentId);
    }

    public static EvidenceRecordDraft NormalizeRecordDraft(EvidenceRecordDraft record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var supersedes = record.Supersedes
            .Select(NormalizeEvidenceIdentifier)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (supersedes.Length != supersedes.Distinct(StringComparer.Ordinal).Count())
        {
            throw new DeterministicValidationException(
                "EVID010: supersedes contains duplicate evidence IDs.");
        }

        var claim = NormalizeText(record.Claim, "claim", 512);
        if (claim.Contains('|', StringComparison.Ordinal) ||
            claim.StartsWith("- ", StringComparison.Ordinal) ||
            claim.StartsWith("* ", StringComparison.Ordinal) ||
            claim.StartsWith("+ ", StringComparison.Ordinal) ||
            claim.StartsWith("#", StringComparison.Ordinal) ||
            OrderedListPattern().IsMatch(claim))
        {
            throw new DeterministicValidationException(
                "EVID004: claim must be one syntactically atomic non-Markdown sentence.");
        }

        return new EvidenceRecordDraft(
            claim,
            NormalizeApplicability(record.Applicability),
            NormalizeProvenance(record.Provenance),
            supersedes);
    }

    public static void ValidateAssessment(ExactAssessmentIdentity assessment)
    {
        if (NormalizeAssessment(assessment) != assessment)
        {
            throw new DeterministicValidationException(
                "EVID001: assessment identity is not canonical.");
        }
    }

    public static void ValidateDigest(Sha256Digest digest, string name)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (!string.Equals(digest.Algorithm, "sha256", StringComparison.Ordinal) ||
            digest.Value.Length != 64 ||
            digest.Value.Any(character =>
                character is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f')))
        {
            throw new DeterministicValidationException(
                $"EVID005: {name} must be canonical lowercase SHA-256.");
        }
    }

    public static void ValidateStableIdentifier(string identifier)
    {
        if (!EvidenceIdentifierPattern().IsMatch(identifier))
        {
            throw new DeterministicValidationException(
                $"EVID002: invalid stable evidence ID '{identifier}'.");
        }
    }

    /// <summary>
    /// Describes the existing logical locator grammar for a provenance kind.
    /// </summary>
    public static string GetLocatorGrammar(string kind) =>
        kind switch
        {
            VendorPublicDocumentation => "https://<public-dns-host>[/path]",
            PackageArtifactMetadata =>
                $"{NupkgInspector.WholePackageEvidenceLocator} or " +
                $"{NupkgInspector.PackageEntryEvidencePrefix}<exact-case relative entry path>",
            VendorSourceRepository => "source:<relative logical path>",
            ReproducedRuntimeObservation or ReviewerGeneratedAnalysis =>
                "<command probe using letters, digits, spaces and ._-:/+=,@()>",
            OwnerSuppliedInternalEvidence or OwnerSuppliedPublicEvidence =>
                "<basename without a path>",
            OwnerDeclaredClosedSource => "source",
            OwnerDeclaredUnavailable => "artifact:<name>",
            _ => throw new DeterministicValidationException(
                $"EVID005: invalid provenance kind '{kind}'.")
        };

    public static EvidencePackageIdentity FromInspectedPackage(PackageIdentity package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return NormalizePackage(new EvidencePackageIdentity(
            package.Id,
            package.Version,
            new Sha256Digest("sha256", package.NupkgSha256)));
    }

    private static EvidencePackageIdentity NormalizePackage(EvidencePackageIdentity package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidateDigest(package.NupkgDigest, "nupkg_sha256");
        var packageId = NormalizeText(package.PackageId, "package_id", 100).ToLowerInvariant();
        if (!PackageIdentifierPattern().IsMatch(packageId))
        {
            throw new DeterministicValidationException(
                "EVID006: package_id has invalid canonical NuGet ID syntax.");
        }

        var version = NuGetVersionNormalizer.Normalize(
            NormalizeText(package.Version, "version", 256));

        return new EvidencePackageIdentity(packageId, version, package.NupkgDigest);
    }

    private static EvidenceApplicability NormalizeApplicability(EvidenceApplicability applicability)
    {
        ArgumentNullException.ThrowIfNull(applicability);
        return applicability.Scope switch
        {
            "repository-wide" when applicability.ComponentId is null =>
                new EvidenceApplicability("repository-wide", null),
            "component-specific" when applicability.ComponentId is not null =>
                new EvidenceApplicability(
                    "component-specific",
                    NormalizeText(applicability.ComponentId, "component_id", 256)),
            "repository-wide" => throw new DeterministicValidationException(
                "EVID007: repository-wide evidence requires component_id null."),
            "component-specific" => throw new DeterministicValidationException(
                "EVID007: component-specific evidence requires component_id."),
            _ => throw new DeterministicValidationException(
                $"EVID007: invalid evidence scope '{applicability.Scope}'.")
        };
    }

    private static EvidenceProvenance NormalizeProvenance(EvidenceProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        if (!string.Equals(provenance.Retention, "commitment-only", StringComparison.Ordinal))
        {
            throw new DeterministicValidationException(
                "EVID005: retention must be exactly 'commitment-only'.");
        }

        ValidateDigest(provenance.ContentDigest, "content_sha256");
        return new EvidenceProvenance(
            provenance.Kind,
            CanonicalizeLocator(provenance.Kind, provenance.Locator),
            NormalizeText(provenance.Method, "method", 512),
            NormalizeTimestamp(provenance.CapturedAtUtc),
            provenance.ContentDigest,
            "commitment-only");
    }

    private static string CanonicalizeLocator(string kind, string value)
    {
        value = NormalizeText(value, "locator", 2048);
        RejectMarkdownAndPathHazards(value, "locator");
        return kind switch
        {
            VendorPublicDocumentation => CanonicalizePublicHttps(value, requirePath: false),
            VendorSourceRepository => CanonicalizeSourceLocator(value),
            PackageArtifactMetadata => CanonicalizePackageLocator(value),
            ReproducedRuntimeObservation => CanonicalizeCommandProbe(value),
            ReviewerGeneratedAnalysis => CanonicalizeCommandProbe(value),
            OwnerSuppliedInternalEvidence => CanonicalizeOwnerInput(value),
            OwnerSuppliedPublicEvidence => CanonicalizeOwnerInput(value),
            OwnerDeclaredClosedSource => CanonicalizeClosedSourceDeclaration(value),
            OwnerDeclaredUnavailable => CanonicalizeUnavailableDeclaration(value),
            _ => throw new DeterministicValidationException(
                $"EVID005: invalid provenance kind '{kind}'.")
        };
    }

    private static string CanonicalizePublicHttps(string value, bool requirePath)
    {
        var (host, path) = ParseRawHttps(value, allowRootPath: !requirePath);
        ValidatePublicDnsHost(host, "public HTTPS locator");
        return $"https://{host}{path}";
    }

    private static string CanonicalizePackageLocator(string value)
    {
        if (string.Equals(
                value,
                NupkgInspector.WholePackageEvidenceLocator,
                StringComparison.Ordinal))
        {
            return value;
        }

        if (!value.StartsWith(NupkgInspector.PackageEntryEvidencePrefix, StringComparison.Ordinal))
        {
            throw new DeterministicValidationException(
                $"EVID005: package artifact locator must be '{NupkgInspector.WholePackageEvidenceLocator}' " +
                $"or '{NupkgInspector.PackageEntryEvidencePrefix}<exact-case relative entry path>'.");
        }

        ValidateRelativeLogicalPath(
            value[NupkgInspector.PackageEntryEvidencePrefix.Length..],
            "package artifact locator");
        return value;
    }

    private static string CanonicalizeSourceLocator(string value)
    {
        const string Prefix = "source:";
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new DeterministicValidationException(
                "EVID005: vendor-source locator must start with 'source:'.");
        }

        ValidateRelativeLogicalPath(value[Prefix.Length..], "vendor-source locator");
        return value;
    }

    private static string CanonicalizeCommandProbe(string value)
    {
        if (!CommandProbePattern().IsMatch(value))
        {
            throw new DeterministicValidationException(
                "EVID005: runtime-observation locator contains unsupported characters.");
        }

        return value;
    }

    private static string CanonicalizeOwnerInput(string value)
    {
        if (value.Length > 256 ||
            value is "." or ".." ||
            value.Contains('/', StringComparison.Ordinal) ||
            value.Contains(':', StringComparison.Ordinal))
        {
            throw new DeterministicValidationException(
                "EVID005: owner-supplied locator must be a basename without a path.");
        }

        return value;
    }

    private static string CanonicalizeUnavailableDeclaration(string value)
    {
        const string Prefix = "artifact:";
        if (!value.StartsWith(Prefix, StringComparison.Ordinal) ||
            value.Length == Prefix.Length ||
            value.Length > 256 ||
            value[Prefix.Length..].Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.'))
        {
            throw new DeterministicValidationException(
                "EVID005: unavailable locator must be 'artifact:<name>'.");
        }

        return value;
    }

    private static string CanonicalizeClosedSourceDeclaration(string value)
    {
        if (!string.Equals(value, "source", StringComparison.Ordinal))
        {
            throw new DeterministicValidationException(
                "EVID005: closed-source locator must be exactly 'source'.");
        }

        return value;
    }

    private static (string AssessmentKind, string? ComponentId) NormalizeAssessmentKind(
        string assessmentKind,
        string? componentId)
    {
        assessmentKind = NormalizeText(assessmentKind, "assessment_kind", 16);
        return assessmentKind switch
        {
            "package" when componentId is null => ("package", null),
            "unified" when componentId is not null =>
                ("unified", NormalizeText(componentId, "component_id", 256)),
            "component" when componentId is not null =>
                ("component", NormalizeText(componentId, "component_id", 256)),
            "package" => throw new DeterministicValidationException(
                "EVID006: package assessment identity requires component_id null."),
            "unified" or "component" => throw new DeterministicValidationException(
                $"EVID006: {assessmentKind} assessment identity requires component_id."),
            _ => throw new DeterministicValidationException(
                $"EVID006: invalid assessment_kind '{assessmentKind}'.")
        };
    }

    private static void ValidateRelativeLogicalPath(string value, string name)
    {
        if (value.Length == 0 ||
            value.StartsWith('/') ||
            value.Contains(':', StringComparison.Ordinal) ||
            value.Contains("//", StringComparison.Ordinal) ||
            value.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new DeterministicValidationException(
                $"EVID005: {name} must be a normalized relative POSIX path.");
        }
    }

    private static string NormalizeTimestamp(string value)
    {
        value = NormalizeText(value, "captured_at_utc", 20);
        if (!DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp) ||
            !string.Equals(
                timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                value,
                StringComparison.Ordinal))
        {
            throw new DeterministicValidationException(
                "EVID005: captured_at_utc must use canonical UTC-second format.");
        }

        return value;
    }

    private static string NormalizeEvidenceIdentifier(string value)
    {
        value = NormalizeText(value, "evidence_id", 68);
        ValidateStableIdentifier(value);
        return value;
    }

    private static string NormalizeText(string value, string name, int maximumUtf8Bytes)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = StrictUtf8.GetBytes(value);
        if (value.Length == 0 ||
            !value.IsNormalized(NormalizationForm.FormC) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            StrictUtf8.GetByteCount(value) > maximumUtf8Bytes ||
            ContainsDisallowedCharacter(value))
        {
            throw new DeterministicValidationException(
                $"EVID005: {name} is empty, non-NFC, untrimmed, too long, or contains controls.");
        }

        return value;
    }

    private static void RejectMarkdownAndPathHazards(string value, string name)
    {
        if (value.Contains('`', StringComparison.Ordinal) ||
            value.Contains('|', StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal) ||
            value.Contains('\r', StringComparison.Ordinal) ||
            value.Contains('\n', StringComparison.Ordinal) ||
            value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            throw new DeterministicValidationException(
                $"EVID005: {name} contains a forbidden delimiter, path, or URI form.");
        }
    }

    private static (string Host, string Path) ParseRawHttps(string value, bool allowRootPath)
    {
        if (value.Contains('%', StringComparison.Ordinal) ||
            value.Contains('?', StringComparison.Ordinal) ||
            value.Contains('#', StringComparison.Ordinal))
        {
            throw new DeterministicValidationException(
                "EVID005: canonical HTTPS identity forbids percent encoding, query, and fragment.");
        }

        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0 ||
            !string.Equals(value[..schemeEnd], Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new DeterministicValidationException(
                "EVID005: canonical HTTPS identity requires the HTTPS scheme.");
        }

        var authorityStart = schemeEnd + 3;
        var authorityEnd = value.IndexOf('/', authorityStart);
        if (authorityEnd < 0)
        {
            authorityEnd = value.Length;
        }

        var authority = value[authorityStart..authorityEnd];
        if (authority.Length == 0 ||
            authority.Contains('@', StringComparison.Ordinal) ||
            authority.Contains(':', StringComparison.Ordinal) ||
            !Uri.TryCreate($"https://{authority}/", UriKind.Absolute, out var authorityUri))
        {
            throw new DeterministicValidationException(
                "EVID005: canonical HTTPS identity has an invalid authority.");
        }

        var host = authorityUri.IdnHost.ToLowerInvariant();
        if (host.Length == 0 || host.EndsWith('.'))
        {
            throw new DeterministicValidationException(
                "EVID005: canonical HTTPS host is empty or ends with a root-label dot.");
        }

        var path = authorityEnd == value.Length ? string.Empty : value[authorityEnd..];
        if (path.Length == 0)
        {
            if (!allowRootPath)
            {
                throw new DeterministicValidationException(
                    "EVID005: source repository locator requires a nonempty path.");
            }

            path = "/";
        }

        ValidateRawHttpsPathCharacters(path);
        if (!path.StartsWith('/') ||
            (!allowRootPath && path == "/") ||
            (path.Length > 1 && path.EndsWith('/')) ||
            path.Contains("//", StringComparison.Ordinal) ||
            (path.Length > 1 &&
             path[1..].Split('/').Any(segment => segment.Length == 0 || segment is "." or "..")))
        {
            throw new DeterministicValidationException(
                "EVID005: canonical HTTPS path is empty, repeated, dotted, or has a trailing slash.");
        }

        return (host, path);
    }

    private static void ValidateRawHttpsPathCharacters(string path)
    {
        const string AllowedAsciiPunctuation = "-._~!$&'()*+,;=:@/";
        foreach (var rune in path.EnumerateRunes())
        {
            if (rune.IsAscii)
            {
                var character = (char)rune.Value;
                if (!char.IsAsciiLetterOrDigit(character) &&
                    !AllowedAsciiPunctuation.Contains(character, StringComparison.Ordinal))
                {
                    throw new DeterministicValidationException(
                        $"EVID005: canonical HTTPS path contains forbidden ASCII character U+{rune.Value:X4}.");
                }

                continue;
            }

            if (Rune.IsWhiteSpace(rune) ||
                Rune.GetUnicodeCategory(rune) is
                    UnicodeCategory.SpaceSeparator or
                    UnicodeCategory.LineSeparator or
                    UnicodeCategory.ParagraphSeparator)
            {
                throw new DeterministicValidationException(
                    $"EVID005: canonical HTTPS path contains Unicode whitespace or separator U+{rune.Value:X4}.");
            }
        }
    }

    private static void ValidatePublicDnsHost(string host, string name)
    {
        if (IPAddress.TryParse(host, out _) ||
            !host.Contains('.', StringComparison.Ordinal) ||
            host.Length > 253 ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            throw new DeterministicValidationException(
                $"EVID005: {name} requires a public DNS or IDN hostname.");
        }

        var labels = host.Split('.');
        if (labels.Any(label =>
                label.Length is < 1 or > 63 ||
                !char.IsAsciiLetterOrDigit(label[0]) ||
                !char.IsAsciiLetterOrDigit(label[^1]) ||
                label.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character != '-')) ||
            labels[^1].All(char.IsAsciiDigit))
        {
            throw new DeterministicValidationException(
                $"EVID005: {name} contains an invalid DNS label.");
        }
    }

    private static bool ContainsDisallowedCharacter(string value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value <= 0x1f ||
                rune.Value is >= 0x7f and <= 0x9f ||
                (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format &&
                 rune.Value is not 0x200c and not 0x200d))
            {
                return true;
            }
        }

        return false;
    }
}
