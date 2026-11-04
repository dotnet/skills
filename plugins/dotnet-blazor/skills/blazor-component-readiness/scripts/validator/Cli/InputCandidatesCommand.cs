using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Cli;

internal static class InputCandidatesCommand
{
    private static string Help =>
        $"""
        Construct discovery candidates with typed options, without authoring JSON.

        Usage: readiness-validator inputs candidates <command> [options]

        init --acquisition <{string.Join("|", InputManifestService.AcquisitionKinds)}>
             --package-locator <url|path> --package-method <method>
             --source-availability <{string.Join("|", InputManifestService.SourceAvailabilityKinds)}>
             [--repository-uri <url>] [--source-commit <commit>]
             [--source-mapping <text>] [--source-confidence <{string.Join("|", InputManifestService.SourceConfidenceKinds)}>] --output <new.json>

        Each add command requires --input <previous.json> --output <new.json>:
          add-document --url <url> --path <local-path>
          add-package-source --kind <kind> --locator <locator> --path <local-path>
          add-source-artifact --source-path <repository-path> --path <local-path>
          add-retrieval --subject <subject> --locator <locator> --method <method>
                        --result <result> [--detail <text>]
          add-evidence --path <root-level-basename> --kind <kind>
          add-owner-input --path <root-level-basename> --provenance <provenance>
          add-component --id <id> --name <name> --mode <mode> [--mode <mode> ...]
                        [--source-path <path> ...] --lifecycle-applicability <value>
                        [--lifecycle-trigger <trigger> ...] [--lifecycle-rationale <text>]
          add-exclusion --subject <subject> --rationale <text>

        --package-method accepts: {string.Join(", ", InputManifestService.PackageOriginMethods)}.
        add-retrieval --method accepts: {string.Join(", ", InputManifestService.RetrievalMethods)}.
        Retrieval subjects: {string.Join(", ", InputManifestService.RetrievalSubjects)}.
        Retrieval results: {string.Join(", ", InputManifestService.RetrievalResults)}.
        Package source kinds: {string.Join(", ", InputManifestService.PackageSourceKinds)}.
        Owner input provenances: {string.Join(", ", InputManifestService.OwnerInputProvenances)}.
        Render modes accept: {string.Join(", ", Canonicalization.RenderModeAliases)}.
        Lifecycle applicability: {string.Join(", ", InputManifestService.DynamicChildLifecycleApplicabilities)}.
        Lifecycle triggers: {string.Join(", ", InputManifestService.DynamicChildLifecycleTriggerKinds)}.
        Record transport-specific routes in locator/detail, not in invented method names.
        Evidence and owner files must be retained directly under the later inputs
        discover --root. Supply only their filenames, without directory components.
        Documentation, package-source and source-artifact content paths may be nested
        relative paths under that root. Source paths are repository-relative.
        local-file/owner-supplied retrieval locators are relative paths; other methods
        use public HTTPS locators. Repository URIs require a path and source commits
        require an exact 40-hex commit. Package-source locators remain bounded text.
        Syntax is checked before writing, without rewriting valid original values.
        No files are copied, relocated or checked for existence during construction.
        All output files must be new. Pass the latest output to the next command.
        Empty collections mean nothing has been recorded yet, not confirmed absence.
        Do not edit generated JSON or supply hashes. inputs discover computes hashes
        and validates completeness and semantics against the retained package/files.
        Creation does not confirm owner inputs or the assessment scope.
        """;

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 0 || args[0] is "--help" or "-h" ||
            (args.Count == 2 && args[1] is "--help" or "-h"))
        {
            output.WriteLine(Help);
            return ExitCodes.Success;
        }

        string[] allowed = args[0] switch
        {
            "init" => new[] { "--acquisition", "--package-locator", "--package-method",
                "--source-availability", "--repository-uri", "--source-commit",
                "--source-mapping", "--source-confidence" },
            "add-document" => ["--url", "--path"],
            "add-package-source" => ["--kind", "--locator", "--path"],
            "add-source-artifact" => ["--source-path", "--path"],
            "add-retrieval" => ["--subject", "--locator", "--method", "--result", "--detail"],
            "add-evidence" => ["--path", "--kind"],
            "add-owner-input" => ["--path", "--provenance"],
            "add-component" => ["--id", "--name", "--mode", "--source-path",
                "--lifecycle-applicability", "--lifecycle-trigger", "--lifecycle-rationale"],
            "add-exclusion" => ["--subject", "--rationale"],
            _ => throw new UsageException($"Unknown inputs candidates command '{args[0]}'.")
        };
        var options = CommandOptions.Parse(
            args.Skip(1).ToArray(),
            args[0] == "init" ? [.. allowed, "--output"] : [.. allowed, "--input", "--output"]);
        var facts = args[0] == "init" ? Initialize(options) : Add(args[0], options);
        var bytes = InputCandidateBuilder.Build(facts);
        var fullPath = Path.GetFullPath(options.Single("--output"));
        var parent = Path.GetDirectoryName(fullPath)
            ?? throw new UsageException("Output path requires a parent directory.");
        Directory.CreateDirectory(parent);
        AtomicFile.WriteNew(parent, Path.GetFileName(fullPath), bytes);
        return ExitCodes.Success;
    }

    private static InputCandidateFacts Initialize(CommandOptions options) => new(
        InputManifestService.SchemaVersion,
        options.Single("--acquisition"),
        options.Single("--package-locator"),
        options.Single("--package-method"),
        options.Single("--source-availability"),
        options.Optional("--repository-uri"),
        options.Optional("--source-commit"),
        options.Optional("--source-mapping"),
        options.Optional("--source-confidence"),
        [], [], [], [], [], [], [], []);

    private static InputCandidateFacts Add(string command, CommandOptions options)
    {
        var facts = InputCandidateBuilder.Read(BoundedIO.ReadAllBytes(
            options.Single("--input"), ResourceLimits.SerializedArtifactBytes, "input candidates"));
        return command switch
        {
            "add-document" => facts with
            {
                Documentation = [.. facts.Documentation, new(options.Single("--url"), options.Single("--path"))]
            },
            "add-package-source" => facts with
            {
                PackageSources = [.. facts.PackageSources, new(options.Single("--kind"),
                    options.Single("--locator"), options.Single("--path"))]
            },
            "add-source-artifact" => facts with
            {
                SourceArtifacts = [.. facts.SourceArtifacts, new(options.Single("--source-path"), options.Single("--path"))]
            },
            "add-retrieval" => facts with
            {
                RetrievalAttempts = [.. facts.RetrievalAttempts, new(options.Single("--subject"),
                    options.Single("--locator"), options.Single("--method"), options.Single("--result"), options.Optional("--detail"))]
            },
            "add-evidence" => facts with
            {
                EvidenceInputs = [.. facts.EvidenceInputs, new(options.Single("--path"), options.Single("--kind"))]
            },
            "add-owner-input" => facts with
            {
                OwnerInputs = [.. facts.OwnerInputs, new(options.Single("--path"), options.Single("--provenance"))]
            },
            "add-component" => facts with
            {
                Components = [.. facts.Components, new(options.Single("--id"), options.Single("--name"),
                    options.Many("--mode"), options.Many("--source-path"), options.Single("--lifecycle-applicability"),
                    options.Many("--lifecycle-trigger"), options.Optional("--lifecycle-rationale"))]
            },
            "add-exclusion" => facts with
            {
                Exclusions = [.. facts.Exclusions, new(options.Single("--subject"), options.Single("--rationale"))]
            },
            _ => throw new UsageException($"Unknown inputs candidates command '{command}'.")
        };
    }
}
