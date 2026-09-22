using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;

namespace BlazorComponentReadiness.Validator.Cli;

public static class AssessmentCommand
{
    private const string Help =
        """
        Canonical readiness assessments

        Usage:
          readiness-validator assessment init --kind <package|component> --root <dir> --input <confirmed> --output <json> [--component <id>] [--package-revision <dir>] [--package-feedback <markdown>]
          readiness-validator assessment canonicalize --assessment <json> --output <canonical-json>
          readiness-validator assessment export-identity --assessment <canonical-assessment> --output <new-identity-json>
          readiness-validator assessment validate --root <dir> --input <confirmed> --assessment <json> --evidence <bundle> [--package-revision <dir>] [--package-feedback <markdown>]
          readiness-validator assessment revise --root <dir> --input <confirmed> --assessment <replacement-json> --evidence <replacement-bundle> --output <revisions-root> --predecessor <digest> --changed-ids <id,id> [--feedback <markdown>] [--package-revision <dir>] [--package-feedback <markdown>]

        After init, export-identity writes the existing identity as exact canonical bytes
        for evidence ledger-build --subject and evidence bundle --assessment.
        This projects identity only; it does not validate evidence, bindings or readiness.
        Do not hand-author or reformat exported identity files. canonicalize writes a full
        assessment, not a standalone evidence identity. It also orders evidence IDs in rows,
        findings, and summary groups without adding, removing, or deduplicating references.
        Completed evidence handoffs require evidence bundle --root/--manifest to
        accept input linkage. This does not score null rows or validate readiness;
        assessment validate remains required for assessment correctness.
        Component assessments contain only the selected component's 52 checks and need no
        package assessment. A package revision may be bound only when explicitly supplied.
        Package assessments contain their separate 60 checks. Unified assessments and old
        scoped-component/package-context contracts are not supported.
        """;

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            output.WriteLine(Help);
            return ExitCodes.Success;
        }

        return args[0] switch
        {
            "init" => Initialize(args.Skip(1).ToArray(), output),
            "canonicalize" => Canonicalize(args.Skip(1).ToArray(), output),
            "export-identity" => ExportIdentity(args.Skip(1).ToArray(), output),
            "validate" => Validate(args.Skip(1).ToArray(), output),
            "revise" => Revise(args.Skip(1).ToArray(), output),
            _ => throw new UsageException($"Unknown assessment command '{args[0]}'.")
        };
    }

    private static int Canonicalize(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 1 && IsHelp(args[0]))
        {
            output.WriteLine(Help);
            return ExitCodes.Success;
        }

        var options = CommandOptions.Parse(args, "--assessment", "--output");
        var assessmentBytes = BoundedIO.ReadAllBytes(
            options.Single("--assessment"),
            ResourceLimits.SerializedArtifactBytes,
            "assessment");
        var assessment = AssessmentService.Parse(assessmentBytes, requireCanonical: false);
        var normalized = assessment with
        {
            Rows = assessment.Rows
                .Select(row => row with { EvidenceIds = row.EvidenceIds.Order(StringComparer.Ordinal).ToArray() })
                .ToArray(),
            Findings = assessment.Findings
                .Select(finding => finding with { EvidenceIds = finding.EvidenceIds.Order(StringComparer.Ordinal).ToArray() })
                .ToArray(),
            SummaryGroups = assessment.SummaryGroups
                .Select(group => group with { EvidenceIds = group.EvidenceIds.Order(StringComparer.Ordinal).ToArray() })
                .ToArray()
        };
        WriteNew(options.Single("--output"), AssessmentService.Serialize(normalized));
        return ExitCodes.Success;
    }

    private static int ExportIdentity(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 1 && IsHelp(args[0]))
        {
            output.WriteLine(Help);
            return ExitCodes.Success;
        }

        var options = CommandOptions.Parse(args, "--assessment", "--output");
        var bytes = BoundedIO.ReadAllBytes(
            options.Single("--assessment"),
            ResourceLimits.SerializedArtifactBytes,
            "canonical assessment");
        var assessment = AssessmentService.Parse(bytes);
        WriteNew(options.Single("--output"), CanonicalEvidenceJson.SerializeAssessment(assessment.Identity));
        return ExitCodes.Success;
    }

    private static int Initialize(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 1 && IsHelp(args[0]))
        {
            output.WriteLine(Help);
            return ExitCodes.Success;
        }

        var options = CommandOptions.Parse(
            args,
            "--kind",
            "--root",
            "--input",
            "--output",
            "--component",
            "--package-revision",
            "--package-feedback",
            "--package-context-revision",
            "--package-context-feedback");
        AssessmentBindingOptions.RejectRetiredOptions(options);
        var root = Path.GetFullPath(options.Single("--root"));
        var inputBytes = BoundedIO.ReadAllBytes(
            options.Single("--input"),
            ResourceLimits.SerializedArtifactBytes,
            "confirmed input manifest");
        var input = InputManifestService.Parse(inputBytes);
        var kind = options.Single("--kind");
        var bindings = AssessmentBindingOptions.Load(
            root,
            kind,
            input,
            options.Optional("--package-revision"),
            ReadFeedback(options.Optional("--package-feedback")),
            options.Optional("--package-context-revision"),
            ReadFeedback(options.Optional("--package-context-feedback")));
        var assessment = AssessmentService.Initialize(
            kind,
            root,
            input,
            inputBytes,
            options.Optional("--component"),
            bindings.Package,
            bindings.Context);
        WriteNew(options.Single("--output"), AssessmentService.Serialize(assessment));
        return ExitCodes.Success;
    }

    private static int Validate(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 1 && IsHelp(args[0]))
        {
            output.WriteLine(Help);
            return ExitCodes.Success;
        }

        var options = CommandOptions.Parse(
            args,
            "--root",
            "--input",
            "--assessment",
            "--evidence",
            "--package-revision",
            "--package-feedback",
            "--package-context-revision",
            "--package-context-feedback");
        AssessmentBindingOptions.RejectRetiredOptions(options);
        var root = Path.GetFullPath(options.Single("--root"));
        var inputBytes = BoundedIO.ReadAllBytes(
            options.Single("--input"),
            ResourceLimits.SerializedArtifactBytes,
            "confirmed input manifest");
        var input = InputManifestService.Parse(inputBytes);
        InputManifestService.Validate(input, root, requireConfirmed: true);
        var assessmentBytes = BoundedIO.ReadAllBytes(
            options.Single("--assessment"),
            ResourceLimits.SerializedArtifactBytes,
            "assessment");
        var assessment = AssessmentService.Parse(assessmentBytes);
        var evidence = CanonicalEvidenceJson.ParseBundle(BoundedIO.ReadAllBytes(
            options.Single("--evidence"),
            ResourceLimits.SerializedArtifactBytes,
            "evidence bundle"));
        var bindings = AssessmentBindingOptions.Load(
            root,
            assessment.AssessmentKind,
            input,
            options.Optional("--package-revision"),
            ReadFeedback(options.Optional("--package-feedback")),
            options.Optional("--package-context-revision"),
            ReadFeedback(options.Optional("--package-context-feedback")));
        AssessmentService.Validate(
            root,
            assessment,
            assessmentBytes,
            input,
            inputBytes,
            evidence,
            bindings.Package,
            bindings.Context);
        return ExitCodes.Success;
    }

    private static int Revise(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 1 && IsHelp(args[0]))
        {
            output.WriteLine(Help);
            return ExitCodes.Success;
        }

        if (!args.Contains("--predecessor", StringComparer.Ordinal) ||
            !args.Contains("--changed-ids", StringComparer.Ordinal))
        {
            throw new UsageException(
                "assessment revise requires --predecessor and --changed-ids.");
        }

        return ReportCommand.Render(args, output);
    }

    private static byte[]? ReadFeedback(string? path) => path is null ? null :
        BoundedIO.ReadAllBytes(path, ResourceLimits.SerializedArtifactBytes, "package/context assessment feedback");

    private static void WriteNew(string path, byte[] bytes)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetDirectoryName(fullPath)
            ?? throw new UsageException("Output path requires a parent directory.");
        Directory.CreateDirectory(root);
        AtomicFile.WriteNew(root, Path.GetFileName(fullPath), bytes);
    }

    private static bool IsHelp(string value) => value is "--help" or "-h";
}
