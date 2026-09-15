using BlazorComponentReadiness.Validator.IO;
using System.Text;

namespace BlazorComponentReadiness.Validator.Cli;

public static class CliApplication
{
    private const string RootHelp =
        """
        Blazor component readiness validator

        Usage:
          readiness-validator <command> [<subcommand> ...] [options]

        Commands:
          package inspect    Inspect the exact identity and digest of a local .nupkg.
          package prepare    Collect package facts/full archive inventory and seal a receipt.
          release facts      Collect offline facts from six confirmed release inputs.
          source inventory-archive
                             Inventory regular files in a retained source archive.
          source capture-inventory
                             Capture selected inventory entry IDs from an extracted source archive.
          source capture-archive
                             Capture selected files from an already extracted source archive.
          inputs candidates  Construct discovery candidates with typed init/add options.
          inputs discover    Canonicalize discovery candidates into a draft input manifest.
          inputs confirm     Freeze a validated draft as an immutable confirmed manifest.
          inputs validate    Recheck confirmed manifest package and local input bytes.
          evidence ledger-build
                             Build an immutable repository or component source ledger.
          evidence draft-add
                             Add a typed, digest-computed record to an untrusted draft.
          evidence ledger-validate
                             Validate canonical source-ledger bytes and identities.
          evidence bundle    Select evidence into a self-contained assessment bundle.
          assessment init    Create canonical unified, package, or component rows.
          assessment validate
                             Validate an assessment against inputs, rubric, and evidence.
          assessment revise  Validate an evidence-backed correction and render its next revision.
          report render      Render the next immutable deterministic revision.
          report verify      Verify report bytes and every validation-manifest binding.
          reader render      Derive the four-column partner-preview report outside revisions.
          reader verify      Recompute and verify reader coverage, source bindings and evidence.
          inventory discover Canonicalize package/component library discovery candidates.
          inventory confirm  Freeze one immutable confirmed library inventory.
          inventory status   Report completed, pending, blocked, incomplete, or invalid work.
          library reconcile  Reconstruct mutable run state from immutable validated revisions.
          library validate   Prove every confirmed library work unit is accounted for.
          library index      Write deterministic factual JSON and Markdown library indexes.
          comparison inputs-freeze
                             Freeze conclusion-free exact inputs for a blinded comparison.
          comparison inputs-validate
                             Revalidate the exact allowed-input set before a blind worker starts.
          help               Show this help.

        Pass each command word as a separate argument. Append --help after the
        command words for details, for example:
          readiness-validator inputs candidates --help
          readiness-validator evidence draft-add --help
        """;

