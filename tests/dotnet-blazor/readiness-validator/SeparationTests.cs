using System.Text;
using System.Text.Json.Nodes;
using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Rendering;
using BlazorComponentReadiness.Validator.Validation;

internal static class SeparationTests
{
    public static void Run(string repositoryRoot, string pluginRoot)
    {
        var root = Path.Combine(Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ??
            Path.Combine(repositoryRoot, "artifacts"), $"separate-units-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var fixture = AssessmentTests.CreateInputFixture(root);
        var input = fixture.Confirmed with { OwnerInputs = [] };
        var inputBytes = InputManifestService.Serialize(input);
        var inputPath = Path.Combine(root, "public.confirmed.json");
        File.WriteAllBytes(inputPath, inputBytes);

        var unrelated = Path.Combine(root, "unrelated", "0001");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "package.validation.json"), "not a package revision");

        var component = Complete(AssessmentService.Initialize(
            "component", root, input, inputBytes, "fancy-tree"));
        Assert(component.PackageReference is null && component.Rows.Count == 52,
            "component initialization has no package prerequisite");
        var componentUnit = WriteUnit(root, "standalone", inputPath, component);
        RenderAndVerify(root, componentUnit);
        var originalComponentFiles = Snapshot(componentUnit.Revision);
        var package = Complete(AssessmentService.Initialize("package", root, input, inputBytes, null));
        var packageUnit = WriteUnit(root, "separate-package", inputPath, package);
        RenderAndVerify(root, packageUnit);
        AssertUnchanged(originalComponentFiles);
        Assert(component.Rows.All(row => row.Scope == "component-specific") &&
            package.Rows.All(row => row.Scope == "repository-wide") && package.Rows.Count == 60,
            "dual-unit output has separate exact ownership inventories");
        Assert(!Directory.EnumerateFiles(root, "unified.*", SearchOption.AllDirectories).Any(),
            "no unified artifact is produced");

