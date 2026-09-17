using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.IO;

namespace BlazorComponentReadiness.Validator.Evidence;

public static class CanonicalEvidenceJson
{
    public const int EvidenceSchemaVersion = 1;
    public const string EvidenceRecordDomain =
        "blazor-component-readiness/evidence-record/v1";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly IEvidenceHasher DefaultHasher = new Sha256EvidenceHasher();

    public static byte[] SerializeAssessment(ExactAssessmentIdentity assessment)
    {
        EvidenceIdentity.ValidateAssessment(assessment);
        var writer = new CanonicalJsonTextWriter();
        WriteAssessment(writer, assessment);
        return writer.ToArray();
    }

    public static byte[] SerializeRepositorySubject(RepositoryLedgerSubject subject)
    {
        if (EvidenceIdentity.NormalizeRepositorySubject(subject) != subject)
        {
            throw new DeterministicValidationException(
                "EVID001: repository ledger subject is not canonical.");
        }

        var writer = new CanonicalJsonTextWriter();
        WriteRepositorySubject(writer, subject);
        return writer.ToArray();
    }

    public static byte[] SerializeSourceLedger(EvidenceSourceLedger ledger)
    {
        EvidenceLedgerValidator.ValidateSourceLedger(ledger);
        var writer = new CanonicalJsonTextWriter();
        WriteSourceLedger(writer, ledger);
        return writer.ToArray();
    }

    /// <summary>
    /// Serializes a normalized draft using the existing evidence schema without creating a ledger.
    /// </summary>
    public static byte[] SerializeDraftDocument(EvidenceDraftDocument draft)
    {
        if (draft.SchemaVersion != EvidenceSchemaVersion || draft.Records.Count == 0)
        {
            throw new DeterministicValidationException(
                "EVID001: evidence draft requires schema 1 and records.");
        }

        var normalized = draft with
        {
            Records = draft.Records.Select(EvidenceIdentity.NormalizeRecordDraft).ToArray()
        };
        var writer = new CanonicalJsonTextWriter();
        writer.Raw("{\"schema_version\":");
        writer.Integer(normalized.SchemaVersion);
        writer.Raw(",\"records\":[");
        for (var index = 0; index < normalized.Records.Count; index++)
        {
            if (index > 0)
            {
                writer.Raw(",");
            }

            WriteRecordIdentityPayload(writer, normalized.Records[index]);
        }

        writer.Raw("]}");
        return writer.ToArray();
    }

    public static byte[] SerializeBundle(EvidenceBundle bundle)
    {
        EvidenceLedgerValidator.ValidateBundle(bundle);
        var writer = new CanonicalJsonTextWriter();
        WriteBundle(writer, bundle);
        return writer.ToArray();
    }

    public static EvidenceSourceLedger ParseSourceLedger(ReadOnlyMemory<byte> bytes)
    {
        var ledger = ParseDocument(bytes, ParseSourceLedger);
        EvidenceLedgerValidator.ValidateSourceLedger(ledger);
        RequireCanonicalBytes(bytes.Span, SerializeSourceLedger(ledger), "source ledger");
        return ledger;
    }

    public static EvidenceBundle ParseBundle(ReadOnlyMemory<byte> bytes)
    {
        var bundle = ParseDocument(bytes, ParseBundle);
        EvidenceLedgerValidator.ValidateBundle(bundle);
        RequireCanonicalBytes(bytes.Span, SerializeBundle(bundle), "evidence bundle");
        return bundle;
    }

    public static ExactAssessmentIdentity ParseAssessment(ReadOnlyMemory<byte> bytes)
    {
        var assessment = ParseDocument(bytes, ParseAssessment);
        EvidenceIdentity.ValidateAssessment(assessment);
        RequireCanonicalBytes(bytes.Span, SerializeAssessment(assessment), "assessment");
        return assessment;
    }

    public static RepositoryLedgerSubject ParseRepositorySubject(ReadOnlyMemory<byte> bytes)
    {
        var subject = ParseDocument(bytes, ParseRepositorySubject);
        if (EvidenceIdentity.NormalizeRepositorySubject(subject) != subject)
        {
            throw new DeterministicValidationException(
                "EVID001: repository ledger subject is not canonical.");
        }

        RequireCanonicalBytes(
            bytes.Span,
            SerializeRepositorySubject(subject),
            "repository ledger subject");
        return subject;
    }

