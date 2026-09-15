using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Cli;

public static class EvidenceCommand
{
    public static readonly string Help =
        $$"""
        Build and validate public-v1 readiness evidence

        Usage:
          readiness-validator evidence draft-add --output <new-path> --claim <text> --scope <repository-wide|component-specific> [--component <id>] --kind <existing-kind> --locator <logical-locator> --method <text> --captured-at <actual-utc> [--supersedes <EV1-id>]... (--nupkg <path>|--content <retained-file>) [--input <previous-draft>]
          readiness-validator evidence draft-add --output <new-path> --claim <text> --scope <repository-wide|component-specific> [--component <id>] --kind reviewer-generated-analysis --root <dir> --manifest <confirmed> --evidence-input <registered-basename> --method <text> --captured-at <actual-utc> [--supersedes <EV1-id>]... [--input <previous-draft>]
          readiness-validator evidence ledger-build --kind <repository|component> --subject <path> --draft <path> --nupkg <path> --output <path>
          readiness-validator evidence ledger-validate --ledger <path>
          readiness-validator evidence bundle --assessment <path> --source-ledger <path>... --ids <EV1-id,...> --output <path> [--root <dir> --manifest <confirmed>]

        Initialize the assessment, then run assessment export-identity to create the
        canonical file used by ledger-build --subject and bundle --assessment.
        A full assessment document or hand-authored identity JSON is not a substitute.

        Evidence draft producer:
          For registered reviewer analysis, supply all of --root, --manifest and
          --evidence-input. This mode validates the confirmed manifest/current files
          and derives BOTH locator and digest from one evidence_inputs registration.
          Do not supply --locator, --content or --nupkg in registered mode.
          The manifest must stay beneath the declared root; it may be
          root-relative or absolute beneath it. The selector is an exact basename.
          Register the retained derived result using inputs candidates add-evidence,
          discover a NEW draft and explicitly run inputs confirm. Preserve the
          pre-output manifest; initialize/export the final identity only afterward.
          No command here discovers or confirms inputs, infers authorization or
          changes prior artifacts. The producer name/version is not a filename.

          The existing manual modes remain available:
          draft-add computes content_sha256 from exactly one retained source. Use
          --nupkg only with package-artifact-metadata; use --content with every
          other existing provenance kind. --content is a regular file bounded by
          ResourceLimits.SupplementalInputAggregateBytes (64 MiB). The locator is logical
          provenance metadata, never a filesystem path. No caller-supplied hash
          or network access is accepted; metadata is explicit, not inferred.
          --input accepts any valid draft through the existing parser normalization
          and never rewrites that original file. The output is a fresh untrusted
          draft, not evidence validation or a readiness conclusion.

        Existing provenance kinds and locator grammars:
        {{string.Join(Environment.NewLine, EvidenceIdentity.ExistingProvenanceKinds.Select(kind => $"  {kind}: {EvidenceIdentity.GetLocatorGrammar(kind)}"))}}

        Evidence draft template (repository-wide package record):
        ```json
        {
          "schema_version": 1,
          "records": [
            {
              "claim": "<observed claim>",
              "applicability": { "scope": "repository-wide", "component_id": null },
              "provenance": {
                "kind": "{{EvidenceIdentity.PackageArtifactMetadata}}",
                "locator": "{{NupkgInspector.WholePackageEvidenceLocator}}",
                "method": "<actual capture method>",
                "captured_at_utc": "<actual capture UTC time>",
                "content_sha256": { "algorithm": "sha256", "value": "<observed content SHA-256>" },
                "retention": "commitment-only"
              },
              "supersedes": []
            }
          ]
        }
        ```
        Replace every placeholder with actual observed facts before ledger-build.
        Do not invent claims, capture methods, timestamps, hashes, or provenance.
        For package-artifact-metadata, the exact locator forms are:
          {{NupkgInspector.WholePackageEvidenceLocator}}
          {{NupkgInspector.PackageEntryEvidencePrefix}}<exact-case relative entry path>
        Use the inspected nupkg SHA-256 for the whole-package locator. For an entry,
        hash that entry's uncompressed bytes, not the whole nupkg. ledger-build
        verifies either digest against --nupkg. "nupkg" and filesystem paths are
        not aliases for these logical locators.
        captured_at_utc records the actual capture instant in UTC, for example
        YYYY-MM-DDTHH:mm:ssZ; it is not a guessed package publication date.
        Timestamp validation checks canonical UTC-second format, not whether the
        supplied instant truthfully describes capture. Explicit recorded historical
        capture times are preserved; deterministic repeats use the same supplied value.
        The example is a draft, not evidence or a claim of whole-input completeness.
        ledger-build generates stable IDs and canonical source-ledger bytes.

        Source ledgers are immutable canonical JSON. Bundle selection order is the
        comma-separated --ids order. At most 32 source-ledger inputs totaling at
        most 64 MiB may be supplied.

        Completed skill handoffs require bundle --root/--manifest input-bound mode:
          it revalidates current input bytes, exact final manifest/package/component
          identity, and ALL selected records' existing provenance binding rules.
          Either option requires both. Failure publishes no bundle and never falls
          back to structural success. Unscored identity skeletons are supported;
          input-bound acceptance is not readiness scoring or factual verification.
        With neither option, bundle is structural-only: input linkage is NOT
        accepted. ledger-validate is also structural validation, not input linkage.
        """;

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 0 ||
            IsHelp(args[0]) ||
            (args.Count == 2 && IsHelp(args[1])))
        {
            output.WriteLine(Help);
            return ExitCodes.Success;
        }

        return args[0] switch
        {
            "draft-add" => AddDraft(args.Skip(1).ToArray(), output),
            "ledger-build" => BuildLedger(args.Skip(1).ToArray(), output),
            "ledger-validate" => ValidateLedger(args.Skip(1).ToArray(), output),
            "bundle" => BuildBundle(args.Skip(1).ToArray(), output),
            _ => throw new UsageException($"Unknown evidence command '{args[0]}'.")
        };
    }

    private static int AddDraft(IReadOnlyList<string> args, TextWriter output)
    {
        var options = ParseOptions(
            args,
            repeatableOptions: ["--supersedes"],
            "--input",
            "--output",
            "--claim",
            "--scope",
            "--component",
            "--kind",
            "--locator",
            "--method",
            "--captured-at",
            "--supersedes",
            "--nupkg",
            "--content",
            "--root",
            "--manifest",
            "--evidence-input");
        RequireExactly(
            options,
            "--output",
            "--claim",
            "--scope",
            "--kind",
            "--method",
            "--captured-at");

        var kind = options.Single("--kind");
        var scope = options.Single("--scope");
        var component = options.All("--component").Count == 0
            ? null
            : options.Single("--component");
        string locator;
        string digest;
        if (options.All("--root").Count != 0 || options.All("--manifest").Count != 0 ||
            options.All("--evidence-input").Count != 0)
        {
            RequireExactly(options, "--root", "--manifest", "--evidence-input");
            foreach (var conflict in new[] { "--locator", "--content", "--nupkg" })
            {
                if (options.All(conflict).Count != 0)
                {
                    throw new UsageException($"{conflict} cannot be supplied with registered evidence-input mode.");
                }
            }
            if (kind != EvidenceIdentity.ReviewerGeneratedAnalysis)
            {
                throw new UsageException(
                    "Registered evidence-input mode requires explicitly selected reviewer-generated-analysis.");
            }
            (locator, digest) = ReadRegisteredEvidence(options);
        }
        else
        {
            RequireExactly(options, "--locator");
            locator = options.Single("--locator");
            var nupkg = options.All("--nupkg").Count == 0 ? null : options.Single("--nupkg");
            var content = options.All("--content").Count == 0 ? null : options.Single("--content");
            if ((nupkg is null) == (content is null))
            {
                throw new UsageException(
                    "Evidence draft-add requires exactly one of --nupkg or --content.");
            }

            if (string.Equals(kind, EvidenceIdentity.PackageArtifactMetadata, StringComparison.Ordinal))
            {
                if (nupkg is null)
                {
                    throw new UsageException("--nupkg is required for package-artifact-metadata.");
                }
            }
            else if (content is null)
            {
                throw new UsageException("--content is required for non-package provenance kinds.");
            }

            digest = (nupkg, content) switch
            {
                ({ } packagePath, null) => ComputePackageDigest(packagePath, locator),
                (null, { } contentPath) => ComputeContentDigest(contentPath),
                _ => throw new UsageException(
                    "Evidence draft-add requires exactly one of --nupkg or --content.")
            };
        }
        var record = EvidenceIdentity.NormalizeRecordDraft(
            new EvidenceRecordDraft(
                options.Single("--claim"),
                new EvidenceApplicability(scope, component),
                new EvidenceProvenance(
                    kind,
                    locator,
                    options.Single("--method"),
                    options.Single("--captured-at"),
                    new Sha256Digest("sha256", digest),
                    "commitment-only"),
                options.All("--supersedes")
                    .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries))
                    .ToArray()));

        var records = new List<EvidenceRecordDraft>();
        if (options.All("--input") is [var input])
        {
            var inputBytes = BoundedIO.ReadAllBytes(
                input,
                ResourceLimits.AuthoredLedgerBytes,
                "evidence draft");
            var previous = CanonicalEvidenceJson.ParseDraftDocument(inputBytes);
            records.AddRange(previous.Records);
        }

        records.Add(record);
        var bytes = CanonicalEvidenceJson.SerializeDraftDocument(
            new EvidenceDraftDocument(CanonicalEvidenceJson.EvidenceSchemaVersion, records));
        BoundedIO.EnsureLength(bytes.Length, ResourceLimits.AuthoredLedgerBytes, "evidence draft");
        WriteNew(options.Single("--output"), bytes);
        output.WriteLine($"Evidence draft written: {records.Count} records.");
        return ExitCodes.Success;
    }

    private static (string Locator, string Digest) ReadRegisteredEvidence(ParsedOptions options)
    {
        var (root, input, _) = LoadConfirmedInput(options);
        var basename = options.Single("--evidence-input");
        if (Canonicalization.Basename(basename, "evidence input basename") != basename)
        {
            throw new DeterministicValidationException("--evidence-input must be an exact registered basename.");
        }
        var matches = input.EvidenceInputs.Where(item => item.Basename == basename).ToArray();
        if (matches.Length != 1)
        {
            throw new DeterministicValidationException(
                $"Evidence input '{basename}' must select exactly one evidence_inputs registration in " +
                $"confirmed manifest '{options.Single("--manifest")}'. Register the retained result through " +
                "candidates, discover and explicitly confirm a new manifest before constructing evidence.");
        }
        var registered = matches[0];
        var path = SafePath.ResolveUnderRoot(root, registered.Basename, requireExisting: true, requireFile: true);
        SafePath.EnsureRegularFile(path);
        var bytes = BoundedIO.ReadAllBytes(path, ResourceLimits.SupplementalInputAggregateBytes, "registered evidence content");
        var digest = ContractJson.RawDigest(bytes);
        if (bytes.LongLength != registered.Size || digest != registered.ContentDigest)
        {
            throw new DeterministicValidationException(
                $"Registered evidence '{basename}' changed after validation of manifest '{options.Single("--manifest")}'.");
        }
        return (registered.Basename, digest.Value);
    }

    private static (string Root, InputManifest Input, byte[] Bytes) LoadConfirmedInput(ParsedOptions options)
    {
        var root = Path.GetFullPath(options.Single("--root"));
        var manifestPath = options.Single("--manifest");
        try
        {
            var relative = Path.IsPathRooted(manifestPath) ? Path.GetRelativePath(root, manifestPath) : manifestPath;
            var path = SafePath.ResolveUnderRoot(root, relative, requireExisting: true, requireFile: true);
            SafePath.EnsureRegularFile(path);
            var bytes = BoundedIO.ReadAllBytes(path, ResourceLimits.SerializedArtifactBytes, "confirmed input manifest");
            var input = InputManifestService.Parse(bytes);
            InputManifestService.Validate(input, root, requireConfirmed: true);
            return (root, input, bytes);
        }
        catch (DeterministicValidationException error)
        {
            throw new DeterministicValidationException(
                $"Confirmed manifest '{manifestPath}' failed input validation: {error.Message}", error);
        }
    }

    private static string ComputePackageDigest(string path, string locator)
    {
        _ = NupkgInspector.Inspect(path);
        return NupkgInspector.ComputeEvidenceContentSha256(path, locator);
    }

    private static string ComputeContentDigest(string path)
    {
        SafePath.EnsureRegularFile(path);
        return CanonicalEvidenceJson.ComputeSha256(
            BoundedIO.ReadAllBytes(
                path,
                ResourceLimits.SupplementalInputAggregateBytes,
                "evidence content"));
    }

    public static void ValidateSupplementalInputs(IReadOnlyList<long> lengths)
    {
        ArgumentNullException.ThrowIfNull(lengths);
        if (lengths.Count > ResourceLimits.SupplementalInputCount)
        {
            throw new DeterministicValidationException(
                $"Evidence bundle accepts at most {ResourceLimits.SupplementalInputCount} " +
                $"explicit supplemental inputs; observed {lengths.Count}.");
        }

        long aggregate = 0;
        foreach (var length in lengths)
        {
            if (length < 0)
            {
                throw new DeterministicValidationException(
                    "A supplemental input reported a negative byte length.");
            }

            if (length > ResourceLimits.SupplementalInputAggregateBytes - aggregate)
            {
                throw new ResourceLimitException(
                    "supplemental evidence inputs",
                    ResourceLimits.SupplementalInputAggregateBytes,
                    length > long.MaxValue - aggregate ? long.MaxValue : aggregate + length);
            }

            aggregate += length;
        }
    }

    public static void EnsureSerializedOutput(long length) =>
        BoundedIO.EnsureLength(
            length,
            ResourceLimits.SerializedArtifactBytes,
            "serialized evidence output");

    private static int BuildLedger(IReadOnlyList<string> args, TextWriter output)
    {
        var options = ParseOptions(
            args,
            repeatableOptions: Array.Empty<string>(),
            "--kind",
            "--subject",
            "--draft",
            "--nupkg",
            "--output");
        RequireExactly(options, "--kind", "--subject", "--draft", "--nupkg", "--output");

        var kind = options.Single("--kind");
        var subjectPath = options.Single("--subject");
        var draftPath = options.Single("--draft");
        var outputPath = options.Single("--output");
        var nupkgPath = options.Single("--nupkg");
        var inspected = EvidenceIdentity.FromInspectedPackage(
            NupkgInspector.Inspect(nupkgPath));
        var draft = CanonicalEvidenceJson.ParseDraftDocument(
            BoundedIO.ReadAllBytes(
                draftPath,
                ResourceLimits.AuthoredLedgerBytes,
                "evidence draft"));
        VerifyPackageArtifactEvidence(nupkgPath, draft.Records);

        EvidenceSourceLedger ledger;
        switch (kind)
        {
            case "repository":
                var repositorySubject = CanonicalEvidenceJson.ParseRepositorySubject(
                    BoundedIO.ReadAllBytes(
                        subjectPath,
                        ResourceLimits.AuthoredLedgerBytes,
                        "repository ledger subject"));
                RequireExactPackage(repositorySubject.Package, inspected);
                ledger = EvidenceLedgerBuilder.BuildRepositoryLedger(
                    repositorySubject,
                    draft.Records);
                break;
            case "component":
                var componentSubject = CanonicalEvidenceJson.ParseAssessment(
                    BoundedIO.ReadAllBytes(
                        subjectPath,
                        ResourceLimits.AuthoredLedgerBytes,
                        "component ledger subject"));
                RequireExactPackage(componentSubject.Package, inspected);
                ledger = EvidenceLedgerBuilder.BuildComponentLedger(
                    componentSubject,
                    draft.Records);
                break;
            default:
                throw new UsageException(
                    $"Unknown evidence ledger kind '{kind}'; expected repository or component.");
        }

        var bytes = CanonicalEvidenceJson.SerializeSourceLedger(ledger);
        BoundedIO.EnsureLength(
            bytes.Length,
            ResourceLimits.AuthoredLedgerBytes,
            "authored source ledger");
        WriteNew(outputPath, bytes);
        output.WriteLine(
            $"Evidence ledger built: {ledger.LedgerKind}, {ledger.Records.Count} records, " +
            $"sha256:{CanonicalEvidenceJson.ComputeSha256(bytes)}.");
        return ExitCodes.Success;
    }

    private static int ValidateLedger(IReadOnlyList<string> args, TextWriter output)
    {
        var options = ParseOptions(args, repeatableOptions: Array.Empty<string>(), "--ledger");
        RequireExactly(options, "--ledger");
        var bytes = BoundedIO.ReadAllBytes(
            options.Single("--ledger"),
            ResourceLimits.AuthoredLedgerBytes,
            "authored source ledger");
        var ledger = CanonicalEvidenceJson.ParseSourceLedger(bytes);
        output.WriteLine(
            $"Evidence ledger valid: {ledger.LedgerKind}, {ledger.Records.Count} records, " +
            $"sha256:{CanonicalEvidenceJson.ComputeSha256(bytes)}.");
        return ExitCodes.Success;
    }

    private static int BuildBundle(IReadOnlyList<string> args, TextWriter output)
    {
        var options = ParseOptions(
            args,
            repeatableOptions: ["--source-ledger"],
            "--assessment",
            "--source-ledger",
            "--ids",
            "--output",
            "--root",
            "--manifest");
        RequireExactly(options, "--assessment", "--ids", "--output");
        var inputBound = options.All("--root").Count != 0 || options.All("--manifest").Count != 0;
        if (inputBound)
        {
            RequireExactly(options, "--root", "--manifest");
        }
        var sourcePaths = options.All("--source-ledger");
        if (sourcePaths.Count == 0)
        {
            throw new UsageException(
                "Evidence bundle requires at least one --source-ledger input.");
        }

        if (sourcePaths.Count > ResourceLimits.SupplementalInputCount)
        {
            throw new DeterministicValidationException(
                $"Evidence bundle accepts at most {ResourceLimits.SupplementalInputCount} " +
                $"explicit supplemental inputs; observed {sourcePaths.Count}.");
        }

        var lengths = sourcePaths
            .Select(path => new FileInfo(path).Length)
            .ToArray();
        ValidateSupplementalInputs(lengths);

        var assessment = CanonicalEvidenceJson.ParseAssessment(
            BoundedIO.ReadAllBytes(
                options.Single("--assessment"),
                ResourceLimits.AuthoredLedgerBytes,
                "assessment identity"));
        var ledgers = sourcePaths
            .Select(path => CanonicalEvidenceJson.ParseSourceLedger(
                BoundedIO.ReadAllBytes(
                    path,
                    ResourceLimits.AuthoredLedgerBytes,
                    "authored source ledger")))
            .ToArray();
        var selected = options.Single("--ids").Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (selected.Length == 0)
        {
            throw new UsageException("--ids must contain at least one EV1 identifier.");
        }

        var bundle = EvidenceLedgerBuilder.BuildBundle(assessment, ledgers, selected);
        if (inputBound)
        {
            var (root, input, inputBytes) = LoadConfirmedInput(options);
            try
            {
                AssessmentService.ValidateInputIdentity(assessment, input, inputBytes);
                EvidenceInputBindingValidator.Validate(root, assessment, input, bundle);
            }
            catch (DeterministicValidationException error)
            {
                throw new DeterministicValidationException(
                    $"Input-bound acceptance against manifest '{options.Single("--manifest")}' failed: {error.Message}", error);
            }
        }
        var bytes = CanonicalEvidenceJson.SerializeBundle(bundle);
        EnsureSerializedOutput(bytes.Length);
        WriteNew(options.Single("--output"), bytes);
        output.WriteLine(
            $"Evidence bundle built ({(inputBound ? "input-bound" : "structural-only; input linkage NOT accepted")}): " +
            $"{bundle.SourceLedgers.Count} source ledgers, " +
            $"{bundle.Selection.Count} selected records, " +
            $"sha256:{CanonicalEvidenceJson.ComputeSha256(bytes)}.");
        return ExitCodes.Success;
    }

    private static ParsedOptions ParseOptions(
        IReadOnlyList<string> args,
        string[] repeatableOptions,
        params string[] allowedOptions)
    {
        var allowed = allowedOptions.ToHashSet(StringComparer.Ordinal);
        var repeatable = repeatableOptions.ToHashSet(StringComparer.Ordinal);
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index++)
        {
            var option = args[index];
            if (!allowed.Contains(option))
            {
                throw new UsageException($"Unknown evidence option '{option}'.");
            }

            if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                throw new UsageException($"{option} requires a non-empty value.");
            }

            if (!values.TryGetValue(option, out var optionValues))
            {
                optionValues = [];
                values.Add(option, optionValues);
            }
            else if (!repeatable.Contains(option))
            {
                throw new UsageException($"{option} may be supplied only once.");
            }

            optionValues.Add(args[++index]);
        }

        return new ParsedOptions(values);
    }

    private static void RequireExactly(ParsedOptions options, params string[] requiredOptions)
    {
        foreach (var option in requiredOptions)
        {
            if (options.All(option).Count != 1)
            {
                throw new UsageException($"Evidence command requires exactly one {option} value.");
            }
        }
    }

    private static void RequireExactPackage(
        EvidencePackageIdentity expected,
        EvidencePackageIdentity actual)
    {
        if (expected != actual)
        {
            throw new DeterministicValidationException(
                "EVID006: exact nupkg ID, version, or digest differs from ledger subject.");
        }
    }

    private static void VerifyPackageArtifactEvidence(
        string nupkgPath,
        IReadOnlyList<EvidenceRecordDraft> records)
    {
        foreach (var record in records.Where(record =>
                     string.Equals(
                         record.Provenance.Kind,
                         EvidenceIdentity.PackageArtifactMetadata,
                         StringComparison.Ordinal)))
        {
            var actual = NupkgInspector.ComputeEvidenceContentSha256(
                nupkgPath,
                record.Provenance.Locator);
            if (!string.Equals(
                    actual,
                    record.Provenance.ContentDigest.Value,
                    StringComparison.Ordinal))
            {
                throw new DeterministicValidationException(
                    $"EVID006: package evidence digest for '{record.Provenance.Locator}' " +
                    "differs from the exact nupkg content.");
            }
        }
    }

    private static void WriteNew(string path, byte[] bytes)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("Evidence output requires a parent directory.");
        AtomicFile.WriteNew(parent, Path.GetFileName(fullPath), bytes);
    }

    private static bool IsHelp(string value) =>
        string.Equals(value, "--help", StringComparison.Ordinal) ||
        string.Equals(value, "-h", StringComparison.Ordinal);

    private sealed class ParsedOptions(
        IReadOnlyDictionary<string, List<string>> values)
    {
        public string Single(string option) =>
            values.TryGetValue(option, out var optionValues) && optionValues.Count == 1
                ? optionValues[0]
                : throw new UsageException($"Evidence command requires exactly one {option} value.");

        public IReadOnlyList<string> All(string option) =>
            values.TryGetValue(option, out var optionValues) ? optionValues : [];
    }
}
