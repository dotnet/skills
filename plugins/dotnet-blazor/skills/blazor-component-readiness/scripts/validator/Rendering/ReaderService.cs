using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Rendering;

public static class ReaderService
{
    public const string Version = "1.0.1";
    public const string LegacyVersion = "1.0.0";
    internal sealed record Group(string Scope, string Area, string? Clause, string? Classification, AssessmentRow[] Rows);

    internal static string RequireSupportedVersion(string version) =>
        version is LegacyVersion or Version ? version :
            throw new DeterministicValidationException("Unsupported reader_version.");

    internal static Group[] Groups(ReadinessAssessment assessment, RubricContract rubric)
    {
        var definitions = RubricLoader.Select(rubric, assessment.AssessmentKind,
            assessment.Overlays.Select(item => item.Id).ToArray()).ToDictionary(item => item.Id, StringComparer.Ordinal);
        // A shared clause is presentation grouping, never a new assessment or a combined pass.
        // Legacy rows without clause metadata stay separate rather than guessing equivalence.
        return assessment.Rows.GroupBy(row =>
        {
            var basis = definitions[row.Id].Basis;
            return (row.Scope, row.Area, Clause: basis?.Clause ?? row.Id, Classification: basis?.Classification);
        }).Select(group => new Group(group.Key.Scope, group.Key.Area,
            group.Key.Classification is null ? null : group.Key.Clause,
            group.Key.Classification, group.ToArray())).ToArray();
    }