    public static EvidenceDraftDocument ParseDraftDocument(ReadOnlyMemory<byte> bytes)
    {
        var draft = ParseDocument(bytes, ParseDraftDocument);
        if (draft.SchemaVersion != EvidenceSchemaVersion || draft.Records.Count == 0)
        {
            throw new DeterministicValidationException(
                "EVID001: evidence draft requires schema 1 and records.");
        }

        return draft with
        {
            Records = draft.Records.Select(EvidenceIdentity.NormalizeRecordDraft).ToArray()
        };
    }

    public static string ComputeStableId(
        string ledgerKind,
        RepositoryLedgerSubject? repositorySubject,
        ExactAssessmentIdentity? componentSubject,
        EvidenceRecordDraft record,
        IEvidenceHasher? hasher = null)
    {
        var preimage = GetRecordIdentityPreimage(
            ledgerKind,
            repositorySubject,
            componentSubject,
            record);
        var digest = (hasher ?? DefaultHasher).Hash(preimage);
        if (digest.Length != SHA256.HashSizeInBytes)
        {
            throw new DeterministicValidationException(
                "EVID003: evidence hasher must return exactly 32 bytes.");
        }

        return "EV1-" + Convert.ToHexStringLower(digest);
    }

    public static byte[] GetRecordIdentityPreimage(
        string ledgerKind,
        RepositoryLedgerSubject? repositorySubject,
        ExactAssessmentIdentity? componentSubject,
        EvidenceRecordDraft record)
    {
        var normalizedRecord = EvidenceIdentity.NormalizeRecordDraft(record);
        (repositorySubject, componentSubject) = NormalizeSubject(
            ledgerKind,
            repositorySubject,
            componentSubject);
        var subjectWriter = new CanonicalJsonTextWriter();
        WriteSubjectEnvelope(
            subjectWriter,
            ledgerKind,
            repositorySubject,
            componentSubject);
        var recordWriter = new CanonicalJsonTextWriter();
        WriteRecordIdentityPayload(recordWriter, normalizedRecord);
        return BuildDomainPreimage(
            EvidenceRecordDomain,
            subjectWriter.ToArray(),
            recordWriter.ToArray());
    }

    public static string ComputeSourceLedgerSha256(EvidenceSourceLedger ledger) =>
        ComputeSha256(SerializeSourceLedger(ledger));

    public static string ComputeBundleSha256(EvidenceBundle bundle) =>
        ComputeSha256(SerializeBundle(bundle));