        TestBindings(root, input, inputBytes, inputPath, componentUnit, packageUnit);
        TestRejectedContracts(root, componentUnit, packageUnit);
        TestContractEntrypoints(root, componentUnit, packageUnit);
        TestInputEntrypoints(fixture);
        TestFeedbackHistory(root, inputPath, component);
        TestStaticSsr(root, input);
        TestStaticSsrOutcomeSets(root, input);
        Console.WriteLine($"Separate assessment contracts passed. Artifacts: {root}");
    }

    private static void TestBindings(
        string root, InputManifest input, byte[] inputBytes, string inputPath,
        Unit standalone, Unit packageUnit)
    {
        var binding = RevisionService.LoadPackageBinding(root, packageUnit.Revision, null);
        var bound = Complete(AssessmentService.Initialize(
            "component", root, input, inputBytes, "fancy-tree", binding));
        var unit = WriteUnit(root, "bound", inputPath, bound);
        RenderAndVerify(root, unit, packageUnit.Revision);
        foreach (var packagePath in new string?[] { null, packageUnit.Revision })
        {
            var initializedPath = Path.Combine(root, $"binding-init-{Guid.NewGuid():N}.json");
            string[] bindingOption = packagePath is null ? [] : ["--package-revision", packagePath];
            Success(["assessment", "init", "--kind", "component", "--component", "fancy-tree",
                "--root", root, "--input", inputPath, "--output", initializedPath, .. bindingOption]);
            Assert(AssessmentService.Parse(File.ReadAllBytes(initializedPath)).PackageReference ==
                (packagePath is null ? null : binding.Reference), "CLI initialization declares only the requested binding");
        }

        var package2 = packageUnit.Assessment with
        {
            Rows = packageUnit.Assessment.Rows.Select((row, index) =>
                index == 0 ? row with { Observation = "A different retained observation in the successor." } : row).ToArray()
        };
        var replacement = Path.Combine(root, "package-successor.json");
        File.WriteAllBytes(replacement, AssessmentService.Serialize(package2));
        var predecessorDigest = ContractJson.RawDigest(File.ReadAllBytes(
            Path.Combine(packageUnit.Revision, "package.validation.json"))).Value;
        Success("assessment", "revise", "--root", root, "--input", inputPath,
            "--assessment", replacement, "--evidence", packageUnit.EvidencePath,
            "--output", packageUnit.Revisions, "--predecessor", predecessorDigest,
            "--changed-ids", "LP-01");
        var newerRevision = Path.Combine(packageUnit.Revisions, "0002");
        Success("report", "verify", "--root", root, "--revision", unit.Revision,
            "--package-revision", packageUnit.Revision);
        Failure("report", "verify", "--root", root, "--revision", unit.Revision,
            "--package-revision", newerRevision);

        var alteredBinding = binding with
        {
            Reference = binding.Reference with
            {
                Package = binding.Reference.Package with { Version = "9.9.9" }
            }
        };
        Reject(() => AssessmentService.Validate(root, bound, AssessmentService.Serialize(bound),
            input, inputBytes, AssessmentTests.BuildEvidence(bound.Identity), alteredBinding),
            "wrong declared package identity");
        Reject(() => AssessmentService.Validate(root, standalone.Assessment,
            AssessmentService.Serialize(standalone.Assessment), input, inputBytes,
            AssessmentTests.BuildEvidence(standalone.Assessment.Identity), binding),
            "verification cannot silently attach an undeclared binding");
        foreach (var invalidBinding in new[]
        {
            binding with { Assessment = binding.Assessment with { SchemaVersion = 1 } },
            binding with { Manifest = binding.Manifest with { SchemaVersion = 999 } },
            binding with { Manifest = binding.Manifest with { RubricVersion = "2.0.1" } },
            binding with { Manifest = binding.Manifest with { AssessmentKind = "unified" } },
            binding with { Manifest = binding.Manifest with { ReportDigest = new("sha256", new string('0', 64)) } },
            binding with { Reference = binding.Reference with { AssessmentDigest = null } }
        })
        {
            Reject(() => AssessmentService.Initialize("component", root, input, inputBytes, "fancy-tree", invalidBinding),
                "direct initialization rejects unsupported or incomplete binding metadata");
            Reject(() => AssessmentService.Validate(root, bound, AssessmentService.Serialize(bound), input, inputBytes,
                AssessmentTests.BuildEvidence(bound.Identity), invalidBinding), "direct validation rejects binding metadata drift");
        }

        var tampered = Path.Combine(root, "tampered-package", "0001");
        Directory.CreateDirectory(tampered);
        foreach (var path in Directory.GetFiles(packageUnit.Revision))
        {
            File.Copy(path, Path.Combine(tampered, Path.GetFileName(path)));
        }
        File.AppendAllText(Path.Combine(tampered, "package.report.md"), "\nchanged");

        var noncurrent = Path.Combine(root, "noncurrent-package", "0001");
        Directory.CreateDirectory(noncurrent);
        foreach (var path in Directory.GetFiles(packageUnit.Revision))
        {
            File.Copy(path, Path.Combine(noncurrent, Path.GetFileName(path)));
        }
        var noncurrentPath = Path.Combine(noncurrent, "package.assessment.json");
        File.WriteAllText(noncurrentPath, File.ReadAllText(noncurrentPath)
            .Replace("\"rubric_version\":\"2.1.0\"", "\"rubric_version\":\"2.0.1\"", StringComparison.Ordinal));

        var otherSourceInput = input with { Source = input.Source with { Commit = new string('c', 40) } };
        var otherInputPath = Path.Combine(root, "other-source-input.json");
        var otherInputBytes = InputManifestService.Serialize(otherSourceInput);
        File.WriteAllBytes(otherInputPath, otherInputBytes);
        var otherSourceUnit = WriteUnit(root, "other-source-package", otherInputPath,
            Complete(AssessmentService.Initialize("package", root, otherSourceInput, otherInputBytes, null)));
        RenderAndVerify(root, otherSourceUnit);

        var partial = AssessmentService.Initialize("package", root, input, inputBytes, null);
        var partialEvidence = AssessmentTests.BuildEvidence(partial.Identity);
        var partialIds = partialEvidence.Selection.Select(item => item.EvidenceId).ToArray();
        partial = partial with
        {
            Rows = [partial.Rows[0] with
            {
                Status = "not tested",
                EvidenceIds = partialIds,
                AssessmentFollowUp = "Synthetic package work remains unfinished."
            }, .. partial.Rows.Skip(1)],
            SummaryGroups = [new("not tested", "One row is accounted for; the rest remain incomplete.",
                [partial.Rows[0].Id], partialIds)]
        };
        var partialUnit = WriteUnit(root, "incomplete-package", inputPath, partial);
        Success("report", "render", "--root", root, "--input", inputPath,
            "--assessment", partialUnit.AssessmentPath, "--evidence", partialUnit.EvidencePath, "--output", partialUnit.Revisions);
        Success("report", "verify", "--root", root, "--revision", partialUnit.Revision);

        var before = Snapshot(unit.Revision);
        foreach (var badBinding in new string?[]
        {
            null, Path.Combine(root, "missing"), newerRevision, tampered, standalone.Revision,
            noncurrent, otherSourceUnit.Revision, partialUnit.Revision
        })
        {
            string[] option = badBinding is null ? [] : ["--package-revision", badBinding];
            var suffix = Guid.NewGuid().ToString("N");
            foreach (var command in new[]
            {
                new[] { "assessment", "validate", "--root", root, "--input", inputPath,
                    "--assessment", unit.AssessmentPath, "--evidence", unit.EvidencePath },
                new[] { "report", "render", "--root", root, "--input", inputPath,
                    "--assessment", unit.AssessmentPath, "--evidence", unit.EvidencePath,
                    "--output", Path.Combine(root, $"rejected-report-{suffix}") },
                new[] { "report", "verify", "--root", root, "--revision", unit.Revision },
                new[] { "reader", "render", "--root", root, "--revision", unit.Revision,
                    "--output", Path.Combine(root, $"rejected-reader-{suffix}") },
                new[] { "reader", "verify", "--root", root, "--revision", unit.Revision,
                    "--output", unit.Reader }
            })
            {
                Failure(badBinding == Path.Combine(root, "missing")
                    ? ExitCodes.EnvironmentFailure : ExitCodes.ValidationFailure, [.. command, .. option]);
            }
            Assert(!Directory.Exists(Path.Combine(root, $"rejected-report-{suffix}")) &&
                !Directory.Exists(Path.Combine(root, $"rejected-reader-{suffix}")),
                "bad binding publishes neither revision nor reader");
            if (badBinding is not null && badBinding != newerRevision)
            {
                var output = Path.Combine(root, $"rejected-init-{suffix}.json");
                Failure(badBinding == Path.Combine(root, "missing") ? ExitCodes.EnvironmentFailure : ExitCodes.ValidationFailure,
                    "assessment", "init", "--kind", "component", "--component", "fancy-tree", "--root", root,
                    "--input", inputPath, "--output", output, "--package-revision", badBinding);
                Assert(!File.Exists(output), "invalid explicit binding cannot initialize a component");
            }
        }
        AssertUnchanged(before);
        Success("report", "verify", "--root", root, "--revision", standalone.Revision);
        Failure("report", "verify", "--root", root, "--revision", standalone.Revision,
            "--package-revision", packageUnit.Revision);
        Failure("reader", "verify", "--root", root, "--revision", standalone.Revision,
            "--output", standalone.Reader, "--package-revision", packageUnit.Revision);
    }

    private static void TestRejectedContracts(string root, Unit component, Unit package)
    {
        foreach (var source in new[] { component, package })
        {
            foreach (var mutation in new[] { "unified", "old-rubric", "wrong-digest" })
            {
                var node = JsonNode.Parse(File.ReadAllBytes(source.AssessmentPath))!.AsObject();
                if (mutation == "unified")
                {
                    node["assessment_kind"] = "unified";
                    node["identity"]!["assessment_kind"] = "unified";
                }
                else if (mutation == "old-rubric")
                {
                    node["rubric_version"] = "2.0.1";
                }
                else
                {
                    node["rubric_sha256"]!["value"] = new string('0', 64);
                }

                var path = Path.Combine(root, $"{source.Assessment.AssessmentKind}-{mutation}.json");
                File.WriteAllText(path, node.ToJsonString());
                foreach (var operation in new[] { "canonicalize", "export-identity" })
                {
                    var output = path + "." + operation;
                    Failure("assessment", operation, "--assessment", path, "--output", output);
                    Assert(!File.Exists(output), "unsupported metadata cannot be projected into a new artifact");
                }
                Failure("assessment", "validate", "--root", root, "--input", source.InputPath,
                    "--assessment", path, "--evidence", source.EvidencePath);
                var rejectedRoot = path + ".revisions";
                Failure("report", "render", "--root", root, "--input", source.InputPath,
                    "--assessment", path, "--evidence", source.EvidencePath, "--output", rejectedRoot);
                Assert(!Directory.Exists(rejectedRoot), "unsupported metadata publishes no revision");
            }
        }

        var unifiedOutput = Path.Combine(root, "retired-init.json");
        Failure("assessment", "init", "--kind", "unified", "--root", root,
            "--input", component.InputPath, "--component", "fancy-tree", "--output", unifiedOutput);
        Assert(!File.Exists(unifiedOutput), "unified init publishes nothing");
        Reject(() => EvidenceIdentity.ValidateAssessment(
            component.Assessment.Identity with { AssessmentKind = "unified" }), "unified evidence identity");

        var artifacts = RevisionService.VerifyRevision(root, component.Revision, null, null, true);
        Reject(() => ReaderService.Build(root, artifacts with { Kind = "unified" }, null, null),
            "direct reader cannot accept retired revision kind");
        Reject(() => ReportService.RenderMarkdown(
            component.Assessment with { RubricVersion = "2.0.1" }, artifacts.Input, artifacts.Evidence),
            "direct report cannot accept an old rubric");
        Reject(() => ReportService.ValidateManifest(
            artifacts.Manifest with { AssessmentKind = "unified" },
            artifacts.Manifest with { AssessmentKind = "unified" }),
            "matching retired manifests are not valid");
    }

    private static void TestStaticSsr(string root, InputManifest initial)
    {
        var source = Complete(AssessmentService.Initialize("component", root, initial,
            InputManifestService.Serialize(initial), "fancy-tree"));
        var docs = AssessmentTests.BuildEvidence(source.Identity);
        Reject(() => Validate(root, initial, source with
        {
            Rows = source.Rows.Select(row => row.Id == "BEQ-05" ? row with
            {
                Status = "not applicable",
                AssessmentFollowUp = null,
                NotApplicableRationale = "A claimed mode cannot be waived because it was not exercised."
            } : row).ToArray()
        }, docs), "claimed but unperformed static SSR is not inapplicable");
        var documented = source with
        {
            Rows = source.Rows.Select(row => row.Id == "BEQ-05" ? row with
            {
                Status = "verified",
                Observation = "Documentation claims static SSR.",
                EvidenceIds = docs.Selection.Select(item => item.EvidenceId).ToArray(),
                AssessmentFollowUp = null
            } : row).ToArray()
        };
        Reject(() => Validate(root, initial, documented, docs), "documentation is not static runtime proof");
        Reject(() => Validate(root, initial, documented with
        {
            Rows = documented.Rows.Select(row => row.Id == "BEQ-05" ? row with { Status = "gap" } : row).ToArray()
        }, docs), "documentation absence is not a static behavior failure");

        var unsupportedInput = initial with
        {
            Components = initial.Components.Select(item => item with
            {
                RenderModes = item.RenderModes.Where(mode => mode != "static-ssr").ToArray()
            }).ToArray()
        };
        var unsupported = Complete(AssessmentService.Initialize("component", root, unsupportedInput,
            InputManifestService.Serialize(unsupportedInput), "fancy-tree"));
        unsupported = unsupported with
        {
            Rows = unsupported.Rows.Select(row => row.Id == "BEQ-05" ? row with
            {
                Status = "not applicable",
                AssessmentFollowUp = null,
                NotApplicableRationale = "The confirmed support claim explicitly includes only interactive modes."
            } : row).ToArray()
        };
        Validate(root, unsupportedInput, unsupported, AssessmentTests.BuildEvidence(unsupported.Identity));

        foreach (var result in new[] { "passed", "failed" })
        {
            var capture = CreateStaticSsrCapture(root, $"static-{result}", result);
            var rawName = capture.Raw.Basename;
            var raw = File.ReadAllBytes(Path.Combine(root, rawName));
            var protocolName = capture.Protocol.Basename;
            var protocol = File.ReadAllBytes(Path.Combine(root, protocolName));
            var input = initial with
            {
                EvidenceInputs = initial.EvidenceInputs.Concat([capture.Raw, capture.Protocol])
                    .OrderBy(item => item.Basename, StringComparer.Ordinal).ToArray()
            };
            var assessment = Complete(AssessmentService.Initialize("component", root, input,
                InputManifestService.Serialize(input), "fancy-tree"));
            var evidence = AssessmentTests.BuildEvidence(assessment.Identity);
            var runtime = new EvidenceRecordDraft("The retained static-SSR observation records its bounded outcome.",
                new("component-specific", "fancy-tree"),
                new(EvidenceIdentity.ReproducedRuntimeObservation, protocolName,
                    EvidenceProtocolValidator.StaticSsrMethod, "2026-09-21T12:00:00Z",
                    ContractJson.RawDigest(protocol), "commitment-only"), []);
            var runtimeLedger = EvidenceLedgerBuilder.BuildComponentLedger(assessment.Identity, [runtime]);
            evidence = EvidenceLedgerBuilder.BuildBundle(assessment.Identity,
                [.. evidence.SourceLedgers.Select(item => item.Ledger), runtimeLedger],
                [.. evidence.Selection.Select(item => item.EvidenceId), runtimeLedger.Records.Single().StableId]);
            assessment = assessment with
            {
                Rows = assessment.Rows.Select(row => row.Id == "BEQ-05" ? row with
                {
                    Status = result == "passed" ? "verified" : "gap",
                    Observation = "The supported-context observation establishes this bounded outcome.",
                    EvidenceIds = [runtimeLedger.Records.Single().StableId],
                    AssessmentFollowUp = null
                } : row).ToArray()
            };
            Validate(root, input, assessment, evidence);
            if (result == "passed")
            {
                foreach (var field in new[]
                {
                    "component_id", "mode", "observed_identity", "result", "schema_version",
                    "expected_behavior", "observed_behavior", "protocol-component", "protocol-result",
                    "protocol-digest", "duplicate-capture"
                })
                {
                    var rawNode = JsonNode.Parse(raw)!;
                    switch (field)
                    {
                        case "component_id": rawNode[field] = "another-component"; break;
                        case "mode": rawNode[field] = "interactive-server"; break;
                        case "observed_identity": rawNode[field] = "server"; break;
                        case "result": rawNode[field] = "failed"; break;
                        case "schema_version": rawNode[field] = 999; break;
                        case "expected_behavior" or "observed_behavior": rawNode[field] = ""; break;
                    }
                    var rawVariant = Encoding.UTF8.GetBytes(rawNode.ToJsonString());
                    var rawPath = "variant-" + field + ".raw.json";
                    var protocolPath = "variant-" + field + ".protocol.json";
                    File.WriteAllBytes(Path.Combine(root, rawPath), rawVariant);
                    var protocolNode = JsonNode.Parse(protocol)!;
                    protocolNode["raw_observation_sha256"]!["value"] = ContractJson.RawDigest(rawVariant).Value;
                    if (field == "protocol-component")
                        protocolNode["component_id"] = "another-component";
                    if (field == "protocol-result")
                        protocolNode["result"] = "failed";
                    if (field == "protocol-digest")
                        protocolNode["raw_observation_sha256"]!["value"] = new string('0', 64);
                    var protocolVariant = Encoding.UTF8.GetBytes(protocolNode.ToJsonString());
                    File.WriteAllBytes(Path.Combine(root, protocolPath), protocolVariant);
                    var registrations = new List<InputEvidenceArtifact>
                    {
                        new(rawPath, "raw-observation", ContractJson.RawDigest(rawVariant), rawVariant.LongLength),
                        new(protocolPath, "structured-protocol", ContractJson.RawDigest(protocolVariant), protocolVariant.LongLength)
                    };
                    if (field == "duplicate-capture")
                    {
                        var duplicatePath = "duplicate-static.raw.json";
                        File.WriteAllBytes(Path.Combine(root, duplicatePath), rawVariant);
                        registrations.Add(new(duplicatePath, "raw-observation",
                            ContractJson.RawDigest(rawVariant), rawVariant.LongLength));
                    }
                    var variantInput = initial with
                    {
                        EvidenceInputs = initial.EvidenceInputs.Concat(registrations)
                            .OrderBy(item => item.Basename, StringComparer.Ordinal).ToArray()
                    };
                    var variantAssessment = Complete(AssessmentService.Initialize("component", root, variantInput,
                        InputManifestService.Serialize(variantInput), "fancy-tree"));
                    var variantEvidence = AssessmentTests.BuildEvidence(variantAssessment.Identity);
                    var variantLedger = EvidenceLedgerBuilder.BuildComponentLedger(variantAssessment.Identity,
                        [runtime with { Provenance = runtime.Provenance with
                        {
                            Locator = protocolPath, ContentDigest = ContractJson.RawDigest(protocolVariant)
                        } }]);
                    variantEvidence = EvidenceLedgerBuilder.BuildBundle(variantAssessment.Identity,
                        [.. variantEvidence.SourceLedgers.Select(item => item.Ledger), variantLedger],
                        [.. variantEvidence.Selection.Select(item => item.EvidenceId), variantLedger.Records.Single().StableId]);
                    variantAssessment = variantAssessment with
                    {
                        Rows = variantAssessment.Rows.Select(row => row.Id == "BEQ-05" ? row with
                        {
                            Status = "verified", Observation = "Synthetic wrong observation with correct byte bindings.",
                            EvidenceIds = [variantLedger.Records.Single().StableId], AssessmentFollowUp = null
                        } : row).ToArray()
                    };
                    Reject(() => Validate(root, variantInput, variantAssessment, variantEvidence),
                        "static-SSR semantic rejection despite correct digests: " + field);
                }
            }
            Reject(() => Validate(root, input, assessment with
            {
                Rows = assessment.Rows.Select(row => row.Id == "BEQ-05" ? row with
                {
                    Status = result == "passed" ? "gap" : "verified"
                } : row).ToArray()
            }, evidence), "static outcome must agree with the selected observation");
            File.WriteAllBytes(Path.Combine(root, rawName), Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(raw).Replace("\"mode\":\"static-ssr\"", "\"mode\":\"interactive-server\"")));
            Reject(() => Validate(root, input, assessment, evidence), "tampered static capture");
            File.WriteAllBytes(Path.Combine(root, rawName), raw);
        }
    }

    private static (InputEvidenceArtifact Raw, InputEvidenceArtifact Protocol) CreateStaticSsrCapture(
        string root, string name, string result)
    {
        var raw = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("observation", "static-ssr-behavior");
            writer.WriteString("component_id", "fancy-tree");
            writer.WriteString("mode", "static-ssr");
            writer.WriteString("observed_identity", "static");
            writer.WriteString("expected_behavior", "The claimed static output contains the supplied content.");
            writer.WriteString("observed_behavior", $"Synthetic retained observation {name}; no runtime execution.");
            writer.WriteString("result", result);
            writer.WriteEndObject();
        });
        var protocol = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("protocol", "static-ssr-behavior");
            writer.WriteString("component_id", "fancy-tree");
            writer.WriteString("result", result);
            ContractJson.WriteDigest(writer, "raw_observation_sha256", ContractJson.RawDigest(raw));
            writer.WriteEndObject();
        });
        var rawName = name + ".raw.json";
        var protocolName = name + ".protocol.json";
        File.WriteAllBytes(Path.Combine(root, rawName), raw);
        File.WriteAllBytes(Path.Combine(root, protocolName), protocol);

        return (new(rawName, "raw-observation", ContractJson.RawDigest(raw), raw.LongLength),
            new(protocolName, "static-ssr-protocol", ContractJson.RawDigest(protocol), protocol.LongLength));
    }

    private static void TestStaticSsrOutcomeSets(string root, InputManifest initial)
    {
        var captures = new[]
        {
            CreateStaticSsrCapture(root, "outcome-pass-one", "passed"),
            CreateStaticSsrCapture(root, "outcome-pass-two", "passed"),
            CreateStaticSsrCapture(root, "outcome-failure", "failed"),
            CreateStaticSsrCapture(root, "outcome-corrected-pass", "passed")
        };
        var input = initial with
        {
            EvidenceInputs = initial.EvidenceInputs.Concat(captures.SelectMany(capture =>
                new[] { capture.Raw, capture.Protocol })).OrderBy(item => item.Basename, StringComparer.Ordinal).ToArray()
        };
        var initialized = AssessmentService.Initialize("component", root, input,
            InputManifestService.Serialize(input), "fancy-tree");
        var drafts = captures.Select(capture => new EvidenceRecordDraft(
            "Synthetic static-SSR outcome-set control, not an executed product observation.",
            new("component-specific", "fancy-tree"),
            new(EvidenceIdentity.ReproducedRuntimeObservation, capture.Protocol.Basename,
                EvidenceProtocolValidator.StaticSsrMethod, "2026-09-22T12:00:00Z",
                capture.Protocol.ContentDigest, "commitment-only"), [])).ToArray();
        var ledger = EvidenceLedgerBuilder.BuildComponentLedger(initialized.Identity, drafts.Take(3));
        var records = ledger.Records.ToDictionary(record => record.Provenance.Locator, StringComparer.Ordinal);

        foreach (var (name, status, selected, error) in new (string, string, int[], string?)[]
        {
            ("multiple passes", "verified", [0, 1], null),
            ("mixed verification", "verified", [0, 2], "cannot be verified while citing a failed"),
            ("mixed gap", "gap", [0, 2], null),
            ("unselected retained failure", "verified", [0], null),
            ("passes cannot establish gap", "gap", [0, 1], "requires the matching digest-bound"),
            ("failure cannot verify", "verified", [2], "requires the matching digest-bound")
        })
        {
            Check(ledger, selected.Select(index => records[captures[index].Protocol.Basename].StableId).ToArray(),
                status, error, name);
        }

        var failureId = records[captures[2].Protocol.Basename].StableId;
        var correction = drafts[3] with { Supersedes = [failureId] };
        var history = EvidenceLedgerBuilder.BuildComponentLedger(initialized.Identity, [.. drafts.Take(3), correction]);
        var correctionId = history.Records.Single(record =>
            record.Provenance.Locator == captures[3].Protocol.Basename).StableId;
        Check(history, [correctionId], "verified", null, "superseded unselected failure does not veto its successor");
        Reject(() => EvidenceLedgerBuilder.BuildBundle(initialized.Identity, [history], [failureId, correctionId]),
            "superseded failure and its successor cannot both be selected", "superseded ancestor");

        void Check(EvidenceSourceLedger source, string[] ids, string status, string? error, string name)
        {
            var evidence = EvidenceLedgerBuilder.BuildBundle(initialized.Identity, [source], ids);
            var assessment = initialized with
            {
                CompletionState = "complete",
                Rows = initialized.Rows.Select(row => row.Id == "BEQ-05"
                    ? row with
                    {
                        Status = status,
                        Observation = "The cited synthetic observations cover the same supported static condition.",
                        EvidenceIds = ids.Order(StringComparer.Ordinal).ToArray()
                    }
                    : row with
                    {
                        Status = "not tested",
                        AssessmentFollowUp = "No product operation was performed in this contract fixture."
                    }).ToArray()
            };
            if (error is null)
            {
                Validate(root, input, assessment, evidence);
            }
            else
            {
                Reject(() => Validate(root, input, assessment, evidence), name, error);
            }
        }
    }

    private static void TestContractEntrypoints(string root, params Unit[] units)
    {
        foreach (var unit in units)
        {
            var revision = RevisionService.VerifyRevision(root, unit.Revision, null, null, true);
            var original = Snapshot(unit.Revision);
            var existingRevisions = Directory.GetDirectories(unit.Revisions).Order(StringComparer.Ordinal).ToArray();
            var nextRevision = Path.Combine(unit.Revisions, (existingRevisions.Length + 1).ToString("D4"));
            var latestDigest = ContractJson.RawDigest(File.ReadAllBytes(Path.Combine(existingRevisions[^1],
                unit.Assessment.AssessmentKind + ".validation.json"))).Value;
            var correctionEvidence = AssessmentTests.BuildEvidence(unit.Assessment.Identity);
            var record = correctionEvidence.SourceLedgers.Single().Ledger.Records.Single();
            var draft = new EvidenceRecordDraft("A new bounded synthetic correction observation.",
                record.Applicability, record.Provenance with { CapturedAtUtc = "2026-09-22T12:00:00Z" }, []);
            var ledger = unit.Assessment.AssessmentKind == "component"
                ? EvidenceLedgerBuilder.BuildComponentLedger(unit.Assessment.Identity, [draft])
                : EvidenceLedgerBuilder.BuildRepositoryLedger(
                    new("package", unit.Assessment.Identity.Package, unit.Assessment.Identity.InputManifestDigest, null), [draft]);
            correctionEvidence = EvidenceLedgerBuilder.BuildBundle(unit.Assessment.Identity,
                [.. correctionEvidence.SourceLedgers.Select(item => item.Ledger), ledger],
                [.. correctionEvidence.Selection.Select(item => item.EvidenceId), ledger.Records.Single().StableId]);
            var rows = unit.Assessment.Rows.Select((row, index) => index == 0 ? row with
            {
                Observation = "A newly retained synthetic observation.",
                EvidenceIds = correctionEvidence.Selection.Select(item => item.EvidenceId).Order(StringComparer.Ordinal).ToArray()
            } : row).ToArray();
            var correction = unit.Assessment with
            {
                Rows = rows,
                SummaryGroups = unit.Assessment.AssessmentKind == "package"
                    ? [new("not tested", "Synthetic correction remains unperformed work.",
                        rows.Select(row => row.Id).ToArray(), rows[0].EvidenceIds)]
                    : []
            };
            var correctionPath = Path.Combine(root, unit.Assessment.AssessmentKind + "-matrix-correction.json");
            var correctionEvidencePath = correctionPath + ".evidence";
            File.WriteAllBytes(correctionPath, AssessmentService.Serialize(correction));
            File.WriteAllBytes(correctionEvidencePath, CanonicalEvidenceJson.SerializeBundle(correctionEvidence));
            AssessmentService.Validate(root, correction, AssessmentService.Serialize(correction),
                revision.Input, revision.InputBytes, correctionEvidence);

            (string Name, Action<JsonObject> Change)[] mutations =
            [
                ("outer-kind", value => value["assessment_kind"] = "unified"),
                ("embedded-kind", value => value["identity"]!["assessment_kind"] = "unified"),
                ("kind-mismatch", value => value["assessment_kind"] =
                    unit.Assessment.AssessmentKind == "package" ? "component" : "package"),
                ("old-schema", value => value["schema_version"] = 1),
                ("unknown-schema", value => value["schema_version"] = 999),
                ("old-rubric", value => value["rubric_version"] = "2.0.1"),
                ("unknown-rubric", value => value["rubric_version"] = "999.0.0"),
                ("old-scope", value => value["scope_schema_version"] = 1),
                ("rubric-digest", value => value["rubric_sha256"]!["value"] = new string('0', 64)),
                ("scope-digest", value => value["scope_map_sha256"]!["value"] = new string('0', 64))
            ];
            foreach (var (name, change) in mutations)
            {
                var node = JsonNode.Parse(revision.AssessmentBytes)!.AsObject();
                change(node);
                var bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
                var path = Path.Combine(root, unit.Assessment.AssessmentKind + "-entry-" + name + ".json");
                File.WriteAllBytes(path, bytes);
                Reject(() => AssessmentService.Parse(bytes), "direct assessment intake " + name);
                foreach (var operation in new[] { "canonicalize", "export-identity" })
                {
                    Failure("assessment", operation, "--assessment", path, "--output", path + "." + operation);
                    Assert(!File.Exists(path + "." + operation), "unsupported assessment cannot be laundered");
                }
                Failure("assessment", "validate", "--root", root, "--input", unit.InputPath,
                    "--assessment", path, "--evidence", unit.EvidencePath);
                Failure("report", "render", "--root", root, "--input", unit.InputPath,
                    "--assessment", path, "--evidence", unit.EvidencePath, "--output", path + ".revisions");
                Assert(!Directory.Exists(path + ".revisions"), "unsupported metadata publishes no initial report");
                var invalidCorrection = JsonNode.Parse(AssessmentService.Serialize(correction))!.AsObject();
                change(invalidCorrection);
                var invalidCorrectionPath = path + ".correction";
                File.WriteAllText(invalidCorrectionPath, invalidCorrection.ToJsonString());
                Failure("assessment", "revise", "--root", root, "--input", unit.InputPath,
                    "--assessment", invalidCorrectionPath, "--evidence", correctionEvidencePath, "--output", unit.Revisions,
                    "--predecessor", latestDigest,
                    "--changed-ids", rows[0].Id);
                Assert(!Directory.Exists(nextRevision), "unsupported replacement publishes no successor");

                var copiedRoot = Path.Combine(root, unit.Assessment.AssessmentKind + "-revision-" + name);
                var copied = Path.Combine(copiedRoot, "0001");
                Directory.CreateDirectory(copied);
                foreach (var (sourcePath, sourceBytes) in original)
                {
                    File.WriteAllBytes(Path.Combine(copied, Path.GetFileName(sourcePath)), sourceBytes);
                }
                File.WriteAllBytes(Path.Combine(copied, unit.Assessment.AssessmentKind + ".assessment.json"), bytes);
                var before = Snapshot(copied);
                Failure("report", "verify", "--root", root, "--revision", copied);
                Failure("reader", "render", "--root", root, "--revision", copied, "--output", copiedRoot + "-reader");
                Failure("reader", "verify", "--root", root, "--revision", copied, "--output", unit.Reader);
                Failure("assessment", "revise", "--root", root, "--input", unit.InputPath,
                    "--assessment", correctionPath, "--evidence", correctionEvidencePath, "--output", copiedRoot,
                    "--predecessor", ContractJson.RawDigest(revision.ManifestBytes).Value, "--changed-ids", rows[0].Id);
                Assert(!Directory.Exists(copiedRoot + "-reader") && !Directory.Exists(Path.Combine(copiedRoot, "0002")),
                    "unsupported predecessor publishes neither reader nor successor");
                AssertUnchanged(before);
            }

            foreach (var invalid in new[]
            {
                unit.Assessment with { SchemaVersion = 1 },
                unit.Assessment with { AssessmentKind = "unified" },
                unit.Assessment with { Identity = unit.Assessment.Identity with { AssessmentKind = "unified" } },
                unit.Assessment with { RubricVersion = "2.0.1" },
                unit.Assessment with { ScopeSchemaVersion = 1 },
                unit.Assessment with { RubricDigest = new("sha256", new string('0', 64)) }
            })
            {
                Reject(() => AssessmentService.Validate(root, invalid, revision.AssessmentBytes,
                    revision.Input, revision.InputBytes, revision.Evidence), "direct assessment metadata");
                Reject(() => ReportService.RenderMarkdown(invalid, revision.Input, revision.Evidence), "direct report metadata");
                Reject(() => ReportService.CreateManifest(invalid, revision.AssessmentBytes,
                    revision.Input, revision.InputBytes, revision.Evidence, revision.EvidenceBytes, revision.ReportBytes),
                    "direct manifest producer metadata");
                Reject(() => ReaderService.Build(root, revision with { Assessment = invalid }, null, null),
                    "direct reader metadata");
            }

            foreach (var (filename, oldText, replacement) in new[]
            {
                (unit.Assessment.AssessmentKind + ".validation.json", "\"rubric_version\":\"2.1.0\"", "\"rubric_version\":\"2.0.1\""),
                (unit.Assessment.AssessmentKind + ".validation.json", "\"schema_version\":1", "\"schema_version\":999"),
                (unit.Assessment.AssessmentKind + ".validation.json",
                    $"\"assessment_kind\":\"{unit.Assessment.AssessmentKind}\"", "\"assessment_kind\":\"unified\""),
                ("input-manifest.json", "\"schema_version\":2", "\"schema_version\":1")
            })
            {
                var directory = Path.Combine(root, "rejected-artifact-" + Guid.NewGuid().ToString("N"), "0001");
                Directory.CreateDirectory(directory);
                foreach (var (sourcePath, bytes) in original)
                {
                    File.WriteAllBytes(Path.Combine(directory, Path.GetFileName(sourcePath)), bytes);
                }
                var path = Path.Combine(directory, filename);
                var text = File.ReadAllText(path);
                Assert(text.Contains(oldText, StringComparison.Ordinal), "metadata control mutates an existing field");
                File.WriteAllText(path, text.Replace(oldText, replacement, StringComparison.Ordinal));
                var snapshot = Snapshot(directory);
                Failure("report", "verify", "--root", root, "--revision", directory);
                Failure("reader", "render", "--root", root, "--revision", directory, "--output", directory + "-reader");
                Failure("reader", "verify", "--root", root, "--revision", directory, "--output", unit.Reader);
                Assert(!Directory.Exists(directory + "-reader"), "unsupported artifact metadata publishes no reader");
                AssertUnchanged(snapshot);
            }
            var retiredFilename = Path.Combine(root, "retired-filename-" + Guid.NewGuid().ToString("N"), "0001");
            Directory.CreateDirectory(retiredFilename);
            foreach (var (sourcePath, bytes) in original)
            {
                var name = Path.GetFileName(sourcePath).Replace(unit.Assessment.AssessmentKind + ".", "unified.", StringComparison.Ordinal);
                File.WriteAllBytes(Path.Combine(retiredFilename, name), bytes);
            }
            Reject(() => RevisionService.DetectKind(root, retiredFilename), "detected unified filename kind is unsupported");
            Failure("report", "verify", "--root", root, "--revision", retiredFilename);
            Failure("reader", "render", "--root", root, "--revision", retiredFilename, "--output", retiredFilename + "-reader");
            Assert(!Directory.Exists(retiredFilename + "-reader"), "unsupported filename kind has no reader fallback");
            foreach (var invalid in new[]
            {
                revision.Evidence with { SchemaVersion = 999 },
                revision.Evidence with { Assessment = revision.Evidence.Assessment with { AssessmentKind = "unified" } }
            })
            {
                Reject(() => ReportService.RenderMarkdown(unit.Assessment, revision.Input, invalid), "direct report evidence metadata");
                Reject(() => ReportService.CreateManifest(unit.Assessment, revision.AssessmentBytes,
                    revision.Input, revision.InputBytes, invalid, revision.EvidenceBytes, revision.ReportBytes),
                    "direct receipt evidence metadata");
                Reject(() => AssessmentService.Validate(root, unit.Assessment, revision.AssessmentBytes,
                    revision.Input, revision.InputBytes, invalid), "direct assessment evidence metadata");
            }

            var identityPath = Path.Combine(root, unit.Assessment.AssessmentKind + "-evidence-identity.json");
            File.WriteAllBytes(identityPath, CanonicalEvidenceJson.SerializeAssessment(unit.Assessment.Identity));
            var sourceLedger = revision.Evidence.SourceLedgers.Single().Ledger;
            var ledgerPath = identityPath + ".ledger";
            var draftPath = identityPath + ".draft";
            File.WriteAllBytes(ledgerPath, CanonicalEvidenceJson.SerializeSourceLedger(sourceLedger));
            File.WriteAllBytes(draftPath, CanonicalEvidenceJson.SerializeDraftDocument(new(1, [draft])));
            Success("evidence", "ledger-validate", "--ledger", ledgerPath);
            foreach (var mutation in new[] { "kind", "schema" })
            {
                var rawLedger = Encoding.UTF8.GetString(CanonicalEvidenceJson.SerializeSourceLedger(sourceLedger));
                rawLedger = mutation == "kind"
                    ? rawLedger.Replace($"\"assessment_kind\":\"{unit.Assessment.AssessmentKind}\"",
                        "\"assessment_kind\":\"unified\"", StringComparison.Ordinal)
                    : rawLedger.Replace("\"schema_version\":1", "\"schema_version\":999", StringComparison.Ordinal);
                var badLedger = ledgerPath + "." + mutation;
                File.WriteAllText(badLedger, rawLedger);
                Failure("evidence", "ledger-validate", "--ledger", badLedger);
                Failure("evidence", "bundle", "--assessment", identityPath, "--source-ledger", badLedger,
                    "--ids", record.StableId, "--root", root, "--manifest", unit.InputPath, "--output", badLedger + ".bundle");
                Assert(!File.Exists(badLedger + ".bundle"), "bad embedded ledger publishes no bundle");
            }
            var identityNode = JsonNode.Parse(File.ReadAllBytes(identityPath))!;
            identityNode["assessment_kind"] = "unified";
            var badIdentity = identityPath + ".unified";
            File.WriteAllText(badIdentity, identityNode.ToJsonString());
            Failure("evidence", "ledger-build", "--kind", sourceLedger.LedgerKind, "--subject", badIdentity,
                "--draft", draftPath, "--nupkg", Path.Combine(root, revision.Input.Package.NupkgPath), "--output", badIdentity + ".ledger");
            Failure("evidence", "bundle", "--assessment", badIdentity, "--source-ledger", ledgerPath,
                "--ids", record.StableId, "--output", badIdentity + ".bundle");
            Assert(!File.Exists(badIdentity + ".ledger") && !File.Exists(badIdentity + ".bundle"),
                "retired evidence identity has no producer fallback");
            AssertUnchanged(original);
        }
    }

    private static void TestInputEntrypoints(AssessmentTests.Fixture fixture)
    {
        foreach (var kind in new[] { "scoped-component-profile-v1", "scoped-package-context-v1", "scoped-component-profile-v999" })
        {
            var basename = kind + ".json";
            File.WriteAllText(Path.Combine(fixture.Root, basename), "{}");
            var candidates = JsonNode.Parse(File.ReadAllBytes(fixture.CandidatesPath))!.AsObject();
            candidates["evidence_inputs"]!.AsArray().Add(new JsonObject { ["path"] = basename, ["kind"] = kind });
            var candidatePath = Path.Combine(fixture.Root, "bad-candidates-" + basename);
            File.WriteAllText(candidatePath, candidates.ToJsonString());
            var output = candidatePath + ".output";
            Failure("inputs", "discover", "--root", fixture.Root, "--nupkg", fixture.NupkgPath,
                "--candidates", candidatePath, "--output", output);
            Failure("inputs", "candidates", "add-evidence", "--input", fixture.CandidatesPath,
                "--path", basename, "--kind", kind, "--output", output);
            var input = fixture.Confirmed with
            {
                EvidenceInputs = [new(basename, kind, ContractJson.RawDigest(Encoding.UTF8.GetBytes("{}")), 2)]
            };
            var inputPath = candidatePath + ".manifest";
            File.WriteAllBytes(inputPath, InputManifestService.Serialize(input));
            Failure("inputs", "validate", "--root", fixture.Root, "--manifest", inputPath);
            Failure("assessment", "init", "--kind", "component", "--component", "fancy-tree",
                "--root", fixture.Root, "--input", inputPath, "--output", output);
            File.WriteAllBytes(inputPath, InputManifestService.Serialize(input with { State = "draft" }));
            Failure("inputs", "confirm", "--root", fixture.Root, "--draft", inputPath, "--output", output);
            Assert(!File.Exists(output), "retired input declarations publish no fallback intake");
        }
        foreach (var version in new[] { 1, 999 })
        {
            var path = Path.Combine(fixture.Root, $"old-input-{version}.json");
            File.WriteAllBytes(path, InputManifestService.Serialize(fixture.Confirmed with { SchemaVersion = version }));
            Failure("inputs", "validate", "--root", fixture.Root, "--manifest", path);
            Failure("assessment", "init", "--kind", "component", "--component", "fancy-tree",
                "--root", fixture.Root, "--input", path, "--output", path + ".assessment");
            Assert(!File.Exists(path + ".assessment"), "unsupported input schema cannot initialize");
        }
    }

    private static void TestFeedbackHistory(string root, string inputPath, ReadinessAssessment assessment)
    {
        var unit = WriteUnit(root, "feedback-history", inputPath, assessment);
        var first = Path.Combine(root, "first-feedback.md");
        var second = Path.Combine(root, "second-feedback.md");
        var firstBytes = Encoding.UTF8.GetBytes("# Assessment feedback\n\n| Requirement IDs | Feedback |\n|---|---|\n" +
            $"| `{assessment.Rows[0].Id}` | First retained commentary. |\n");
        File.WriteAllBytes(first, firstBytes);
        File.WriteAllBytes(second, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(firstBytes)
            .Replace("First retained", "Second retained", StringComparison.Ordinal)));
        string[] render = ["report", "render", "--root", root, "--input", inputPath,
            "--assessment", unit.AssessmentPath, "--evidence", unit.EvidencePath, "--output", unit.Revisions];
        Success([.. render, "--feedback", first]);
        var before = Snapshot(unit.Revision);
        var predecessor = ContractJson.RawDigest(File.ReadAllBytes(
            Path.Combine(unit.Revision, "component.validation.json"))).Value;
        string[] successor = [.. render, "--feedback", second, "--predecessor", predecessor];
        Failure(successor);
        Assert(!Directory.Exists(Path.Combine(unit.Revisions, "0002")), "missing predecessor feedback publishes nothing");
        Success([.. successor, "--feedback-history", first]);
        var next = Path.Combine(unit.Revisions, "0002");
        Failure("report", "verify", "--root", root, "--revision", next, "--feedback", second);
        Success("report", "verify", "--root", root, "--revision", next, "--feedback", second, "--feedback-history", first);
        var reader = Path.Combine(root, "feedback-history-reader");
        Failure("reader", "render", "--root", root, "--revision", next, "--output", reader, "--feedback", second);
        Assert(!Directory.Exists(reader), "reader cannot silently skip predecessor commentary");
        Success("reader", "render", "--root", root, "--revision", next, "--output", reader,
            "--feedback", second, "--feedback-history", first);
        Success("reader", "verify", "--root", root, "--revision", next, "--output", reader,
            "--feedback", second, "--feedback-history", first);
        Assert(Directory.GetFiles(reader, "*", SearchOption.AllDirectories)
            .All(path => !File.ReadAllText(path).Contains("First retained commentary.", StringComparison.Ordinal)),
            "historical verification inputs are not exported");
        foreach (var file in new[] { "input-manifest.json", "component.assessment.json", "component.evidence.json" })
        {
            Assert(File.ReadAllBytes(Path.Combine(unit.Revision, file)).AsSpan()
                .SequenceEqual(File.ReadAllBytes(Path.Combine(next, file))), "feedback-only content is immutable");
        }
        File.AppendAllText(first, "changed");
        Failure("report", "verify", "--root", root, "--revision", next, "--feedback", second, "--feedback-history", first);
        Failure("reader", "verify", "--root", root, "--revision", next, "--output", reader,
            "--feedback", second, "--feedback-history", first);
        File.WriteAllBytes(first, firstBytes);
        Failure("report", "verify", "--root", root, "--revision", next, "--feedback", second,
            "--feedback-history", first, "--feedback-history", first);
        var nextDigest = ContractJson.RawDigest(File.ReadAllBytes(Path.Combine(next, "component.validation.json"))).Value;
        var changed = assessment with
        {
            Rows = [assessment.Rows[0] with { Observation = "A changed component finding with unchanged evidence." },
                .. assessment.Rows.Skip(1)]
        };
        var changedPath = Path.Combine(root, "same-status-change.json");
        File.WriteAllBytes(changedPath, AssessmentService.Serialize(changed));
        var correctionError = new StringWriter();
        var correctionExit = CliApplication.Run(["assessment", "revise", "--root", root, "--input", inputPath,
            "--assessment", changedPath, "--evidence", unit.EvidencePath, "--output", unit.Revisions,
            "--feedback", second, "--feedback-history", first, "--predecessor", nextDigest,
            "--changed-ids", assessment.Rows[0].Id], new StringWriter(), correctionError);
        Assert(correctionExit == ExitCodes.ValidationFailure &&
            correctionError.ToString().Contains("requires evidence absent from the predecessor", StringComparison.Ordinal),
            "same-status component corrections require newly selected evidence, not merely changed prose");
        Assert(!Directory.Exists(Path.Combine(unit.Revisions, "0003")), "unsupported correction publishes nothing");
        ReportCommand.BeforePublishForTests = () => File.AppendAllText(first, "raced");
        try
        {
            Failure([.. render, "--feedback", second, "--feedback-history", first, "--predecessor", nextDigest]);
            Assert(!Directory.Exists(Path.Combine(unit.Revisions, "0003")), "historical feedback race publishes no revision");
        }
        finally
        {
            ReportCommand.BeforePublishForTests = null;
            File.WriteAllBytes(first, firstBytes);
        }
        ReaderCommand.BeforePublishForTests = () => File.AppendAllText(first, "raced");
        var racedReader = reader + "-raced";
        try
        {
            Failure("reader", "render", "--root", root, "--revision", next, "--output", racedReader,
                "--feedback", second, "--feedback-history", first);
            Assert(!Directory.Exists(racedReader), "historical feedback race publishes no reader");
        }
        finally
        {
            ReaderCommand.BeforePublishForTests = null;
            File.WriteAllBytes(first, firstBytes);
        }
        AssertUnchanged(before);
    }

    private static ReadinessAssessment Complete(ReadinessAssessment assessment)
    {
        var evidence = AssessmentTests.BuildEvidence(assessment.Identity);
        var first = assessment.Rows[0].Id;
        var rows = assessment.Rows.Select(row => row with
        {
            Status = "not tested",
            Observation = row.Id == first ? "A retained synthetic input supplies bounded context." : null,
            EvidenceIds = row.Id == first ? evidence.Selection.Select(item => item.EvidenceId).ToArray() : [],
            AssessmentFollowUp = "The synthetic contract fixture does not execute assessed code."
        }).ToArray();
        return assessment with
        {
            Rows = rows,
            CompletionState = "complete",
            SummaryGroups = assessment.AssessmentKind == "package"
                ? [new("not tested", "The package checks retain their explicit unperformed outcomes.",
                    rows.Select(row => row.Id).ToArray(), evidence.Selection.Select(item => item.EvidenceId).ToArray())]
                : []
        };
    }

    private static Unit WriteUnit(string root, string name, string inputPath, ReadinessAssessment assessment)
    {
        var assessmentPath = Path.Combine(root, name + ".assessment.json");
        var evidencePath = Path.Combine(root, name + ".evidence.json");
        File.WriteAllBytes(assessmentPath, AssessmentService.Serialize(assessment));
        File.WriteAllBytes(evidencePath,
            CanonicalEvidenceJson.SerializeBundle(AssessmentTests.BuildEvidence(assessment.Identity)));
        var revisions = Path.Combine(root, name + "-revisions");
        return new(assessment, inputPath, assessmentPath, evidencePath, revisions,
            Path.Combine(revisions, "0001"), Path.Combine(root, name + "-reader"));
    }

    private static void RenderAndVerify(string root, Unit unit, string? packageRevision = null)
    {
        string[] binding = packageRevision is null ? [] : ["--package-revision", packageRevision];
        Success(["assessment", "validate", "--root", root, "--input", unit.InputPath,
            "--assessment", unit.AssessmentPath, "--evidence", unit.EvidencePath, .. binding]);
        Success(["report", "render", "--root", root, "--input", unit.InputPath,
            "--assessment", unit.AssessmentPath, "--evidence", unit.EvidencePath,
            "--output", unit.Revisions, .. binding]);
        Success(["report", "verify", "--root", root, "--revision", unit.Revision, .. binding]);
        Success(["reader", "render", "--root", root, "--revision", unit.Revision,
            "--output", unit.Reader, .. binding]);
        Success(["reader", "verify", "--root", root, "--revision", unit.Revision,
            "--output", unit.Reader, .. binding]);
    }

    private static void Validate(string root, InputManifest input, ReadinessAssessment assessment, EvidenceBundle evidence) =>
        AssessmentService.Validate(root, assessment, AssessmentService.Serialize(assessment),
            input, InputManifestService.Serialize(input), evidence);

    private static Dictionary<string, byte[]> Snapshot(string directory) =>
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);

    private static void AssertUnchanged(Dictionary<string, byte[]> files)
    {
        foreach (var (path, bytes) in files)
        {
            Assert(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "earlier immutable bytes remain unchanged");
        }
    }

    private static void Success(params string[] arguments)
    {
        var error = new StringWriter();
        var exit = CliApplication.Run(arguments, new StringWriter(), error);
        Assert(exit == ExitCodes.Success, $"{string.Join(' ', arguments.Take(2))}: {error}");
    }

    private static void Failure(params string[] arguments) =>
        Failure(ExitCodes.ValidationFailure, arguments);

    private static void Failure(int expectedExit, params string[] arguments)
    {
        var error = new StringWriter();
        var exit = CliApplication.Run(arguments, new StringWriter(), error);
        Assert(exit == expectedExit, $"Expected failure {expectedExit}: {string.Join(' ', arguments.Take(2))}: {error}");
    }

    private static void Reject(Action action, string name, string? expectedMessage = null)
    {
        try
        {
            action();
        }
        catch (DeterministicValidationException exception)
        {
            if (expectedMessage is not null)
            {
                Assert(exception.Message.Contains(expectedMessage, StringComparison.Ordinal),
                    $"{name}: expected '{expectedMessage}', actual '{exception.Message}'.");
            }
            return;
        }

        throw new InvalidOperationException("Expected rejection: " + name);
    }

    private static void Assert(bool value, string name)
    {
        if (!value)
        {
            throw new InvalidOperationException(name);
        }
    }

    private sealed record Unit(ReadinessAssessment Assessment, string InputPath, string AssessmentPath,
        string EvidencePath, string Revisions, string Revision, string Reader);
}
