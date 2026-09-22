using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.Comparison;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Rendering;
using BlazorComponentReadiness.Validator.Validation;
using CurrentAssessmentService = BlazorComponentReadiness.Validator.Assessment.AssessmentService;

internal static class EnforcementTests
{
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

    public static void Run(string repositoryRoot, string pluginRoot)
    {
        var root = Path.Combine(
            Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ?? Path.Combine(repositoryRoot, "artifacts"),
            "readiness-enforcement-tests");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        var previousSkillRoot = Environment.GetEnvironmentVariable("READINESS_SKILL_ROOT");
        Environment.SetEnvironmentVariable(
            "READINESS_SKILL_ROOT",
            Path.Combine(pluginRoot, "skills", "blazor-component-readiness"));
        try
        {
            TestDocumentedSourceFinding(repositoryRoot, pluginRoot, root);
            TestStructuredVerifiedBoundaries(root);
            TestAutoTransitionProtocol(root);
            TestDynamicLifecycleMatrix(root);
            TestNonDynamicSourceProofs(root);
            TestToolchainDisposition(root);
            TestComparisonInputGate(root);
        }
        finally
        {
            Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", previousSkillRoot);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void TestDocumentedSourceFinding(string repositoryRoot, string pluginRoot, string root)
    {
        var acceptanceParent = Path.Combine(Path.GetDirectoryName(root)!, "source-finding-acceptance");
        var priorAcceptanceFiles = Directory.Exists(acceptanceParent)
            ? SourceFindingFileDigests(acceptanceParent)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var exampleRoot = Path.Combine(root, "documented-source-finding");
        Directory.CreateDirectory(exampleRoot);
        var payload = Path.Combine(exampleRoot, "preview-plugin");
        CopySourceFindingDirectory(pluginRoot, payload);
        var skill = Path.Combine(payload, "skills", "blazor-component-readiness");
        var originalSkill = Path.Combine(pluginRoot, "skills", "blazor-component-readiness");
        var reference = File.ReadAllText(Path.Combine(skill, "references", "area-blazor-runtime.md"));
        const string templateRelative = "assets/source-finding/SyntheticCallbackGroup.cs.txt";
        var template = Path.GetFullPath(Path.Combine(skill, templateRelative));
        var originalTemplate = Path.GetFullPath(Path.Combine(originalSkill, templateRelative));
        AssertEqual(false, Path.GetRelativePath(payload, template).StartsWith("..", StringComparison.Ordinal),
            "delivered template is contained in copied plugin");
        AssertEqual(true, File.Exists(Path.Combine(payload, "plugin.json")),
            "local-preview directory includes its manifest");
        AssertEqual(true, reference.Contains($"../{templateRelative}", StringComparison.Ordinal),
            "copied documentation resolves its own delivered content");
        AssertEqual(true, File.ReadAllBytes(originalTemplate).SequenceEqual(File.ReadAllBytes(template)),
            "delivered template bytes equal checkout content");
        var payloadBefore = SourceFindingFileDigests(payload);
        var commands = new List<object>();
        var controls = new List<object>();
        var compileItems = new List<object>();
        var hiddenLookups = new Dictionary<string, bool>(StringComparer.Ordinal);
        void ConfigureHiddenLookup(string path)
        {
            if (OperatingSystem.IsWindows())
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
            var hidden = File.GetAttributes(path).HasFlag(FileAttributes.Hidden);
            AssertEqual(OperatingSystem.IsWindows() || Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal),
                hidden, "Windows Hidden attributes and Unix dot-prefix visibility are distinct");
            hiddenLookups[path] = hidden;
        }
        var environment = new Dictionary<string, string>();
        var pwsh = ResolveSourceFindingApplication(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
        var dotnet = ResolveSourceFindingApplication(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        EvidenceTests.ProcessResult Process(string executable, string[] arguments, int? expected = 0)
        {
            var result = EvidenceTests.RunProcess(executable, arguments, exampleRoot, environment, expected);
            commands.Add(new { executable, arguments, result.ExitCode, result.StandardOutput, result.StandardError });
            return result;
        }
        var hostScript = Path.Combine(exampleRoot, "host.ps1");
        File.WriteAllText(hostScript,
            "$PSVersionTable | Select-Object PSVersion,PSEdition | ConvertTo-Json -Compress",
            new UTF8Encoding(false));
        var host = Process(pwsh, ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", hostScript]);
        using var hostJson = JsonDocument.Parse(host.StandardOutput);
        AssertEqual("Core", hostJson.RootElement.GetProperty("PSEdition").GetString(), "actual PowerShell edition");
        AssertEqual(7, hostJson.RootElement.GetProperty("PSVersion").GetProperty("Major").GetInt32(),
            "actual PowerShell major version");
        AssertEqual(true, hostJson.RootElement.GetProperty("PSVersion").GetProperty("Minor").GetInt32() >= 3,
            "actual PowerShell host must be Core 7.3 or later within 7.x");
        var sdk = Process(dotnet, ["--version"]).StandardOutput.Trim();
        AssertEqual(true, sdk.StartsWith("11.", StringComparison.Ordinal), "active source-finding SDK");
        var copyScript = Path.Combine(exampleRoot, "retain-template.ps1");
        File.WriteAllText(copyScript,
            EvidenceTests.ExtractCodeBlock(reference, "### Retain the inert template during prerequisite intake", "powershell"),
            new UTF8Encoding(false));
        var inputRoot = Path.Combine(exampleRoot, ".inputs");
        Directory.CreateDirectory(inputRoot);
        foreach (var path in new[] { skill, inputRoot, template })
            ConfigureHiddenLookup(path);
        Process(pwsh, ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", copyScript,
            "-SkillDir", skill, "-InputRoot", inputRoot]);
        var retainedSource = Path.Combine(inputRoot, "SyntheticCallbackGroup.cs");
        AssertEqual(true, File.ReadAllBytes(template).SequenceEqual(File.ReadAllBytes(retainedSource)),
            "documented copy retains exact inert-template bytes");
        var retainAgain = Process(pwsh, ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", copyScript,
            "-SkillDir", skill, "-InputRoot", inputRoot], expected: null);
        AssertEqual(false, retainAgain.ExitCode == 0, "hidden retained source must not be overwritten");
        AssertEqual(true, retainAgain.StandardError.Contains("already exists", StringComparison.OrdinalIgnoreCase),
            "actual documented copy preserves no-overwrite failure");
        AssertEqual(true, File.ReadAllBytes(template).SequenceEqual(File.ReadAllBytes(retainedSource)),
            "failed repeat retention preserves exact bytes");
        controls.Add(new { name = "hidden-retain-no-overwrite", retainAgain.ExitCode, message = retainAgain.StandardError });

        var missingPayload = Path.Combine(exampleRoot, "missing-template-plugin");
        CopySourceFindingDirectory(payload, missingPayload);
        var missingSkill = Path.Combine(missingPayload, "skills", "blazor-component-readiness");
        File.Delete(Path.Combine(missingSkill, templateRelative));
        var missingInputs = Path.Combine(exampleRoot, "missing-template-inputs");
        Directory.CreateDirectory(missingInputs);
        var missing = Process(pwsh, ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", copyScript,
            "-SkillDir", missingSkill, "-InputRoot", missingInputs], expected: null);
        AssertEqual(false, missing.ExitCode == 0, "missing delivered template fails execution");
        AssertEqual(true, missing.StandardError.Contains("Missing source-finding template", StringComparison.Ordinal),
            "missing template has explicit failure rather than checkout fallback");
        AssertEqual(true, File.Exists(originalTemplate), "checkout template remains accessible to the negative control");
        AssertEqual(0, Directory.GetFiles(missingInputs).Length, "missing template produces no retained substitute");
        controls.Add(new { name = "missing-template", missing.ExitCode, message = missing.StandardError });

        var dotPrefixedChild = Path.Combine(payload, "..inputs");
        Directory.CreateDirectory(dotPrefixedChild);
        ConfigureHiddenLookup(dotPrefixedChild);
        var contained = Process(pwsh, ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", copyScript,
            "-SkillDir", skill, "-InputRoot", dotPrefixedChild], expected: null);
        AssertEqual(false, contained.ExitCode == 0, "dot-prefixed plugin child is not external");
        AssertEqual(true, contained.StandardError.Contains("Retained source must be outside the plugin.", StringComparison.Ordinal),
            "actual documented retain block rejects dot-prefixed child");
        AssertEqual(0, Directory.GetFiles(dotPrefixedChild, "*", SearchOption.AllDirectories).Length,
            "contained dot-prefixed input is rejected before any source copy");
        controls.Add(new { name = "dot-prefixed-plugin-child", contained.ExitCode, message = contained.StandardError });

        var captureStatement = EvidenceTests.ExtractCodeBlock(reference,
                "### Reconfirm inputs and bind the evidence", "powershell")
            .Split('\n').Single(line => line.StartsWith("$CapturedAt = ", StringComparison.Ordinal));
        var cultureScript = Path.Combine(exampleRoot, "capture-non-gregorian.ps1");
        File.WriteAllText(cultureScript,
            "$ErrorActionPreference = 'Stop'\n" +
            "[System.Globalization.CultureInfo]::CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('th-TH')\n" +
            captureStatement + "\n" +
            "[ordered]@{ culture = [System.Globalization.CultureInfo]::CurrentCulture.Name; " +
            "calendar = [System.Globalization.CultureInfo]::CurrentCulture.Calendar.GetType().Name; " +
            "captured_at = $CapturedAt } | ConvertTo-Json -Compress\n", new UTF8Encoding(false));
        var captureStart = DateTimeOffset.UtcNow.AddSeconds(-1);
        var culture = Process(pwsh, ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", cultureScript]);
        var captureEnd = DateTimeOffset.UtcNow;
        using var cultureJson = JsonDocument.Parse(culture.StandardOutput);
        AssertEqual("th-TH", cultureJson.RootElement.GetProperty("culture").GetString(), "actual non-Gregorian culture");
        AssertEqual("ThaiBuddhistCalendar", cultureJson.RootElement.GetProperty("calendar").GetString(),
            "capture control actually uses the non-Gregorian host calendar");
        var capturedUtc = DateTimeOffset.ParseExact(cultureJson.RootElement.GetProperty("captured_at").GetString()!,
            "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        AssertEqual(true, capturedUtc >= captureStart && capturedUtc <= captureEnd,
            "actual documented capture expression emits the correct Gregorian UTC instant");
        controls.Add(new { name = "non-gregorian-capture", culture.ExitCode, message = culture.StandardOutput });

        const string componentId = "synthetic-callback-group";
        const string siblingId = "synthetic-sibling";
        const string sourcePath = "src/SyntheticCallbackGroup.cs";
        const string siblingSourcePath = "src/SyntheticSibling.cs";
        const string siblingBasename = "SyntheticSibling.cs.txt";
        var nupkg = Path.Combine(inputRoot, "package.nupkg");
        CreatePackage(nupkg);
        WriteFile(inputRoot, siblingBasename, "// Synthetic sibling retained only as inspection data.");
        WriteFile(inputRoot, "intake-note.txt", "Test-only synthetic intake; no package or source was compiled.");
        var sequence = 0;
        var candidates = string.Empty;
        string Candidate(string verb, params string[] arguments)
        {
            var output = Path.Combine(inputRoot, $"..setup-candidates-{sequence++}.json");
            RunSourceFindingCli(["inputs", "candidates", verb,
                .. (verb == "init" ? Array.Empty<string>() : ["--input", candidates]),
                .. arguments, "--output", output]);
            candidates = output;
            return output;
        }
        Candidate("init", "--acquisition", "release-candidate", "--package-locator", "package.nupkg",
            "--package-method", "local-file", "--source-availability", "source-available",
            "--repository-uri", "https://source.example.test/synthetic/callbacks",
            "--source-commit", new string('a', 40),
            "--source-mapping", "Test-only package and source are paired by fixture construction, not by compilation.",
            "--source-confidence", "high");
        Candidate("add-retrieval", "--subject", "package", "--locator", "package.nupkg",
            "--method", "local-file", "--result", "succeeded");
        Candidate("add-source-artifact", "--source-path", sourcePath, "--path", "SyntheticCallbackGroup.cs");
        Candidate("add-source-artifact", "--source-path", siblingSourcePath, "--path", siblingBasename);
        Candidate("add-evidence", "--path", "intake-note.txt", "--kind", "reviewer-analysis");
        foreach (var (id, allowedPath) in new[] { (componentId, sourcePath), (siblingId, siblingSourcePath) })
        {
            Candidate("add-component", "--id", id, "--name", id, "--mode", "interactive-server",
                "--source-path", allowedPath, "--lifecycle-applicability", "required",
                "--lifecycle-trigger", "grouped-children", "--lifecycle-trigger", "registered-children");
        }
        var priorDraft = Path.Combine(inputRoot, "setup-draft.json");
        var priorConfirmed = Path.Combine(inputRoot, "..setup-confirmed.json");
        RunSourceFindingCli(["inputs", "discover", "--root", inputRoot, "--nupkg", nupkg,
            "--candidates", candidates, "--output", priorDraft]);
        RunSourceFindingCli(["inputs", "confirm", "--root", inputRoot, "--draft", priorDraft, "--output", priorConfirmed]);
        RunSourceFindingCli(["inputs", "validate", "--root", inputRoot, "--manifest", priorConfirmed]);
        foreach (var path in new[] { retainedSource, nupkg, candidates, priorConfirmed,
            Path.Combine(skill, "scripts", "validator", "run-validator.ps1") })
            ConfigureHiddenLookup(path);
        var prerequisitesBefore = SourceFindingFileDigests(inputRoot);
        var prior = InputManifestService.Parse(File.ReadAllBytes(priorConfirmed));
        var buildScratch = Path.Combine(exampleRoot, "validator-build");
        var validatorRelative = Path.Combine("scripts", "validator", "BlazorComponentReadiness.Validator.csproj");
        var contractProject = Path.Combine(repositoryRoot, "tests", "dotnet-blazor", "readiness-validator",
            "BlazorComponentReadiness.ContractTests.csproj");
        var contractBuild = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var offline = Path.Combine(originalSkill, "scripts", "validator", "restore-offline.config");
        foreach (var (project, build) in new[]
        {
            (contractProject, contractBuild),
            (Path.Combine(originalSkill, validatorRelative),
                Path.Combine(Path.GetDirectoryName(root)!, "readiness-evidence-cli-tests", "validator-build")),
            (Path.Combine(skill, validatorRelative), buildScratch)
        })
        {
            var arguments = SourceFindingCompileArguments(project, build,
                project == contractProject ? offline : Path.Combine(Path.GetDirectoryName(project)!, "restore-offline.config"));
            var result = Process(dotnet, arguments);
            using var items = JsonDocument.Parse(result.StandardOutput);
            var paths = items.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray()
                .Select(item => Path.GetFullPath(item.GetProperty("FullPath").GetString()!)).ToArray();
            AssertEqual(true, paths.Length > 0, "Compile-item gate must inspect a nonempty evaluated list");
            foreach (var excluded in new[] { originalTemplate, template, retainedSource, Path.Combine(inputRoot, siblingBasename) })
            {
                AssertEqual(false, paths.Contains(excluded, StringComparer.OrdinalIgnoreCase),
                    $"retained/source template excluded from actual Compile items: {project}: {excluded}");
            }
            compileItems.Add(new { project, build, retainedSourceExists = File.Exists(retainedSource), paths });
        }
        AssertEqual(false, Directory.Exists(buildScratch), "item evaluation does not restore or build the copied validator");
        var authorScript = Path.Combine(exampleRoot, "author-source-finding.ps1");
        File.WriteAllText(authorScript, string.Join(Environment.NewLine,
            new[] { "### Validate the existing setup", "### Materialize both complete protocols",
                "### Reconfirm inputs and bind the evidence", "### Author only the source-backed gap" }
            .Select(anchor => EvidenceTests.ExtractCodeBlock(reference, anchor, "powershell"))),
            new UTF8Encoding(false));
        string[] AuthorArguments(string input, string candidateFile, string confirmedFile, string scratch,
            bool confirm = true) =>
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", authorScript,
                "-SkillDir", skill, "-InputRoot", input, "-Candidates", candidateFile, "-Confirmed", confirmedFile,
                "-ComponentId", componentId, "-SourcePath", sourcePath, "-BuildScratch", scratch,
                .. (confirm ? new[] { "-ConfirmNewInputs" } : Array.Empty<string>())];
        var noConfirmation = Process(pwsh, AuthorArguments(inputRoot, candidates, priorConfirmed, buildScratch, false),
            expected: null);
        AssertEqual(false, noConfirmation.ExitCode == 0, "new evidence requires explicit reconfirmation");
        AssertEqual(true, noConfirmation.StandardError.Contains("Explicit reconfirmation", StringComparison.Ordinal),
            "reconfirmation failure is intentional");
        AssertEqual(false, File.Exists(Path.Combine(inputRoot, "callback-source-proof.json")),
            "missing confirmation does not write protocols");
        controls.Add(new { name = "explicit-reconfirmation", noConfirmation.ExitCode, message = noConfirmation.StandardError });

        var incompatibleRoot = Path.Combine(exampleRoot, "incompatible-inputs");
        CopySourceFindingDirectory(inputRoot, incompatibleRoot);
        File.AppendAllText(Path.Combine(incompatibleRoot, "SyntheticCallbackGroup.cs"), "\n// changed", new UTF8Encoding(false));
        var incompatible = Process(pwsh, AuthorArguments(incompatibleRoot,
            Path.Combine(incompatibleRoot, Path.GetFileName(candidates)),
            Path.Combine(incompatibleRoot, Path.GetFileName(priorConfirmed)),
            Path.Combine(exampleRoot, "incompatible-build")), expected: null);
        AssertEqual(false, incompatible.ExitCode == 0, "incompatible prerequisite source fails execution");
        AssertEqual(true, incompatible.StandardError.Contains("must exactly match the delivered template", StringComparison.Ordinal),
            "incompatible setup is not silently repaired");
        AssertEqual(false, Directory.Exists(Path.Combine(incompatibleRoot, "source-finding-output")),
            "incompatible prerequisite rejected before example outputs");
        controls.Add(new { name = "incompatible-prerequisite", incompatible.ExitCode, message = incompatible.StandardError });

        var started = DateTimeOffset.UtcNow.AddSeconds(-1);
        var positive = Process(pwsh, AuthorArguments(inputRoot, candidates, priorConfirmed, buildScratch));
        var finished = DateTimeOffset.UtcNow;
        var outputRoot = Path.Combine(inputRoot, "source-finding-output");
        ConfigureHiddenLookup(outputRoot);
        var outputsBeforeRefusal = SourceFindingFileDigests(inputRoot);
        var authorAgain = Process(pwsh, AuthorArguments(inputRoot, candidates, priorConfirmed,
            Path.Combine(exampleRoot, "unused-repeat-build")), expected: null);
        AssertEqual(false, authorAgain.ExitCode == 0, "hidden existing authoring outputs must not be overwritten");
        AssertEqual(true, authorAgain.StandardError.Contains("Example output already exists:", StringComparison.Ordinal),
            "literal Test-Path still rejects hidden authoring outputs");
        AssertEqual(true, outputsBeforeRefusal.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SequenceEqual(SourceFindingFileDigests(inputRoot).OrderBy(pair => pair.Key, StringComparer.Ordinal)),
            "refused authoring leaves all inputs and outputs unchanged");
        controls.Add(new { name = "hidden-output-no-overwrite", authorAgain.ExitCode, message = authorAgain.StandardError });
        var finalInput = Path.Combine(outputRoot, "inputs-confirmed.json");
        var assessmentPath = Path.Combine(outputRoot, "assessment.json");
        var bundlePath = Path.Combine(outputRoot, "evidence.json");
        var finalBytes = File.ReadAllBytes(finalInput);
        var finalManifest = InputManifestService.Parse(finalBytes);
        var assessment = AssessmentService.Parse(File.ReadAllBytes(assessmentPath));
        var bundle = CanonicalEvidenceJson.ParseBundle(File.ReadAllBytes(bundlePath));
        InputManifestService.Validate(finalManifest, inputRoot, requireConfirmed: true);
        Validate(inputRoot, finalManifest, finalBytes, assessment, bundle);
        AssertEqual("component", assessment.AssessmentKind, "documented flow uses standalone component route");
        AssertEqual(52, assessment.Rows.Count, "documented flow uses current component inventory");
        AssertEqual(true, assessment.PackageReference is null, "documented flow has no package prerequisite");
        AssertEqual(componentId, assessment.Identity.ComponentId, "exact component identity");
        AssertEqual("incomplete", assessment.CompletionState, "example is incomplete");
        AssertEqual(true, File.ReadAllBytes(Path.Combine(outputRoot, "assessment-identity.json"))
            .SequenceEqual(CanonicalEvidenceJson.SerializeAssessment(assessment.Identity)), "identity producer bytes unchanged");
        AssertEqual(prior.EvidenceInputs.Count + 2, finalManifest.EvidenceInputs.Count, "new evidence appends to prior registration");
        AssertEqual(true, prior.EvidenceInputs.All(finalManifest.EvidenceInputs.Contains), "prior supplemental evidence retained");
        AssertEqual(true, prior.SourceArtifacts.SequenceEqual(finalManifest.SourceArtifacts), "all confirmed source registrations retained");
        AssertEqual(prior.Package, finalManifest.Package, "exact package context preserved");
        var beq = assessment.Rows.Single(row => row.Id == "BEQ-12");
        AssertEqual("gap", beq.Status, "documented source-backed gap");
        AssertEqual(2, beq.EvidenceIds.Count, "gap cites both protocols");
        AssertEqual(true, beq.EvidenceIds.ToHashSet(StringComparer.Ordinal)
            .SetEquals(bundle.Selection.Select(item => item.EvidenceId)), "selected and cited evidence agree");
        var initialized = AssessmentService.Parse(File.ReadAllBytes(Path.Combine(outputRoot, "assessment-initial.json")));
        AssertEqual(true, AssessmentService.Serialize(assessment with { Rows = initialized.Rows })
            .SequenceEqual(AssessmentService.Serialize(initialized)), "no non-row assessment fields changed");
        foreach (var row in assessment.Rows.Where(row => row.Id != "BEQ-12"))
        {
            AssertEqual<string?>(null, row.Status, $"unrelated {row.Id} status remains null");
            AssertEqual<string?>(null, row.Observation, $"unrelated {row.Id} observation remains null");
            AssertEqual(0, row.EvidenceIds.Count, $"unrelated {row.Id} has no evidence references");
            AssertEqual<string?>(null, row.OwnerAction, $"unrelated {row.Id} action remains null");
            AssertEqual<string?>(null, row.AssessmentFollowUp, $"unrelated {row.Id} follow-up remains null");
            AssertEqual<string?>(null, row.NotApplicableRationale, $"unrelated {row.Id} rationale remains null");
        }
        var proofBytes = File.ReadAllBytes(Path.Combine(inputRoot, "callback-source-proof.json"));
        var lifecycleBytes = File.ReadAllBytes(Path.Combine(inputRoot, "callback-lifecycle.json"));
        using var proofJson = JsonDocument.Parse(proofBytes);
        AssertEqual(sourcePath, proofJson.RootElement.GetProperty("source_path").GetString(), "logical confirmed source path");
        AssertEqual(ContractJson.RawDigest(File.ReadAllBytes(retainedSource)).Value,
            proofJson.RootElement.GetProperty("source_sha256").GetProperty("value").GetString(),
            "proof hashes exact externally retained source");
        AssertEqual(false, proofJson.RootElement.TryGetProperty("component_id", out _), "no invented protocol component field");
        using var lifecycleJson = JsonDocument.Parse(lifecycleBytes);
        var operations = lifecycleJson.RootElement.GetProperty("operations").EnumerateArray().ToArray();
        AssertEqual(true, operations.Select(item => item.GetProperty("operation").GetString())
            .SequenceEqual(LifecycleOperations), "all ten ordered lifecycle operations");
        foreach (var operation in operations)
        {
            AssertEqual("not-tested", operation.GetProperty("disposition").GetString(), "no fabricated runtime disposition");
            AssertEqual(JsonValueKind.Null, operation.GetProperty("outcome").ValueKind, "no invented runtime outcome");
            AssertEqual(JsonValueKind.Null, operation.GetProperty("raw_observation_sha256").ValueKind, "no invented raw observation");
            AssertEqual(true, operation.GetProperty("not_tested_reason").GetString()!.Length > 30,
                "substantive operation-specific reason");
        }
        AssertEqual(10, operations.Select(item => item.GetProperty("not_tested_reason").GetString()).Distinct().Count(),
            "blockers are operation-specific");
        var records = bundle.SourceLedgers.Single().Ledger.Records;
        AssertEqual(EvidenceIdentity.ReviewerGeneratedAnalysis,
            records.Single(record => record.Provenance.Method == EvidenceProtocolValidator.SourceProofMethod).Provenance.Kind,
            "source proof uses reviewer analysis");
        var lifecycleRecord = records.Single(record => record.Provenance.Method == EvidenceProtocolValidator.LifecycleMethod);
        AssertEqual(EvidenceIdentity.ReproducedRuntimeObservation, lifecycleRecord.Provenance.Kind, "existing lifecycle kind");
        AssertEqual(true, lifecycleRecord.Claim.Contains("No runtime operation was performed", StringComparison.Ordinal),
            "lifecycle kind does not imply execution");
        foreach (var record in records)
        {
            AssertEqual(componentId, record.Applicability.ComponentId, "outer component-specific binding");
            var captured = DateTimeOffset.Parse(record.Provenance.CapturedAtUtc, System.Globalization.CultureInfo.InvariantCulture);
            AssertEqual(true, captured >= started && captured <= finished, "actual authoring timestamp");
        }

        const string operationError = "Dynamic child lifecycle protocol must contain every required operation exactly once in canonical order.";
        const string sourceError = "Source proof protocol must bind the assessed component and one of its allowed confirmed source artifacts.";
        const string canonicalError = "source proof protocol is not in canonical JSON property order and encoding.";
        byte[] Mutate(byte[] bytes, Action<JsonNode> action)
        {
            var value = JsonNode.Parse(bytes)!;
            action(value);
            return Encoding.UTF8.GetBytes(value.ToJsonString());
        }
        Control("unchanged-positive", proofBytes, lifecycleBytes);
        Control("missing-operation", proofBytes, Mutate(lifecycleBytes, value => value["operations"]!.AsArray().RemoveAt(0)),
            operationError);
        Control("duplicate-operation", proofBytes, Mutate(lifecycleBytes, value =>
            value["operations"]!.AsArray().Add(value["operations"]![0]!.DeepClone())), operationError);
        Control("reordered-operations", proofBytes, Mutate(lifecycleBytes, value =>
        {
            var list = value["operations"]!.AsArray();
            var first = list[0]!;
            list.RemoveAt(0);
            list.Insert(1, first);
        }), operationError);
        Control("wrong-source-digest", Mutate(proofBytes, value => value["source_sha256"]!["value"] = new string('0', 64)),
            lifecycleBytes, sourceError);
        Control("sibling-source-association", Mutate(proofBytes, value =>
        {
            value["source_path"] = siblingSourcePath;
            value["source_sha256"]!["value"] = finalManifest.SourceArtifacts.Single(item => item.SourcePath == siblingSourcePath).ContentDigest.Value;
        }), lifecycleBytes, sourceError);
        Control("wrong-component-record", proofBytes, lifecycleBytes,
            "EVID007: component ledger record", recordComponent: siblingId);
        Control("noncanonical-whitespace", [(byte)' ', .. proofBytes], lifecycleBytes, canonicalError);
        Control("noncanonical-newline", [.. proofBytes, (byte)'\n'], lifecycleBytes, canonicalError);
        Control("noncanonical-bom", [0xef, 0xbb, 0xbf, .. proofBytes], lifecycleBytes,
            "source proof protocol must be UTF-8 without a byte-order mark.");
        Control("noncanonical-property-order", Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(proofBytes).Replace(
            "\"schema_version\":1,\"protocol\":\"source-proof\"", "\"protocol\":\"source-proof\",\"schema_version\":1",
            StringComparison.Ordinal)), lifecycleBytes,
            "JSON properties must be exactly [schema_version, protocol, requirement_id, proof_kind, result, source_path, source_sha256] in canonical order.");
        Control("gap-without-selected-proof", proofBytes, lifecycleBytes,
            "Lifecycle row 'BEQ-12' cannot be a gap without an observed failed operation or matching source-proof protocol.",
            selectProof: false);
        Control("verified-without-runtime", proofBytes, lifecycleBytes,
            "Lifecycle row 'BEQ-12' cannot be verified without passed raw observations for every applicable operation.",
            selectProof: false, status: "verified");

