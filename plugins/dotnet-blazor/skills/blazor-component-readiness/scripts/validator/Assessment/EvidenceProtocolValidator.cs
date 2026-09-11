using System.Text.Json;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Assessment;

public static class EvidenceProtocolValidator
{
    public const string LifecycleMethod = "protocol:dynamic-child-lifecycle-v1";
    public const string NoticeCoverageMethod = "protocol:notice-coverage-v1";
    public const string AuthenticodeMethod = "protocol:authenticode-verification-v1";
    public const string SupportOwnershipMethod = "protocol:support-ownership-v1";
    public const string ToolchainMethod = "protocol:toolchain-probe-v1";
    public const string SourceProofMethod = "protocol:source-proof-v1";
    public const string PublicAbsenceMethod = "protocol:public-absence-v1";
    public const string DirectFailureMethod = "protocol:direct-failure-v1";
    public const string AutoTransitionMethod = "protocol:auto-renderer-transition-v1";

    private static readonly string[] LifecycleOperations =
    [
        "add-child",
        "remove-child",
        "keyed-reorder",
        "disable-or-remove-selected-child",
        "membership-reconciliation",
        "propagated-name-default-value-state",
        "focus-ownership-restoration",
        "single-roving-tab-stop",
        "callbacks-error-routing",
        "cleanup-disposal"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> LifecycleRows =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["A11Y-06"] =
            [
                "add-child",
                "remove-child",
                "keyed-reorder",
                "disable-or-remove-selected-child",
                "membership-reconciliation",
                "propagated-name-default-value-state",
                "single-roving-tab-stop"
            ],
            ["A11Y-07"] =
            [
                "disable-or-remove-selected-child",
                "focus-ownership-restoration",
                "single-roving-tab-stop"
            ],
            ["A11Y-08"] =
            [
                "add-child",
                "membership-reconciliation",
                "propagated-name-default-value-state"
            ],
            ["BEQ-12"] = ["callbacks-error-routing"],
            ["BEQ-15"] = ["cleanup-disposal"]
        };
    private static readonly IReadOnlyDictionary<string, string> SourceProofKinds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BEQ-12"] = "async-callback-not-awaited",
            ["BEQ-15"] = "async-cleanup-not-awaited"
        };
    private static readonly IReadOnlyDictionary<string, PublicAbsenceRequirement> PublicAbsenceRequirements =
        new Dictionary<string, PublicAbsenceRequirement>(StringComparer.Ordinal)
        {
            ["SUP-03"] = new("public-support-corpus", "support-response-sla"),
            ["SUP-05"] = new("public-support-corpus", "security-patch-cadence"),
            ["SUP-06"] = new("public-support-corpus", "eol-advance-notice"),
            ["BEQ-05"] = new("public-document-corpus", "static-ssr-contract"),
            ["CI-09"] = new("sample-inventory", "behavioral-assertions")
        };
    private static readonly IReadOnlyDictionary<string, DirectFailureRequirement> DirectFailureRequirements =
        new Dictionary<string, DirectFailureRequirement>(StringComparer.Ordinal)
        {
            ["CI-09"] = new(
                "sample-compilation-failed",
                "sample-compilation-result")
        };

    public static void Validate(
        string root,
        ReadinessAssessment assessment,
        InputManifest input,
        EvidenceBundle evidence,
        bool enforceCurrentProtocols,
        bool enforceAutoTransitionProtocol = true)
    {
        var selected = evidence.Selection
            .Select(selection => selection.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);
        var records = evidence.SourceLedgers
            .SelectMany(source => source.Ledger.Records)
            .Where(record => selected.Contains(record.StableId))
            .ToDictionary(record => record.StableId, StringComparer.Ordinal);

        var protocols = new Dictionary<string, ParsedProtocol>(StringComparer.Ordinal);
        foreach (var record in records.Values.Where(record =>
                     record.Provenance.Method.StartsWith("protocol:", StringComparison.Ordinal)))
        {
            if (!protocols.TryAdd(
                    record.StableId,
                    ParseProtocol(root, input, assessment.Identity, record)))
            {
                throw new DeterministicValidationException(
                    $"Evidence protocol '{record.StableId}' is duplicated.");
            }
        }

        ValidateDirectedGapProtocols(assessment, protocols);
        if (enforceCurrentProtocols)
        {
            RequireTypedProtocolForGapRows(assessment, protocols);
        }

        ValidateLifecycle(
            assessment,
            input,
            records,
            protocols,
            enforceCurrentProtocols);
        RequireProtocolForOutcomeRow(
            assessment,
            records,
            protocols,
            "LP-04",
            NoticeCoverageMethod,
            "complete",
            "incomplete");
        RequireProtocolForOutcomeRow(
            assessment,
            records,
            protocols,
            "PI-02",
            AuthenticodeMethod,
            "passed",
            "failed");
        RequireProtocolForVerifiedRow(
            assessment,
            records,
            protocols,
            "SUP-01",
            SupportOwnershipMethod);
        ValidateToolchainRows(assessment, input, records, protocols);
        if (enforceAutoTransitionProtocol)
        {
            RequireAutoTransitionProtocol(root, assessment, input, protocols);
        }
    }

    private static ParsedProtocol ParseProtocol(
        string root,
        InputManifest input,
        ExactAssessmentIdentity assessmentIdentity,
        EvidenceRecord record)
    {
        var bytes = ReadProtocolBytes(root, input, record);
        return record.Provenance.Method switch
        {
            LifecycleMethod => ParseLifecycle(bytes, input),
            NoticeCoverageMethod => ParseNoticeCoverage(bytes, input),
            AuthenticodeMethod => ParseAuthenticode(root, bytes, input),
            SupportOwnershipMethod => ParseSupportOwnership(bytes),
            ToolchainMethod => ParseToolchain(bytes, input),
            SourceProofMethod => ParseSourceProof(
                bytes,
                input,
                assessmentIdentity,
                record),
            PublicAbsenceMethod => ParsePublicAbsence(root, bytes, input),
            DirectFailureMethod => ParseDirectFailure(
                root,
                bytes,
                input,
                assessmentIdentity,
                record),
            AutoTransitionMethod => ParseAutoTransition(
                root,
                bytes,
                input,
                assessmentIdentity),
            _ => throw new DeterministicValidationException(
                $"Evidence '{record.StableId}' uses unknown structured method '{record.Provenance.Method}'.")
        };
    }

    private static byte[] ReadProtocolBytes(
        string root,
        InputManifest input,
        EvidenceRecord record)
    {
        var provenance = record.Provenance;
        var basename = Canonicalization.Basename(
            provenance.Locator,
            "structured evidence locator");
        var evidenceInput = input.EvidenceInputs.SingleOrDefault(item =>
            item.Basename == basename &&
            item.ContentDigest == provenance.ContentDigest);
        var ownerInput = input.OwnerInputs.SingleOrDefault(item =>
            item.Basename == basename &&
            item.ContentDigest == provenance.ContentDigest);
        var bound = provenance.Kind switch
        {
            EvidenceIdentity.ReproducedRuntimeObservation or
                EvidenceIdentity.ReviewerGeneratedAnalysis => evidenceInput is not null,
            EvidenceIdentity.OwnerSuppliedInternalEvidence or
                EvidenceIdentity.OwnerSuppliedPublicEvidence => ownerInput is not null,
            _ => false
        };
        if (!bound)
        {
            throw new DeterministicValidationException(
                $"Evidence '{record.StableId}' structured protocol is not bound to a compatible confirmed input.");
        }

        return BoundedIO.ReadAllBytes(
            SafePath.ResolveUnderRoot(root, basename, requireExisting: true, requireFile: true),
            ResourceLimits.SupplementalInputAggregateBytes,
            "structured evidence protocol");
    }

    private static ParsedProtocol ParseLifecycle(
        ReadOnlyMemory<byte> bytes,
        InputManifest input)
    {
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SupplementalInputAggregateBytes,
            "dynamic child lifecycle protocol");
        var root = document.RootElement;
        ContractJson.RequireProperties(root, "schema_version", "protocol", "operations");
        RequireProtocolHeader(root, "dynamic-child-lifecycle");
        var operations = ContractJson.Array(root, "operations")
            .EnumerateArray()
            .Select(ParseLifecycleOperation)
            .ToArray();
        if (!operations.Select(item => item.Operation)
                .SequenceEqual(LifecycleOperations, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                "Dynamic child lifecycle protocol must contain every required operation exactly once in canonical order.");
        }

        foreach (var operation in operations)
        {
            if (operation.Disposition == "observed")
            {
                if (operation.Outcome is not ("passed" or "failed") ||
                    operation.RawObservationDigest is null ||
                    operation.NotTestedReason is not null ||
                    !HasInputDigest(input, operation.RawObservationDigest, "raw-observation"))
                {
                    throw new DeterministicValidationException(
                        $"Lifecycle operation '{operation.Operation}' requires a digest-bound raw observation and passed/failed outcome.");
                }
            }
            else if (operation.Disposition == "not-tested")
            {
                if (operation.Outcome is not null ||
                    operation.RawObservationDigest is not null ||
                    !IsSubstantive(operation.NotTestedReason))
                {
                    throw new DeterministicValidationException(
                        $"Lifecycle operation '{operation.Operation}' requires an explicit not-tested reason.");
                }
            }
            else
            {
                throw new DeterministicValidationException(
                    $"Lifecycle operation '{operation.Operation}' has invalid disposition.");
            }
        }

        var canonical = SerializeLifecycle(operations);
        ContractJson.RequireCanonical(bytes.Span, canonical, "dynamic child lifecycle protocol");
        return new ParsedProtocol(
            LifecycleMethod,
            operations.ToDictionary(item => item.Operation, StringComparer.Ordinal),
            null);
    }

    private static ParsedProtocol ParseSourceProof(
        ReadOnlyMemory<byte> bytes,
        InputManifest input,
        ExactAssessmentIdentity assessmentIdentity,
        EvidenceRecord record)
    {
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SupplementalInputAggregateBytes,
            "source proof protocol");
        var root = document.RootElement;
        ContractJson.RequireProperties(
            root,
            "schema_version",
            "protocol",
            "requirement_id",
            "proof_kind",
            "result",
            "source_path",
            "source_sha256");
        RequireProtocolHeader(root, "source-proof");
        var requirementId = ContractJson.String(root, "requirement_id");
        var proofKind = ContractJson.String(root, "proof_kind");
        if (!SourceProofKinds.TryGetValue(requirementId, out var expectedProofKind) ||
            proofKind != expectedProofKind ||
            ContractJson.String(root, "result") != "failed")
        {
            throw new DeterministicValidationException(
                "Source proof protocol has an unsupported requirement, proof kind, or result.");
        }

        var sourcePath = Canonicalization.RelativePath(
            ContractJson.String(root, "source_path"),
            "source proof path");
        var sourceDigest = ContractJson.Digest(ContractJson.Object(root, "source_sha256"));
        var componentId = assessmentIdentity.ComponentId
            ?? throw new DeterministicValidationException(
                "Source proof protocol requires a component assessment identity.");
        var component = input.Components.SingleOrDefault(item => item.Id == componentId)
            ?? throw new DeterministicValidationException(
                "Source proof protocol component is absent from the confirmed input.");
        if (!input.SourceArtifacts.Any(item =>
                item.SourcePath == sourcePath &&
                item.ContentDigest == sourceDigest) ||
            !component.AllowedSourcePaths.Contains(sourcePath, StringComparer.Ordinal) ||
            component.DynamicChildLifecycle.Applicability != "required" ||
            record.Applicability.Scope != "component-specific" ||
            record.Applicability.ComponentId != componentId)
        {
            throw new DeterministicValidationException(
                "Source proof protocol must bind the assessed lifecycle-required component and one of its allowed confirmed source artifacts.");
        }

        var canonical = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "source-proof");
            writer.WriteString("requirement_id", requirementId);
            writer.WriteString("proof_kind", proofKind);
            writer.WriteString("result", "failed");
            writer.WriteString("source_path", sourcePath);
            ContractJson.WriteDigest(writer, "source_sha256", sourceDigest);
            writer.WriteEndObject();
        });
        ContractJson.RequireCanonical(bytes.Span, canonical, "source proof protocol");
        return new ParsedProtocol(SourceProofMethod, null, "failed", requirementId);
    }

    private static ParsedProtocol ParsePublicAbsence(
        string root,
        ReadOnlyMemory<byte> bytes,
        InputManifest input)
    {
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SupplementalInputAggregateBytes,
            "public absence protocol");
        var value = document.RootElement;
        ContractJson.RequireProperties(
            value,
            "schema_version",
            "protocol",
            "requirement_id",
            "corpus_kind",
            "result",
            "corpus_sha256");
        RequireProtocolHeader(value, "public-absence");
        var requirementId = ContractJson.String(value, "requirement_id");
        var corpusKind = ContractJson.String(value, "corpus_kind");
        if (!PublicAbsenceRequirements.TryGetValue(requirementId, out var requirement) ||
            corpusKind != requirement.CorpusKind ||
            ContractJson.String(value, "result") != "absent")
        {
            throw new DeterministicValidationException(
                "Public absence protocol has an unsupported requirement, corpus kind, or result.");
        }

        var corpusDigest = ContractJson.Digest(ContractJson.Object(value, "corpus_sha256"));
        var corpusInputs = input.EvidenceInputs.Where(item =>
                item.Kind == corpusKind &&
                item.ContentDigest == corpusDigest)
            .ToArray();
        if (corpusInputs.Length != 1)
        {
            throw new DeterministicValidationException(
                "Public absence protocol must bind exactly one confirmed typed public corpus.");
        }
        ValidatePublicCorpus(
            root,
            corpusInputs[0],
            requirement.RequiredMarker);

        var canonical = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "public-absence");
            writer.WriteString("requirement_id", requirementId);
            writer.WriteString("corpus_kind", corpusKind);
            writer.WriteString("result", "absent");
            ContractJson.WriteDigest(writer, "corpus_sha256", corpusDigest);
            writer.WriteEndObject();
        });
        ContractJson.RequireCanonical(bytes.Span, canonical, "public absence protocol");
        return new ParsedProtocol(PublicAbsenceMethod, null, "absent", requirementId);
    }

    private static ParsedProtocol ParseDirectFailure(
        string root,
        ReadOnlyMemory<byte> bytes,
        InputManifest input,
        ExactAssessmentIdentity assessmentIdentity,
        EvidenceRecord record)
    {
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SupplementalInputAggregateBytes,
            "direct failure protocol");
        var value = document.RootElement;
        ContractJson.RequireProperties(
            value,
            "schema_version",
            "protocol",
            "requirement_id",
            "cause_kind",
            "result",
            "evidence_sha256");
        RequireProtocolHeader(value, "direct-failure");
        var requirementId = ContractJson.String(value, "requirement_id");
        var causeKind = ContractJson.String(value, "cause_kind");
        if (!DirectFailureRequirements.TryGetValue(requirementId, out var requirement) ||
            causeKind != requirement.CauseKind ||
            ContractJson.String(value, "result") != "failed")
        {
            throw new DeterministicValidationException(
                "Direct failure protocol has an unsupported requirement, cause kind, or result.");
        }

        var evidenceDigest = ContractJson.Digest(
            ContractJson.Object(value, "evidence_sha256"));
        var evidenceInputs = input.EvidenceInputs.Where(item =>
                item.Kind == requirement.EvidenceKind &&
                item.ContentDigest == evidenceDigest)
            .ToArray();
        if (evidenceInputs.Length != 1)
        {
            throw new DeterministicValidationException(
                "Direct failure protocol must bind exactly one confirmed typed failure artifact.");
        }

        ValidateDirectFailureArtifact(
            root,
            evidenceInputs[0],
            input,
            assessmentIdentity,
            record,
            requirementId,
            causeKind);
        var canonical = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "direct-failure");
            writer.WriteString("requirement_id", requirementId);
            writer.WriteString("cause_kind", causeKind);
            writer.WriteString("result", "failed");
            ContractJson.WriteDigest(writer, "evidence_sha256", evidenceDigest);
            writer.WriteEndObject();
        });
        ContractJson.RequireCanonical(bytes.Span, canonical, "direct failure protocol");
        return new ParsedProtocol(DirectFailureMethod, null, "failed", requirementId);
    }

    private static void ValidateDirectFailureArtifact(
        string root,
        InputEvidenceArtifact evidenceInput,
        InputManifest input,
        ExactAssessmentIdentity assessmentIdentity,
        EvidenceRecord record,
        string requirementId,
        string causeKind)
    {
        var bytes = BoundedIO.ReadAllBytes(
            SafePath.ResolveUnderRoot(
                root,
                evidenceInput.Basename,
                requireExisting: true,
                requireFile: true),
            ResourceLimits.SupplementalInputAggregateBytes,
            "typed direct failure artifact");
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SupplementalInputAggregateBytes,
            "typed direct failure artifact");
        var value = document.RootElement;
        ContractJson.RequireProperties(
            value,
            "schema_version",
            "evidence_kind",
            "package_sha256",
            "component_id",
            "requirement_id",
            "cause_kind",
            "sample_source_path",
            "sample_source_sha256",
            "toolchain_sha256",
            "raw_log_sha256",
            "required_surface_present",
            "outcome");
        var componentId = assessmentIdentity.ComponentId;
        var samplePath = Canonicalization.RelativePath(
            ContractJson.String(value, "sample_source_path"),
            "direct failure sample source path");
        var sampleDigest = ContractJson.Digest(
            ContractJson.Object(value, "sample_source_sha256"));
        var toolchainDigest = ContractJson.Digest(
            ContractJson.Object(value, "toolchain_sha256"));
        var rawLogDigest = ContractJson.Digest(
            ContractJson.Object(value, "raw_log_sha256"));
        var component = componentId is null
            ? null
            : input.Components.SingleOrDefault(item => item.Id == componentId);
        if (ContractJson.Int32(value, "schema_version") != 1 ||
            ContractJson.String(value, "evidence_kind") != evidenceInput.Kind ||
            ContractJson.Digest(
                ContractJson.Object(value, "package_sha256")) !=
                assessmentIdentity.Package.NupkgDigest ||
            ContractJson.NullableString(value, "component_id") != componentId ||
            ContractJson.String(value, "requirement_id") != requirementId ||
            ContractJson.String(value, "cause_kind") != causeKind ||
            value.GetProperty("required_surface_present").ValueKind != JsonValueKind.True ||
            ContractJson.String(value, "outcome") != "failed" ||
            component is null ||
            !component.AllowedSourcePaths.Contains(samplePath, StringComparer.Ordinal) ||
            !input.SourceArtifacts.Any(item =>
                item.SourcePath == samplePath &&
                item.ContentDigest == sampleDigest) ||
            input.EvidenceInputs.Count(item =>
                item.Kind == "toolchain-identity" &&
                item.ContentDigest == toolchainDigest) != 1 ||
            input.EvidenceInputs.Count(item =>
                item.Kind == "sample-compilation-log" &&
                item.ContentDigest == rawLogDigest) != 1 ||
            record.Applicability.Scope != "component-specific" ||
            record.Applicability.ComponentId != componentId)
        {
            throw new DeterministicValidationException(
                "Typed direct failure artifact does not match the assessed component, package, input, sample, toolchain, raw log, requirement, and cause.");
        }

        var canonical = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("evidence_kind", evidenceInput.Kind);
            ContractJson.WriteDigest(
                writer,
                "package_sha256",
                assessmentIdentity.Package.NupkgDigest);
            writer.WriteString("component_id", componentId);
            writer.WriteString("requirement_id", requirementId);
            writer.WriteString("cause_kind", causeKind);
            writer.WriteString("sample_source_path", samplePath);
            ContractJson.WriteDigest(writer, "sample_source_sha256", sampleDigest);
            ContractJson.WriteDigest(writer, "toolchain_sha256", toolchainDigest);
            ContractJson.WriteDigest(writer, "raw_log_sha256", rawLogDigest);
            writer.WriteBoolean("required_surface_present", true);
            writer.WriteString("outcome", "failed");
            writer.WriteEndObject();
        });
        ContractJson.RequireCanonical(
            bytes.AsSpan(),
            canonical,
            "typed direct failure artifact");
    }

    private static void ValidatePublicCorpus(
        string root,
        InputEvidenceArtifact corpusInput,
        string requiredMarker)
    {
        var bytes = BoundedIO.ReadAllBytes(
            SafePath.ResolveUnderRoot(
                root,
                corpusInput.Basename,
                requireExisting: true,
                requireFile: true),
            ResourceLimits.SupplementalInputAggregateBytes,
            "typed public corpus");
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SupplementalInputAggregateBytes,
            "typed public corpus");
        var value = document.RootElement;
        ContractJson.RequireProperties(
            value,
            "schema_version",
            "corpus_kind",
            "owner_controlled",
            "complete",
            "covered_markers",
            "present_markers");
        var corpusKind = ContractJson.String(value, "corpus_kind");
        if (ContractJson.Int32(value, "schema_version") != 1 ||
            corpusKind != corpusInput.Kind ||
            value.GetProperty("owner_controlled").ValueKind != JsonValueKind.True ||
            value.GetProperty("complete").ValueKind != JsonValueKind.True)
        {
            throw new DeterministicValidationException(
                "Typed public corpus must be owner-controlled and complete schema version 1.");
        }

        var covered = CanonicalMarkers(
            ContractJson.StringArray(value, "covered_markers"),
            "covered public-corpus marker");
        var present = CanonicalMarkers(
            ContractJson.StringArray(value, "present_markers"),
            "present public-corpus marker");
        if (!covered.Contains(requiredMarker, StringComparer.Ordinal) ||
            present.Contains(requiredMarker, StringComparer.Ordinal) ||
            present.Except(covered, StringComparer.Ordinal).Any())
        {
            throw new DeterministicValidationException(
                $"Typed public corpus does not establish required marker '{requiredMarker}' as absent.");
        }

        var canonical = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("corpus_kind", corpusKind);
            writer.WriteBoolean("owner_controlled", true);
            writer.WriteBoolean("complete", true);
            WriteStrings(writer, "covered_markers", covered);
            WriteStrings(writer, "present_markers", present);
            writer.WriteEndObject();
        });
        ContractJson.RequireCanonical(bytes.AsSpan(), canonical, "typed public corpus");
    }

    private static void ValidateDirectedGapProtocols(
        ReadinessAssessment assessment,
        IReadOnlyDictionary<string, ParsedProtocol> protocols)
    {
        foreach (var (evidenceId, protocol) in protocols.Where(item =>
                     item.Value.Method is SourceProofMethod or PublicAbsenceMethod or DirectFailureMethod))
        {
            var row = assessment.Rows.SingleOrDefault(item =>
                item.Id == protocol.RequirementId);
            if (row?.Status != "gap" ||
                !row.EvidenceIds.Contains(evidenceId, StringComparer.Ordinal))
            {
                throw new DeterministicValidationException(
                    $"Protocol '{protocol.Method}' for '{protocol.RequirementId}' must be cited by that row with status 'gap'.");
            }
        }

        foreach (var group in protocols
                     .Where(item =>
                         item.Value.Method is
                             SourceProofMethod or
                             PublicAbsenceMethod or
                             DirectFailureMethod)
                     .GroupBy(item => item.Value.RequirementId, StringComparer.Ordinal))
        {
            var citedFamilies = group
                .Where(item =>
                    assessment.Rows.Single(row => row.Id == group.Key)
                        .EvidenceIds.Contains(item.Key, StringComparer.Ordinal))
                .Select(item => item.Value.Method)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (citedFamilies.Length > 1)
            {
                throw new DeterministicValidationException(
                    $"Requirement '{group.Key}' cannot cite mutually exclusive directed-gap protocol families: " +
                    $"{string.Join(", ", citedFamilies)}.");
            }
        }
    }

    private static void RequireTypedProtocolForGapRows(
        ReadinessAssessment assessment,
        IReadOnlyDictionary<string, ParsedProtocol> protocols)
    {
        foreach (var requirementId in PublicAbsenceRequirements.Keys)
        {
            var row = assessment.Rows.SingleOrDefault(item => item.Id == requirementId);
            if (row?.Status != "gap")
            {
                continue;
            }

            if (!row.EvidenceIds.Any(id =>
                    protocols.TryGetValue(id, out var protocol) &&
                    protocol.RequirementId == requirementId &&
                    (protocol.Method == PublicAbsenceMethod &&
                     protocol.Result == "absent" ||
                     protocol.Method == DirectFailureMethod &&
                     protocol.Result == "failed")))
            {
                throw new DeterministicValidationException(
                    $"Schema-v2 row '{requirementId}' gap requires a matching typed public-absence or direct-failure protocol.");
            }
        }
    }

    private static void RequireAutoTransitionProtocol(
        string root,
        ReadinessAssessment assessment,
        InputManifest input,
        IReadOnlyDictionary<string, ParsedProtocol> protocols)
    {
        var row = assessment.Rows.SingleOrDefault(item => item.Id == "BEQ-08");
        if (row?.Status != "verified")
        {
            return;
        }

        if (!row.EvidenceIds.Any(id =>
                protocols.TryGetValue(id, out var protocol) &&
                protocol.Method == AutoTransitionMethod &&
                protocol.Result == "passed"))
        {
            throw new DeterministicValidationException(
                "Verified BEQ-08 requires a passed digest-bound Auto renderer transition protocol.");
        }
    }

    private static ParsedProtocol ParseAutoTransition(
        string root,
        ReadOnlyMemory<byte> bytes,
        InputManifest input,
        ExactAssessmentIdentity assessmentIdentity)
    {
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SupplementalInputAggregateBytes,
            "Auto renderer transition protocol");
        var documentRoot = document.RootElement;
        ContractJson.RequireProperties(documentRoot, "schema_version", "protocol", "component_id", "cold", "warm");
        RequireProtocolHeader(documentRoot, "auto-renderer-transition");
        var componentId = ContractJson.String(documentRoot, "component_id");
        if (assessmentIdentity.ComponentId != componentId)
        {
            throw new DeterministicValidationException(
                "Auto renderer transition protocol component identity does not match the assessment.");
        }

        var cold = ParseAutoVisit(
            root,
            input,
            ContractJson.Object(documentRoot, "cold"),
            componentId,
            "cold");
        var warm = ParseAutoVisit(
            root,
            input,
            ContractJson.Object(documentRoot, "warm"),
            componentId,
            "warm");
        if (cold.ExpectedIdentity != "server" ||
            cold.ObservedIdentity != "server" ||
            warm.ExpectedIdentity != "webassembly" ||
            warm.ObservedIdentity != "webassembly" ||
            cold.ObservedIdentity == warm.ObservedIdentity)
        {
            throw new DeterministicValidationException(
                "Auto renderer transition protocol must prove Server on cold visit and WebAssembly on warm visit with distinct observed identities.");
        }

        var canonical = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "auto-renderer-transition");
            writer.WriteString("component_id", componentId);
            WriteAutoVisit(writer, "cold", cold);
            WriteAutoVisit(writer, "warm", warm);
            writer.WriteEndObject();
        });
        ContractJson.RequireCanonical(bytes.Span, canonical, "Auto renderer transition protocol");
        return new ParsedProtocol(AutoTransitionMethod, null, "passed");
    }

    private static AutoVisit ParseAutoVisit(
        string root,
        InputManifest input,
        JsonElement element,
        string componentId,
        string expectedVisit)
    {
        ContractJson.RequireProperties(
            element,
            "visit",
            "expected_identity",
            "observed_identity",
            "raw_observation_sha256");
        var visit = ContractJson.String(element, "visit");
        if (visit != expectedVisit)
        {
            throw new DeterministicValidationException(
                $"Auto renderer transition visit must be '{expectedVisit}'.");
        }

        var expected = ContractJson.String(element, "expected_identity");
        var observed = ContractJson.String(element, "observed_identity");
        var rawDigest = ContractJson.Digest(
            ContractJson.Object(element, "raw_observation_sha256"));
        if (expected is not ("server" or "webassembly") ||
            observed is not ("server" or "webassembly"))
        {
            throw new DeterministicValidationException(
                $"Auto renderer transition {expectedVisit} visit has an unsupported renderer identity.");
        }

        var rawInput = input.EvidenceInputs.SingleOrDefault(item =>
            item.Kind == "raw-observation" &&
            item.ContentDigest == rawDigest);
        if (rawInput is null)
        {
            throw new DeterministicValidationException(
                $"Auto renderer transition {expectedVisit} visit requires a matching raw-observation input.");
        }

        var rawPath = SafePath.ResolveUnderRoot(
            root,
            rawInput.Basename,
            requireExisting: true,
            requireFile: true);
        var rawBytes = BoundedIO.ReadAllBytes(
            rawPath,
            ResourceLimits.SupplementalInputAggregateBytes,
            $"Auto renderer {expectedVisit} raw observation");
        var raw = ParseRawAutoObservation(rawBytes, expectedVisit);
        if (raw.ComponentId != componentId ||
            raw.ObservedIdentity != observed ||
            raw.Visit != expectedVisit)
        {
            throw new DeterministicValidationException(
                $"Auto renderer transition {expectedVisit} raw observation does not match its component, visit, or observed identity.");
        }

        return new AutoVisit(expectedVisit, expected, observed, rawDigest);
    }

    private static RawAutoObservation ParseRawAutoObservation(
        ReadOnlyMemory<byte> bytes,
        string expectedVisit)
    {
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SupplementalInputAggregateBytes,
            $"Auto renderer {expectedVisit} raw observation");
        var root = document.RootElement;
        ContractJson.RequireProperties(
            root,
            "schema_version",
            "observation",
            "component_id",
            "visit",
            "mode",
            "observed_identity");
        if (ContractJson.Int32(root, "schema_version") != 1 ||
            ContractJson.String(root, "observation") != "auto-renderer-visit" ||
            ContractJson.String(root, "mode") != "interactive-auto")
        {
            throw new DeterministicValidationException(
                $"Auto renderer {expectedVisit} raw observation has an invalid observation kind or mode.");
        }

        var canonical = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("observation", "auto-renderer-visit");
            writer.WriteString("component_id", ContractJson.String(root, "component_id"));
            writer.WriteString("visit", ContractJson.String(root, "visit"));
            writer.WriteString("mode", "interactive-auto");
            writer.WriteString("observed_identity", ContractJson.String(root, "observed_identity"));
            writer.WriteEndObject();
        });
        ContractJson.RequireCanonical(bytes.Span, canonical, "Auto renderer raw observation");
        return new RawAutoObservation(
            ContractJson.String(root, "component_id"),
            ContractJson.String(root, "visit"),
            ContractJson.String(root, "observed_identity"));
    }

    private static void WriteAutoVisit(
        Utf8JsonWriter writer,
        string name,
        AutoVisit visit)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteString("visit", visit.Visit);
        writer.WriteString("expected_identity", visit.ExpectedIdentity);
        writer.WriteString("observed_identity", visit.ObservedIdentity);
        WriteDigest(writer, "raw_observation_sha256", visit.RawObservationDigest);
        writer.WriteEndObject();
    }

    private static LifecycleOperation ParseLifecycleOperation(JsonElement element)
    {
        ContractJson.RequireProperties(
            element,
            "operation",
            "disposition",
            "outcome",
            "raw_observation_sha256",
            "not_tested_reason");
        return new LifecycleOperation(
            ContractJson.String(element, "operation"),
            ContractJson.String(element, "disposition"),
            ContractJson.NullableString(element, "outcome"),
            ContractJson.NullableObject(element, "raw_observation_sha256") is { } digest
                ? ContractJson.Digest(digest)
                : null,
            ContractJson.NullableString(element, "not_tested_reason"));
    }

    private static byte[] SerializeLifecycle(IReadOnlyList<LifecycleOperation> operations) =>
        StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "dynamic-child-lifecycle");
            writer.WritePropertyName("operations");
            writer.WriteStartArray();
            foreach (var operation in operations)
            {
                writer.WriteStartObject();
                writer.WriteString("operation", operation.Operation);
                writer.WriteString("disposition", operation.Disposition);
                WriteNullable(writer, "outcome", operation.Outcome);
                writer.WritePropertyName("raw_observation_sha256");
                if (operation.RawObservationDigest is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    WriteDigest(writer, operation.RawObservationDigest);
                }

                WriteNullable(writer, "not_tested_reason", operation.NotTestedReason);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    private static ParsedProtocol ParseNoticeCoverage(
        ReadOnlyMemory<byte> bytes,
        InputManifest input)
    {
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SupplementalInputAggregateBytes,
            "notice coverage protocol");
        var root = document.RootElement;
        ContractJson.RequireProperties(
            root,
            "schema_version",
            "protocol",
            "coverage",
            "dependency_inventory_sha256",
            "bundled_asset_inventory_sha256",
            "notice_mapping_sha256");
        RequireProtocolHeader(root, "notice-coverage");
        var coverage = ContractJson.String(root, "coverage");
        var dependencies = ContractJson.Digest(
            ContractJson.Object(root, "dependency_inventory_sha256"));
        var assets = ContractJson.Digest(
            ContractJson.Object(root, "bundled_asset_inventory_sha256"));
        var mapping = ContractJson.Digest(
            ContractJson.Object(root, "notice_mapping_sha256"));
        if (coverage is not ("complete" or "incomplete") ||
            !HasInputDigest(input, dependencies, "dependency-inventory") ||
            !HasInputDigest(input, assets, "bundled-asset-inventory") ||
            !HasInputDigest(input, mapping, "notice-mapping"))
        {
            throw new DeterministicValidationException(
                "Notice coverage verification requires dependency, bundled-asset, and notice-mapping inputs plus a complete/incomplete disposition.");
        }

        var canonical = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "notice-coverage");
            writer.WriteString("coverage", coverage);
            WriteDigest(writer, "dependency_inventory_sha256", dependencies);
            WriteDigest(writer, "bundled_asset_inventory_sha256", assets);
            WriteDigest(writer, "notice_mapping_sha256", mapping);
            writer.WriteEndObject();
        });
        ContractJson.RequireCanonical(bytes.Span, canonical, "notice coverage protocol");
        return new ParsedProtocol(NoticeCoverageMethod, null, coverage);
    }

    private static ParsedProtocol ParseAuthenticode(
        string rootPath,
        ReadOnlyMemory<byte> bytes,
        InputManifest input)
    {
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SupplementalInputAggregateBytes,
            "Authenticode verification protocol");
        var root = document.RootElement;
        ContractJson.RequireProperties(root, "schema_version", "protocol", "artifacts");
        RequireProtocolHeader(root, "authenticode-verification");
        var artifacts = ContractJson.Array(root, "artifacts")
            .EnumerateArray()
            .Select(ParseAuthenticodeArtifact)
            .ToArray();
        var packagePath = SafePath.ResolveUnderRoot(
            rootPath,
            input.Package.NupkgPath,
            requireExisting: true,
            requireFile: true);
        var expectedEntries = NupkgInspector.GetFileEntryDigests(packagePath, ".dll");
        if (expectedEntries.Count == 0 ||
            !artifacts.Select(item => item.PackageEntry)
                .SequenceEqual(expectedEntries.Keys, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                "Authenticode verification must cover every shipped DLL package entry exactly once.");
        }

        var passed = true;
        foreach (var artifact in artifacts)
        {
            if (artifact.ArtifactDigest.Value != expectedEntries[artifact.PackageEntry] ||
                !IsSubstantive(artifact.ExpectedIdentity) ||
                !HasText(artifact.ObservedIdentity, 3) ||
                artifact.FileDigestDisposition is not ("valid" or "invalid") ||
                artifact.ChainDisposition is not ("valid" or "invalid" or "not-tested") ||
                artifact.TimestampDisposition is not ("valid" or "invalid" or "not-applicable" or "not-tested") ||
                artifact.RevocationDisposition is not ("valid" or "invalid" or "not-applicable" or "not-tested") ||
                !HasInputDigest(input, artifact.RawObservationDigest, "authenticode-log"))
            {
                throw new DeterministicValidationException(
                    $"Authenticode verification for '{artifact.PackageEntry}' is malformed or lacks a digest-bound raw verification log.");
            }

            passed &= artifact.ExpectedIdentity == artifact.ObservedIdentity &&
                artifact.FileDigestDisposition == "valid" &&
                artifact.ChainDisposition == "valid" &&
                artifact.TimestampDisposition is "valid" or "not-applicable" &&
                artifact.RevocationDisposition is "valid" or "not-applicable";
        }

        var canonical = SerializeAuthenticode(artifacts);
        ContractJson.RequireCanonical(bytes.Span, canonical, "Authenticode verification protocol");
        return new ParsedProtocol(AuthenticodeMethod, null, passed ? "passed" : "failed");
    }

    private static AuthenticodeArtifact ParseAuthenticodeArtifact(JsonElement element)
    {
        ContractJson.RequireProperties(
            element,
            "package_entry",
            "artifact_sha256",
            "expected_identity",
            "observed_identity",
            "file_digest_disposition",
            "chain_disposition",
            "timestamp_disposition",
            "revocation_disposition",
            "raw_observation_sha256");
        return new AuthenticodeArtifact(
            Canonicalization.RelativePath(
                ContractJson.String(element, "package_entry"),
                "Authenticode package entry"),
            ContractJson.Digest(ContractJson.Object(element, "artifact_sha256")),
            ContractJson.String(element, "expected_identity"),
            ContractJson.String(element, "observed_identity"),
            ContractJson.String(element, "file_digest_disposition"),
            ContractJson.String(element, "chain_disposition"),
            ContractJson.String(element, "timestamp_disposition"),
            ContractJson.String(element, "revocation_disposition"),
            ContractJson.Digest(ContractJson.Object(element, "raw_observation_sha256")));
    }

    private static byte[] SerializeAuthenticode(IReadOnlyList<AuthenticodeArtifact> artifacts) =>
        StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "authenticode-verification");
            writer.WritePropertyName("artifacts");
            writer.WriteStartArray();
            foreach (var artifact in artifacts)
            {
                writer.WriteStartObject();
                writer.WriteString("package_entry", artifact.PackageEntry);
                WriteDigest(writer, "artifact_sha256", artifact.ArtifactDigest);
                writer.WriteString("expected_identity", artifact.ExpectedIdentity);
                writer.WriteString("observed_identity", artifact.ObservedIdentity);
                writer.WriteString("file_digest_disposition", artifact.FileDigestDisposition);
                writer.WriteString("chain_disposition", artifact.ChainDisposition);
                writer.WriteString("timestamp_disposition", artifact.TimestampDisposition);
                writer.WriteString("revocation_disposition", artifact.RevocationDisposition);
                WriteDigest(writer, "raw_observation_sha256", artifact.RawObservationDigest);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    private static ParsedProtocol ParseSupportOwnership(ReadOnlyMemory<byte> bytes)
    {
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SupplementalInputAggregateBytes,
            "support ownership protocol");
        var root = document.RootElement;
        ContractJson.RequireProperties(
            root,
            "schema_version",
            "protocol",
            "accountable_role",
            "accountable_owner",
            "scope",
            "backup_owner",
            "escalation_path",
            "effective_at_utc");
        RequireProtocolHeader(root, "support-ownership");
        var values = new[]
        {
            ContractJson.String(root, "accountable_role"),
            ContractJson.String(root, "accountable_owner"),
            ContractJson.String(root, "scope"),
            ContractJson.String(root, "backup_owner"),
            ContractJson.String(root, "escalation_path")
        };
        if (values.Any(value => !IsSubstantive(value)) ||
            !DateTimeOffset.TryParse(
                ContractJson.String(root, "effective_at_utc"),
                out var effective) ||
            effective.Offset != TimeSpan.Zero)
        {
            throw new DeterministicValidationException(
                "Support ownership verification requires accountable role/owner, scope, backup, escalation, and a UTC effective time.");
        }

        var canonical = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "support-ownership");
            writer.WriteString("accountable_role", values[0]);
            writer.WriteString("accountable_owner", values[1]);
            writer.WriteString("scope", values[2]);
            writer.WriteString("backup_owner", values[3]);
            writer.WriteString("escalation_path", values[4]);
            writer.WriteString(
                "effective_at_utc",
                effective.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
            writer.WriteEndObject();
        });
        ContractJson.RequireCanonical(bytes.Span, canonical, "support ownership protocol");
        return new ParsedProtocol(SupportOwnershipMethod, null, null);
    }

    private static ParsedProtocol ParseToolchain(
        ReadOnlyMemory<byte> bytes,
        InputManifest input)
    {
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SupplementalInputAggregateBytes,
            "toolchain probe protocol");
        var root = document.RootElement;
        ContractJson.RequireProperties(
            root,
            "schema_version",
            "protocol",
            "toolchain_name",
            "toolchain_version",
            "target_framework",
            "support_disposition",
            "command",
            "result",
            "raw_log_sha256");
        RequireProtocolHeader(root, "toolchain-probe");
        var name = ContractJson.String(root, "toolchain_name");
        var version = ContractJson.String(root, "toolchain_version");
        var framework = ContractJson.String(root, "target_framework");
        var support = ContractJson.String(root, "support_disposition");
        var command = ContractJson.String(root, "command");
        var result = ContractJson.String(root, "result");
        var rawLog = ContractJson.Digest(ContractJson.Object(root, "raw_log_sha256"));
        if (!HasText(name, 3) ||
            !HasText(version, 3) ||
            !HasText(framework, 3) ||
            !IsSubstantive(command) ||
            support != "supported" ||
            result is not ("passed" or "failed") ||
            !HasInputDigest(input, rawLog, "toolchain-log"))
        {
            throw new DeterministicValidationException(
                "Toolchain probe verification requires a supported named toolchain, target framework, command, result, and digest-bound raw log.");
        }

        var canonical = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "toolchain-probe");
            writer.WriteString("toolchain_name", name);
            writer.WriteString("toolchain_version", version);
            writer.WriteString("target_framework", framework);
            writer.WriteString("support_disposition", support);
            writer.WriteString("command", command);
            writer.WriteString("result", result);
            WriteDigest(writer, "raw_log_sha256", rawLog);
            writer.WriteEndObject();
        });
        ContractJson.RequireCanonical(bytes.Span, canonical, "toolchain probe protocol");
        return new ParsedProtocol(ToolchainMethod, null, result);
    }

    private static void ValidateLifecycle(
        ReadinessAssessment assessment,
        InputManifest input,
        IReadOnlyDictionary<string, EvidenceRecord> records,
        IReadOnlyDictionary<string, ParsedProtocol> protocols,
        bool enforceCurrentProtocols)
    {
        if (assessment.Identity.ComponentId is null)
        {
            return;
        }

        var component = input.Components.Single(item =>
            item.Id == assessment.Identity.ComponentId);
        var lifecycleProtocols = protocols
            .Where(item =>
                item.Value.Method == LifecycleMethod &&
                records[item.Key].Applicability.Scope == "component-specific" &&
                records[item.Key].Applicability.ComponentId == component.Id)
            .ToArray();
        if (component.DynamicChildLifecycle.Applicability == "not-applicable")
        {
            if (lifecycleProtocols.Length != 0)
            {
                throw new DeterministicValidationException(
                    "A component marked dynamic-child not-applicable cannot select a lifecycle protocol.");
            }

            return;
        }

        if (lifecycleProtocols.Length != 1)
        {
            throw new DeterministicValidationException(
                "A component with grouped, registered, selected, or composite children requires exactly one dynamic lifecycle protocol.");
        }

        var protocolId = lifecycleProtocols[0].Key;
        var operations = lifecycleProtocols[0].Value.Operations!;
        foreach (var (rowId, operationNames) in LifecycleRows)
        {
            var row = assessment.Rows.SingleOrDefault(item => item.Id == rowId);
            if (row is null || row.Status is null)
            {
                continue;
            }

            if (!row.EvidenceIds.Contains(protocolId, StringComparer.Ordinal))
            {
                throw new DeterministicValidationException(
                    $"Lifecycle row '{rowId}' must cite the selected dynamic lifecycle protocol.");
            }

            var relevant = operationNames.Select(name => operations[name]).ToArray();
            var hasSourceProof = row.EvidenceIds.Any(id =>
                protocols.TryGetValue(id, out var protocol) &&
                protocol.Method == SourceProofMethod &&
                protocol.RequirementId == rowId &&
                protocol.Result == "failed");
            switch (row.Status)
            {
                case "verified" when relevant.Any(item =>
                    item.Disposition != "observed" || item.Outcome != "passed"):
                    throw new DeterministicValidationException(
                        $"Lifecycle row '{rowId}' cannot be verified without passed raw observations for every applicable operation.");
                case "gap" when !relevant.Any(item =>
                    item.Disposition == "observed" && item.Outcome == "failed") &&
                    relevant.All(item =>
                        item.Disposition == "observed" && item.Outcome == "passed"):
                    throw new DeterministicValidationException(
                        $"Lifecycle row '{rowId}' source gap contradicts a passed mapped lifecycle operation.");
                case "gap" when !relevant.Any(item =>
                    item.Disposition == "observed" && item.Outcome == "failed") &&
                    (!enforceCurrentProtocols || !hasSourceProof):
                    throw new DeterministicValidationException(
                        $"Lifecycle row '{rowId}' cannot be a gap without an observed failed operation or matching source-proof protocol.");
                case "not tested" when !relevant.Any(item =>
                    item.Disposition == "not-tested"):
                    throw new DeterministicValidationException(
                        $"Lifecycle row '{rowId}' cannot be not tested without an operation-specific blocker.");
                case "not tested" when !IsSubstantive(row.Observation):
                    throw new DeterministicValidationException(
                        $"Lifecycle row '{rowId}' not tested status requires the exact blocker in observation.");
                case "not applicable" or "owner evidence required":
                    throw new DeterministicValidationException(
                        $"Lifecycle row '{rowId}' must resolve to verified, gap, or not tested for an applicable dynamic-child control.");
            }
        }
    }

    private static void RequireProtocolForVerifiedRow(
        ReadinessAssessment assessment,
        IReadOnlyDictionary<string, EvidenceRecord> records,
        IReadOnlyDictionary<string, ParsedProtocol> protocols,
        string rowId,
        string method)
    {
        var row = assessment.Rows.SingleOrDefault(item => item.Id == rowId);
        if (row?.Status != "verified")
        {
            return;
        }

        var matches = row.EvidenceIds.Where(id =>
            protocols.TryGetValue(id, out var protocol) &&
            protocol.Method == method &&
            records[id].Applicability.Scope == row.Scope).ToArray();
        if (matches.Length == 0)
        {
            throw new DeterministicValidationException(
                $"Row '{rowId}' verified status requires structured '{method}' evidence.");
        }
    }

    private static void RequireProtocolForOutcomeRow(
        ReadinessAssessment assessment,
        IReadOnlyDictionary<string, EvidenceRecord> records,
        IReadOnlyDictionary<string, ParsedProtocol> protocols,
        string rowId,
        string method,
        string verifiedResult,
        string gapResult)
    {
        var row = assessment.Rows.SingleOrDefault(item => item.Id == rowId);
        if (row?.Status is not ("verified" or "gap"))
        {
            return;
        }

        var expectedResult = row.Status == "verified" ? verifiedResult : gapResult;
        if (!row.EvidenceIds.Any(id =>
                protocols.TryGetValue(id, out var protocol) &&
                protocol.Method == method &&
                protocol.Result == expectedResult &&
                records[id].Applicability.Scope == row.Scope))
        {
            throw new DeterministicValidationException(
                $"Row '{rowId}' status '{row.Status}' requires structured '{method}' evidence with result '{expectedResult}'.");
        }
    }

    private static void ValidateToolchainRows(
        ReadinessAssessment assessment,
        InputManifest input,
        IReadOnlyDictionary<string, EvidenceRecord> records,
        IReadOnlyDictionary<string, ParsedProtocol> protocols)
    {
        if (!input.EvidenceInputs.Any(item => item.Kind == "toolchain-log"))
        {
            return;
        }

        foreach (var rowId in new[] { "TA-02", "TA-04" })
        {
            var row = assessment.Rows.SingleOrDefault(item => item.Id == rowId);
            if (row?.Status is null)
            {
                continue;
            }

            if (row.Status == "not tested")
            {
                if (!IsSubstantive(row.Observation))
                {
                    throw new DeterministicValidationException(
                        $"Row '{rowId}' not tested status requires the exact toolchain blocker in observation.");
                }

                continue;
            }

            if (row.Status is not ("verified" or "gap"))
            {
                continue;
            }

            var matching = row.EvidenceIds
                .Where(id =>
                    records.ContainsKey(id) &&
                    protocols.TryGetValue(id, out var protocol) &&
                    protocol.Method == ToolchainMethod)
                .Select(id => protocols[id])
                .ToArray();
            var expectedResult = row.Status == "verified" ? "passed" : "failed";
            if (!matching.Any(protocol => protocol.Result == expectedResult))
            {
                throw new DeterministicValidationException(
                    $"Row '{rowId}' status '{row.Status}' requires a supported named-toolchain protocol with result '{expectedResult}'.");
            }
        }
    }

    private static void RequireProtocolHeader(JsonElement root, string expectedProtocol)
    {
        if (ContractJson.Int32(root, "schema_version") != 1 ||
            ContractJson.String(root, "protocol") != expectedProtocol)
        {
            throw new DeterministicValidationException(
                $"Structured evidence protocol must be '{expectedProtocol}' schema version 1.");
        }
    }

    private static bool HasInputDigest(
        InputManifest input,
        Sha256Digest digest,
        string kind) =>
        input.EvidenceInputs.Any(item =>
            item.Kind == kind &&
            item.ContentDigest == digest);

    private static bool IsSubstantive(string? value) =>
        HasText(value, 8);

    private static bool HasText(string? value, int minimumLength) =>
        value is not null &&
        ContractJson.NormalizeText(value, "structured evidence value", 4096).Length >= minimumLength;

    private static string[] CanonicalMarkers(
        IReadOnlyList<string> values,
        string name)
    {
        var result = values
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (result.Length != values.Count ||
            result.Any(value =>
                value.Length is 0 or > 128 ||
                value.Any(character =>
                    character is not (>= 'a' and <= 'z') and
                    not (>= '0' and <= '9') and
                    not '-')))
        {
            throw new DeterministicValidationException(
                $"{name} values must be unique, sorted canonical IDs.");
        }

        if (!values.SequenceEqual(result, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"{name} values must be canonically sorted.");
        }

        return result;
    }

    private static void WriteStrings(
        Utf8JsonWriter writer,
        string property,
        IReadOnlyList<string> values)
    {
        writer.WritePropertyName(property);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteDigest(
        Utf8JsonWriter writer,
        string property,
        Sha256Digest digest)
    {
        writer.WritePropertyName(property);
        WriteDigest(writer, digest);
    }

    private static void WriteDigest(Utf8JsonWriter writer, Sha256Digest digest)
    {
        writer.WriteStartObject();
        writer.WriteString("algorithm", digest.Algorithm);
        writer.WriteString("value", digest.Value);
        writer.WriteEndObject();
    }

    private static void WriteNullable(
        Utf8JsonWriter writer,
        string property,
        string? value)
    {
        if (value is null)
        {
            writer.WriteNull(property);
        }
        else
        {
            writer.WriteString(property, value);
        }
    }

    private sealed record LifecycleOperation(
        string Operation,
        string Disposition,
        string? Outcome,
        Sha256Digest? RawObservationDigest,
        string? NotTestedReason);

    private sealed record AuthenticodeArtifact(
        string PackageEntry,
        Sha256Digest ArtifactDigest,
        string ExpectedIdentity,
        string ObservedIdentity,
        string FileDigestDisposition,
        string ChainDisposition,
        string TimestampDisposition,
        string RevocationDisposition,
        Sha256Digest RawObservationDigest);

    private sealed record ParsedProtocol(
        string Method,
        IReadOnlyDictionary<string, LifecycleOperation>? Operations,
        string? Result,
        string? RequirementId = null);

    private sealed record AutoVisit(
        string Visit,
        string ExpectedIdentity,
        string ObservedIdentity,
        Sha256Digest RawObservationDigest);

    private sealed record RawAutoObservation(
        string ComponentId,
        string Visit,
        string ObservedIdentity);

    private sealed record PublicAbsenceRequirement(
        string CorpusKind,
        string RequiredMarker);

    private sealed record DirectFailureRequirement(
        string CauseKind,
        string EvidenceKind);
}