    public static string ComputeSha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static T ParseDocument<T>(ReadOnlyMemory<byte> bytes, Func<JsonElement, T> parser)
    {
        if (bytes.Span.StartsWith(Utf8Bom))
        {
            throw new DeterministicValidationException(
                "EVID001: canonical evidence JSON must not contain a byte-order mark.");
        }

        try
        {
            _ = StrictUtf8.GetString(bytes.Span);
            using var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64
                });
            return parser(document.RootElement);
        }
        catch (Exception exception) when (
            exception is JsonException or
            DecoderFallbackException or
            FormatException or
            OverflowException)
        {
            throw new DeterministicValidationException(
                $"EVID001: invalid canonical evidence JSON: {exception.Message}",
                exception);
        }
    }

    private static EvidenceSourceLedger ParseSourceLedger(JsonElement element)
    {
        RequireObjectProperties(
            element,
            "schema_version",
            "ledger_kind",
            "repository_subject",
            "component_subject",
            "records");
        return new EvidenceSourceLedger(
            GetRequiredInt32(element, "schema_version"),
            GetRequiredString(element, "ledger_kind"),
            GetNullableObject(element, "repository_subject", ParseRepositorySubject),
            GetNullableObject(element, "component_subject", ParseAssessment),
            GetRequiredArray(element, "records")
                .EnumerateArray()
                .Select(ParseEvidenceRecord)
                .ToArray());
    }

    private static EvidenceBundle ParseBundle(JsonElement element)
    {
        RequireObjectProperties(
            element,
            "schema_version",
            "assessment",
            "source_ledgers",
            "selection");
        return new EvidenceBundle(
            GetRequiredInt32(element, "schema_version"),
            ParseAssessment(GetRequiredObject(element, "assessment")),
            GetRequiredArray(element, "source_ledgers")
                .EnumerateArray()
                .Select(ParseEmbeddedSourceLedger)
                .ToArray(),
            GetRequiredArray(element, "selection")
                .EnumerateArray()
                .Select(ParseSelection)
                .ToArray());
    }

    private static EvidenceDraftDocument ParseDraftDocument(JsonElement element)
    {
        RequireObjectProperties(element, "schema_version", "records");
        return new EvidenceDraftDocument(
            GetRequiredInt32(element, "schema_version"),
            GetRequiredArray(element, "records")
                .EnumerateArray()
                .Select(ParseEvidenceRecordDraft)
                .ToArray());
    }

    private static ExactAssessmentIdentity ParseAssessment(JsonElement element)
    {
        RequireObjectProperties(
            element,
            "assessment_kind",
            "package",
            "input_manifest_sha256",
            "component_id");
        return new ExactAssessmentIdentity(
            GetRequiredString(element, "assessment_kind"),
            ParsePackage(GetRequiredObject(element, "package")),
            ParseDigest(GetRequiredObject(element, "input_manifest_sha256")),
            GetNullableString(element, "component_id"));
    }

    private static RepositoryLedgerSubject ParseRepositorySubject(JsonElement element)
    {
        RequireObjectProperties(
            element,
            "assessment_kind",
            "package",
            "input_manifest_sha256",
            "component_id");
        return new RepositoryLedgerSubject(
            GetRequiredString(element, "assessment_kind"),
            ParsePackage(GetRequiredObject(element, "package")),
            ParseDigest(GetRequiredObject(element, "input_manifest_sha256")),
            GetNullableString(element, "component_id"));
    }

    private static EvidencePackageIdentity ParsePackage(JsonElement element)
    {
        RequireObjectProperties(element, "package_id", "version", "nupkg_sha256");
        return new EvidencePackageIdentity(
            GetRequiredString(element, "package_id"),
            GetRequiredString(element, "version"),
            ParseDigest(GetRequiredObject(element, "nupkg_sha256")));
    }

    private static Sha256Digest ParseDigest(JsonElement element)
    {
        RequireObjectProperties(element, "algorithm", "value");
        return new Sha256Digest(
            GetRequiredString(element, "algorithm"),
            GetRequiredString(element, "value"));
    }

    private static EvidenceRecord ParseEvidenceRecord(JsonElement element)
    {
        RequireObjectProperties(
            element,
            "stable_id",
            "claim",
            "applicability",
            "provenance",
            "supersedes");
        return new EvidenceRecord(
            GetRequiredString(element, "stable_id"),
            GetRequiredString(element, "claim"),
            ParseApplicability(GetRequiredObject(element, "applicability")),
            ParseProvenance(GetRequiredObject(element, "provenance")),
            GetRequiredArray(element, "supersedes")
                .EnumerateArray()
                .Select(GetStringValue)
                .ToArray());
    }

    private static EvidenceRecordDraft ParseEvidenceRecordDraft(JsonElement element)
    {
        RequireObjectProperties(
            element,
            "claim",
            "applicability",
            "provenance",
            "supersedes");
        return new EvidenceRecordDraft(
            GetRequiredString(element, "claim"),
            ParseApplicability(GetRequiredObject(element, "applicability")),
            ParseProvenance(GetRequiredObject(element, "provenance")),
            GetRequiredArray(element, "supersedes")
                .EnumerateArray()
                .Select(GetStringValue)
                .ToArray());
    }

    private static EvidenceApplicability ParseApplicability(JsonElement element)
    {
        RequireObjectProperties(element, "scope", "component_id");
        return new EvidenceApplicability(
            GetRequiredString(element, "scope"),
            GetNullableString(element, "component_id"));
    }

    private static EvidenceProvenance ParseProvenance(JsonElement element)
    {
        RequireObjectProperties(
            element,
            "kind",
            "locator",
            "method",
            "captured_at_utc",
            "content_sha256",
            "retention");
        return new EvidenceProvenance(
            GetRequiredString(element, "kind"),
            GetRequiredString(element, "locator"),
            GetRequiredString(element, "method"),
            GetRequiredString(element, "captured_at_utc"),
            ParseDigest(GetRequiredObject(element, "content_sha256")),
            GetRequiredString(element, "retention"));
    }

    private static EmbeddedSourceLedger ParseEmbeddedSourceLedger(JsonElement element)
    {
        RequireObjectProperties(element, "source_ledger_sha256", "ledger");
        return new EmbeddedSourceLedger(
            GetRequiredString(element, "source_ledger_sha256"),
            ParseSourceLedger(GetRequiredObject(element, "ledger")));
    }

    private static EvidenceSelection ParseSelection(JsonElement element)
    {
        RequireObjectProperties(
            element,
            "display_order",
            "source_ledger_sha256",
            "evidence_id");
        return new EvidenceSelection(
            GetRequiredInt32(element, "display_order"),
            GetRequiredString(element, "source_ledger_sha256"),
            GetRequiredString(element, "evidence_id"));
    }

    private static void WriteAssessment(
        CanonicalJsonTextWriter writer,
        ExactAssessmentIdentity assessment)
    {
        writer.Raw("{\"assessment_kind\":");
        writer.String(assessment.AssessmentKind);
        writer.Raw(",\"package\":");
        WritePackage(writer, assessment.Package);
        writer.Raw(",\"input_manifest_sha256\":");
        WriteDigest(writer, assessment.InputManifestDigest);
        writer.Raw(",\"component_id\":");
        if (assessment.ComponentId is null)
        {
            writer.Raw("null");
        }
        else
        {
            writer.String(assessment.ComponentId);
        }

        writer.Raw("}");
    }

    private static void WriteRepositorySubject(
        CanonicalJsonTextWriter writer,
        RepositoryLedgerSubject subject)
    {
        writer.Raw("{\"assessment_kind\":");
        writer.String(subject.AssessmentKind);
        writer.Raw(",\"package\":");
        WritePackage(writer, subject.Package);
        writer.Raw(",\"input_manifest_sha256\":");
        WriteDigest(writer, subject.InputManifestDigest);
        writer.Raw(",\"component_id\":");
        if (subject.ComponentId is null)
        {
            writer.Raw("null");
        }
        else
        {
            writer.String(subject.ComponentId);
        }

        writer.Raw("}");
    }

    private static void WritePackage(
        CanonicalJsonTextWriter writer,
        EvidencePackageIdentity package)
    {
        writer.Raw("{\"package_id\":");
        writer.String(package.PackageId);
        writer.Raw(",\"version\":");
        writer.String(package.Version);
        writer.Raw(",\"nupkg_sha256\":");
        WriteDigest(writer, package.NupkgDigest);
        writer.Raw("}");
    }

    private static void WriteDigest(CanonicalJsonTextWriter writer, Sha256Digest digest)
    {
        writer.Raw("{\"algorithm\":");
        writer.String(digest.Algorithm);
        writer.Raw(",\"value\":");
        writer.String(digest.Value);
        writer.Raw("}");
    }

    private static void WriteSourceLedger(
        CanonicalJsonTextWriter writer,
        EvidenceSourceLedger ledger)
    {
        writer.Raw("{\"schema_version\":");
        writer.Integer(ledger.SchemaVersion);
        writer.Raw(",\"ledger_kind\":");
        writer.String(ledger.LedgerKind);
        writer.Raw(",\"repository_subject\":");
        if (ledger.RepositorySubject is null)
        {
            writer.Raw("null");
        }
        else
        {
            WriteRepositorySubject(writer, ledger.RepositorySubject);
        }

        writer.Raw(",\"component_subject\":");
        if (ledger.ComponentSubject is null)
        {
            writer.Raw("null");
        }
        else
        {
            WriteAssessment(writer, ledger.ComponentSubject);
        }

        writer.Raw(",\"records\":[");
        for (var index = 0; index < ledger.Records.Count; index++)
        {
            if (index > 0)
            {
                writer.Raw(",");
            }

            WriteEvidenceRecord(writer, ledger.Records[index]);
        }

        writer.Raw("]}");
    }

    private static void WriteEvidenceRecord(
        CanonicalJsonTextWriter writer,
        EvidenceRecord record)
    {
        writer.Raw("{\"stable_id\":");
        writer.String(record.StableId);
        writer.Raw(",\"claim\":");
        writer.String(record.Claim);
        writer.Raw(",\"applicability\":");
        WriteApplicability(writer, record.Applicability);
        writer.Raw(",\"provenance\":");
        WriteProvenance(writer, record.Provenance);
        writer.Raw(",\"supersedes\":");
        WriteStringArray(writer, record.Supersedes);
        writer.Raw("}");
    }

    private static void WriteRecordIdentityPayload(
        CanonicalJsonTextWriter writer,
        EvidenceRecordDraft record)
    {
        writer.Raw("{\"claim\":");
        writer.String(record.Claim);
        writer.Raw(",\"applicability\":");
        WriteApplicability(writer, record.Applicability);
        writer.Raw(",\"provenance\":");
        WriteProvenance(writer, record.Provenance);
        writer.Raw(",\"supersedes\":");
        WriteStringArray(writer, record.Supersedes);
        writer.Raw("}");
    }

    private static void WriteApplicability(
        CanonicalJsonTextWriter writer,
        EvidenceApplicability applicability)
    {
        writer.Raw("{\"scope\":");
        writer.String(applicability.Scope);
        writer.Raw(",\"component_id\":");
        if (applicability.ComponentId is null)
        {
            writer.Raw("null");
        }
        else
        {
            writer.String(applicability.ComponentId);
        }

        writer.Raw("}");
    }

    private static void WriteProvenance(
        CanonicalJsonTextWriter writer,
        EvidenceProvenance provenance)
    {
        writer.Raw("{\"kind\":");
        writer.String(provenance.Kind);
        writer.Raw(",\"locator\":");
        writer.String(provenance.Locator);
        writer.Raw(",\"method\":");
        writer.String(provenance.Method);
        writer.Raw(",\"captured_at_utc\":");
        writer.String(provenance.CapturedAtUtc);
        writer.Raw(",\"content_sha256\":");
        WriteDigest(writer, provenance.ContentDigest);
        writer.Raw(",\"retention\":");
        writer.String(provenance.Retention);
        writer.Raw("}");
    }

    private static void WriteSubjectEnvelope(
        CanonicalJsonTextWriter writer,
        string ledgerKind,
        RepositoryLedgerSubject? repositorySubject,
        ExactAssessmentIdentity? componentSubject)
    {
        writer.Raw("{\"ledger_kind\":");
        writer.String(ledgerKind);
        writer.Raw(",\"repository_subject\":");
        if (repositorySubject is null)
        {
            writer.Raw("null");
        }
        else
        {
            WriteRepositorySubject(writer, repositorySubject);
        }

        writer.Raw(",\"component_subject\":");
        if (componentSubject is null)
        {
            writer.Raw("null");
        }
        else
        {
            WriteAssessment(writer, componentSubject);
        }

        writer.Raw("}");
    }

    private static void WriteBundle(CanonicalJsonTextWriter writer, EvidenceBundle bundle)
    {
        writer.Raw("{\"schema_version\":");
        writer.Integer(bundle.SchemaVersion);
        writer.Raw(",\"assessment\":");
        WriteAssessment(writer, bundle.Assessment);
        writer.Raw(",\"source_ledgers\":[");
        for (var index = 0; index < bundle.SourceLedgers.Count; index++)
        {
            if (index > 0)
            {
                writer.Raw(",");
            }

            var source = bundle.SourceLedgers[index];
            writer.Raw("{\"source_ledger_sha256\":");
            writer.String(source.SourceLedgerSha256);
            writer.Raw(",\"ledger\":");
            WriteSourceLedger(writer, source.Ledger);
            writer.Raw("}");
        }

        writer.Raw("],\"selection\":[");
        for (var index = 0; index < bundle.Selection.Count; index++)
        {
            if (index > 0)
            {
                writer.Raw(",");
            }

            var selection = bundle.Selection[index];
            writer.Raw("{\"display_order\":");
            writer.Integer(selection.DisplayOrder);
            writer.Raw(",\"source_ledger_sha256\":");
            writer.String(selection.SourceLedgerSha256);
            writer.Raw(",\"evidence_id\":");
            writer.String(selection.EvidenceId);
            writer.Raw("}");
        }

        writer.Raw("]}");
    }

    private static void WriteStringArray(
        CanonicalJsonTextWriter writer,
        IReadOnlyList<string> values)
    {
        writer.Raw("[");
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
            {
                writer.Raw(",");
            }

            writer.String(values[index]);
        }

        writer.Raw("]");
    }

    private static byte[] BuildDomainPreimage(
        string domain,
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second)
    {
        var domainBytes = Encoding.ASCII.GetBytes(domain);
        var result = new byte[domainBytes.Length + 1 + first.Length + 1 + second.Length];
        var offset = 0;
        domainBytes.CopyTo(result, offset);
        offset += domainBytes.Length + 1;
        first.CopyTo(result.AsSpan(offset));
        offset += first.Length + 1;
        second.CopyTo(result.AsSpan(offset));
        return result;
    }

    private static (
        RepositoryLedgerSubject? RepositorySubject,
        ExactAssessmentIdentity? ComponentSubject) NormalizeSubject(
        string ledgerKind,
        RepositoryLedgerSubject? repositorySubject,
        ExactAssessmentIdentity? componentSubject)
    {
        return ledgerKind switch
        {
            "repository" when repositorySubject is not null && componentSubject is null =>
                (EvidenceIdentity.NormalizeRepositorySubject(repositorySubject), null),
            "component" when repositorySubject is null && componentSubject is not null =>
                (null, EvidenceIdentity.NormalizeAssessment(componentSubject)),
            _ => throw new DeterministicValidationException(
                "EVID007: stable evidence identity requires one canonical ledger subject.")
        };
    }

    private static void RequireCanonicalBytes(
        ReadOnlySpan<byte> actual,
        ReadOnlySpan<byte> expected,
        string artifact)
    {
        if (!actual.SequenceEqual(expected))
        {
            throw new DeterministicValidationException(
                $"EVID001: persisted {artifact} bytes are not canonical.");
        }
    }

    private static void RequireObjectProperties(
        JsonElement element,
        params string[] expectedNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new DeterministicValidationException("EVID001: expected a JSON object.");
        }

        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (!actual.SequenceEqual(expectedNames, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                "EVID001: object properties are missing, duplicated, unknown, or out of order. " +
                $"Expected [{string.Join(", ", expectedNames)}], " +
                $"found [{string.Join(", ", actual)}].");
        }
    }

    private static JsonElement GetRequiredObject(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new DeterministicValidationException(
                $"EVID001: '{name}' must be an object.");
        }

        return value;
    }

    private static JsonElement GetRequiredArray(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new DeterministicValidationException(
                $"EVID001: '{name}' must be an array.");
        }

        return value;
    }

    private static string GetRequiredString(JsonElement element, string name) =>
        GetStringValue(element.GetProperty(name));

    private static string GetStringValue(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new DeterministicValidationException(
                "EVID001: expected a JSON string.");
        }

        return element.GetString()!;
    }

    private static string? GetNullableString(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => throw new DeterministicValidationException(
                $"EVID001: '{name}' must be a string or null.")
        };
    }

    private static T? GetNullableObject<T>(
        JsonElement element,
        string name,
        Func<JsonElement, T> parser)
        where T : class
    {
        var value = element.GetProperty(name);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Object => parser(value),
            _ => throw new DeterministicValidationException(
                $"EVID001: '{name}' must be an object or null.")
        };
    }

    private static int GetRequiredInt32(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw new DeterministicValidationException(
                $"EVID001: '{name}' must be an Int32.");
        }

        return result;
    }

    private sealed class Sha256EvidenceHasher : IEvidenceHasher
    {
        public byte[] Hash(ReadOnlySpan<byte> content) => SHA256.HashData(content);
    }

    private sealed class CanonicalJsonTextWriter
    {
        private readonly StringBuilder _builder = new();

        public void Raw(string value) => _builder.Append(value);

        public void Integer(int value) =>
            _builder.Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public void String(string value)
        {
            _builder.Append('"');
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"':
                        _builder.Append("\\\"");
                        break;
                    case '\\':
                        _builder.Append("\\\\");
                        break;
                    case <= '\u001f':
                        _builder.Append("\\u00");
                        _builder.Append(
                            ((int)character).ToString(
                                "x2",
                                System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    default:
                        _builder.Append(character);
                        break;
                }
            }

            _builder.Append('"');
        }

        public byte[] ToArray() => StrictUtf8.GetBytes(_builder.ToString());
    }
}