    private const string PackageInspectHelp =
        """
        Inspect a local NuGet package

        Usage:
          readiness-validator package inspect --nupkg <path> [--output <new-file>]

        Output:
          Canonical JSON containing the exact package ID, version, archive digest,
          archive size, and nuspec entry name. When --output is supplied, the
          canonical JSON is written once to the new file instead of stdout.
        """;

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Count == 0)
            {
                throw new UsageException("A command is required.");
            }

            if (IsHelp(args[0]) || string.Equals(args[0], "help", StringComparison.Ordinal))
            {
                output.WriteLine(RootHelp);
                return ExitCodes.Success;
            }

            if (string.Equals(args[0], "evidence", StringComparison.Ordinal))
            {
                return EvidenceCommand.Run(args.Skip(1).ToArray(), output);
            }

            if (string.Equals(args[0], "release", StringComparison.Ordinal))
            {
                return ReleaseCommand.Run(args.Skip(1).ToArray(), output);
            }

            if (string.Equals(args[0], "inputs", StringComparison.Ordinal))
            {
                return InputsCommand.Run(args.Skip(1).ToArray(), output);
            }

            if (string.Equals(args[0], "assessment", StringComparison.Ordinal))
            {
                return AssessmentCommand.Run(args.Skip(1).ToArray(), output);
            }

            if (string.Equals(args[0], "report", StringComparison.Ordinal))
            {
                return ReportCommand.Run(args.Skip(1).ToArray(), output);
            }

            if (string.Equals(args[0], "reader", StringComparison.Ordinal))
            {
                return ReaderCommand.Run(args.Skip(1).ToArray(), output);
            }

            if (string.Equals(args[0], "inventory", StringComparison.Ordinal))
            {
                return InventoryCommand.Run(args.Skip(1).ToArray(), output);
            }

            if (string.Equals(args[0], "library", StringComparison.Ordinal))
            {
                return LibraryCommand.Run(args.Skip(1).ToArray(), output);
            }

            if (string.Equals(args[0], "comparison", StringComparison.Ordinal))
            {
                return ComparisonCommand.Run(args.Skip(1).ToArray(), output);
            }

            if (string.Equals(args[0], "source", StringComparison.Ordinal))
            {
                return RunSource(args.Skip(1).ToArray(), output);
            }

            if (!string.Equals(args[0], "package", StringComparison.Ordinal))
            {
                throw new UsageException($"Unknown command '{args[0]}'.");
            }

            return RunPackage(args.Skip(1).ToArray(), output);
        }
        catch (UsageException exception)
        {
            error.WriteLine($"usage error: {exception.Message}");
            error.WriteLine();
            error.WriteLine(RootHelp);
            return ExitCodes.InvalidUsage;
        }
        catch (DeterministicValidationException exception)
        {
            error.WriteLine($"validation error: {exception.Message}");
            return ExitCodes.ValidationFailure;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error.WriteLine($"environment error: {exception.Message}");
            return ExitCodes.EnvironmentFailure;
        }
        catch (Exception exception)
        {
            error.WriteLine($"environment error: unexpected validator failure: {exception.Message}");
            return ExitCodes.EnvironmentFailure;
        }
    }

    private static int RunPackage(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count > 0 && args[0] == "prepare")
            return PreparationCommand.Run(args.Skip(1).ToArray(), output);
        if (args.Count == 0 || IsHelp(args[0]))
        {
            output.WriteLine(PackageInspectHelp);
            return ExitCodes.Success;
        }

        if (!string.Equals(args[0], "inspect", StringComparison.Ordinal))
        {
            throw new UsageException($"Unknown package command '{args[0]}'.");
        }

        if (args.Count == 2 && IsHelp(args[1]))
        {
            output.WriteLine(PackageInspectHelp);
            return ExitCodes.Success;
        }

        var options = CommandOptions.Parse(args.Skip(1).ToArray(), "--nupkg", "--output");
        var packagePath = options.Single("--nupkg");
        var outputPath = options.Optional("--output");
        var identity = NupkgInspector.Inspect(packagePath);
        var json = NupkgInspector.SerializeInspection(identity);

        if (outputPath is not null)
        {
            var result = json.Concat(Encoding.UTF8.GetBytes(Environment.NewLine)).ToArray();
            var fullOutputPath = Path.GetFullPath(outputPath);
            var outputParent = Path.GetDirectoryName(fullOutputPath)
                ?? throw new UsageException("Output path requires a parent directory.");
            AtomicFile.WriteNew(outputParent, Path.GetFileName(fullOutputPath), result);
            return ExitCodes.Success;
        }

        output.Write(Encoding.UTF8.GetString(json));
        output.WriteLine();
        return ExitCodes.Success;
    }

    private const string SourceCaptureHelp =
        """
        Inventory and capture selected files from an already extracted source archive.

        Usage:
          readiness-validator source inventory-archive --root <root>
            --archive <retained.tar.gz|zip> --source-root <extracted-repository-root>
            [--archive-prefix <directory>] --archive-format <tar.gz|zip>
            [--expected-sha256 <64-hex>] --output <new-inventory.json>

          readiness-validator source capture-inventory --root <root>
            --inventory <inventory.json> --entry-id <1-based-id> [--entry-id <id> ...]
            --repository-uri <url> --source-commit <40-hex-commit>
            --source-mapping <text> --source-confidence <low|medium|high>
            --acquisition-locator <url> --output <new-receipt.json>

          readiness-validator source capture-archive --root <root>
            --archive <retained.tar.gz|zip> --source-root <extracted-repository-root>
            [--archive-prefix <directory>] --archive-format <tar.gz|zip>
            --repository-uri <url> --source-commit <40-hex-commit>
            --source-mapping <text> --source-confidence <low|medium|high>
            --acquisition-locator <url> --source-path <repository-path> [--source-path <path> ...]
            [--expected-sha256 <64-hex>] --output <new-receipt.json>

        source-root is the actual local repository root where each repository-relative source_path
        exists; it may be nested below the extraction destination. archive-prefix applies only to
        archive entry names and never strips or changes local extraction paths. For example, after
        'tar -xzf archive.tar.gz -C extracted', use --source-root extracted/bundle with
        --archive-prefix bundle. With '--strip-components=1', use --source-root extracted but
        keep --archive-prefix bundle because the retained archive bytes are unchanged.

        The operation never invokes Git or extracts an archive. It verifies selected files against
        the retained archive and writes one deterministic, immutable receipt.
        """;

    private static int RunSource(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            output.WriteLine(SourceCaptureHelp);
            return ExitCodes.Success;
        }

        if (args[0] is not ("inventory-archive" or "capture-inventory" or "capture-archive"))
        {
            throw new UsageException($"Unknown source command '{args[0]}'.");
        }

        if (args.Count == 2 && IsHelp(args[1]))
        {
            output.WriteLine(SourceCaptureHelp);
            return ExitCodes.Success;
        }

        if (args[0] == "inventory-archive")
        {
            var options = CommandOptions.Parse(
                args.Skip(1).ToArray(),
                "--root",
                "--archive",
                "--source-root",
                "--archive-prefix",
                "--archive-format",
                "--expected-sha256",
                "--output");
            _ = SourceArchiveCaptureService.InventoryArchive(
                options.Single("--root"),
                options.Single("--archive"),
                options.Single("--source-root"),
                options.Optional("--archive-prefix"),
                options.Single("--archive-format"),
                options.Optional("--expected-sha256"),
                options.Single("--output"));
            return ExitCodes.Success;
        }

        if (args[0] == "capture-inventory")
        {
            var options = CommandOptions.Parse(
                args.Skip(1).ToArray(),
                "--root",
                "--inventory",
                "--entry-id",
                "--repository-uri",
                "--source-commit",
                "--source-mapping",
                "--source-confidence",
                "--acquisition-locator",
                "--output");
            _ = SourceArchiveCaptureService.CaptureInventory(
                options.Single("--root"),
                options.Single("--inventory"),
                options.Many("--entry-id"),
                options.Single("--repository-uri"),
                options.Single("--source-commit"),
                options.Single("--source-mapping"),
                options.Single("--source-confidence"),
                options.Single("--acquisition-locator"),
                options.Single("--output"));
            return ExitCodes.Success;
        }

        var captureOptions = CommandOptions.Parse(
            args.Skip(1).ToArray(),
            "--root",
            "--archive",
            "--source-root",
            "--archive-prefix",
            "--archive-format",
            "--repository-uri",
            "--source-commit",
            "--source-mapping",
            "--source-confidence",
            "--acquisition-locator",
            "--source-path",
            "--expected-sha256",
            "--output");
        _ = SourceArchiveCaptureService.Capture(
            captureOptions.Single("--root"),
            captureOptions.Single("--archive"),
            captureOptions.Single("--source-root"),
            captureOptions.Optional("--archive-prefix"),
            captureOptions.Single("--archive-format"),
            captureOptions.Single("--repository-uri"),
            captureOptions.Single("--source-commit"),
            captureOptions.Single("--source-mapping"),
            captureOptions.Single("--source-confidence"),
            captureOptions.Single("--acquisition-locator"),
            captureOptions.Many("--source-path"),
            captureOptions.Optional("--expected-sha256"),
            captureOptions.Single("--output"));
        return ExitCodes.Success;
    }

    private static bool IsHelp(string value) =>
        string.Equals(value, "--help", StringComparison.Ordinal) ||
        string.Equals(value, "-h", StringComparison.Ordinal);

}