    public static SortedDictionary<string, byte[]> Build(
        string root, RevisionArtifacts revision, AssessmentFeedback? feedback, byte[]? feedbackBytes,
        RevisionArtifacts? package = null,
        ScopedPackageContextBinding? scopedPackageContext = null,
        string readerVersion = Version)
    {
        RequireSupportedVersion(readerVersion);
        var legacy = readerVersion == LegacyVersion;
        var input = revision.Input;
        var assessment = revision.Assessment;
        var profile = ScopedComponentProfile.Load(root, input);
        if (profile is not null)
        {
            if (package is not null || revision.Kind != assessment.AssessmentKind)
                throw new DeterministicValidationException("Scoped component readers cannot carry an ordinary package binding.");
            ContractJson.RequireCanonical(revision.ManifestBytes,
                ReportService.SerializeManifest(revision.Manifest), "scoped source validation manifest");
            InputManifestService.Validate(input, root, requireConfirmed: true);
            AssessmentService.Validate(root, assessment, revision.AssessmentBytes, input, revision.InputBytes,
                revision.Evidence, scopedPackageContext: scopedPackageContext);
            var parsedFeedback = feedbackBytes is null ? null : FeedbackService.Parse(feedbackBytes, assessment);
            if (parsedFeedback?.Digest != feedback?.Digest)
                throw new DeterministicValidationException("Scoped reader feedback must match its exact retained bytes.");
            feedback = parsedFeedback;
        }
        else if (scopedPackageContext is not null)
            throw new DeterministicValidationException("Ordinary readers cannot use scoped package context.");
        else
            ScopedComponentProfile.RejectUnboundComponent(assessment);
        var authorizedScope = AuthorizedPackageScope.Load(root, input);
        authorizedScope?.Validate(assessment, input, revision.Evidence);
        if (authorizedScope is not null || profile is not null)
        {
            var expectedReport = ReportService.RenderMarkdown(
                assessment, input, revision.Evidence, feedback, root, scopedPackageContext);
            if (!revision.ReportBytes.AsSpan().SequenceEqual(expectedReport) ||
                revision.Manifest.FeedbackDigest != feedback?.Digest)
            {
                throw new DeterministicValidationException("Authorized reader requires the exact scope-bound report and feedback.");
            }

            ReportService.ValidateManifest(revision.Manifest, ReportService.CreateManifest(
                assessment, revision.AssessmentBytes, input, revision.InputBytes,
                revision.Evidence, revision.EvidenceBytes, revision.ReportBytes,
                revision.Manifest.PredecessorManifestDigest, feedback?.Digest,
                revision.Manifest.DeclaredChangedIds, root, scopedPackageContext));
        }

        if (input.OwnerInputs.Any(item => item.Provenance == "owner-supplied-internal-evidence") ||
            revision.Evidence.SourceLedgers.SelectMany(item => item.Ledger.Records)
                .Any(item => item.Provenance.Kind == "owner-supplied-internal-evidence"))
            throw new DeterministicValidationException(
                "Partner reader refuses internal evidence. Keep the canonical local report and resolve sharing permissions separately.");
        var rubric = RubricLoader.Load(assessment.RubricVersion);
        var groups = Groups(assessment, rubric);
        var evidence = revision.Evidence.SourceLedgers.SelectMany(item => item.Ledger.Records)
            .ToDictionary(item => item.StableId, StringComparer.Ordinal);
        var evidenceNames = revision.Evidence.Selection.ToDictionary(item => item.EvidenceId,
            item => $"Evidence {item.DisplayOrder}", StringComparer.Ordinal);
        var companion = profile?.BuildSelectedCompanion(revision.Evidence);
        var companionBytes = companion is null ? null : CanonicalEvidenceJson.SerializeBundle(companion);
        var companionNotice = companion is null
            ? "**Structured selected-only evidence companion: omitted.** Selected records depend on historical supersession " +
                "ancestors that are not exported. The complete canonical bundle and dependency closure remain unchanged " +
                "in the internal validation workspace. Selected record identities, provenance and supersession references " +
                "are preserved in the evidence view; they do not deliver the omitted history."
            : "**Structured selected-only evidence companion: included.** It was constructed and verified with the existing " +
                "evidence builders, retains selected record identities and provenance, and has its own ledger digests. " +
                "It is not a relabeled historical ledger or an exact copy of the full internal source bundle.";
        if (companionBytes is not null && !legacy)
        {
            companionNotice = "**Structured selected-only evidence companion: included.** It was constructed and verified " +
                "with the existing evidence builders and preserves selected record identities and provenance. " +
                (companionBytes.AsSpan().SequenceEqual(revision.EvidenceBytes)
                    ? "For this revision, its bytes are identical to the current internal evidence bundle. "
                    : "For this revision, its bytes differ from the current internal evidence bundle. ") +
                "Construction does not imply different bytes. Raw inputs and omitted historical dependencies are not delivered; " +
                "this reader is not a self-contained validation bundle.";
        }
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        AddTechnical(files, revision, profile is not null);
        if (companionBytes is not null)
            files["technical/selected.evidence.json"] = companionBytes;
        if (authorizedScope is not null)
        {
            files[$"technical/{AuthorizedPackageScope.Filename}"] = authorizedScope.CopyBytes();
        }
        if (feedbackBytes is not null) files["technical/feedback.txt"] = feedbackBytes;
        if (package is not null)
        {
            // Only the verified package relationship is referenced; package/private evidence is not projected into this control.
            files["technical/package-binding.json"] = package.ManifestBytes;
        }
        var title = assessment.AssessmentKind == "package" ? "Library and release report" :
            input.Components.Single(item => item.Id == assessment.Identity.ComponentId).DisplayName + " control report";
        var lines = new List<string>
        {
            "# " + Text(title), "",
            $"**Release assessed:** {Text(input.Package.PackageId)} {Text(input.Package.Version)}",
            $"**Ownership:** {Text(assessment.AssessmentKind)}. " +
                (profile is not null && !legacy ? "**Check accounting:** " : "**Completion:** ") +
                $"{Text(assessment.CompletionState)}.",
            $"**Source:** {Text(input.Source.Availability)}; {Text(input.Source.RepositoryUri)}; {Text(input.Source.Commit)}.", "",
            $"**Source mapping:** {Text(input.Source.Mapping)}; confidence: {Text(input.Source.Confidence)}.", "",
            "PARTNER PREVIEW: a deterministic projection of retained assessment facts, not a new execution, certification or promotion decision. " +
                "Qualifications are preserved; source/configuration, owner records and untested behavior remain distinct.",
            "",
            "**Result legend:** verified; gap; owner evidence required; not tested; not applicable; incomplete. " +
                "Mixed groups retain each check's result. Missing evidence and unrun tests are not established defects. " +
                "A verified status does not erase qualifications in its observation.",
            "",
            "[Evidence records](evidence.md) | [Exact technical assessment](technical/" + revision.Kind + ".assessment.json) | " +
                "[Original raw report](technical/" + revision.Kind + ".report.md)", ""
        };
        if (authorizedScope is not null)
        {
            lines.AddRange([authorizedScope.Declaration, ""]);
        }
        if (profile is not null)
        {
            if (!legacy)
                lines.AddRange(["Check accounting describes whether selected checks have dispositions. It does not establish " +
                    "completed testing, complete evidence coverage, readiness or approval.", ""]);
            lines.AddRange([profile.Declaration, "", ScopedComponentProfile.ExportNotice, "", companionNotice, ""]);
        }

        if (package is not null)
            lines.Add("Package-wide findings remain in the separately delivered Library and release reader. " +
                "This control does not re-assess or clone them; its exact validated package relationship is in " +
                "[package binding](technical/package-binding.json).\n");
        lines.AddRange(["| Requirement | Check / requirement | Result | Evidence |", "|---|---|---|---|"]);
        var positions = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            for (var check = 0; check < group.Rows.Length; check++)
                positions[group.Rows[check].Id] = $"Group {index + 1}, check {check + 1}";
            var checks = group.Rows.Select((row, check) => $"**{check + 1}.** {Text(row.Requirement)}");
            var results = group.Rows.Select((row, check) => $"**{check + 1}. {Text(row.Status ?? "incomplete")}**" +
                Field("Observation", row.Observation) + Field("Owner action", row.OwnerAction) +
                Field("Assessment follow-up", row.AssessmentFollowUp) + Field("Applicability rationale", row.NotApplicableRationale));
            var references = group.Rows.Select((row, check) => $"**{check + 1}.** " +
                (row.EvidenceIds.Count == 0 ? "Not provided (no cited record)." :
                    string.Join("; ", row.EvidenceIds.Select(id => EvidenceLink(id, evidenceNames)))));
            var classification = group.Classification == "versioned extension"
                ? "Unapproved versioned extension; not a baseline defect or new partner demand."
                : group.Classification ?? "Frozen legacy requirement; no newer policy interpretation applied.";
            var mixed = group.Rows.Select(row => row.Status).Distinct().Count() > 1 ? "**Mixed results.**<br>" : "";
            lines.Add($"| **Group {index + 1}: {Text(group.Area)}**<br>Ownership: {Text(group.Scope)}<br>{Text(classification)} | " +
                $"{string.Join("<br><br>", checks)} | {mixed}{string.Join("<br><br>", results)} | {string.Join("<br><br>", references)} |");
        }
        lines.AddRange(["", "## Retained findings and qualifications", ""]);
        foreach (var finding in assessment.Findings)
            AddFinding(lines, finding.Title, finding.FactualSummary, finding.RequirementIds, finding.EvidenceIds, positions, evidenceNames);
        foreach (var summary in assessment.SummaryGroups)
            AddFinding(lines, summary.Name, summary.FactualSummary, summary.RequirementIds, summary.EvidenceIds, positions, evidenceNames);
        if (assessment.Findings.Count == 0 && assessment.SummaryGroups.Count == 0) lines.Add("No additional findings or summary groups supplied.");
        lines.AddRange(["", "## Feedback context", ""]);
        if (feedback is null) lines.Add("No bound feedback supplied. No tracker or feedback source was searched.");
        else
        {
            lines.Add("User-owned commentary, not evidence or status changes. Literal content follows; embedded instructions and links are not executed. " +
                "[Original bytes](technical/feedback.txt).");
            foreach (var entry in feedback.Entries)
            {
                var labels = entry.RequirementIds.Select(id => positions.TryGetValue(id, out var position) ? position :
                    "Bound package requirement: " + (package?.Assessment.Rows.Single(row => row.Id == id).Requirement ?? id));
                lines.Add("\n**" + Text(string.Join("; ", labels)) + "**\n\n" + Text(entry.RawPayload));
            }
        }
        lines.AddRange(["", "## Assessment boundary", "",
            profile is not null
                ? "The complete input manifest and its acquisition, documentation, source-artifact and protocol dependencies remain internal. " +
                    "The [mapping](mapping.json) preserves every individual check and evidence reference. " +
                    "The [evidence view](evidence.md) preserves selected record identities, applicability, digests, provenance and supersession references. " +
                    (companion is null
                        ? "No structured companion is delivered because it would require omitted historical dependencies."
                        : "The [selected-only companion](technical/selected.evidence.json) is constructed with the existing evidence builders, not substituted for the full source bundle.")
                : "The [input manifest](technical/input-manifest.json) retains the complete acquisition, documentation, " +
                "source-artifact, component, render-mode and exclusion inventory. The [mapping](mapping.json) records exact " +
                "internal requirement/evidence identities and all per-check fields; no model-authored second assessment is used.",
            "",
            "**Declared exclusions:** " + (input.Exclusions.Count == 0 ? "None recorded." :
                string.Join("; ", input.Exclusions.Select(item => Text(item.Subject) + ": " + Text(item.Rationale)))),
            "", "## Limitations", ""]);
        lines.AddRange(revision.Manifest.Limitations.Select(item => "- " + Text(item)));
        lines.Add("- Evidence summaries and commitments are labeled separately from retained source bytes; a digest does not establish availability.");
        lines.Add("- Review the complete local output for sharing permissions. This command neither publishes nor authorizes transfer.");
        files["report.md"] = Bytes(lines);
        var evidenceLines = new List<string> { "# Retained evidence", "",
            "Claims, methods and original provenance below are preserved, not independently re-adjudicated. " +
            "A retained summary is not proof that all underlying outputs were retained or rerun.", "" };
        if (profile is not null)
        {
            evidenceLines.AddRange([profile.Declaration, "", ScopedComponentProfile.ExportNotice, "", companionNotice, "",
                "**Internal source bundle SHA-256:** `" + ContractJson.RawDigest(revision.EvidenceBytes).Value + "`."]);
            if (companion is not null)
                evidenceLines.Add("**Constructed selected-only companion SHA-256:** `" +
                    ContractJson.RawDigest(files["technical/selected.evidence.json"]).Value + "`.");
            evidenceLines.AddRange([
                legacy
                    ? "The original full ledgers remain internal with their unchanged digests:"
                    : "Source ledger artifacts remain retained internally. Any included companion contains their selected record " +
                        "payloads and may contain complete current ledger payloads when all records are selected. Source ledger digests:",
                string.Join("\n", revision.Evidence.SourceLedgers.Select(item => "- `" + item.SourceLedgerSha256 + "`.")), ""]);
        }
        foreach (var selection in revision.Evidence.Selection)
        {
            var record = evidence[selection.EvidenceId];
            var number = selection.DisplayOrder;
            evidenceLines.AddRange([$"<a id=\"evidence-{number}\"></a>", "## " + evidenceNames[record.StableId], "",
                "**Checks:** " + Text(string.Join("; ", assessment.Rows.Where(row => row.EvidenceIds.Contains(record.StableId)).Select(row => positions[row.Id]))),
                "", Text(record.Claim), "",
                "**Original provenance:** " + Text(record.Provenance.Kind),
                "**Locator:** " + Text(record.Provenance.Locator),
                "**Method:** " + Text(record.Provenance.Method),
                "**Captured:** " + Text(record.Provenance.CapturedAtUtc),
                "**Retention:** " + Text(record.Provenance.Retention), ""]);
            if (profile is not null)
            {
                evidenceLines.AddRange([
                    "**Stable evidence ID:** `" + record.StableId + "`.",
                    "**Applicability scope:** " + Text(record.Applicability.Scope),
                    "**Applicability component:** " + (record.Applicability.ComponentId is null
                        ? "`null` (no component-specific applicability)." : Text(record.Applicability.ComponentId)),
                    "**Source ledger SHA-256:** `" + selection.SourceLedgerSha256 + "`.",
                    "**Content digest:** `" + record.Provenance.ContentDigest.Algorithm + ":" + record.Provenance.ContentDigest.Value + "`.",
                    "**Supersedes:** " + (record.Supersedes.Count == 0 ? "None (`[]`)." :
                        string.Join("; ", record.Supersedes.Select(id => "`" + id + "`"))), ""]);
                if (record.Supersedes.Count != 0)
                    evidenceLines.Add("Supersession references identify predecessor records in the fully validated internal ledger. " +
                        "Their payloads and transitive history are not delivered; inspecting or re-verifying that history requires the internal workspace.");
            }
            var retained = authorizedScope is null && profile is null ? Resolve(root, input, record.Provenance) : null;
            if (profile is not null)
                evidenceLines.Add(HasRetainedInput(input, record.Provenance)
                    ? "Confirmed source bytes are retained in the internal validation workspace and deliberately omitted from this reader. No raw attachment is delivered."
                    : "No confirmed source attachment is available for this record: declaration or observation commitment only. The reference does not imply retained or delivered raw bytes.");
            else if (authorizedScope is not null)
                evidenceLines.Add("Underlying source bytes are retained in the local validation inputs, not copied into this scoped partner reader. Their omission does not change the recorded claim or evidence identity.");
            else if (retained is null)
                evidenceLines.Add("Underlying bytes not provided here: declaration or legacy observation commitment only.");
            else
            {
                if (ContractJson.RawDigest(retained) != record.Provenance.ContentDigest)
                    throw new DeterministicValidationException("Reader evidence digest differs from its retained input.");
                var relative = $"evidence/record-{number}.bin";
                files[relative] = retained;
                evidenceLines.Add($"[Retained artifact]({relative}). This link is local, not publicly hosted.");
            }
            if (record.Provenance.Kind == "reproduced-runtime-observation")
                evidenceLines.Add("Prior runtime observation record: this projection did not reproduce it. Retaining this record or summary does not establish availability of secondary raw runtime/build outputs.");
            evidenceLines.Add("");
        }
        files["evidence.md"] = Bytes(evidenceLines);
        files["mapping.json"] = StrictJson.SerializeCanonical(writer =>
        {
            using var assessmentJson = JsonDocument.Parse(revision.AssessmentBytes);
            var sourceRows = assessmentJson.RootElement.GetProperty("rows").EnumerateArray()
                .ToDictionary(item => item.GetProperty("id").GetString()!, StringComparer.Ordinal);
            writer.WriteStartObject();
            writer.WriteString("reader_version", readerVersion);
            writer.WritePropertyName("groups");
            writer.WriteStartArray();
            foreach (var group in groups)
            {
                writer.WriteStartObject();
                writer.WriteString("scope", group.Scope);
                writer.WriteString("area", group.Area);
                writer.WriteString("clause", group.Clause);
                writer.WriteString("classification", group.Classification);
                writer.WritePropertyName("checks");
                writer.WriteStartArray();
                foreach (var row in group.Rows)
                {
                    sourceRows[row.Id].WriteTo(writer);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        files["reader.validation.json"] = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("reader_version", readerVersion);
            writer.WriteString("source_validation_sha256", ContractJson.RawDigest(revision.ManifestBytes).Value);
            writer.WriteString("rubric_sha256", rubric.RubricDigest.Value);
            writer.WriteString("package_validation_sha256", package is null ? null : ContractJson.RawDigest(package.ManifestBytes).Value);
            writer.WritePropertyName("files");
            writer.WriteStartObject();
            foreach (var file in files) writer.WriteString(file.Key, ContractJson.RawDigest(file.Value).Value);
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
        if (authorizedScope is not null)
        {
            foreach (var file in files.Values)
            {
                authorizedScope.RejectDisclosure(Encoding.UTF8.GetString(file));
            }
        }
        if (profile is not null)
            foreach (var file in files)
                profile.RejectDisclosure(file.Value, json: file.Key.EndsWith(".json", StringComparison.Ordinal));

        return files;
    }

    private static void AddFinding(List<string> lines, string title, string summary, IReadOnlyList<string> ids,
        IReadOnlyList<string> evidence, Dictionary<string, string> positions, Dictionary<string, string> names)
    {
        lines.AddRange(["### " + Text(title), "", Text(summary), "",
            "**Checks:** " + Text(string.Join("; ", ids.Select(id => positions[id]))),
            "**Evidence:** " + (evidence.Count == 0 ? "Not provided." : string.Join("; ", evidence.Select(id => EvidenceLink(id, names)))), ""]);
    }

    private static string EvidenceLink(string id, Dictionary<string, string> names) =>
        $"[{names[id]}](evidence.md#evidence-{names[id]["Evidence ".Length..]})";

    private static string Field(string label, string? value) => value is null ? "" : $"<br>{label}: {Text(value)}";

    internal static string Text(string? value)
    {
        var builder = new StringBuilder();
        foreach (var character in value ?? "")
            builder.Append(character switch
            {
                '\n' => "<br>", '\r' => "&#13;", '&' => "&amp;", '<' => "&lt;", '>' => "&gt;",
                '|' => "&#124;", '*' => "&#42;", '_' => "&#95;", '[' => "&#91;", ']' => "&#93;",
                '`' => "&#96;", '\\' => "&#92;", '#' => "&#35;", _ => character.ToString()
            });
        return builder.ToString();
    }

    private static byte[] Bytes(IEnumerable<string> lines)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
        BoundedIO.EnsureLength(bytes.Length, ResourceLimits.SerializedArtifactBytes, "reader artifact");
        return bytes;
    }

    private static void AddTechnical(IDictionary<string, byte[]> files, RevisionArtifacts revision, bool scopedComponent)
    {
        if (!scopedComponent) files["technical/input-manifest.json"] = revision.InputBytes;
        files[$"technical/{revision.Kind}.assessment.json"] = revision.AssessmentBytes;
        if (!scopedComponent) files[$"technical/{revision.Kind}.evidence.json"] = revision.EvidenceBytes;
        files[$"technical/{revision.Kind}.report.md"] = revision.ReportBytes;
        files[$"technical/{revision.Kind}.validation.json"] = revision.ManifestBytes;
    }

    private static bool HasRetainedInput(InputManifest input, EvidenceProvenance provenance) =>
        provenance.Kind is not ("owner-declared-closed-source" or "owner-declared-unavailable") &&
        (provenance.Kind == "package-artifact-metadata" ||
         input.Documentation.Any(item => item.Url == provenance.Locator && item.ContentDigest == provenance.ContentDigest) ||
         input.SourceArtifacts.Any(item => "source:" + item.SourcePath == provenance.Locator && item.ContentDigest == provenance.ContentDigest) ||
         input.EvidenceInputs.Any(item => item.Basename == provenance.Locator && item.ContentDigest == provenance.ContentDigest) ||
         input.OwnerInputs.Any(item => item.Basename == provenance.Locator && item.ContentDigest == provenance.ContentDigest));

    private static byte[]? Resolve(string root, InputManifest input, EvidenceProvenance provenance)
    {
        if (provenance.Kind is "owner-declared-closed-source" or "owner-declared-unavailable") return null;
        if (provenance.Kind == "package-artifact-metadata")
        {
            var package = SafePath.ResolveUnderRoot(root, input.Package.NupkgPath, true, true);
            if (provenance.Locator == NupkgInspector.WholePackageEvidenceLocator)
                return BoundedIO.ReadAllBytes(package, ResourceLimits.NupkgBytes, "package evidence");
            _ = NupkgInspector.ComputeEvidenceContentSha256(package, provenance.Locator);
            using var zip = ZipFile.OpenRead(package);
            using var stream = zip.Entries.Single(item => item.FullName ==
                provenance.Locator[NupkgInspector.PackageEntryEvidencePrefix.Length..]).Open();
            return BoundedIO.ReadAllBytes(stream, ResourceLimits.NupkgBytes, "package entry evidence");
        }
        var candidates = input.Documentation.Where(item => item.Url == provenance.Locator).Select(item => item.ContentPath)
            .Concat(input.SourceArtifacts.Where(item => "source:" + item.SourcePath == provenance.Locator).Select(item => item.ContentPath))
            .Concat(input.EvidenceInputs.Where(item => item.Basename == provenance.Locator).Select(item => item.Basename))
            .Concat(input.OwnerInputs.Where(item => item.Basename == provenance.Locator).Select(item => item.Basename))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0 && provenance.Kind == "reproduced-runtime-observation" &&
            !provenance.Method.StartsWith("protocol:", StringComparison.Ordinal))
            return null;
        if (candidates.Length != 1)
            throw new DeterministicValidationException("Reader evidence locator is not uniquely bound to a confirmed input.");
        return BoundedIO.ReadAllBytes(SafePath.ResolveUnderRoot(root, candidates[0], true, true),
            ResourceLimits.NupkgBytes, "reader retained evidence");
    }
}