        foreach (var pair in prerequisitesBefore)
        {
            AssertEqual(pair.Value, ContractJson.RawDigest(File.ReadAllBytes(Path.Combine(inputRoot, pair.Key))).Value,
                $"existing retained prerequisite unchanged: {pair.Key}");
        }
        AssertEqual(true, payloadBefore.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SequenceEqual(SourceFindingFileDigests(payload).OrderBy(pair => pair.Key, StringComparer.Ordinal)),
            "copied plugin is unchanged by launcher builds and authored outputs");
        var receiptRoot = Path.Combine(acceptanceParent, Guid.NewGuid().ToString("N"));
        AssertEqual(false, Directory.Exists(receiptRoot), "each invocation retains evidence in a fresh receipt directory");
        Directory.CreateDirectory(receiptRoot);
        foreach (var path in new[] { Path.Combine(inputRoot, "callback-source-proof.json"),
            Path.Combine(inputRoot, "callback-lifecycle.json"), assessmentPath, bundlePath, finalInput })
        {
            File.Copy(path, Path.Combine(receiptRoot, Path.GetFileName(path)), overwrite: false);
        }
        foreach (var pair in priorAcceptanceFiles)
        {
            AssertEqual(pair.Value, ContractJson.RawDigest(File.ReadAllBytes(Path.Combine(acceptanceParent, pair.Key))).Value,
                $"earlier invocation receipt remains unchanged: {pair.Key}");
        }
        File.WriteAllText(Path.Combine(receiptRoot, "receipt.json"), JsonSerializer.Serialize(new
        {
            platform = OperatingSystem.IsWindows() ? "Windows" : Environment.OSVersion.Platform.ToString(),
            pwsh, dotnet, sdk, host = hostJson.RootElement,
            templateSha256 = ContractJson.RawDigest(File.ReadAllBytes(template)).Value,
            sourceProofSha256 = ContractJson.RawDigest(proofBytes).Value,
            sourceSha256 = ContractJson.RawDigest(File.ReadAllBytes(retainedSource)).Value,
            positiveExitCode = positive.ExitCode, completion = assessment.CompletionState,
            hiddenLookupMechanism = OperatingSystem.IsWindows() ? "Windows Hidden attribute" : "Unix dot-prefix",
            hiddenLookups, preservedPriorAcceptanceFiles = priorAcceptanceFiles.Count,
            compileItems, controls, commands, assessedSourceExecuted = false
        }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Console.WriteLine($"Source finding: documented PowerShell Core 7.3+ (7.x) flow accepted; 3 Compile-item gates, " +
            $"delivery/exact-byte preservation and {controls.Count} isolated controls passed. Receipt: {receiptRoot}");

        void Control(string name, byte[] proof, byte[] lifecycle, string? expected = null,
            string recordComponent = componentId, bool selectProof = true, string status = "gap")
        {
            var variantRoot = Path.Combine(exampleRoot, name);
            CopySourceFindingDirectory(inputRoot, variantRoot);
            File.WriteAllBytes(Path.Combine(variantRoot, "callback-source-proof.json"), proof);
            File.WriteAllBytes(Path.Combine(variantRoot, "callback-lifecycle.json"), lifecycle);
            var draft = Path.Combine(variantRoot, "control-input-draft.json");
            var confirmed = Path.Combine(variantRoot, "control-input.json");
            var initial = Path.Combine(variantRoot, "control-initial.json");
            var identity = Path.Combine(variantRoot, "control-identity.json");
            var proofDraft = Path.Combine(variantRoot, "control-proof-draft.json");
            var bothDraft = Path.Combine(variantRoot, "control-evidence-draft.json");
            var ledger = Path.Combine(variantRoot, "control-ledger.json");
            var evidence = Path.Combine(variantRoot, "control-evidence.json");
            var variantPackage = Path.Combine(variantRoot, "package.nupkg");
            RunSourceFindingCli(["inputs", "discover", "--root", variantRoot, "--nupkg", variantPackage,
                "--candidates", Path.Combine(variantRoot, "source-finding-output", "candidates-both.json"), "--output", draft]);
            RunSourceFindingCli(["inputs", "confirm", "--root", variantRoot, "--draft", draft, "--output", confirmed]);
            RunSourceFindingCli(["inputs", "validate", "--root", variantRoot, "--manifest", confirmed]);
            RunSourceFindingCli(["assessment", "init", "--kind", "component", "--component", componentId,
                "--root", variantRoot, "--input", confirmed, "--output", initial]);
            RunSourceFindingCli(["assessment", "export-identity", "--assessment", initial, "--output", identity]);
            foreach (var record in records.OrderBy(record => record.Provenance.Method == EvidenceProtocolValidator.SourceProofMethod ? 0 : 1))
            {
                var isProof = record.Provenance.Method == EvidenceProtocolValidator.SourceProofMethod;
                string[] content = isProof
                    ? ["--root", variantRoot, "--manifest", confirmed, "--evidence-input", record.Provenance.Locator]
                    : ["--locator", record.Provenance.Locator, "--content", Path.Combine(variantRoot, record.Provenance.Locator)];
                RunSourceFindingCli(["evidence", "draft-add",
                    .. (isProof ? Array.Empty<string>() : ["--input", proofDraft]),
                    "--output", isProof ? proofDraft : bothDraft, "--claim", record.Claim,
                    "--scope", "component-specific", "--component", isProof ? recordComponent : componentId,
                    "--kind", record.Provenance.Kind, "--method", record.Provenance.Method,
                    "--captured-at", record.Provenance.CapturedAtUtc, .. content]);
            }
            var wrongComponent = recordComponent != componentId;
            var ledgerResult = RunSourceFindingCli(["evidence", "ledger-build", "--kind", "component", "--subject", identity,
                "--draft", bothDraft, "--nupkg", variantPackage, "--output", ledger],
                wrongComponent ? ExitCodes.ValidationFailure : ExitCodes.Success, wrongComponent ? expected : null);
            if (wrongComponent)
            {
                controls.Add(new { name, exitCode = ExitCodes.ValidationFailure, message = ledgerResult });
                return;
            }
            RunSourceFindingCli(["evidence", "ledger-validate", "--ledger", ledger]);
            var selected = CanonicalEvidenceJson.ParseSourceLedger(File.ReadAllBytes(ledger)).Records
                .Where(record => selectProof || record.Provenance.Method != EvidenceProtocolValidator.SourceProofMethod)
                .Select(record => record.StableId).ToArray();
            RunSourceFindingCli(["evidence", "bundle", "--assessment", identity, "--source-ledger", ledger,
                "--ids", string.Join(',', selected), "--root", variantRoot, "--manifest", confirmed, "--output", evidence]);
            var value = AssessmentService.Parse(File.ReadAllBytes(initial));
            var changed = value with
            {
                Rows = value.Rows.Select(row => row.Id == "BEQ-12" ? row with
                {
                    Status = status,
                    Observation = beq.Observation,
                    EvidenceIds = selected,
                    OwnerAction = status == "gap" ? beq.OwnerAction : null
                } : row).ToArray()
            };
            var assessmentDraft = Path.Combine(variantRoot, "control-assessment-draft.json");
            var canonical = Path.Combine(variantRoot, "control-assessment.json");
            File.WriteAllBytes(assessmentDraft, AssessmentService.Serialize(changed));
            RunSourceFindingCli(["assessment", "canonicalize", "--assessment", assessmentDraft, "--output", canonical]);
            var validation = RunSourceFindingCli(["assessment", "validate", "--root", variantRoot, "--input", confirmed,
                "--assessment", canonical, "--evidence", evidence],
                expected is null ? ExitCodes.Success : ExitCodes.ValidationFailure, expected);
            controls.Add(new { name, exitCode = expected is null ? ExitCodes.Success : ExitCodes.ValidationFailure, message = validation });
        }
    }

    private static string RunSourceFindingCli(string[] arguments, int expected = ExitCodes.Success, string? message = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        AssertEqual(expected, CliApplication.Run(arguments, output, error),
            $"source-finding producer {string.Join(' ', arguments)}: {error}");
        if (message is not null)
        {
            AssertEqual(true, error.ToString().Contains(message, StringComparison.Ordinal),
                $"source-finding intended rejection '{message}': {error}");
        }
        return error.ToString();
    }

    private static string ResolveSourceFindingApplication(string filename)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (!Path.IsPathFullyQualified(directory)) continue;
            var candidate = Path.GetFullPath(Path.Combine(directory, filename));
            if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException($"Required {filename} could not be resolved explicitly; no fallback or installation.");
    }

    private static string[] SourceFindingCompileArguments(string project, string build, string offline) =>
    [
        "msbuild", project, "-getItem:Compile", "-noAutoResponse", "-property:Configuration=Release",
        "-property:ImportDirectoryBuildProps=false", "-property:ImportDirectoryBuildTargets=false",
        "-property:ImportDirectoryPackagesProps=false", $"-property:RestoreConfigFile={offline}",
        $"-property:RestorePackagesPath={Path.Combine(build, "packages")}", "-property:NuGetAudit=false",
        $"-property:BaseOutputPath={Path.Combine(build, "bin")}{Path.DirectorySeparatorChar}",
        $"-property:BaseIntermediateOutputPath={Path.Combine(build, "obj")}{Path.DirectorySeparatorChar}"
    ];

    private static void CopySourceFindingDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var item in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            AssertEqual(false, item.Attributes.HasFlag(FileAttributes.ReparsePoint), $"no link in source-finding fixture: {item.FullName}");
            if (item is DirectoryInfo directory)
            {
                if (directory.Name is not ("bin" or "obj"))
                    CopySourceFindingDirectory(directory.FullName, Path.Combine(destination, directory.Name));
            }
            else
            {
                File.Copy(item.FullName, Path.Combine(destination, item.Name), overwrite: false);
            }
        }
    }

    private static Dictionary<string, string> SourceFindingFileDigests(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path),
                path => ContractJson.RawDigest(File.ReadAllBytes(path)).Value, StringComparer.Ordinal);

    private static void TestStructuredVerifiedBoundaries(string root)
    {
        var fixture = CreateFixture(Path.Combine(root, "boundaries"), lifecycleRequired: false);
        var weakCases = new[]
        {
            ("LP-04", EvidenceIdentity.PackageArtifactMetadata, "package:entry/README.md"),
            ("PI-02", EvidenceIdentity.PackageArtifactMetadata, "package:entry/lib/net11.0/Synthetic.Controls.dll"),
            ("SUP-01", EvidenceIdentity.OwnerSuppliedPublicEvidence, "owner-contact.txt")
        };
        File.WriteAllText(
            Path.Combine(fixture.Root, "owner-contact.txt"),
            "Organization and contact metadata only.",
            new UTF8Encoding(false));
        foreach (var (rowId, kind, locator) in weakCases)
        {
            var input = kind == EvidenceIdentity.OwnerSuppliedPublicEvidence
                ? fixture.Manifest with
                {
                    OwnerInputs =
                    [
                        CreateOwnerInput(
                            fixture.Root,
                            "owner-contact.txt",
                            EvidenceIdentity.OwnerSuppliedPublicEvidence)
                    ]
                }
                : fixture.Manifest;
            var inputBytes = InputManifestService.Serialize(input);
            var initialized = AssessmentService.Initialize(
                "package",
                fixture.Root,
                input,
                inputBytes,
                null);
            var digest = kind == EvidenceIdentity.OwnerSuppliedPublicEvidence
                ? input.OwnerInputs.Single().ContentDigest
                : new Sha256Digest(
                    "sha256",
                    NupkgInspector.ComputeEvidenceContentSha256(fixture.NupkgPath, locator));
            var weakEvidence = BuildEvidence(
                initialized.Identity,
                [
                    Draft(
                        initialized.Identity,
                        kind,
                        locator,
                        "Captured only a weak metadata hint.",
                        digest)
                ]);
            var weakAssessment = CompleteRows(
                initialized,
                new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
                {
                    [rowId] = Verified(weakEvidence.Selection.Single().EvidenceId)
                });
            ExpectValidation(
                () => Validate(
                    fixture.Root,
                    input,
                    inputBytes,
                    weakAssessment,
                    weakEvidence),
                $"{rowId} weak hint cannot verify");
        }

        WriteFile(fixture.Root, "dependencies.json", "{\"dependencies\":[\"alpha\"]}");
        WriteFile(fixture.Root, "assets.json", "{\"assets\":[\"bundle.js\"]}");
        WriteFile(fixture.Root, "notices.json", "{\"mapping\":{\"bundle.js\":\"alpha\"}}");
        var dependency = CreateEvidenceInput(fixture.Root, "dependencies.json", "dependency-inventory");
        var assets = CreateEvidenceInput(fixture.Root, "assets.json", "bundled-asset-inventory");
        var notices = CreateEvidenceInput(fixture.Root, "notices.json", "notice-mapping");
        var noticeProtocolBytes = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "notice-coverage");
            writer.WriteString("coverage", "complete");
            WriteDigest(writer, "dependency_inventory_sha256", dependency.ContentDigest);
            WriteDigest(writer, "bundled_asset_inventory_sha256", assets.ContentDigest);
            WriteDigest(writer, "notice_mapping_sha256", notices.ContentDigest);
            writer.WriteEndObject();
        });
        File.WriteAllBytes(Path.Combine(fixture.Root, "notice-protocol.json"), noticeProtocolBytes);
        var noticeProtocol = CreateEvidenceInput(
            fixture.Root,
            "notice-protocol.json",
            "structured-protocol");

        WriteFile(fixture.Root, "authenticode.log", "Signature verification completed for the exact artifact.");
        var authenticodeLog = CreateEvidenceInput(
            fixture.Root,
            "authenticode.log",
            "authenticode-log");
        var dll = NupkgInspector.GetFileEntryDigests(fixture.NupkgPath, ".dll").Single();
        var authenticodeProtocolBytes = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "authenticode-verification");
            writer.WritePropertyName("artifacts");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("package_entry", dll.Key);
            WriteDigest(writer, "artifact_sha256", new Sha256Digest("sha256", dll.Value));
            writer.WriteString("expected_identity", "Synthetic release signing identity");
            writer.WriteString("observed_identity", "Synthetic release signing identity");
            writer.WriteString("file_digest_disposition", "valid");
            writer.WriteString("chain_disposition", "valid");
            writer.WriteString("timestamp_disposition", "valid");
            writer.WriteString("revocation_disposition", "not-applicable");
            WriteDigest(writer, "raw_observation_sha256", authenticodeLog.ContentDigest);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        File.WriteAllBytes(
            Path.Combine(fixture.Root, "authenticode-protocol.json"),
            authenticodeProtocolBytes);
        var authenticodeProtocol = CreateEvidenceInput(
            fixture.Root,
            "authenticode-protocol.json",
            "structured-protocol");

        var supportProtocolBytes = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "support-ownership");
            writer.WriteString("accountable_role", "Release support owner");
            writer.WriteString("accountable_owner", "Component support rotation");
            writer.WriteString("scope", "Synthetic.Controls package line");
            writer.WriteString("backup_owner", "Secondary component support rotation");
            writer.WriteString("escalation_path", "Escalate through the release incident channel");
            writer.WriteString("effective_at_utc", "2026-09-03T00:00:00Z");
            writer.WriteEndObject();
        });
        File.WriteAllBytes(
            Path.Combine(fixture.Root, "support-ownership.json"),
            supportProtocolBytes);
        var supportOwner = CreateOwnerInput(
            fixture.Root,
            "support-ownership.json",
            EvidenceIdentity.OwnerSuppliedPublicEvidence);

        var strongInput = fixture.Manifest with
        {
            EvidenceInputs = new[]
            {
                assets,
                authenticodeLog,
                authenticodeProtocol,
                dependency,
                noticeProtocol,
                notices
            }.OrderBy(item => item.Basename, StringComparer.Ordinal).ToArray(),
            OwnerInputs = [supportOwner]
        };
        var strongInputBytes = InputManifestService.Serialize(strongInput);
        InputManifestService.Validate(strongInput, fixture.Root, requireConfirmed: true);
        var package = AssessmentService.Initialize(
            "package",
            fixture.Root,
            strongInput,
            strongInputBytes,
            null);
        var evidence = BuildEvidence(
            package.Identity,
            [
                Draft(
                    package.Identity,
                    EvidenceIdentity.ReviewerGeneratedAnalysis,
                    noticeProtocol.Basename,
                    EvidenceProtocolValidator.NoticeCoverageMethod,
                    noticeProtocol.ContentDigest),
                Draft(
                    package.Identity,
                    EvidenceIdentity.ReproducedRuntimeObservation,
                    authenticodeProtocol.Basename,
                    EvidenceProtocolValidator.AuthenticodeMethod,
                    authenticodeProtocol.ContentDigest),
                Draft(
                    package.Identity,
                    EvidenceIdentity.OwnerSuppliedPublicEvidence,
                    supportOwner.Basename,
                    EvidenceProtocolValidator.SupportOwnershipMethod,
                    supportOwner.ContentDigest)
            ]);
        var ids = evidence.SourceLedgers.Single().Ledger.Records.ToDictionary(
            record => record.Provenance.Method,
            record => record.StableId,
            StringComparer.Ordinal);
        var assessment = CompleteRows(
            package,
            new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
            {
                ["LP-04"] = Verified(ids[EvidenceProtocolValidator.NoticeCoverageMethod]),
                ["PI-02"] = Verified(ids[EvidenceProtocolValidator.AuthenticodeMethod]),
                ["SUP-01"] = Verified(ids[EvidenceProtocolValidator.SupportOwnershipMethod])
            });
        Validate(fixture.Root, strongInput, strongInputBytes, assessment, evidence);
        var incompleteNoticeBytes = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "notice-coverage");
            writer.WriteString("coverage", "incomplete");
            WriteDigest(writer, "dependency_inventory_sha256", dependency.ContentDigest);
            WriteDigest(writer, "bundled_asset_inventory_sha256", assets.ContentDigest);
            WriteDigest(writer, "notice_mapping_sha256", notices.ContentDigest);
            writer.WriteEndObject();
        });
        File.WriteAllBytes(
            Path.Combine(fixture.Root, "notice-incomplete.json"),
            incompleteNoticeBytes);
        var incompleteNotice = CreateEvidenceInput(
            fixture.Root,
            "notice-incomplete.json",
            "structured-protocol");
        var gapInput = strongInput with
        {
            EvidenceInputs = strongInput.EvidenceInputs
                .Append(incompleteNotice)
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        var gapInputBytes = InputManifestService.Serialize(gapInput);
        var gapPackage = AssessmentService.Initialize(
            "package",
            fixture.Root,
            gapInput,
            gapInputBytes,
            null);
        var gapEvidence = BuildEvidence(
            gapPackage.Identity,
            [
                Draft(
                    gapPackage.Identity,
                    EvidenceIdentity.ReviewerGeneratedAnalysis,
                    incompleteNotice.Basename,
                    EvidenceProtocolValidator.NoticeCoverageMethod,
                    incompleteNotice.ContentDigest)
            ]);
        var gapId = gapEvidence.Selection.Single().EvidenceId;
        var gapAssessment = CompleteRows(
            gapPackage,
            new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
            {
                ["LP-04"] = new RowConclusion(
                    "gap",
                    "The exact inventory shows that required notice coverage is incomplete.",
                    [gapId],
                    null,
                    null,
                    null)
            });
        Validate(fixture.Root, gapInput, gapInputBytes, gapAssessment, gapEvidence);

        var failedAuthenticodeBytes = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "authenticode-verification");
            writer.WritePropertyName("artifacts");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("package_entry", dll.Key);
            WriteDigest(writer, "artifact_sha256", new Sha256Digest("sha256", dll.Value));
            writer.WriteString("expected_identity", "Synthetic release signing identity");
            writer.WriteString("observed_identity", "Unexpected signing identity");
            writer.WriteString("file_digest_disposition", "invalid");
            writer.WriteString("chain_disposition", "not-tested");
            writer.WriteString("timestamp_disposition", "not-tested");
            writer.WriteString("revocation_disposition", "not-tested");
            WriteDigest(writer, "raw_observation_sha256", authenticodeLog.ContentDigest);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        File.WriteAllBytes(
            Path.Combine(fixture.Root, "authenticode-failed.json"),
            failedAuthenticodeBytes);
        var failedAuthenticode = CreateEvidenceInput(
            fixture.Root,
            "authenticode-failed.json",
            "structured-protocol");
        var authGapInput = strongInput with
        {
            EvidenceInputs = strongInput.EvidenceInputs
                .Append(failedAuthenticode)
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        var authGapInputBytes = InputManifestService.Serialize(authGapInput);
        var authGapPackage = AssessmentService.Initialize(
            "package",
            fixture.Root,
            authGapInput,
            authGapInputBytes,
            null);
        var authGapEvidence = BuildEvidence(
            authGapPackage.Identity,
            [
                Draft(
                    authGapPackage.Identity,
                    EvidenceIdentity.ReproducedRuntimeObservation,
                    failedAuthenticode.Basename,
                    EvidenceProtocolValidator.AuthenticodeMethod,
                    failedAuthenticode.ContentDigest)
            ]);
        var authGapId = authGapEvidence.Selection.Single().EvidenceId;
        var authGapAssessment = CompleteRows(
            authGapPackage,
            new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
            {
                ["PI-02"] = new RowConclusion(
                    "gap",
                    "Direct verification found an unexpected identity and invalid file digest.",
                    [authGapId],
                    null,
                    null,
                    null)
            });
        Validate(
            fixture.Root,
            authGapInput,
            authGapInputBytes,
            authGapAssessment,
            authGapEvidence);

        File.WriteAllBytes(
            Path.Combine(fixture.Root, "public-support-corpus.json"),
            PublicCorpus(
                "public-support-corpus",
                ["support-response-sla"],
                []));
        var publicCorpus = CreateEvidenceInput(
            fixture.Root,
            "public-support-corpus.json",
            "public-support-corpus");
        File.WriteAllBytes(
            Path.Combine(fixture.Root, "public-support-absence.json"),
            PublicAbsence("SUP-03", "public-support-corpus", publicCorpus.ContentDigest));
        var publicAbsence = CreateEvidenceInput(
            fixture.Root,
            "public-support-absence.json",
            "structured-protocol");
        var publicInput = fixture.Manifest with
        {
            EvidenceInputs = new[] { publicCorpus, publicAbsence }
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        var publicInputBytes = InputManifestService.Serialize(publicInput);
        var publicPackage = CurrentAssessmentService.Initialize(
            "package",
            fixture.Root,
            publicInput,
            publicInputBytes,
            null);
        var publicEvidence = BuildEvidence(
            publicPackage.Identity,
            [
                Draft(
                    publicPackage.Identity,
                    EvidenceIdentity.ReviewerGeneratedAnalysis,
                    publicAbsence.Basename,
                    EvidenceProtocolValidator.PublicAbsenceMethod,
                    publicAbsence.ContentDigest)
            ]);
        var publicEvidenceId = publicEvidence.Selection.Single().EvidenceId;
        var publicGap = CompleteRows(
            publicPackage,
            new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
            {
                ["SUP-03"] = new RowConclusion(
                    "gap",
                    "The complete public support corpus directly contains no published general response SLA.",
                    [publicEvidenceId],
                    "Publish the general support response SLA.",
                    null,
                    null)
            });
        Validate(
            fixture.Root,
            publicInput,
            publicInputBytes,
            publicGap,
            publicEvidence);

        var publicRevisionRoot = Path.Combine(fixture.Root, "public-support-revisions");
        var publicManifest1 = WriteRevision(
            fixture.Root,
            publicRevisionRoot,
            1,
            publicInput,
            publicGap,
            publicEvidence,
            predecessor: null);
        _ = WriteRevision(
            fixture.Root,
            publicRevisionRoot,
            2,
            publicInput,
            publicGap,
            publicEvidence,
            ContractJson.RawDigest(publicManifest1));
        _ = RevisionService.VerifyRevision(
            fixture.Root,
            Path.Combine(publicRevisionRoot, "0002"),
            feedbackBytes: null,
            packageBinding: null,
            validateChain: true);

        var currentWithoutProtocolEvidence = BuildEvidence(
            publicPackage.Identity,
            [
                Draft(
                    publicPackage.Identity,
                    EvidenceIdentity.ReviewerGeneratedAnalysis,
                    publicCorpus.Basename,
                    "bounded complete public corpus inspection",
                    publicCorpus.ContentDigest)
            ]);
        var currentWithoutProtocolId =
            currentWithoutProtocolEvidence.Selection.Single().EvidenceId;
        var currentWithoutProtocolGap = CompleteRows(
            publicPackage,
            new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
            {
                ["SUP-03"] = new RowConclusion(
                    "gap",
                    "The complete public support corpus directly contains no published general response SLA.",
                    [currentWithoutProtocolId],
                    "Publish the general support response SLA.",
                    null,
                    null)
            });
        ExpectValidation(
            () => Validate(
                fixture.Root,
                publicInput,
                publicInputBytes,
                currentWithoutProtocolGap,
                currentWithoutProtocolEvidence),
            "schema-v2 public gap requires typed protocol");

        var publicNotTested = CompleteRows(
            publicPackage,
            new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
            {
                ["SUP-03"] = new RowConclusion(
                    "not tested",
                    "The public corpus records the required commitment as absent.",
                    [publicEvidenceId],
                    null,
                    "Repeat the already complete public corpus search.",
                    null)
            });
        ExpectValidation(
            () => Validate(
                fixture.Root,
                publicInput,
                publicInputBytes,
                publicNotTested,
                publicEvidence),
            "direct public absence cannot be classified as not tested");

        var wrongPublicInput = publicInput with
        {
            EvidenceInputs = publicInput.EvidenceInputs.Select(item =>
                item.Basename == publicCorpus.Basename
                    ? item with { Kind = "source-analysis" }
                    : item).ToArray()
        };
        var wrongPublicBytes = InputManifestService.Serialize(wrongPublicInput);
        var wrongPublicPackage = AssessmentService.Initialize(
            "package",
            fixture.Root,
            wrongPublicInput,
            wrongPublicBytes,
            null);
        var wrongPublicEvidence = BuildEvidence(
            wrongPublicPackage.Identity,
            [
                Draft(
                    wrongPublicPackage.Identity,
                    EvidenceIdentity.ReviewerGeneratedAnalysis,
                    publicAbsence.Basename,
                    EvidenceProtocolValidator.PublicAbsenceMethod,
                    publicAbsence.ContentDigest)
            ]);
        var wrongPublicId = wrongPublicEvidence.Selection.Single().EvidenceId;
        var wrongPublicGap = CompleteRows(
            wrongPublicPackage,
            new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
            {
                ["SUP-03"] = new RowConclusion(
                    "gap",
                    "An untyped file is incorrectly used as a complete public support corpus.",
                    [wrongPublicId],
                    "Bind the exact typed public support corpus.",
                    null,
                    null)
            });
        ExpectValidation(
            () => Validate(
                fixture.Root,
                wrongPublicInput,
                wrongPublicBytes,
                wrongPublicGap,
                wrongPublicEvidence),
            "public absence requires the accepted typed corpus");

        File.WriteAllBytes(
            Path.Combine(fixture.Root, "duplicate-public-support-corpus.json"),
            File.ReadAllBytes(Path.Combine(fixture.Root, publicCorpus.Basename)));
        var duplicateCorpus = CreateEvidenceInput(
            fixture.Root,
            "duplicate-public-support-corpus.json",
            "public-support-corpus");
        var duplicatePublicInput = fixture.Manifest with
        {
            EvidenceInputs = new[] { publicCorpus, duplicateCorpus, publicAbsence }
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        ExpectPublicAbsenceValidationFailure(
            fixture,
            duplicatePublicInput,
            publicAbsence,
            "duplicate typed public corpus identities fail deterministically");

        File.WriteAllBytes(
            Path.Combine(fixture.Root, "incomplete-public-corpus.json"),
            PublicCorpus(
                "public-support-corpus",
                ["support-response-sla"],
                [],
                complete: false));
        var incompleteCorpus = CreateEvidenceInput(
            fixture.Root,
            "incomplete-public-corpus.json",
            "public-support-corpus");
        File.WriteAllBytes(
            Path.Combine(fixture.Root, "incomplete-public-absence.json"),
            PublicAbsence(
                "SUP-03",
                "public-support-corpus",
                incompleteCorpus.ContentDigest));
        var incompleteAbsence = CreateEvidenceInput(
            fixture.Root,
            "incomplete-public-absence.json",
            "structured-protocol");
        var incompletePublicInput = fixture.Manifest with
        {
            EvidenceInputs = new[] { incompleteCorpus, incompleteAbsence }
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        ExpectPublicAbsenceValidationFailure(
            fixture,
            incompletePublicInput,
            incompleteAbsence,
            "incomplete public corpus fails closed");

        File.WriteAllBytes(
            Path.Combine(fixture.Root, "present-public-corpus.json"),
            PublicCorpus(
                "public-support-corpus",
                ["support-response-sla"],
                ["support-response-sla"]));
        var presentCorpus = CreateEvidenceInput(
            fixture.Root,
            "present-public-corpus.json",
            "public-support-corpus");
        File.WriteAllBytes(
            Path.Combine(fixture.Root, "present-public-absence.json"),
            PublicAbsence(
                "SUP-03",
                "public-support-corpus",
                presentCorpus.ContentDigest));
        var presentAbsence = CreateEvidenceInput(
            fixture.Root,
            "present-public-absence.json",
            "structured-protocol");
        var presentPublicInput = fixture.Manifest with
        {
            EvidenceInputs = new[] { presentCorpus, presentAbsence }
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        ExpectPublicAbsenceValidationFailure(
            fixture,
            presentPublicInput,
            presentAbsence,
            "present required marker cannot prove absence");

        File.WriteAllBytes(
            Path.Combine(fixture.Root, "wrong-digest-public-absence.json"),
            PublicAbsence(
                "SUP-03",
                "public-support-corpus",
                new Sha256Digest(
                    "sha256",
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")));
        var wrongDigestAbsence = CreateEvidenceInput(
            fixture.Root,
            "wrong-digest-public-absence.json",
            "structured-protocol");
        var wrongDigestInput = fixture.Manifest with
        {
            EvidenceInputs = new[] { publicCorpus, wrongDigestAbsence }
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        ExpectPublicAbsenceValidationFailure(
            fixture,
            wrongDigestInput,
            wrongDigestAbsence,
            "public absence digest mismatch fails closed");

        File.WriteAllBytes(
            Path.Combine(fixture.Root, "wrong-requirement-public-absence.json"),
            PublicAbsence(
                "SEC-01",
                "public-support-corpus",
                publicCorpus.ContentDigest));
        var wrongRequirementAbsence = CreateEvidenceInput(
            fixture.Root,
            "wrong-requirement-public-absence.json",
            "structured-protocol");
        var wrongRequirementInput = fixture.Manifest with
        {
            EvidenceInputs = new[] { publicCorpus, wrongRequirementAbsence }
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        ExpectPublicAbsenceValidationFailure(
            fixture,
            wrongRequirementInput,
            wrongRequirementAbsence,
            "unsupported public absence requirement fails closed");

        RejectRetiredPublicAbsenceCase(
            fixture,
            "BEQ-05",
            "public-document-corpus",
            "static-ssr-contract");
        RejectRetiredPublicAbsenceCase(
            fixture,
            "CI-09",
            "sample-inventory",
            "behavioral-assertions");
    }

    private static void RejectRetiredPublicAbsenceCase(
        Fixture fixture,
        string requirementId,
        string corpusKind,
        string requiredMarker)
    {
        var suffix = requirementId.ToLowerInvariant();
        var corpusBasename = $"{suffix}-corpus.json";
        File.WriteAllBytes(
            Path.Combine(fixture.Root, corpusBasename),
            PublicCorpus(corpusKind, [requiredMarker], []));
        var corpus = CreateEvidenceInput(
            fixture.Root,
            corpusBasename,
            corpusKind);
        var protocolBasename = $"{suffix}-absence.json";
        File.WriteAllBytes(
            Path.Combine(fixture.Root, protocolBasename),
            PublicAbsence(requirementId, corpusKind, corpus.ContentDigest));
        var protocol = CreateEvidenceInput(
            fixture.Root,
            protocolBasename,
            "structured-protocol");
        var input = fixture.Manifest with
        {
            EvidenceInputs = new[] { corpus, protocol }
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        var inputBytes = InputManifestService.Serialize(input);
        var assessment = AssessmentService.Initialize(
            "component",
            fixture.Root,
            input,
            inputBytes,
            "static-control");
        var evidence = BuildEvidence(
            assessment.Identity,
            [
                Draft(
                    assessment.Identity,
                    EvidenceIdentity.ReviewerGeneratedAnalysis,
                    protocol.Basename,
                    EvidenceProtocolValidator.PublicAbsenceMethod,
                    protocol.ContentDigest)
            ]);
        var evidenceId = evidence.Selection.Single().EvidenceId;
        var completed = CompleteRows(
            assessment,
            new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
            {
                [requirementId == "CI-09" ? "BEQ-01" : requirementId] = new RowConclusion(
                    "gap",
                    "The complete typed public corpus directly records the required marker as absent.",
                    [evidenceId],
                    "Publish the missing required public record.",
                    null,
                    null)
            });
        ExpectValidationMessage(
            () => Validate(fixture.Root, input, inputBytes, completed, evidence),
            "unsupported",
            $"{requirementId} no longer supports documentation-absence scoring");
    }

    private static void ExpectPublicAbsenceValidationFailure(
        Fixture fixture,
        InputManifest input,
        InputEvidenceArtifact protocol,
        string name)
    {
        var inputBytes = InputManifestService.Serialize(input);
        var assessment = AssessmentService.Initialize(
            "package",
            fixture.Root,
            input,
            inputBytes,
            null);
        var evidence = BuildEvidence(
            assessment.Identity,
            [
                Draft(
                    assessment.Identity,
                    EvidenceIdentity.ReviewerGeneratedAnalysis,
                    protocol.Basename,
                    EvidenceProtocolValidator.PublicAbsenceMethod,
                    protocol.ContentDigest)
            ]);
        var evidenceId = evidence.Selection.Single().EvidenceId;
        var completed = CompleteRows(
            assessment,
            new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
            {
                ["SUP-03"] = new RowConclusion(
                    "gap",
                    "The claimed public corpus does not validly establish this required record as absent.",
                    [evidenceId],
                    "Provide a valid complete public corpus.",
                    null,
                    null)
            });
        ExpectValidation(
            () => Validate(
                fixture.Root,
                input,
                inputBytes,
                completed,
                evidence),
            name);
    }

    private static void TestAutoTransitionProtocol(string root)
    {
        var fixture = CreateFixture(Path.Combine(root, "auto-transition"), lifecycleRequired: false);
        var coldRawBasename = "auto-cold.raw.json";
        var warmRawBasename = "auto-warm.raw.json";
        File.WriteAllBytes(
            Path.Combine(fixture.Root, coldRawBasename),
            RawAutoObservation("static-control", "cold", "server"));
        File.WriteAllBytes(
            Path.Combine(fixture.Root, warmRawBasename),
            RawAutoObservation("static-control", "warm", "webassembly"));
        var coldRaw = CreateEvidenceInput(fixture.Root, coldRawBasename, "raw-observation");
        var warmRaw = CreateEvidenceInput(fixture.Root, warmRawBasename, "raw-observation");
        var validBasename = "auto-transition-valid.json";
        File.WriteAllBytes(
            Path.Combine(fixture.Root, validBasename),
            AutoTransitionProtocol(
                "static-control",
                coldRaw.ContentDigest,
                warmRaw.ContentDigest,
                "server",
                "webassembly"));
        var validProtocol = CreateEvidenceInput(fixture.Root, validBasename, "structured-protocol");
        var validInput = fixture.Manifest with
        {
            EvidenceInputs = new[] { coldRaw, warmRaw, validProtocol }
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        var validInputBytes = InputManifestService.Serialize(validInput);
        var initialized = CurrentAssessmentService.Initialize(
            "component",
            fixture.Root,
            validInput,
            validInputBytes,
            "static-control");
        var validEvidence = BuildEvidence(
            initialized.Identity,
            [
                Draft(
                    initialized.Identity,
                    EvidenceIdentity.ReproducedRuntimeObservation,
                    validProtocol.Basename,
                    EvidenceProtocolValidator.AutoTransitionMethod,
                    validProtocol.ContentDigest,
                    componentSpecific: true)
            ]);
        var validId = validEvidence.Selection.Single().EvidenceId;
        var validAssessment = CompleteRows(
            initialized,
            new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
            {
                ["BEQ-08"] = Verified(validId)
            });
        Validate(fixture.Root, validInput, validInputBytes, validAssessment, validEvidence);

        var invalidRawBasename = "auto-identical.raw.json";
        File.WriteAllBytes(
            Path.Combine(fixture.Root, invalidRawBasename),
            RawAutoObservation("static-control", "warm", "server"));
        var invalidRaw = CreateEvidenceInput(fixture.Root, invalidRawBasename, "raw-observation");
        var invalidBasename = "auto-transition-identical.json";
        File.WriteAllBytes(
            Path.Combine(fixture.Root, invalidBasename),
            AutoTransitionProtocol(
                "static-control",
                coldRaw.ContentDigest,
                invalidRaw.ContentDigest,
                "server",
                "server"));
        var invalidProtocol = CreateEvidenceInput(fixture.Root, invalidBasename, "structured-protocol");
        var invalidInput = fixture.Manifest with
        {
            EvidenceInputs = new[] { coldRaw, invalidRaw, invalidProtocol }
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        var invalidInputBytes = InputManifestService.Serialize(invalidInput);
        var invalidInitialized = CurrentAssessmentService.Initialize(
            "component",
            fixture.Root,
            invalidInput,
            invalidInputBytes,
            "static-control");
        var invalidEvidence = BuildEvidence(
            invalidInitialized.Identity,
            [
                Draft(
                    invalidInitialized.Identity,
                    EvidenceIdentity.ReproducedRuntimeObservation,
                    invalidProtocol.Basename,
                    EvidenceProtocolValidator.AutoTransitionMethod,
                    invalidProtocol.ContentDigest,
                    componentSpecific: true)
            ]);
        var invalidId = invalidEvidence.Selection.Single().EvidenceId;
        var invalidAssessment = CompleteRows(
            invalidInitialized,
            new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
            {
                ["BEQ-08"] = Verified(invalidId)
            });
        ExpectValidation(
            () => Validate(
                fixture.Root,
                invalidInput,
                invalidInputBytes,
                invalidAssessment,
                invalidEvidence),
            "BEQ-08 cannot infer distinct renderer identities");

        void ExpectAutoProtocolFailure(
            string name,
            string protocolBasename,
            byte[] protocolBytes,
            IReadOnlyList<InputEvidenceArtifact> rawEvidence)
        {
            File.WriteAllBytes(Path.Combine(fixture.Root, protocolBasename), protocolBytes);
            var protocol = CreateEvidenceInput(fixture.Root, protocolBasename, "structured-protocol");
            var input = fixture.Manifest with
            {
                EvidenceInputs = rawEvidence
                    .Append(protocol)
                    .OrderBy(item => item.Basename, StringComparer.Ordinal)
                    .ToArray()
            };
            var inputBytes = InputManifestService.Serialize(input);
            var assessment = CurrentAssessmentService.Initialize(
                "component",
                fixture.Root,
                input,
                inputBytes,
                "static-control");
            var evidence = BuildEvidence(
                assessment.Identity,
                [
                    Draft(
                        assessment.Identity,
                        EvidenceIdentity.ReproducedRuntimeObservation,
                        protocol.Basename,
                        EvidenceProtocolValidator.AutoTransitionMethod,
                        protocol.ContentDigest,
                        componentSpecific: true)
                ]);
            var completed = CompleteRows(
                assessment,
                new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
                {
                    ["BEQ-08"] = Verified(evidence.Selection.Single().EvidenceId)
                });
            ExpectValidation(
                () => Validate(fixture.Root, input, inputBytes, completed, evidence),
                name);
        }

        ExpectAutoProtocolFailure(
            "Auto transition requires a retained raw observation",
            "auto-transition-missing-raw.json",
            AutoTransitionProtocol(
                "static-control",
                coldRaw.ContentDigest,
                new Sha256Digest("sha256", new string('f', 64)),
                "server",
                "webassembly"),
            [coldRaw, warmRaw]);

        var contradictoryRawBasename = "auto-contradictory.raw.json";
        File.WriteAllBytes(
            Path.Combine(fixture.Root, contradictoryRawBasename),
            RawAutoObservation("static-control", "warm", "server"));
        var contradictoryRaw = CreateEvidenceInput(
            fixture.Root,
            contradictoryRawBasename,
            "raw-observation");
        ExpectAutoProtocolFailure(
            "Auto transition rejects protocol versus raw identity contradiction",
            "auto-transition-contradictory.json",
            AutoTransitionProtocol(
                "static-control",
                coldRaw.ContentDigest,
                contradictoryRaw.ContentDigest,
                "server",
                "webassembly"),
            [coldRaw, contradictoryRaw]);

        var wrongComponentRawBasename = "auto-wrong-component.raw.json";
        File.WriteAllBytes(
            Path.Combine(fixture.Root, wrongComponentRawBasename),
            RawAutoObservation("other-control", "warm", "webassembly"));
        var wrongComponentRaw = CreateEvidenceInput(
            fixture.Root,
            wrongComponentRawBasename,
            "raw-observation");
        ExpectAutoProtocolFailure(
            "Auto transition rejects wrong component raw observation",
            "auto-transition-wrong-component.json",
            AutoTransitionProtocol(
                "static-control",
                coldRaw.ContentDigest,
                wrongComponentRaw.ContentDigest,
                "server",
                "webassembly"),
            [coldRaw, wrongComponentRaw]);

        var wrongVisitRawBasename = "auto-wrong-visit.raw.json";
        File.WriteAllBytes(
            Path.Combine(fixture.Root, wrongVisitRawBasename),
            RawAutoObservation("static-control", "cold", "webassembly"));
        var wrongVisitRaw = CreateEvidenceInput(
            fixture.Root,
            wrongVisitRawBasename,
            "raw-observation");
        ExpectAutoProtocolFailure(
            "Auto transition rejects wrong visit raw observation",
            "auto-transition-wrong-visit.json",
            AutoTransitionProtocol(
                "static-control",
                coldRaw.ContentDigest,
                wrongVisitRaw.ContentDigest,
                "server",
                "webassembly"),
            [coldRaw, wrongVisitRaw]);

        var downgradedAssessment = initialized with
        {
            SchemaVersion = 1
        };
        ExpectValidation(
            () => Validate(
                fixture.Root,
                validInput,
                validInputBytes,
                CompleteRows(
                    downgradedAssessment,
                    new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
                    {
                        ["BEQ-08"] = Verified(validEvidence.Selection.Single().EvidenceId)
                    }),
                validEvidence),
            "current rubric rejects a schema-downgraded Auto assessment");

    }

    private static byte[] AutoTransitionProtocol(
        string componentId,
        Sha256Digest coldRawDigest,
        Sha256Digest warmRawDigest,
        string coldObserved,
        string warmObserved) =>
        StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "auto-renderer-transition");
            writer.WriteString("component_id", componentId);
            writer.WritePropertyName("cold");
            writer.WriteStartObject();
            writer.WriteString("visit", "cold");
            writer.WriteString("expected_identity", "server");
            writer.WriteString("observed_identity", coldObserved);
            WriteDigest(writer, "raw_observation_sha256", coldRawDigest);
            writer.WriteEndObject();
            writer.WritePropertyName("warm");
            writer.WriteStartObject();
            writer.WriteString("visit", "warm");
            writer.WriteString("expected_identity", "webassembly");
            writer.WriteString("observed_identity", warmObserved);
            WriteDigest(writer, "raw_observation_sha256", warmRawDigest);
            writer.WriteEndObject();
            writer.WriteEndObject();
        });

    private static byte[] RawAutoObservation(
        string componentId,
        string visit,
        string observedIdentity) =>
        StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("observation", "auto-renderer-visit");
            writer.WriteString("component_id", componentId);
            writer.WriteString("visit", visit);
            writer.WriteString("mode", "interactive-auto");
            writer.WriteString("observed_identity", observedIdentity);
            writer.WriteEndObject();
        });

    private static void TestDynamicLifecycleMatrix(string root)
    {
        var fixture = CreateFixture(Path.Combine(root, "lifecycle"), lifecycleRequired: true);
        var rawInputs = new List<InputEvidenceArtifact>();
        var rawDigests = new Dictionary<string, Sha256Digest>(StringComparer.Ordinal);
        foreach (var operation in LifecycleOperations)
        {
            var basename = $"raw-{operation}.json";
            WriteFile(
                fixture.Root,
                basename,
                $"{{\"operation\":\"{operation}\",\"phase\":\"after-initial-render\",\"outcome\":\"passed\"}}");
            var rawInput = CreateEvidenceInput(fixture.Root, basename, "raw-observation");
            rawInputs.Add(rawInput);
            rawDigests.Add(operation, rawInput.ContentDigest);
        }

        var protocolBytes = LifecycleProtocol(rawDigests);
        File.WriteAllBytes(Path.Combine(fixture.Root, "lifecycle-protocol.json"), protocolBytes);
        var protocolInput = CreateEvidenceInput(
            fixture.Root,
            "lifecycle-protocol.json",
            "structured-protocol");
        var input = fixture.Manifest with
        {
            EvidenceInputs = rawInputs
                .Append(protocolInput)
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        var inputBytes = InputManifestService.Serialize(input);
        var initialized = AssessmentService.Initialize(
            "component",
            fixture.Root,
            input,
            inputBytes,
            "dynamic-group");
        var evidence = BuildEvidence(
            initialized.Identity,
            [
                Draft(
                    initialized.Identity,
                    EvidenceIdentity.ReproducedRuntimeObservation,
                    protocolInput.Basename,
                    EvidenceProtocolValidator.LifecycleMethod,
                    protocolInput.ContentDigest)
            ]);
        var evidenceId = evidence.Selection.Single().EvidenceId;
        var conclusions = new Dictionary<string, RowConclusion>(StringComparer.Ordinal);
        foreach (var rowId in new[] { "A11Y-06", "A11Y-07", "A11Y-08", "BEQ-12", "BEQ-15" })
        {
            conclusions[rowId] = Verified(evidenceId);
        }

        var assessment = CompleteRows(initialized, conclusions);
        Validate(fixture.Root, input, inputBytes, assessment, evidence);

        var listedOnly = input with
        {
            EvidenceInputs = input.EvidenceInputs
                .Where(item => item.Basename != protocolInput.Basename)
                .ToArray()
        };
        WriteFile(
            fixture.Root,
            "listed-only.json",
            "{\"schema_version\":1,\"protocol\":\"dynamic-child-lifecycle\",\"operations\":[]}");
        var listedInput = CreateEvidenceInput(
            fixture.Root,
            "listed-only.json",
            "structured-protocol");
        listedOnly = listedOnly with
        {
            EvidenceInputs = listedOnly.EvidenceInputs
                .Append(listedInput)
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        var listedBytes = InputManifestService.Serialize(listedOnly);
        var listedInitialized = AssessmentService.Initialize(
            "component",
            fixture.Root,
            listedOnly,
            listedBytes,
            "dynamic-group");
        var listedEvidence = BuildEvidence(
            listedInitialized.Identity,
            [
                Draft(
                    listedInitialized.Identity,
                    EvidenceIdentity.ReproducedRuntimeObservation,
                    listedInput.Basename,
                    EvidenceProtocolValidator.LifecycleMethod,
                    listedInput.ContentDigest)
            ]);
        var listedAssessment = CompleteRows(
            listedInitialized,
            conclusions.ToDictionary(
                item => item.Key,
                _ => Verified(listedEvidence.Selection.Single().EvidenceId),
                StringComparer.Ordinal));
        ExpectValidation(
            () => Validate(
                fixture.Root,
                listedOnly,
                listedBytes,
                listedAssessment,
                listedEvidence),
            "listing lifecycle matrix without post-initialization observations");

        var notTestedDigests = rawDigests.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.Ordinal);
        var blockedProtocolBytes = LifecycleProtocol(
            notTestedDigests,
            blockedOperation: "keyed-reorder");
        File.WriteAllBytes(
            Path.Combine(fixture.Root, "lifecycle-blocked.json"),
            blockedProtocolBytes);
        var blockedProtocol = CreateEvidenceInput(
            fixture.Root,
            "lifecycle-blocked.json",
            "structured-protocol");
        var blockedInput = input with
        {
            EvidenceInputs = input.EvidenceInputs
                .Where(item => item.Basename != protocolInput.Basename)
                .Append(blockedProtocol)
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        var blockedBytes = InputManifestService.Serialize(blockedInput);
        var blockedInitialized = AssessmentService.Initialize(
            "component",
            fixture.Root,
            blockedInput,
            blockedBytes,
            "dynamic-group");
        var blockedEvidence = BuildEvidence(
            blockedInitialized.Identity,
            [
                Draft(
                    blockedInitialized.Identity,
                    EvidenceIdentity.ReproducedRuntimeObservation,
                    blockedProtocol.Basename,
                    EvidenceProtocolValidator.LifecycleMethod,
                    blockedProtocol.ContentDigest)
            ]);
        var blockedId = blockedEvidence.Selection.Single().EvidenceId;
        var blockedConclusions = conclusions.ToDictionary(
            item => item.Key,
            _ => Verified(blockedId),
            StringComparer.Ordinal);
        blockedConclusions["A11Y-06"] = new RowConclusion(
            "not tested",
            "The keyed reorder operation was blocked because the synthetic host omitted stable keys.",
            [blockedId],
            null,
            "Rerun keyed reorder with stable child keys and retain the raw observation.",
            null);
        Validate(
            fixture.Root,
            blockedInput,
            blockedBytes,
            CompleteRows(blockedInitialized, blockedConclusions),
            blockedEvidence);

        const string sourcePath = "src/SyntheticDynamicGroup.cs";
        const string contentPath = "synthetic-dynamic-group.cs";
        WriteFile(
            fixture.Root,
            contentPath,
            "sealed class SyntheticDynamicGroup : IDisposable { void Dispose() { _ = SendCleanupAsync(); } }");
        var sourceDigest = ContractJson.RawDigest(File.ReadAllBytes(
            Path.Combine(fixture.Root, contentPath)));
        var sourceArtifact = new InputSourceArtifact(sourcePath, contentPath, sourceDigest);
        File.WriteAllBytes(
            Path.Combine(fixture.Root, "cleanup-source-proof.json"),
            SourceProof("BEQ-15", "async-cleanup-not-awaited", sourcePath, sourceDigest));
        var sourceProofInput = CreateEvidenceInput(
            fixture.Root,
            "cleanup-source-proof.json",
            "structured-protocol");
        var sourceComponents = blockedInput.Components.Select(component =>
            component.Id == "dynamic-group"
                ? component with { AllowedSourcePaths = [sourcePath] }
                : component).ToArray();
        var sourceInput = blockedInput with
        {
            Source = new InputSource(
                "source-available",
                "https://code.example.test/synthetic/component",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "Exact synthetic source snapshot.",
                "high"),
            SourceArtifacts = [sourceArtifact],
            Components = sourceComponents,
            EvidenceInputs = blockedInput.EvidenceInputs
                .Append(sourceProofInput)
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        var sourceBytes = InputManifestService.Serialize(sourceInput);
        var sourceInitialized = AssessmentService.Initialize(
            "component",
            fixture.Root,
            sourceInput,
            sourceBytes,
            "dynamic-group");
        var sourceEvidence = BuildEvidence(
            sourceInitialized.Identity,
            [
                Draft(
                    sourceInitialized.Identity,
                    EvidenceIdentity.ReproducedRuntimeObservation,
                    blockedProtocol.Basename,
                    EvidenceProtocolValidator.LifecycleMethod,
                    blockedProtocol.ContentDigest),
                Draft(
                    sourceInitialized.Identity,
                    EvidenceIdentity.ReviewerGeneratedAnalysis,
                    sourceProofInput.Basename,
                    EvidenceProtocolValidator.SourceProofMethod,
                    sourceProofInput.ContentDigest)
            ]);
        var sourceRecords = sourceEvidence.SourceLedgers
            .SelectMany(source => source.Ledger.Records)
            .ToArray();
        var sourceLifecycleId = sourceRecords.Single(record =>
            record.Provenance.Method == EvidenceProtocolValidator.LifecycleMethod).StableId;
        var sourceProofId = sourceRecords.Single(record =>
            record.Provenance.Method == EvidenceProtocolValidator.SourceProofMethod).StableId;
        var sourceConclusions = conclusions.ToDictionary(
            item => item.Key,
            _ => Verified(sourceLifecycleId),
            StringComparer.Ordinal);
        sourceConclusions["A11Y-06"] = new RowConclusion(
            "not tested",
            "The keyed reorder operation was blocked because the synthetic host omitted stable keys.",
            [sourceLifecycleId],
            null,
            "Rerun keyed reorder with stable child keys and retain the raw observation.",
            null);
        sourceConclusions["BEQ-15"] = new RowConclusion(
            "gap",
            "The exact component source discards an asynchronous browser cleanup task from synchronous disposal.",
            new[] { sourceLifecycleId, sourceProofId }.Order(StringComparer.Ordinal).ToArray(),
            "Await browser cleanup through an asynchronous disposal path.",
            null,
            null);
        ExpectValidation(
            () => Validate(
                fixture.Root,
                sourceInput,
                sourceBytes,
                CompleteRows(sourceInitialized, sourceConclusions),
                sourceEvidence),
            "source cleanup gap cannot contradict a passed lifecycle operation");

        var cleanupBlockedBytes = LifecycleProtocol(
            notTestedDigests,
            blockedOperation: "cleanup-disposal");
        File.WriteAllBytes(
            Path.Combine(fixture.Root, "lifecycle-cleanup-blocked.json"),
            cleanupBlockedBytes);
        var cleanupBlockedProtocol = CreateEvidenceInput(
            fixture.Root,
            "lifecycle-cleanup-blocked.json",
            "structured-protocol");
        var validSourceInput = sourceInput with
        {
            EvidenceInputs = sourceInput.EvidenceInputs
                .Where(item => item.Basename != blockedProtocol.Basename)
                .Append(cleanupBlockedProtocol)
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        var validSourceBytes = InputManifestService.Serialize(validSourceInput);
        var validSourceInitialized = AssessmentService.Initialize(
            "component",
            fixture.Root,
            validSourceInput,
            validSourceBytes,
            "dynamic-group");
        var validSourceEvidence = BuildEvidence(
            validSourceInitialized.Identity,
            [
                Draft(
                    validSourceInitialized.Identity,
                    EvidenceIdentity.ReproducedRuntimeObservation,
                    cleanupBlockedProtocol.Basename,
                    EvidenceProtocolValidator.LifecycleMethod,
                    cleanupBlockedProtocol.ContentDigest),
                Draft(
                    validSourceInitialized.Identity,
                    EvidenceIdentity.ReviewerGeneratedAnalysis,
                    sourceProofInput.Basename,
                    EvidenceProtocolValidator.SourceProofMethod,
                    sourceProofInput.ContentDigest)
            ]);
        var validRecords = validSourceEvidence.SourceLedgers
            .SelectMany(source => source.Ledger.Records)
            .ToArray();
        var validLifecycleId = validRecords.Single(record =>
            record.Provenance.Method == EvidenceProtocolValidator.LifecycleMethod).StableId;
        var validProofId = validRecords.Single(record =>
            record.Provenance.Method == EvidenceProtocolValidator.SourceProofMethod).StableId;
        var validConclusions = conclusions.ToDictionary(
            item => item.Key,
            _ => Verified(validLifecycleId),
            StringComparer.Ordinal);
        validConclusions["BEQ-15"] = new RowConclusion(
            "gap",
            "The lifecycle cleanup probe was blocked, while exact source proves the asynchronous cleanup task is discarded.",
            new[] { validLifecycleId, validProofId }.Order(StringComparer.Ordinal).ToArray(),
            "Await browser cleanup through an asynchronous disposal path.",
            null,
            null);
        Validate(
            fixture.Root,
            validSourceInput,
            validSourceBytes,
            CompleteRows(validSourceInitialized, validConclusions),
            validSourceEvidence);

        WriteFile(
            fixture.Root,
            "cleanup-free-text.json",
            "{\"finding\":\"An asynchronous browser cleanup task is discarded.\"}");
        var freeTextInput = CreateEvidenceInput(
            fixture.Root,
            "cleanup-free-text.json",
            "source-analysis");
        var freeTextManifest = validSourceInput with
        {
            EvidenceInputs = validSourceInput.EvidenceInputs
                .Where(item => item.Basename != sourceProofInput.Basename)
                .Append(freeTextInput)
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        var freeTextBytes = InputManifestService.Serialize(freeTextManifest);
        var freeTextInitialized = AssessmentService.Initialize(
            "component",
            fixture.Root,
            freeTextManifest,
            freeTextBytes,
            "dynamic-group");
        var freeTextEvidence = BuildEvidence(
            freeTextInitialized.Identity,
            [
                Draft(
                    freeTextInitialized.Identity,
                    EvidenceIdentity.ReproducedRuntimeObservation,
                    cleanupBlockedProtocol.Basename,
                    EvidenceProtocolValidator.LifecycleMethod,
                    cleanupBlockedProtocol.ContentDigest),
                Draft(
                    freeTextInitialized.Identity,
                    EvidenceIdentity.ReviewerGeneratedAnalysis,
                    freeTextInput.Basename,
                    "exact source cleanup analysis",
                    freeTextInput.ContentDigest)
            ]);
        var freeTextRecords = freeTextEvidence.SourceLedgers
            .SelectMany(source => source.Ledger.Records)
            .ToArray();
        var freeTextLifecycleId = freeTextRecords.Single(record =>
            record.Provenance.Method == EvidenceProtocolValidator.LifecycleMethod).StableId;
        var freeTextId = freeTextRecords.Single(record =>
            record.Provenance.Method == "exact source cleanup analysis").StableId;
        var freeTextConclusions = conclusions.ToDictionary(
            item => item.Key,
            _ => Verified(freeTextLifecycleId),
            StringComparer.Ordinal);
        freeTextConclusions["BEQ-15"] = new RowConclusion(
            "gap",
            "Free-text analysis claims that asynchronous cleanup is discarded.",
            new[] { freeTextLifecycleId, freeTextId }.Order(StringComparer.Ordinal).ToArray(),
            "Replace free-text analysis with a digest-bound source-proof protocol.",
            null,
            null);
        ExpectValidation(
            () => Validate(
                fixture.Root,
                freeTextManifest,
                freeTextBytes,
                CompleteRows(freeTextInitialized, freeTextConclusions),
                freeTextEvidence),
            "free-text source analysis cannot establish a lifecycle gap");

        File.WriteAllBytes(
            Path.Combine(fixture.Root, "non-source-proof.json"),
            SourceProof(
                "BEQ-15",
                "async-cleanup-not-awaited",
                "src/NotConfirmed.cs",
                cleanupBlockedProtocol.ContentDigest));
        var nonSourceProofInput = CreateEvidenceInput(
            fixture.Root,
            "non-source-proof.json",
            "structured-protocol");
        var nonSourceManifest = validSourceInput with
        {
            EvidenceInputs = validSourceInput.EvidenceInputs
                .Where(item => item.Basename != sourceProofInput.Basename)
                .Append(nonSourceProofInput)
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        var nonSourceBytes = InputManifestService.Serialize(nonSourceManifest);
        var nonSourceInitialized = AssessmentService.Initialize(
            "component",
            fixture.Root,
            nonSourceManifest,
            nonSourceBytes,
            "dynamic-group");
        var nonSourceEvidence = BuildEvidence(
            nonSourceInitialized.Identity,
            [
                Draft(
                    nonSourceInitialized.Identity,
                    EvidenceIdentity.ReproducedRuntimeObservation,
                    cleanupBlockedProtocol.Basename,
                    EvidenceProtocolValidator.LifecycleMethod,
                    cleanupBlockedProtocol.ContentDigest),
                Draft(
                    nonSourceInitialized.Identity,
                    EvidenceIdentity.ReviewerGeneratedAnalysis,
                    nonSourceProofInput.Basename,
                    EvidenceProtocolValidator.SourceProofMethod,
                    nonSourceProofInput.ContentDigest)
            ]);
        var nonSourceRecords = nonSourceEvidence.SourceLedgers
            .SelectMany(source => source.Ledger.Records)
            .ToArray();
        var nonSourceLifecycleId = nonSourceRecords.Single(record =>
            record.Provenance.Method == EvidenceProtocolValidator.LifecycleMethod).StableId;
        var nonSourceProofId = nonSourceRecords.Single(record =>
            record.Provenance.Method == EvidenceProtocolValidator.SourceProofMethod).StableId;
        var nonSourceConclusions = conclusions.ToDictionary(
            item => item.Key,
            _ => Verified(nonSourceLifecycleId),
            StringComparer.Ordinal);
        nonSourceConclusions["BEQ-15"] = new RowConclusion(
            "gap",
            "The claimed proof does not bind a confirmed component source artifact.",
            new[] { nonSourceLifecycleId, nonSourceProofId }.Order(StringComparer.Ordinal).ToArray(),
            "Bind the proof to an exact allowed source artifact.",
            null,
            null);
        ExpectValidation(
            () => Validate(
                fixture.Root,
                nonSourceManifest,
                nonSourceBytes,
                CompleteRows(nonSourceInitialized, nonSourceConclusions),
                nonSourceEvidence),
            "source proof cannot bind non-source evidence");

        var siblingManifest = validSourceInput with
        {
            Components =
            [
                validSourceInput.Components.Single() with { AllowedSourcePaths = [] },
                new InputComponent(
                    "sibling",
                    "Sibling",
                    ["interactive-server"],
                    [sourcePath],
                    new DynamicChildLifecycle(
                        "not-applicable",
                        [],
                        "The sibling does not manage dynamic children."))
            ]
        };
        var siblingBytes = InputManifestService.Serialize(siblingManifest);
        var siblingAssessment = AssessmentService.Initialize(
            "component",
            fixture.Root,
            siblingManifest,
            siblingBytes,
            "dynamic-group");
        var siblingEvidence = BuildEvidence(
            siblingAssessment.Identity,
            [
                Draft(
                    siblingAssessment.Identity,
                    EvidenceIdentity.ReproducedRuntimeObservation,
                    cleanupBlockedProtocol.Basename,
                    EvidenceProtocolValidator.LifecycleMethod,
                    cleanupBlockedProtocol.ContentDigest),
                Draft(
                    siblingAssessment.Identity,
                    EvidenceIdentity.ReviewerGeneratedAnalysis,
                    sourceProofInput.Basename,
                    EvidenceProtocolValidator.SourceProofMethod,
                    sourceProofInput.ContentDigest)
            ]);
        var siblingRecords = siblingEvidence.SourceLedgers
            .SelectMany(source => source.Ledger.Records)
            .ToArray();
        var siblingLifecycleId = siblingRecords.Single(record =>
            record.Provenance.Method == EvidenceProtocolValidator.LifecycleMethod).StableId;
        var siblingProofId = siblingRecords.Single(record =>
            record.Provenance.Method == EvidenceProtocolValidator.SourceProofMethod).StableId;
        var siblingConclusions = conclusions.ToDictionary(
            item => item.Key,
            _ => Verified(siblingLifecycleId),
            StringComparer.Ordinal);
        siblingConclusions["BEQ-15"] = new RowConclusion(
            "gap",
            "A sibling-only source artifact is incorrectly used to prove this component cleanup gap.",
            new[] { siblingLifecycleId, siblingProofId }.Order(StringComparer.Ordinal).ToArray(),
            "Bind proof to the assessed component source closure.",
            null,
            null);
        ExpectValidation(
            () => Validate(
                fixture.Root,
                siblingManifest,
                siblingBytes,
                CompleteRows(siblingAssessment, siblingConclusions),
                siblingEvidence),
            "source proof cannot bind sibling-only source");

        var nonDynamicManifest = sourceInput with
        {
            Components =
            [
                sourceInput.Components.Single() with
                {
                    DynamicChildLifecycle = new DynamicChildLifecycle(
                        "not-applicable",
                        [],
                        "This synthetic component has no dynamic child lifecycle surface.")
                }
            ]
        };
        var nonDynamicBytes = InputManifestService.Serialize(nonDynamicManifest);
        var nonDynamicAssessment = AssessmentService.Initialize(
            "component",
            fixture.Root,
            nonDynamicManifest,
            nonDynamicBytes,
            "dynamic-group");
        var nonDynamicEvidence = BuildEvidence(
            nonDynamicAssessment.Identity,
            [
                Draft(
                    nonDynamicAssessment.Identity,
                    EvidenceIdentity.ReviewerGeneratedAnalysis,
                    sourceProofInput.Basename,
                    EvidenceProtocolValidator.SourceProofMethod,
                    sourceProofInput.ContentDigest)
            ]);
        var nonDynamicProofId = nonDynamicEvidence.Selection.Single().EvidenceId;
        var nonDynamicConclusions = new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
        {
            ["BEQ-15"] = new RowConclusion(
                "gap",
                "The confirmed non-dynamic component source discards the asynchronous cleanup task.",
                [nonDynamicProofId],
                "Observe the asynchronous cleanup task.",
                null,
                null)
        };
        Validate(
            fixture.Root,
            nonDynamicManifest,
            nonDynamicBytes,
            CompleteRows(nonDynamicAssessment, nonDynamicConclusions),
            nonDynamicEvidence);
    }

    private static void TestNonDynamicSourceProofs(string root)
    {
        foreach (var (requirement, proofKind, source) in new[]
        {
            ("BEQ-12", "async-callback-not-awaited",
                "sealed class StaticControl { Task SelectAsync() { _ = SelectionChanged.InvokeAsync(); return Task.CompletedTask; } }"),
            ("BEQ-15", "async-cleanup-not-awaited",
                "sealed class StaticControl : IDisposable { void Dispose() { _ = SendCleanupAsync(); } }")
        })
        {
            var fixture = CreateFixture(Path.Combine(root, "non-dynamic-" + requirement), lifecycleRequired: false);
            const string sourcePath = "src/StaticControl.cs";
            const string contentPath = "static-control.cs";
            WriteFile(fixture.Root, contentPath, source);
            var sourceDigest = ContractJson.RawDigest(Encoding.UTF8.GetBytes(source));
            var proof = SourceProof(requirement, proofKind, sourcePath, sourceDigest);
            File.WriteAllBytes(Path.Combine(fixture.Root, "source-proof.json"), proof);
            var registration = CreateEvidenceInput(fixture.Root, "source-proof.json", "structured-protocol");
            var input = fixture.Manifest with
            {
                Source = new("source-available", "https://code.example.test/synthetic/static",
                    new string('a', 40), "Exact inert synthetic source fixture.", "high"),
                SourceArtifacts = [new(sourcePath, contentPath, sourceDigest)],
                Components = [fixture.Manifest.Components.Single() with { AllowedSourcePaths = [sourcePath] }],
                EvidenceInputs = [registration]
            };
            var inputBytes = InputManifestService.Serialize(input);
            var initialized = AssessmentService.Initialize("component", fixture.Root, input, inputBytes, "static-control");
            var evidence = BuildEvidence(initialized.Identity,
                [Draft(initialized.Identity, EvidenceIdentity.ReviewerGeneratedAnalysis,
                    registration.Basename, EvidenceProtocolValidator.SourceProofMethod, registration.ContentDigest)]);
            var completed = CompleteRows(initialized, new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
            {
                [requirement] = new("gap", "The exact inert source discards the named asynchronous task.",
                    [evidence.Selection.Single().EvidenceId], "Observe the task; runtime behavior was not executed.", null, null)
            });
            Validate(fixture.Root, input, inputBytes, completed, evidence);
            AssertEqual(false, evidence.SourceLedgers.SelectMany(item => item.Ledger.Records)
                .Any(item => item.Provenance.Method == EvidenceProtocolValidator.LifecycleMethod),
                "non-dynamic proof needs no invented lifecycle companion");
            var requiredInput = input with
            {
                Components = [input.Components.Single() with
                {
                    DynamicChildLifecycle = new("required", ["grouped-children"], null)
                }]
            };
            ExpectValidationMessage(
                () => EvidenceProtocolValidator.Validate(fixture.Root, completed, requiredInput, evidence),
                "requires exactly one dynamic lifecycle protocol", requirement + " still requires a genuine lifecycle companion");
            var lifecycleBytes = StrictJson.SerializeCanonical(writer =>
            {
                writer.WriteStartObject();
                writer.WriteNumber("schema_version", 1);
                writer.WriteString("protocol", "dynamic-child-lifecycle");
                writer.WriteStartArray("operations");
                foreach (var operation in LifecycleOperations)
                {
                    writer.WriteStartObject();
                    writer.WriteString("operation", operation);
                    writer.WriteString("disposition", "not-tested");
                    writer.WriteNull("outcome");
                    writer.WriteNull("raw_observation_sha256");
                    writer.WriteString("not_tested_reason", "No runtime operation was performed in this static-only fixture.");
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            });
            File.WriteAllBytes(Path.Combine(fixture.Root, "unrequested-lifecycle.json"), lifecycleBytes);
            var matrixInput = input with
            {
                EvidenceInputs = [registration,
                    new("unrequested-lifecycle.json", "structured-protocol",
                        ContractJson.RawDigest(lifecycleBytes), lifecycleBytes.LongLength)]
            };
            var matrixEvidence = BuildEvidence(initialized.Identity,
            [
                Draft(initialized.Identity, EvidenceIdentity.ReviewerGeneratedAnalysis,
                    registration.Basename, EvidenceProtocolValidator.SourceProofMethod, registration.ContentDigest),
                Draft(initialized.Identity, EvidenceIdentity.ReproducedRuntimeObservation,
                    "unrequested-lifecycle.json", EvidenceProtocolValidator.LifecycleMethod, ContractJson.RawDigest(lifecycleBytes))
            ]);
            ExpectValidationMessage(
                () => EvidenceProtocolValidator.Validate(fixture.Root, completed, matrixInput, matrixEvidence),
                "not-applicable cannot select a lifecycle protocol",
                requirement + " rejects a fabricated non-dynamic lifecycle companion");

            foreach (var lifecycle in new DynamicChildLifecycle[]
            {
                new("", [], "Missing applicability."),
                new("unknown", [], "Unknown is not confirmed non-applicability."),
                new("not-applicable", [], null),
                new("not-applicable", [], " "),
                new("not-applicable", ["grouped-children"], "Contradictory trigger."),
                new("required", [], null),
                new("required", ["grouped-children"], "A non-applicability rationale is contradictory."),
                new("required", ["grouped-children", "grouped-children"], null),
                new("required", ["unsupported-trigger"], null)
            })
            {
                var malformed = input with
                {
                    Components = [input.Components.Single() with { DynamicChildLifecycle = lifecycle }]
                };
                ExpectValidation(() => EvidenceProtocolValidator.Validate(fixture.Root, completed, malformed, evidence),
                    requirement + " source proof rejects malformed applicability without depending on prior intake");
            }
            foreach (var field in new[] { "dynamic_child_lifecycle", "applicability" })
            {
                var malformed = JsonNode.Parse(inputBytes)!;
                var component = malformed["components"]![0]!.AsObject();
                if (field == "dynamic_child_lifecycle")
                {
                    component.Remove(field);
                }
                else
                {
                    component["dynamic_child_lifecycle"]!.AsObject().Remove(field);
                }
                ExpectValidation(() => InputManifestService.Parse(Encoding.UTF8.GetBytes(malformed.ToJsonString())),
                    requirement + " missing applicability is not non-applicability");
            }
            foreach (var status in new[] { "verified", "not tested", "not applicable" })
            {
                var wrongStatus = completed with
                {
                    Rows = completed.Rows.Select(row => row.Id == requirement ? row with { Status = status } : row).ToArray()
                };
                ExpectValidation(() => EvidenceProtocolValidator.Validate(fixture.Root, wrongStatus, input, evidence),
                    requirement + " source proof remains gap-only");
            }
            foreach (var mutation in new[] { "digest", "path", "requirement", "proof-kind", "result", "newline" })
            {
                var value = JsonNode.Parse(proof)!;
                switch (mutation)
                {
                    case "digest": value["source_sha256"]!["value"] = new string('0', 64); break;
                    case "path": value["source_path"] = "src/OtherControl.cs"; break;
                    case "requirement": value["requirement_id"] = "BEQ-05"; break;
                    case "proof-kind": value["proof_kind"] = "unsupported-proof"; break;
                    case "result": value["result"] = "passed"; break;
                }
                var bytes = Encoding.UTF8.GetBytes(value.ToJsonString() + (mutation == "newline" ? "\n" : ""));
                var basename = "invalid-" + mutation + ".json";
                File.WriteAllBytes(Path.Combine(fixture.Root, basename), bytes);
                var changedInput = input with
                {
                    EvidenceInputs = [new(basename, "structured-protocol", ContractJson.RawDigest(bytes), bytes.LongLength)]
                };
                var changedInitial = AssessmentService.Initialize("component", fixture.Root, changedInput,
                    InputManifestService.Serialize(changedInput), "static-control");
                var changedEvidence = BuildEvidence(changedInitial.Identity,
                    [Draft(changedInitial.Identity, EvidenceIdentity.ReviewerGeneratedAnalysis, basename,
                        EvidenceProtocolValidator.SourceProofMethod, ContractJson.RawDigest(bytes))]);
                var changedAssessment = CompleteRows(changedInitial, new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
                {
                    [requirement] = new("gap", "A deliberately invalid proof is otherwise correctly registered.",
                        [changedEvidence.Selection.Single().EvidenceId], null, null, null)
                });
                ExpectValidation(() => Validate(fixture.Root, changedInput, InputManifestService.Serialize(changedInput),
                    changedAssessment, changedEvidence), requirement + " rejects independently bound " + mutation);
            }

            var inputPath = Path.Combine(fixture.Root, "input.confirmed.json");
            var assessmentPath = Path.Combine(fixture.Root, "component.assessment.json");
            var evidencePath = Path.Combine(fixture.Root, "component.evidence.json");
            File.WriteAllBytes(inputPath, inputBytes);
            File.WriteAllBytes(assessmentPath, AssessmentService.Serialize(completed));
            File.WriteAllBytes(evidencePath, CanonicalEvidenceJson.SerializeBundle(evidence));
            var revisions = Path.Combine(fixture.Root, "revisions");
            RunSourceFindingCli(["report", "render", "--root", fixture.Root, "--input", inputPath,
                "--assessment", assessmentPath, "--evidence", evidencePath, "--output", revisions]);
            var revision = Path.Combine(revisions, "0001");
            var reader = Path.Combine(fixture.Root, "readable");
            RunSourceFindingCli(["report", "verify", "--root", fixture.Root, "--revision", revision]);
            RunSourceFindingCli(["reader", "render", "--root", fixture.Root, "--revision", revision, "--output", reader]);
            RunSourceFindingCli(["reader", "verify", "--root", fixture.Root, "--revision", revision, "--output", reader]);
            AssertEqual("gap", AssessmentService.Parse(File.ReadAllBytes(Path.Combine(revision, "component.assessment.json")))
                .Rows.Single(row => row.Id == requirement).Status, "typed source gap survives complete report/reader production");
        }
        Console.WriteLine("Non-dynamic callback and cleanup source proofs: paired acceptance, rejection and report/reader checks passed.");
    }

    private static void TestToolchainDisposition(string root)
    {
        var fixture = CreateFixture(Path.Combine(root, "toolchain"), lifecycleRequired: false);
        WriteFile(fixture.Root, "trim.log", "Supported toolchain emitted one package warning.");
        var rawLog = CreateEvidenceInput(fixture.Root, "trim.log", "toolchain-log");
        var protocolBytes = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "toolchain-probe");
            writer.WriteString("toolchain_name", "Synthetic .NET SDK");
            writer.WriteString("toolchain_version", "11.0.100-preview.7");
            writer.WriteString("target_framework", "net11.0");
            writer.WriteString("support_disposition", "supported");
            writer.WriteString("command", "dotnet publish -p:PublishTrimmed=true");
            writer.WriteString("result", "failed");
            WriteDigest(writer, "raw_log_sha256", rawLog.ContentDigest);
            writer.WriteEndObject();
        });
        File.WriteAllBytes(Path.Combine(fixture.Root, "toolchain-protocol.json"), protocolBytes);
        var protocol = CreateEvidenceInput(
            fixture.Root,
            "toolchain-protocol.json",
            "structured-protocol");
        var input = fixture.Manifest with
        {
            EvidenceInputs = new[] { rawLog, protocol }
                .OrderBy(item => item.Basename, StringComparer.Ordinal)
                .ToArray()
        };
        var inputBytes = InputManifestService.Serialize(input);
        var initialized = AssessmentService.Initialize(
            "component",
            fixture.Root,
            input,
            inputBytes,
            "static-control");
        var evidence = BuildEvidence(
            initialized.Identity,
            [
                Draft(
                    initialized.Identity,
                    EvidenceIdentity.ReproducedRuntimeObservation,
                    protocol.Basename,
                    EvidenceProtocolValidator.ToolchainMethod,
                    protocol.ContentDigest)
            ]);
        var id = evidence.Selection.Single().EvidenceId;
        var assessment = CompleteRows(
            initialized,
            new Dictionary<string, RowConclusion>(StringComparer.Ordinal)
            {
                ["TA-02"] = new RowConclusion(
                    "gap",
                    "The supported named toolchain emitted a package-attributable trimming warning.",
                    [id],
                    null,
                    null,
                    null),
                ["TA-04"] = new RowConclusion(
                    "not tested",
                    "Reachability analysis was blocked because detailed linker diagnostics were unavailable.",
                    [],
                    null,
                    "Rerun with detailed linker diagnostics and retain the exact supported-toolchain log.",
                    null)
            });
        Validate(fixture.Root, input, inputBytes, assessment, evidence);

        var unresolved = assessment with
        {
            Rows = assessment.Rows.Select(row =>
                row.Id == "TA-04"
                    ? row with
                    {
                        Status = "not tested",
                        Observation = null
                    }
                    : row).ToArray()
        };
        ExpectValidation(
            () => Validate(fixture.Root, input, inputBytes, unresolved, evidence),
            "trim comparison row requires exact blocker");
    }

    private static void TestComparisonInputGate(string root)
    {
        var fixtureRoot = Path.Combine(root, "comparison");
        Directory.CreateDirectory(fixtureRoot);
        var nupkg = Path.Combine(fixtureRoot, "package.bin");
        CreatePackage(nupkg);
        WriteFile(fixtureRoot, "source.bin", "exact source archive bytes");
        WriteFile(fixtureRoot, "retrieval.bin", "HTTP 200 raw response metadata");
        WriteFile(fixtureRoot, "sdk.bin", "Synthetic SDK 11.0.100");
        WriteFile(fixtureRoot, "browser.bin", "Synthetic Browser 1.0");
        WriteFile(fixtureRoot, "trim.bin", "raw trim output");
        foreach (var id in CoverageTypedInputIds())
        {
            WriteFile(
                fixtureRoot,
                $"{id}.bin",
                $"typed conclusion-free material for {id}");
        }
        var inspected = NupkgInspector.Inspect(nupkg);
        var draft = CreateComparisonDraft(inspected, includeToolchain: true);
        var manifest = ComparisonInputService.Freeze(
            fixtureRoot,
            Encoding.UTF8.GetBytes(draft));
        var bytes = ComparisonInputService.Serialize(manifest);
        ComparisonInputService.Validate(
            ComparisonInputService.Parse(bytes),
            fixtureRoot);
        foreach (var label in new[] { "unified", "schema", "retired-regression-surface" })
        {
            void Mutate(JsonObject value)
            {
                if (label == "unified")
                {
                    value["assessment_kinds"] = new JsonArray("unified");
                }
                else if (label == "schema")
                {
                    value["schema_version"] = 999;
                }
                else
                {
                    value["coverage"]!.AsArray().Single(item =>
                        item!["id"]!.GetValue<string>() == "release-revalidation")!["id"] = "regression-and-release-mapping";
                }
            }
            var badDraft = JsonNode.Parse(draft)!.AsObject();
            var badConfirmed = JsonNode.Parse(bytes)!.AsObject();
            Mutate(badDraft);
            Mutate(badConfirmed);
            var rejectedDraftPath = Path.Combine(fixtureRoot, label + "-draft.json");
            var rejectedManifestPath = Path.Combine(fixtureRoot, label + "-confirmed.json");
            var rejectedOutput = Path.Combine(fixtureRoot, label + "-frozen.json");
            File.WriteAllText(rejectedDraftPath, badDraft.ToJsonString());
            File.WriteAllText(rejectedManifestPath, badConfirmed.ToJsonString());
            var invalidError = new StringWriter();
            AssertEqual(ExitCodes.ValidationFailure, CliApplication.Run(
                ["comparison", "inputs-freeze", "--root", fixtureRoot, "--draft", rejectedDraftPath, "--output", rejectedOutput],
                new StringWriter(), invalidError), "comparison freeze rejects " + label);
            AssertEqual(ExitCodes.ValidationFailure, CliApplication.Run(
                ["comparison", "inputs-validate", "--root", fixtureRoot, "--manifest", rejectedManifestPath],
                new StringWriter(), invalidError), "comparison intake rejects " + label);
            AssertEqual(false, File.Exists(rejectedOutput), "unsupported comparison publishes no current input");
        }
        var legacyManifest = manifest with
        {
            SchemaVersion = 1,
            AssessmentKinds = [],
            Coverage = []
        };
        var legacyBytes = ComparisonInputService.Serialize(legacyManifest);
        ComparisonInputService.Validate(
            ComparisonInputService.Parse(legacyBytes),
            fixtureRoot);
        ExpectValidation(
            () => ComparisonInputService.Validate(
                legacyManifest with { Coverage = manifest.Coverage },
                fixtureRoot),
            "legacy comparison manifest rejects v2 coverage fields");
        var draftPath = Path.Combine(fixtureRoot, "comparison-inputs.draft.json");
        var confirmedPath = Path.Combine(fixtureRoot, "comparison-inputs.confirmed.json");
        File.WriteAllText(draftPath, draft, new UTF8Encoding(false));
        var output = new StringWriter();
        var error = new StringWriter();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(
                [
                    "comparison",
                    "inputs-freeze",
                    "--root",
                    fixtureRoot,
                    "--draft",
                    draftPath,
                    "--output",
                    confirmedPath
                ],
                output,
                error),
            $"comparison freeze CLI: {error}");
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(
                [
                    "comparison",
                    "inputs-validate",
                    "--root",
                    fixtureRoot,
                    "--manifest",
                    confirmedPath
                ],
                output,
                error),
            $"comparison validate CLI: {error}");

        var missingCoverage = CreateComparisonDraft(
            inspected,
            includeToolchain: true,
            omittedCoverageSurface: "browser-interop-and-style-assets");
        ExpectValidation(
            () => ComparisonInputService.Freeze(
                fixtureRoot,
                Encoding.UTF8.GetBytes(missingCoverage)),
            "comparison requires complete evidence-surface coverage");

        var blockedExactPackage = draft.Replace(
            "\"id\":\"exact-package\",\"disposition\":\"available\",\"blocker\":null,\"bindings\":[{\"role\":\"package\",\"input_id\":\"package\",\"origin_kind\":\"package\",\"origin_id\":\"package\"}]",
            "\"id\":\"exact-package\",\"disposition\":\"blocked\",\"blocker\":\"The exact package was not supplied.\",\"bindings\":[]",
            StringComparison.Ordinal);
        ExpectValidationMessage(
            () => ComparisonInputService.Freeze(
                fixtureRoot,
                Encoding.UTF8.GetBytes(blockedExactPackage)),
            "exact-package coverage surface must be available",
            "blocked exact package fails deterministically");

        var wrongCoverageKind = draft.Replace(
            "\"id\":\"component-source-closure\",\"kind\":\"component-source-closure\"",
            "\"id\":\"component-source-closure\",\"kind\":\"probe-log\"",
            StringComparison.Ordinal);
        ExpectValidation(
            () => ComparisonInputService.Freeze(
                fixtureRoot,
                Encoding.UTF8.GetBytes(wrongCoverageKind)),
            "comparison rejects wrong-kind coverage bindings");

        var wrongCoverageOrigin = draft.Replace(
            "\"role\":\"component-source-closure\",\"input_id\":\"component-source-closure\",\"origin_kind\":\"probe\",\"origin_id\":\"coverage-material\"",
            "\"role\":\"component-source-closure\",\"input_id\":\"component-source-closure\",\"origin_kind\":\"source\",\"origin_id\":\"release-source\"",
            StringComparison.Ordinal);
        ExpectValidation(
            () => ComparisonInputService.Freeze(
                fixtureRoot,
                Encoding.UTF8.GetBytes(wrongCoverageOrigin)),
            "comparison rejects wrong-origin coverage bindings");

        var duplicateCoveragePath = draft.Replace(
            "\"id\":\"style-asset-inventory\",\"kind\":\"style-asset-inventory\",\"path\":\"style-asset-inventory.bin\"",
            "\"id\":\"style-asset-inventory\",\"kind\":\"style-asset-inventory\",\"path\":\"component-source-closure.bin\"",
            StringComparison.Ordinal);
        ExpectValidation(
            () => ComparisonInputService.Freeze(
                fixtureRoot,
                Encoding.UTF8.GetBytes(duplicateCoveragePath)),
            "comparison rejects coverage path reuse");

        WriteFile(
            fixtureRoot,
            "duplicate-component-source.bin",
            "typed conclusion-free material for component-source-closure");
        var duplicateCoverageBytes = draft.Replace(
            "\"id\":\"style-asset-inventory\",\"kind\":\"style-asset-inventory\",\"path\":\"style-asset-inventory.bin\"",
            "\"id\":\"style-asset-inventory\",\"kind\":\"style-asset-inventory\",\"path\":\"duplicate-component-source.bin\"",
            StringComparison.Ordinal);
        ExpectValidation(
            () => ComparisonInputService.Freeze(
                fixtureRoot,
                Encoding.UTF8.GetBytes(duplicateCoverageBytes)),
            "comparison rejects coverage byte reuse");

        var globalAllowedAlias = draft.Replace(
            "\"id\":\"trim-log\",\"kind\":\"probe-log\",\"path\":\"trim.bin\"",
            "\"id\":\"trim-log\",\"kind\":\"probe-log\",\"path\":\"sdk.bin\"",
            StringComparison.Ordinal);
        ExpectValidation(
            () => ComparisonInputService.Freeze(
                fixtureRoot,
                Encoding.UTF8.GetBytes(globalAllowedAlias)),
            "comparison rejects global allowed-input byte and path reuse");

        var missingFile = draft.Replace(
            "\"id\":\"trim-aot-observations\",\"kind\":\"toolchain-probe\",\"path\":\"trim-aot-observations.bin\"",
            "\"id\":\"trim-aot-observations\",\"kind\":\"toolchain-probe\",\"path\":\"missing-trim.bin\"",
            StringComparison.Ordinal);
        ExpectValidation(
            () => ComparisonInputService.Freeze(
                fixtureRoot,
                Encoding.UTF8.GetBytes(missingFile)),
            "comparison rejects a missing allowed input");

        var withoutToolchain = CreateComparisonDraft(inspected, includeToolchain: false);
        ExpectValidationMessage(
            () => ComparisonInputService.Freeze(
                fixtureRoot,
                Encoding.UTF8.GetBytes(withoutToolchain)),
            "requires a named supported toolchain",
            "available trim probe requires named toolchain");

        WriteFile(fixtureRoot, "notes.bin", "{\"status\":\"verified\"}");
        var conclusionBearing = CreateComparisonDraft(
            inspected,
            includeToolchain: true,
            extraAllowedInput: ",{\"id\":\"notes\",\"kind\":\"raw-probe\",\"path\":\"notes.bin\"}",
            extraProbeInput: ",\"notes\"");
        ExpectValidation(
            () => ComparisonInputService.Freeze(
                fixtureRoot,
                Encoding.UTF8.GetBytes(conclusionBearing)),
            "comparison allowed input rejects conclusion material");

        var unresolved = CreateComparisonDraft(inspected, includeToolchain: true)
            .Replace(
                "\"disposition\":\"available\",\"blocker\":null,\"toolchain_id\":\"sdk\"",
                "\"disposition\":\"unresolved\",\"blocker\":null,\"toolchain_id\":\"sdk\"",
                StringComparison.Ordinal);
        ExpectValidation(
            () => ComparisonInputService.Freeze(
                fixtureRoot,
                Encoding.UTF8.GetBytes(unresolved)),
            "comparison dispositions cannot remain unresolved");
    }

    private static Fixture CreateFixture(string root, bool lifecycleRequired)
    {
        Directory.CreateDirectory(root);
        var nupkgPath = Path.Combine(root, "package.nupkg");
        CreatePackage(nupkgPath);
        WriteFile(root, "README.md", "# Synthetic package");
        var inspected = NupkgInspector.Inspect(nupkgPath);
        var lifecycle = lifecycleRequired
            ? new DynamicChildLifecycle(
                "required",
                ["grouped-children", "registered-children", "selected-children"],
                null)
            : new DynamicChildLifecycle(
                "not-applicable",
                [],
                "This synthetic control does not manage grouped, registered, selected, or composite children.");
        var manifest = new InputManifest(
            InputManifestService.SchemaVersion,
            "confirmed",
            "release-candidate",
            new InputPackage(
                "synthetic.controls",
                "1.0.0",
                new Sha256Digest("sha256", inspected.NupkgSha256),
                "package.nupkg",
                "package.nupkg",
                "local-file"),
            new InputSource(
                "closed-source",
                null,
                null,
                null,
                null),
            [new InputRetrievalAttempt("package", "package.nupkg", "local-file", "succeeded", null)],
            [],
            [],
            [],
            [],
            [],
            [
                new InputComponent(
                    lifecycleRequired ? "dynamic-group" : "static-control",
                    lifecycleRequired ? "Dynamic Group" : "Static Control",
                    ["interactive-server"],
                    [],
                    lifecycle)
            ],
            []);
        InputManifestService.Validate(manifest, root, requireConfirmed: true);
        return new Fixture(root, nupkgPath, manifest);
    }

    private static ReadinessAssessment CompleteRows(
        ReadinessAssessment initialized,
        IReadOnlyDictionary<string, RowConclusion> conclusions)
    {
        var rows = initialized.Rows.Select(row =>
        {
            if (conclusions.TryGetValue(row.Id, out var conclusion))
            {
                return row with
                {
                    Status = conclusion.Status,
                    Observation = conclusion.Observation,
                    EvidenceIds = conclusion.EvidenceIds,
                    OwnerAction = conclusion.OwnerAction,
                    AssessmentFollowUp = conclusion.AssessmentFollowUp,
                    NotApplicableRationale = conclusion.NotApplicableRationale
                };
            }

            return row with
            {
                Status = "not applicable",
                Observation = null,
                EvidenceIds = [],
                OwnerAction = null,
                AssessmentFollowUp = null,
                NotApplicableRationale =
                    "This synthetic enforcement fixture does not exercise this requirement."
            };
        }).ToArray();
        return initialized with
        {
            Rows = rows,
            SummaryGroups = initialized.AssessmentKind == "package"
                ? BuildSummaryGroups(rows)
                : [],
            CompletionState = "complete"
        };
    }

    private static RowConclusion Verified(string evidenceId) =>
        new(
            "verified",
            "The structured evidence protocol directly establishes this bounded requirement.",
            [evidenceId],
            null,
            null,
            null);

    private static EvidenceRecordDraft Draft(
        ExactAssessmentIdentity identity,
        string kind,
        string locator,
        string method,
        Sha256Digest digest,
        bool? componentSpecific = null)
    {
        var appliesToComponent = componentSpecific ??
            (identity.AssessmentKind == "component" ||
             method == EvidenceProtocolValidator.LifecycleMethod ||
             method == EvidenceProtocolValidator.SourceProofMethod);
        return new EvidenceRecordDraft(
            "The structured protocol records direct evidence for this bounded requirement.",
            appliesToComponent
                ? new EvidenceApplicability("component-specific", identity.ComponentId)
                : new EvidenceApplicability("repository-wide", null),
            new EvidenceProvenance(
                kind,
                locator,
                method,
                "2026-09-03T00:00:00Z",
                digest,
                "commitment-only"),
            []);
    }

    private static EvidenceBundle BuildEvidence(
        ExactAssessmentIdentity identity,
        IReadOnlyList<EvidenceRecordDraft> drafts)
    {
        var repositoryDrafts = drafts
            .Where(draft => draft.Applicability.Scope == "repository-wide")
            .ToArray();
        var componentDrafts = drafts
            .Where(draft => draft.Applicability.Scope == "component-specific")
            .ToArray();
        var ledgers = new List<EvidenceSourceLedger>();
        if (repositoryDrafts.Length > 0)
        {
            ledgers.Add(EvidenceLedgerBuilder.BuildRepositoryLedger(
                new RepositoryLedgerSubject(
                    identity.AssessmentKind,
                    identity.Package,
                    identity.InputManifestDigest,
                    identity.ComponentId),
                repositoryDrafts));
        }

        if (componentDrafts.Length > 0)
        {
            ledgers.Add(EvidenceLedgerBuilder.BuildComponentLedger(identity, componentDrafts));
        }

        return EvidenceLedgerBuilder.BuildBundle(
            identity,
            ledgers,
            ledgers.SelectMany(ledger => ledger.Records)
                .Select(record => record.StableId)
                .ToArray());
    }

    private static void Validate(
        string root,
        InputManifest input,
        byte[] inputBytes,
        ReadinessAssessment assessment,
        EvidenceBundle evidence)
    {
        AssessmentService.Validate(
            root,
            assessment,
            AssessmentService.Serialize(assessment),
            input,
            inputBytes,
            evidence);
    }

    private static IReadOnlyList<AssessmentSummaryGroup> BuildSummaryGroups(
        IReadOnlyList<AssessmentRow> rows) =>
        RubricLoader.Load().Statuses
            .Select(status => new
            {
                Status = status,
                Rows = rows.Where(row => row.Status == status).ToArray()
            })
            .Where(group => group.Rows.Length > 0)
            .Select(group => new AssessmentSummaryGroup(
                group.Status,
                $"These package rows have the factual status '{group.Status}'.",
                group.Rows.Select(row => row.Id).ToArray(),
                group.Rows.SelectMany(row => row.EvidenceIds)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();

    private static InputEvidenceArtifact CreateEvidenceInput(
        string root,
        string basename,
        string kind)
    {
        var bytes = File.ReadAllBytes(Path.Combine(root, basename));
        return new InputEvidenceArtifact(
            basename,
            kind,
            ContractJson.RawDigest(bytes),
            bytes.LongLength);
    }

    private static OwnerInput CreateOwnerInput(
        string root,
        string basename,
        string provenance)
    {
        var bytes = File.ReadAllBytes(Path.Combine(root, basename));
        return new OwnerInput(
            basename,
            provenance,
            ContractJson.RawDigest(bytes),
            bytes.LongLength);
    }

    private static byte[] LifecycleProtocol(
        IReadOnlyDictionary<string, Sha256Digest> digests,
        string? blockedOperation = null) =>
        StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "dynamic-child-lifecycle");
            writer.WritePropertyName("operations");
            writer.WriteStartArray();
            foreach (var operation in LifecycleOperations)
            {
                writer.WriteStartObject();
                writer.WriteString("operation", operation);
                if (operation == blockedOperation)
                {
                    writer.WriteString("disposition", "not-tested");
                    writer.WriteNull("outcome");
                    writer.WriteNull("raw_observation_sha256");
                    writer.WriteString(
                        "not_tested_reason",
                        "The synthetic host omitted stable child keys for this operation.");
                }
                else
                {
                    writer.WriteString("disposition", "observed");
                    writer.WriteString("outcome", "passed");
                    WriteDigest(writer, "raw_observation_sha256", digests[operation]);
                    writer.WriteNull("not_tested_reason");
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    private static byte[] SourceProof(
        string requirementId,
        string proofKind,
        string sourcePath,
        Sha256Digest sourceDigest) =>
        StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "source-proof");
            writer.WriteString("requirement_id", requirementId);
            writer.WriteString("proof_kind", proofKind);
            writer.WriteString("result", "failed");
            writer.WriteString("source_path", sourcePath);
            WriteDigest(writer, "source_sha256", sourceDigest);
            writer.WriteEndObject();
        });

    private static byte[] PublicAbsence(
        string requirementId,
        string corpusKind,
        Sha256Digest corpusDigest) =>
        StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "public-absence");
            writer.WriteString("requirement_id", requirementId);
            writer.WriteString("corpus_kind", corpusKind);
            writer.WriteString("result", "absent");
            WriteDigest(writer, "corpus_sha256", corpusDigest);
            writer.WriteEndObject();
        });

    private static byte[] PublicCorpus(
        string corpusKind,
        IReadOnlyList<string> coveredMarkers,
        IReadOnlyList<string> presentMarkers,
        bool complete = true) =>
        StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("corpus_kind", corpusKind);
            writer.WriteBoolean("owner_controlled", true);
            writer.WriteBoolean("complete", complete);
            writer.WritePropertyName("covered_markers");
            writer.WriteStartArray();
            foreach (var marker in coveredMarkers.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(marker);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("present_markers");
            writer.WriteStartArray();
            foreach (var marker in presentMarkers.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(marker);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    private static byte[] WriteRevision(
        string root,
        string revisionsRoot,
        int sequence,
        InputManifest input,
        ReadinessAssessment assessment,
        EvidenceBundle evidence,
        Sha256Digest? predecessor)
    {
        var inputBytes = InputManifestService.Serialize(input);
        var assessmentBytes = AssessmentService.Serialize(assessment);
        var evidenceBytes = CanonicalEvidenceJson.SerializeBundle(evidence);
        var reportBytes = ReportService.RenderMarkdown(assessment, input, evidence);
        var manifest = ReportService.CreateManifest(
            assessment,
            assessmentBytes,
            input,
            inputBytes,
            evidence,
            evidenceBytes,
            reportBytes,
            predecessor,
            feedbackDigest: null,
            declaredChangedIds: []);
        var manifestBytes = ReportService.SerializeManifest(manifest);
        var revision = Path.Combine(
            revisionsRoot,
            sequence.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(revision);
        File.WriteAllBytes(Path.Combine(revision, "input-manifest.json"), inputBytes);
        File.WriteAllBytes(
            Path.Combine(revision, $"{assessment.AssessmentKind}.assessment.json"),
            assessmentBytes);
        File.WriteAllBytes(
            Path.Combine(revision, $"{assessment.AssessmentKind}.evidence.json"),
            evidenceBytes);
        File.WriteAllBytes(
            Path.Combine(revision, $"{assessment.AssessmentKind}.report.md"),
            reportBytes);
        File.WriteAllBytes(
            Path.Combine(revision, $"{assessment.AssessmentKind}.validation.json"),
            manifestBytes);
        return manifestBytes;
    }

    private static string CreateComparisonDraft(
        PackageIdentity package,
        bool includeToolchain,
        string extraAllowedInput = "",
        string extraProbeInput = "",
        string? omittedCoverageSurface = null)
    {
        var toolchain = includeToolchain
            ? """
              [{"id":"sdk","name":"Synthetic .NET SDK","version":"11.0.100","disposition":"available","blocker":null,"input_ids":["sdk-version"]}]
              """
            : "[]";
        var toolchainId = includeToolchain ? "\"sdk\"" : "null";
        var trimCoverageDisposition = includeToolchain ? "available" : "blocked";
        var trimCoverageBlocker = includeToolchain
            ? null
            : "No named supported trim or AOT toolchain was supplied.";
        (string Role, string InputId, string OriginKind, string OriginId)[] trimCoverageBindings =
            includeToolchain
                ?
                [
                    ("toolchain-identities", "sdk-version", "toolchain", "sdk"),
                    ("trim-aot-observations", "trim-aot-observations", "probe", "coverage-material")
                ]
                : [];
        var coverageEntries = new (
            string Surface,
            string Disposition,
            string? Blocker,
            (string Role, string InputId, string OriginKind, string OriginId)[] Bindings)[]
        {
            ("accessibility-and-localization", "available", null,
                [("accessibility-observations", "accessibility-observations", "probe", "coverage-material"), ("localization-claims", "localization-claims", "retrieval", "package-fetch")]),
            ("browser-interop-and-style-assets", "available", null,
                [("browser-interop-source", "browser-interop-source", "probe", "coverage-material"), ("style-asset-inventory", "style-asset-inventory", "probe", "coverage-material")]),
            ("claimed-mode-runtime", "available", null,
                [("mode-claims", "mode-claims", "retrieval", "package-fetch"), ("runtime-observations", "runtime-observations", "probe", "coverage-material")]),
            ("component-api-and-base-source", "available", null,
                [("component-source-closure", "component-source-closure", "probe", "coverage-material")]),
            ("dependency-and-notice-inventory", "available", null,
                [("asset-inventory", "asset-inventory", "probe", "coverage-material"), ("dependency-inventory", "dependency-inventory", "probe", "coverage-material"), ("notice-mapping", "notice-mapping", "probe", "coverage-material")]),
            ("exact-package", "available", null,
                [("package", "package", "package", "package")]),
            ("official-public-documents", "available", null,
                [("documentation-corpus", "public-documents", "retrieval", "package-fetch")]),
            ("owner-held-records", "blocked",
                "No owner-held records were supplied for this blind comparison.", []),
            ("performance-measurements", "blocked",
                "No representative performance measurement was supplied for this blind comparison.", []),
            ("release-revalidation", "available", null,
                [("release-revalidation", "release-revalidation", "probe", "coverage-material")]),
            ("release-source-and-workflows", "available", null,
                [("source-snapshot", "source", "source", "release-source"), ("workflow-inventory", "workflow-inventory", "probe", "coverage-material")]),
            ("signing-sbom-provenance", "available", null,
                [("assembly-signing", "assembly-signing", "probe", "coverage-material"), ("package-signing", "package-signing", "probe", "coverage-material"), ("sbom-provenance", "sbom-provenance", "probe", "coverage-material")]),
            ("support-and-lifecycle", "available", null,
                [("public-support-corpus", "public-support-corpus", "probe", "coverage-material"), ("release-lifecycle-corpus", "release-lifecycle-corpus", "probe", "coverage-material")]),
            ("tests-and-samples", "available", null,
                [("sample-inventory", "sample-inventory", "probe", "coverage-material"), ("test-inventory", "test-inventory", "probe", "coverage-material")]),
            ("trim-aot-toolchains", trimCoverageDisposition, trimCoverageBlocker,
                trimCoverageBindings)
        };
        var coverage = string.Join(
            ",",
            coverageEntries
                .Where(item => item.Surface != omittedCoverageSurface)
                .Select(item =>
                {
                    var bindings = JsonSerializer.Serialize(item.Bindings.Select(binding => new
                    {
                        role = binding.Role,
                        input_id = binding.InputId,
                        origin_kind = binding.OriginKind,
                        origin_id = binding.OriginId
                    }));
                    return $$"""{"id":"{{item.Surface}}","disposition":"{{item.Disposition}}","blocker":{{(item.Blocker is null ? "null" : JsonSerializer.Serialize(item.Blocker))}},"bindings":{{bindings}}}""";
                }));
        var typedAllowedInputs = new (string Id, string Kind, string Path)[]
        {
            ("accessibility-observations", "accessibility-observation", "accessibility-observations.bin"),
            ("assembly-signing", "assembly-signing", "assembly-signing.bin"),
            ("asset-inventory", "asset-inventory", "asset-inventory.bin"),
            ("browser-interop-source", "browser-interop-source", "browser-interop-source.bin"),
            ("component-source-closure", "component-source-closure", "component-source-closure.bin"),
            ("dependency-inventory", "dependency-inventory", "dependency-inventory.bin"),
            ("localization-claims", "localization-claim-corpus", "localization-claims.bin"),
            ("mode-claims", "target-manifest", "mode-claims.bin"),
            ("notice-mapping", "notice-mapping", "notice-mapping.bin"),
            ("package-signing", "package-signing", "package-signing.bin"),
            ("public-documents", "public-document-corpus", "public-documents.bin"),
            ("public-support-corpus", "public-support-corpus", "public-support-corpus.bin"),
            ("release-lifecycle-corpus", "release-lifecycle-corpus", "release-lifecycle-corpus.bin"),
            ("release-revalidation", "release-revalidation", "release-revalidation.bin"),
            ("runtime-observations", "runtime-observation", "runtime-observations.bin"),
            ("sample-inventory", "sample-inventory", "sample-inventory.bin"),
            ("sbom-provenance", "sbom-provenance", "sbom-provenance.bin"),
            ("style-asset-inventory", "style-asset-inventory", "style-asset-inventory.bin"),
            ("test-inventory", "test-inventory", "test-inventory.bin"),
            ("trim-aot-observations", "toolchain-probe", "trim-aot-observations.bin"),
            ("workflow-inventory", "workflow-inventory", "workflow-inventory.bin")
        };
        var typedAllowed = string.Join(
            ",",
            typedAllowedInputs.Select(item =>
                $$"""{"id":"{{item.Id}}","kind":"{{item.Kind}}","path":"{{item.Path}}"}"""));
        return $$"""
        {
          "schema_version": 2,
          "state": "draft",
          "assessment_kinds": ["component", "package"],
          "package": {
            "package_id": "{{package.Id}}",
            "version": "{{package.Version}}",
            "nupkg_input_id": "package",
            "nupkg_sha256": {"algorithm":"sha256","value":"{{package.NupkgSha256}}"}
          },
          "sources": [
            {
              "id": "release-source",
              "availability": "source-available",
              "repository_uri": "https://code.example.test/synthetic/source",
              "commit": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "archive_input_id": "source"
            }
          ],
          "coverage": [{{coverage}}],
          "allowed_inputs": [
            {"id":"browser-version","kind":"browser-identity","path":"browser.bin"},
            {"id":"package","kind":"nupkg","path":"package.bin"},
            {"id":"retrieval","kind":"retrieval-record","path":"retrieval.bin"},
            {"id":"sdk-version","kind":"toolchain-identity","path":"sdk.bin"},
            {"id":"source","kind":"source-archive","path":"source.bin"},
            {"id":"trim-log","kind":"probe-log","path":"trim.bin"},
            {{typedAllowed}}{{extraAllowedInput}}
          ],
          "toolchains": {{toolchain}},
          "browsers": [
            {"id":"browser","name":"Synthetic Browser","version":"1.0","disposition":"available","blocker":null,"input_ids":["browser-version"]}
          ],
          "retrievals": [
            {"id":"package-fetch","subject":"Exact package and public-input retrieval","locator":"https://packages.example.test/synthetic","disposition":"available","blocker":null,"toolchain_id":null,"browser_id":null,"input_ids":["localization-claims","mode-claims","public-documents","retrieval"]}
          ],
          "probes": [
            {"id":"coverage-material","subject":"Typed conclusion-free coverage material","locator":"local deterministic inventory generation","disposition":"available","blocker":null,"toolchain_id":null,"browser_id":null,"input_ids":["accessibility-observations","assembly-signing","asset-inventory","browser-interop-source","component-source-closure","dependency-inventory","notice-mapping","package-signing","public-support-corpus","release-lifecycle-corpus","release-revalidation","runtime-observations","sample-inventory","sbom-provenance","style-asset-inventory","test-inventory","trim-aot-observations","workflow-inventory"]},
            {"id":"trim-probe","subject":"Supported trim comparison","locator":"local trim command","disposition":"available","blocker":null,"toolchain_id":{{toolchainId}},"browser_id":null,"input_ids":["trim-log"{{extraProbeInput}}]}
          ]
        }
        """;
    }

    private static string[] CoverageTypedInputIds() =>
    [
        "accessibility-observations",
        "assembly-signing",
        "asset-inventory",
        "browser-interop-source",
        "component-source-closure",
        "dependency-inventory",
        "localization-claims",
        "mode-claims",
        "notice-mapping",
        "package-signing",
        "public-documents",
        "public-support-corpus",
        "release-lifecycle-corpus",
        "release-revalidation",
        "runtime-observations",
        "sample-inventory",
        "sbom-provenance",
        "style-asset-inventory",
        "test-inventory",
        "trim-aot-observations",
        "workflow-inventory"
    ];

    private static void CreatePackage(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var nuspec = archive.CreateEntry("Synthetic.Controls.nuspec", CompressionLevel.NoCompression);
        nuspec.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using (var stream = nuspec.Open())
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: false))
        {
            writer.Write(
                "<package><metadata><id>Synthetic.Controls</id><version>1.0.0</version></metadata></package>");
        }

        var dll = archive.CreateEntry(
            "lib/net11.0/Synthetic.Controls.dll",
            CompressionLevel.NoCompression);
        dll.LastWriteTime = nuspec.LastWriteTime;
        using (var dllStream = dll.Open())
        {
            dllStream.Write(Encoding.UTF8.GetBytes("synthetic managed assembly bytes"));
        }

        var readme = archive.CreateEntry("README.md", CompressionLevel.NoCompression);
        readme.LastWriteTime = nuspec.LastWriteTime;
        using var readmeStream = readme.Open();
        readmeStream.Write(Encoding.UTF8.GetBytes("# Synthetic package"));
    }

    private static void WriteFile(string root, string relativePath, string content) =>
        File.WriteAllText(
            Path.Combine(root, relativePath),
            content,
            new UTF8Encoding(false));

    private static void WriteDigest(
        Utf8JsonWriter writer,
        string property,
        Sha256Digest digest)
    {
        writer.WritePropertyName(property);
        writer.WriteStartObject();
        writer.WriteString("algorithm", digest.Algorithm);
        writer.WriteString("value", digest.Value);
        writer.WriteEndObject();
    }

    private static void ExpectValidation(Action action, string name)
    {
        try
        {
            action();
            throw new InvalidOperationException(
                $"{name}: expected deterministic validation failure.");
        }
        catch (DeterministicValidationException)
        {
        }
    }

    private static void ExpectValidationMessage(
        Action action,
        string expectedMessage,
        string name)
    {
        try
        {
            action();
            throw new InvalidOperationException(
                $"{name}: expected deterministic validation failure.");
        }
        catch (DeterministicValidationException exception)
        {
            if (!exception.Message.Contains(expectedMessage, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{name}: expected message containing '{expectedMessage}', actual '{exception.Message}'.");
            }
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{name}: expected '{expected}', actual '{actual}'.");
        }
    }

    private sealed record Fixture(
        string Root,
        string NupkgPath,
        InputManifest Manifest);

    private sealed record RowConclusion(
        string Status,
        string? Observation,
        IReadOnlyList<string> EvidenceIds,
        string? OwnerAction,
        string? AssessmentFollowUp,
        string? NotApplicableRationale);
}
