using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.Inventory;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Library;
using BlazorComponentReadiness.Validator.Validation;
using AssessmentService = LegacyAssessmentService;

internal static class LibraryTests
{
    public static void Run(string repositoryRoot, string pluginRoot)
    {
        var root = Path.Combine(
            Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ?? Path.Combine(repositoryRoot, "artifacts"),
            "readiness-commit6-tests");
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
            TestReleaseCandidateDigestRoots(Path.Combine(root, "same-version-candidates"));
            var fixture = CreateFixture(root);
            TestDiscoveryAndConfirmation(fixture);
            TestPackageInputInventoryBoundary(fixture);
            TestDraftCannotRun(fixture);
            TestInterruptedAndIncompleteState(fixture);
            var packageRevisions = CreatePackageRevisions(fixture);
            TestMissingOutputPreventsComplete(fixture, packageRevisions);
            TestComponentInputIsolation(fixture, packageRevisions);
            CreateComponentRevisions(fixture, packageRevisions);
            TestPackageArtifactCannotSatisfyComponentHandoff(fixture);
            TestCompleteReconcileAndIndex(fixture);
            TestCorruptManifestReconstruction(fixture);
            TestConflictsAndWrongPackage(fixture);
            TestCliSubprocess(fixture, pluginRoot);
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

    private static Fixture CreateFixture(string root)
    {
        var inputs = Path.Combine(root, "inputs");
        Directory.CreateDirectory(inputs);
        var packageA = CreatePackageSet(
            root,
            "Alpha.Controls",
            "01.000.000",
            "release-candidate",
            [
                ("Alpha Grid", new[] { "interactive-server", "static-ssr" }),
                ("Alpha Tree", new[] { "interactive-webassembly" })
            ]);
        var packageB = CreatePackageSet(
            root,
            "Beta.Controls",
            "2.5.0-rc.1",
            "published",
            [
                ("Beta Chart", new[] { "interactive-auto", "interactive-server" }),
                ("Beta Dialog", new[] { "standalone-webassembly", "static-ssr" })
            ]);

        var candidatesPath = Path.Combine(root, "inventory-candidates.json");
        File.WriteAllText(
            candidatesPath,
            JsonSerializer.Serialize(
                new
                {
                    schema_version = 1,
                    packages = new[]
                    {
                        Candidate(packageB),
                        Candidate(packageA)
                    }
                }),
            new UTF8Encoding(false));
        var draftPath = Path.Combine(root, "inventory.draft.json");
        var confirmedPath = Path.Combine(root, "inventory.confirmed.json");
        var runPath = Path.Combine(root, "run-manifest.json");
        var output = new StringWriter();
        var error = new StringWriter();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(
                [
                    "inventory", "discover",
                    "--root", root,
                    "--candidates", candidatesPath,
                    "--output", draftPath
                ],
                output,
                error),
            $"inventory discover: {error}");
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(
                [
                    "inventory", "confirm",
                    "--root", root,
                    "--draft", draftPath,
                    "--output", confirmedPath
                ],
                output,
                error),
            $"inventory confirm: {error}");
        var inventoryBytes = File.ReadAllBytes(confirmedPath);
        var inventory = InventoryService.Parse(inventoryBytes);
        return new Fixture(
            root,
            candidatesPath,
            draftPath,
            confirmedPath,
            runPath,
            inventory,
            inventoryBytes,
            [packageA, packageB]);
    }

    private static PackageSet CreatePackageSet(
        string root,
        string id,
        string version,
        string acquisition,
        IReadOnlyList<(string DisplayName, string[] Modes)> components,
        string? fileQualifier = null)
    {
        var canonicalId = Canonicalization.PackageId(id);
        var canonicalVersion = NuGetVersionNormalizer.Normalize(version);
        var qualifier = fileQualifier is null ? string.Empty : $".{fileQualifier}";
        var componentContracts = components
            .Select(item => new InputComponent(
                Canonicalization.ComponentId(item.DisplayName),
                item.DisplayName,
                item.Modes.Order(StringComparer.Ordinal).ToArray(),
                [$"src/{Canonicalization.ComponentId(item.DisplayName)}.razor"],
                new DynamicChildLifecycle(
                    "not-applicable",
                    [],
                    "This synthetic component does not manage grouped, registered, selected, or composite children.")))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var nupkgRelative = $"inputs/{canonicalId}.{canonicalVersion}{qualifier}.nupkg";
        var nupkg = Path.Combine(root, nupkgRelative);
        CreatePackage(
            nupkg,
            id,
            version,
            componentContracts.Select(component => $"content/{component.Id}.txt").ToArray());
        var inspected = NupkgInspector.Inspect(nupkg);
        var package = new EvidencePackageIdentity(
            canonicalId,
            canonicalVersion,
            new Sha256Digest("sha256", inspected.NupkgSha256));
        var componentInputs = componentContracts.ToDictionary(
            component => component.Id,
            component => CreateComponentInputs(root, canonicalId, component.Id),
            StringComparer.Ordinal);
        var packageInput = CreateInput(
            acquisition,
            package,
            nupkgRelative,
            componentContracts,
            componentInputs.Values.SelectMany(value => value.Documentation).ToArray(),
            componentInputs.Values.SelectMany(value => value.PackageSources).ToArray(),
            componentInputs.Values.SelectMany(value => value.SourceArtifacts).ToArray(),
            componentInputs.Values.SelectMany(value => value.OwnerInputs).ToArray());
        var packageInputPath = $"inputs/{canonicalId}{qualifier}.package.input-manifest.json";
        File.WriteAllBytes(
            Path.Combine(root, packageInputPath),
            InputManifestService.Serialize(packageInput));
        InputManifestService.Validate(packageInput, root, requireConfirmed: true);

        var componentPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var component in componentContracts)
        {
            var componentInput = CreateInput(
                acquisition,
                package,
                nupkgRelative,
                [component],
                componentInputs[component.Id].Documentation,
                componentInputs[component.Id].PackageSources,
                componentInputs[component.Id].SourceArtifacts,
                componentInputs[component.Id].OwnerInputs);
            var path = $"inputs/{canonicalId}{qualifier}.{component.Id}.input-manifest.json";
            File.WriteAllBytes(Path.Combine(root, path), InputManifestService.Serialize(componentInput));
            InputManifestService.Validate(componentInput, root, requireConfirmed: true);
            componentPaths.Add(component.Id, path);
        }

        return new PackageSet(
            package,
            acquisition,
            packageInputPath,
            componentContracts,
            componentPaths,
            componentInputs);
    }

    private static void TestReleaseCandidateDigestRoots(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "inputs"));
        var first = CreatePackageSet(
            root,
            "Gamma.Controls",
            "4.0.0-rc.1",
            "release-candidate",
            [("Gamma First", new[] { "interactive-server" })],
            "first");
        var second = CreatePackageSet(
            root,
            "Gamma.Controls",
            "4.0.0-rc.1",
            "release-candidate",
            [("Gamma Second", new[] { "static-ssr" })],
            "second");
        Assert(first.Package.NupkgDigest != second.Package.NupkgDigest, "candidate package bytes differ");

        var candidatesPath = Path.Combine(root, "inventory-candidates.json");
        File.WriteAllText(
            candidatesPath,
            JsonSerializer.Serialize(new
            {
                schema_version = 1,
                packages = new[] { Candidate(second), Candidate(first) }
            }),
            new UTF8Encoding(false));
        var draftPath = Path.Combine(root, "inventory.draft.json");
        var confirmedPath = Path.Combine(root, "inventory.confirmed.json");
        RunCliRaw([
            "inventory", "discover",
            "--root", root,
            "--candidates", candidatesPath,
            "--output", draftPath
        ]);
        RunCliRaw([
            "inventory", "confirm",
            "--root", root,
            "--draft", draftPath,
            "--output", confirmedPath
        ]);
        var inventoryBytes = File.ReadAllBytes(confirmedPath);
        var inventory = InventoryService.Parse(inventoryBytes);
        AssertEqual(2, inventory.Packages.Count, "same-version release candidates remain distinct units");
        AssertEqual(
            2,
            inventory.Packages.Select(package => package.RevisionRoot).Distinct(StringComparer.Ordinal).Count(),
            "same-version release candidates have distinct package roots");
        foreach (var package in inventory.Packages)
        {
            Assert(
                package.RevisionRoot.Contains(
                    $"/candidates/{package.Package.NupkgDigest.Value}/",
                    StringComparison.Ordinal),
                "release-candidate package root contains digest-derived segment");
            Assert(
                package.Components.Single().RevisionRoot.Contains(
                    $"/candidates/{package.Package.NupkgDigest.Value}/",
                    StringComparison.Ordinal),
                "release-candidate component root contains digest-derived segment");
        }

        var fixture = new Fixture(
            root,
            candidatesPath,
            draftPath,
            confirmedPath,
            Path.Combine(root, "run-manifest.json"),
            inventory,
            inventoryBytes,
            [first, second]);
        var packageRevisions = CreatePackageRevisions(fixture);
        CreateComponentRevisions(fixture, packageRevisions);
        Reconcile(fixture);
        var manifestBytes = File.ReadAllBytes(fixture.RunPath);
        var manifest = LibraryService.Parse(manifestBytes);
        AssertEqual("complete", manifest.State, "same-version candidates complete independently");
        foreach (var package in inventory.Packages)
        {
            var packageUnit = manifest.Units.Single(unit => unit.UnitId == package.UnitId);
            var componentUnit = manifest.Units.Single(unit => unit.UnitId == package.Components.Single().UnitId);
            AssertEqual(
                packageUnit.ValidationManifestDigest,
                componentUnit.PackageValidationManifestDigest,
                "candidate component binds its own package validation revision");
        }

        var jsonPath = Path.Combine(root, "library-index.json");
        var markdownPath = Path.Combine(root, "library-index.md");
        RunCliRaw([
            "library", "index",
            "--root", root,
            "--inventory", confirmedPath,
            "--run-manifest", fixture.RunPath,
            "--json", jsonPath,
            "--markdown", markdownPath
        ]);
        using var index = JsonDocument.Parse(File.ReadAllBytes(jsonPath));
        var entries = index.RootElement.GetProperty("packages").EnumerateArray().ToArray();
        AssertEqual(2, entries.Length, "index retains both same-ID/version candidate entries");
        AssertEqual(
            2,
            entries.Select(entry => entry.GetProperty("unit_id").GetString())
                .Distinct(StringComparer.Ordinal)
                .Count(),
            "index candidate unit bindings are distinct");
        AssertEqual(
            2,
            entries.Select(entry => entry.GetProperty("report").GetString())
                .Distinct(StringComparer.Ordinal)
                .Count(),
            "index candidate report roots are distinct");
    }

    private static InputManifest CreateInput(
        string acquisition,
        EvidencePackageIdentity package,
        string nupkgRelative,
        IReadOnlyList<InputComponent> components,
        IReadOnlyList<InputDocument>? documentation = null,
        IReadOnlyList<InputPackageSource>? packageSources = null,
        IReadOnlyList<InputSourceArtifact>? sourceArtifacts = null,
        IReadOnlyList<OwnerInput>? ownerInputs = null) =>
        new(
            InputManifestService.SchemaVersion,
            "confirmed",
            acquisition,
            new InputPackage(
                package.PackageId,
                package.Version,
                package.NupkgDigest,
                nupkgRelative,
                nupkgRelative,
                "local-file"),
            new InputSource(
                "source-available",
                "https://source.example.test/vendor/components",
                new string('a', 40),
                "The package metadata maps each declared component path to this exact source commit.",
                "high"),
            [new InputRetrievalAttempt("package", nupkgRelative, "local-file", "succeeded", null)],
            documentation ?? [],
            packageSources ?? [],
            sourceArtifacts ?? [],
            [],
            ownerInputs ?? [],
            components,
            [new InputExclusion("Synthetic external services", "Outside this deterministic library fixture.")]);

    private static ComponentInputs CreateComponentInputs(
        string root,
        string packageId,
        string componentId)
    {
        var documentationPath = $"inputs/{packageId}.{componentId}.html";
        var documentationBytes = Encoding.UTF8.GetBytes($"Documentation for {componentId}.");
        File.WriteAllBytes(Path.Combine(root, documentationPath), documentationBytes);
        var packageSourcePath = $"inputs/{packageId}.{componentId}.package-source.txt";
        var packageSourceBytes = Encoding.UTF8.GetBytes($"Package entry content/{componentId}.txt.");
        File.WriteAllBytes(Path.Combine(root, packageSourcePath), packageSourceBytes);
        var sourceCapturePath = $"inputs/{packageId}.{componentId}.source.txt";
        var sourceBytes = Encoding.UTF8.GetBytes($"Source for {componentId}.");
        File.WriteAllBytes(Path.Combine(root, sourceCapturePath), sourceBytes);
        var ownerBasename = $"{packageId}.{componentId}.owner.txt";
        var ownerBytes = Encoding.UTF8.GetBytes($"Owner evidence for {componentId}.");
        File.WriteAllBytes(Path.Combine(root, ownerBasename), ownerBytes);
        return new ComponentInputs(
            [
                new InputDocument(
                    $"https://docs.example.test/{packageId}/{componentId}",
                    documentationPath,
                    ContractJson.RawDigest(documentationBytes))
            ],
            [
                new InputPackageSource(
                    "package-metadata",
                    $"{NupkgInspector.PackageEntryEvidencePrefix}content/{componentId}.txt",
                    packageSourcePath,
                    ContractJson.RawDigest(packageSourceBytes))
            ],
            [
                new InputSourceArtifact(
                    $"src/{componentId}.razor",
                    sourceCapturePath,
                    ContractJson.RawDigest(sourceBytes))
            ],
            [
                new OwnerInput(
                    ownerBasename,
                    "owner-supplied-internal-evidence",
                    ContractJson.RawDigest(ownerBytes),
                    ownerBytes.LongLength)
            ]);
    }

    private static object Candidate(PackageSet package) => new
    {
        input_manifest_path = package.PackageInputPath,
        revision_root = PackageRevisionRoot(package),
        components = package.Components
            .Reverse()
            .Select(component => new
            {
                component_id = $" {component.DisplayName} ",
                input_manifest_path = package.ComponentInputPaths[component.Id],
                revision_root = ComponentRevisionRoot(package, component.Id),
                render_modes = component.RenderModes
                    .Select(mode => mode switch
                    {
                        "interactive-server" => "Server",
                        "interactive-webassembly" => "WASM",
                        "static-ssr" => "STATIC SSR",
                        "interactive-auto" => "Auto",
                        "standalone-webassembly" => "standalone wasm",
                        _ => mode
                    })
                    .Reverse()
                    .ToArray(),
                exclusions = Array.Empty<object>()
            })
            .ToArray(),
        exclusions = new[]
        {
            new
            {
                subject = "Synthetic external services",
                rationale = "Outside this deterministic library fixture."
            }
        }
    };

    private static string PackageRevisionRoot(PackageSet package) =>
        package.Acquisition == "release-candidate"
            ? $"packages/{package.Package.PackageId}/{package.Package.Version}/candidates/{package.Package.NupkgDigest.Value}/revisions"
            : $"packages/{package.Package.PackageId}/{package.Package.Version}/revisions";

    private static string ComponentRevisionRoot(PackageSet package, string componentId) =>
        package.Acquisition == "release-candidate"
            ? $"packages/{package.Package.PackageId}/{package.Package.Version}/candidates/{package.Package.NupkgDigest.Value}/components/{componentId}/revisions"
            : $"packages/{package.Package.PackageId}/{package.Package.Version}/components/{componentId}/revisions";

    private static void TestDiscoveryAndConfirmation(Fixture fixture)
    {
        var draft = InventoryService.Parse(File.ReadAllBytes(fixture.DraftPath));
        AssertEqual("draft", draft.State, "inventory draft state");
        Assert(draft.InventoryId is null && draft.RunId is null, "draft has no run identity");
        AssertEqual("confirmed", fixture.Inventory.State, "inventory confirmed state");
        Assert(fixture.Inventory.InventoryId == fixture.Inventory.RunId, "inventory and run identity match");
        Assert(
            fixture.Inventory.InventoryId!.StartsWith("LIB1-", StringComparison.Ordinal),
            "deterministic inventory ID prefix");
        AssertEqual(2, fixture.Inventory.Packages.Count, "two exact package units");
        AssertEqual(4, fixture.Inventory.Packages.Sum(item => item.Components.Count), "four component units");
        AssertEqual(
            6,
            fixture.Inventory.Packages.Count + fixture.Inventory.Packages.Sum(item => item.Components.Count),
            "six total work units");
        Assert(
            fixture.Inventory.Packages.Select(item => item.Package.PackageId)
                .SequenceEqual(["alpha.controls", "beta.controls"], StringComparer.Ordinal),
            "packages canonical and separately sorted");
        Assert(
            fixture.Inventory.Packages.SelectMany(item => item.Components)
                .All(component => component.RenderModes.SequenceEqual(
                    component.RenderModes.Order(StringComparer.Ordinal),
                    StringComparer.Ordinal)),
            "render modes canonical and sorted");
        var published = fixture.Inventory.Packages.Single(package => package.Acquisition == "published");
        AssertEqual(
            $"packages/{published.Package.PackageId}/{published.Package.Version}/revisions",
            published.RevisionRoot,
            "published stable package root retains version path");
        InventoryService.Validate(fixture.Inventory, fixture.Root, requireConfirmed: true);

        var repeated = InventoryService.Confirm(draft, fixture.Root);
        AssertBytes(
            fixture.InventoryBytes,
            InventoryService.Serialize(repeated),
            "confirmed inventory deterministic bytes and identity");
        var immutableBytes = File.ReadAllBytes(fixture.ConfirmedPath);
        AssertEqual(
            ExitCodes.EnvironmentFailure,
            CliApplication.Run(
                [
                    "inventory", "confirm",
                    "--root", fixture.Root,
                    "--draft", fixture.DraftPath,
                    "--output", fixture.ConfirmedPath
                ],
                new StringWriter(),
                new StringWriter()),
            "confirmed inventory immutable create-new");
        AssertBytes(
            immutableBytes,
            File.ReadAllBytes(fixture.ConfirmedPath),
            "confirmed inventory overwrite preserves bytes");

        var duplicateCandidates = File.ReadAllText(fixture.CandidatesPath);
        using var duplicateDocument = JsonDocument.Parse(duplicateCandidates);
        var firstPackage = duplicateDocument.RootElement.GetProperty("packages")[0];
        var packagePath = firstPackage.GetProperty("input_manifest_path").GetString()!;
        var package = fixture.Packages.Single(item => item.PackageInputPath == packagePath);
        var duplicatePath = Path.Combine(fixture.Root, "duplicate-candidates.json");
        File.WriteAllText(
            duplicatePath,
            JsonSerializer.Serialize(new
            {
                schema_version = 1,
                packages = new[] { Candidate(package), Candidate(package) }
            }),
            new UTF8Encoding(false));
        ExpectValidation(
            () => InventoryService.Discover(fixture.Root, File.ReadAllBytes(duplicatePath)),
            "duplicate package ownership");

        var siblingInput = CreateInput(
            package.Acquisition,
            package.Package,
            $"inputs/{package.Package.PackageId}.{package.Package.Version}.nupkg",
            package.Components);
        var siblingPath = $"inputs/{package.Package.PackageId}.sibling-component.input-manifest.json";
        File.WriteAllBytes(
            Path.Combine(fixture.Root, siblingPath),
            InputManifestService.Serialize(siblingInput));
        var badCandidate = JsonSerializer.Serialize(new
        {
            schema_version = 1,
            packages = new[]
            {
                new
                {
                    input_manifest_path = package.PackageInputPath,
                    revision_root = PackageRevisionRoot(package),
                    components = new[]
                    {
                        new
                        {
                            component_id = package.Components[0].Id,
                            input_manifest_path = siblingPath,
                            revision_root = ComponentRevisionRoot(package, package.Components[0].Id),
                            render_modes = package.Components[0].RenderModes,
                            exclusions = Array.Empty<object>()
                        },
                        new
                        {
                            component_id = package.Components[1].Id,
                            input_manifest_path = package.ComponentInputPaths[package.Components[1].Id],
                            revision_root = ComponentRevisionRoot(package, package.Components[1].Id),
                            render_modes = package.Components[1].RenderModes,
                            exclusions = Array.Empty<object>()
                        }
                    },
                    exclusions = Array.Empty<object>()
                }
            }
        });
        ExpectValidation(
            () => InventoryService.Discover(fixture.Root, Encoding.UTF8.GetBytes(badCandidate)),
            "sibling component input leakage");
    }

    private static void TestPackageInputInventoryBoundary(Fixture fixture)
    {
        var package = fixture.Packages[0];
        var packageInputPath = Path.Combine(fixture.Root, package.PackageInputPath);
        var packageInput = InputManifestService.Parse(File.ReadAllBytes(packageInputPath));
        var emptyInput = packageInput with { Components = [] };
        var emptyInputRelativePath = $"inputs/{package.Package.PackageId}.empty.package.input-manifest.json";
        File.WriteAllBytes(
            Path.Combine(fixture.Root, emptyInputRelativePath),
            InputManifestService.Serialize(emptyInput));
        InputManifestService.Validate(emptyInput, fixture.Root, requireConfirmed: true);

        var emptyInventoryCandidate = JsonSerializer.Serialize(new
        {
            schema_version = 1,
            packages = new[]
            {
                new
                {
                    input_manifest_path = emptyInputRelativePath,
                    revision_root = PackageRevisionRoot(package),
                    components = Array.Empty<object>(),
                    exclusions = Array.Empty<object>()
                }
            }
        });
        ExpectValidation(
            () => InventoryService.Discover(fixture.Root, Encoding.UTF8.GetBytes(emptyInventoryCandidate)),
            "full-library inventory cannot select zero components");

        string NonemptyInventoryCandidate(string inputPath) => JsonSerializer.Serialize(new
        {
            schema_version = 1,
            packages = new[]
            {
                new
                {
                    input_manifest_path = inputPath,
                    revision_root = PackageRevisionRoot(package),
                    components = package.Components.Select(component => new
                    {
                        component_id = component.Id,
                        input_manifest_path = package.ComponentInputPaths[component.Id],
                        revision_root = ComponentRevisionRoot(package, component.Id),
                        render_modes = component.RenderModes,
                        exclusions = Array.Empty<object>()
                    }).ToArray(),
                    exclusions = Array.Empty<object>()
                }
            }
        });
        _ = InventoryService.Discover(
            fixture.Root,
            Encoding.UTF8.GetBytes(NonemptyInventoryCandidate(package.PackageInputPath)));
        ExpectValidation(
            () => InventoryService.Discover(
                fixture.Root,
                Encoding.UTF8.GetBytes(NonemptyInventoryCandidate(emptyInputRelativePath))),
            "full-library inventory cannot reference components absent from package input");
    }

    private static void TestDraftCannotRun(Fixture fixture)
    {
        var draftBytes = File.ReadAllBytes(fixture.DraftPath);
        var draft = InventoryService.Parse(draftBytes);
        ExpectValidation(
            () => LibraryService.Reconcile(fixture.Root, draft, draftBytes),
            "draft inventory cannot run");
    }

    private static void TestInterruptedAndIncompleteState(Fixture fixture)
    {
        RunCli(
            fixture,
            [
                "library", "reconcile",
                "--root", fixture.Root,
                "--inventory", fixture.ConfirmedPath,
                "--run-manifest", fixture.RunPath
            ]);
        var pendingBytes = File.ReadAllBytes(fixture.RunPath);
        var pending = LibraryService.Parse(pendingBytes);
        AssertEqual("incomplete", pending.State, "empty library incomplete");
        AssertEqual(6, pending.Units.Count(unit => unit.State == "pending"), "all initial units pending");

        var firstUnit = pending.Units[0].UnitId;
        var activeMissing = new[] { "Awaiting the bounded interactive-server probe output." };
        const string ActiveReason = "Started the component probe before the worker interruption.";
        Reconcile(
            fixture,
            new LibraryStateUpdate(
                1,
                firstUnit,
                "active",
                activeMissing,
                [],
                "2026-09-02T20:00:00Z",
                ActiveReason));
        var active = LibraryService.Parse(File.ReadAllBytes(fixture.RunPath));
        AssertEqual("active", active.Units[0].State, "state update records active");
        AssertEqual(ActiveReason, active.Units[0].TransitionReason, "active transition reason");
        Assert(
            active.Units[0].MissingInputs.SequenceEqual(activeMissing, StringComparer.Ordinal),
            "active missing-input reason preserved");

        var activeBytes = File.ReadAllBytes(fixture.RunPath);
        File.Delete(fixture.RunPath);
        Reconcile(fixture);
        AssertBytes(
            activeBytes,
            File.ReadAllBytes(fixture.RunPath),
            "missing run manifest reconstructs active state and exact reasons");
        File.WriteAllText(fixture.RunPath, "{\"schema_version\":1", new UTF8Encoding(false));
        Reconcile(fixture);
        AssertBytes(
            activeBytes,
            File.ReadAllBytes(fixture.RunPath),
            "truncated run manifest reconstructs active state and exact reasons");

        Reconcile(fixture);
        var interrupted = LibraryService.Parse(File.ReadAllBytes(fixture.RunPath));
        Assert(interrupted.Interrupted, "interrupted run state recorded");
        AssertEqual("incomplete", interrupted.Units[0].State, "interrupted active unit becomes incomplete");
        AssertEqual(ActiveReason, interrupted.Units[0].TransitionReason, "interruption preserves exact reason");
        Assert(
            interrupted.Units[0].MissingInputs.SequenceEqual(activeMissing, StringComparer.Ordinal),
            "interruption preserves exact missing inputs");
        var interruptedReceipt = LibraryStateReceiptService.Load(
            fixture.Root,
            fixture.Inventory,
            fixture.InventoryBytes)[firstUnit].Receipt;
        AssertEqual(2, interruptedReceipt.Sequence, "interruption appends immutable state receipt");
        AssertEqual(1, interruptedReceipt.PriorReceiptSequence, "state receipt binds prior sequence");
        Assert(interruptedReceipt.PriorReceiptDigest is not null, "state receipt binds prior digest");
        AssertEqual(5, interrupted.Units.Count(unit => unit.State == "pending"), "remaining units stay pending");

        var blockedUnit = interrupted.Units[1].UnitId;
        var blockedMissing = new[] { "Vendor API token was not supplied." };
        var blockedProbes = new[] { "Authenticated export probe cannot run without the vendor API token." };
        const string BlockedReason = "The confirmed owner input set does not contain the required token.";
        Reconcile(
            fixture,
            new LibraryStateUpdate(
                1,
                blockedUnit,
                "blocked",
                blockedMissing,
                blockedProbes,
                "2026-09-02T20:01:00Z",
                BlockedReason));
        var blockedBytes = File.ReadAllBytes(fixture.RunPath);
        File.WriteAllText(fixture.RunPath, "not-json", new UTF8Encoding(false));
        Reconcile(fixture);
        AssertBytes(
            blockedBytes,
            File.ReadAllBytes(fixture.RunPath),
            "corrupt run manifest reconstructs blocked state and exact reasons");
        var blocked = LibraryService.Parse(File.ReadAllBytes(fixture.RunPath)).Units[1];
        AssertEqual(BlockedReason, blocked.TransitionReason, "blocked transition reason preserved");
        Assert(
            blocked.MissingInputs.SequenceEqual(blockedMissing, StringComparer.Ordinal) &&
            blocked.BlockedProbes.SequenceEqual(blockedProbes, StringComparer.Ordinal),
            "blocked details preserved exactly");

        var receiptPath = Path.GetFullPath(Path.Combine(fixture.Root, blocked.StateReceiptPath!));
        var receiptBytes = File.ReadAllBytes(receiptPath);
        File.WriteAllText(receiptPath, "{\"schema_version\":1", new UTF8Encoding(false));
        ExpectValidation(
            () => LibraryStateReceiptService.Load(
                fixture.Root,
                fixture.Inventory,
                fixture.InventoryBytes),
            "corrupt state receipt rejected");
        File.WriteAllBytes(receiptPath, receiptBytes);
        TestStateReceiptPathSafety(fixture, receiptPath);
        AssertBytes(receiptBytes, File.ReadAllBytes(receiptPath), "path-safety fixture restores original receipt bytes");

        RunCli(
            fixture,
            [
                "library", "index",
                "--root", fixture.Root,
                "--inventory", fixture.ConfirmedPath,
                "--run-manifest", fixture.RunPath,
                "--json", Path.Combine(fixture.Root, "library-index.json"),
                "--markdown", Path.Combine(fixture.Root, "library-index.md")
            ]);
        Assert(
            File.ReadAllText(Path.Combine(fixture.Root, "library-index.md"))
                .Contains("**Overall state:** `incomplete`", StringComparison.Ordinal),
            "incomplete factual index");
    }

    private static void TestStateReceiptPathSafety(Fixture fixture, string receiptPath)
    {
        var safetyRoot = Path.Combine(fixture.Root, "state-receipt-path-safety");
        Directory.CreateDirectory(safetyRoot);
        var savedFile = Path.Combine(safetyRoot, "saved.state-receipt.json");
        File.Move(receiptPath, savedFile);
        var fileLinkCreated = false;
        try
        {
            fileLinkCreated = TryCreateLink(
                () => File.CreateSymbolicLink(receiptPath, savedFile), "file");
            if (fileLinkCreated)
            {
                ExpectValidation(
                    () => LibraryStateReceiptService.Load(
                        fixture.Root,
                        fixture.Inventory,
                        fixture.InventoryBytes),
                    "symlinked state receipt file");
            }
        }
        finally
        {
            if (fileLinkCreated) File.Delete(receiptPath);
            File.Move(savedFile, receiptPath);
        }

        var unitDirectory = Path.GetDirectoryName(receiptPath)!;
        var savedDirectory = Path.Combine(safetyRoot, "saved-unit");
        Directory.Move(unitDirectory, savedDirectory);
        var directoryLinkCreated = false;
        try
        {
            directoryLinkCreated = TryCreateLink(
                () => Directory.CreateSymbolicLink(unitDirectory, savedDirectory), "directory");
            if (directoryLinkCreated)
            {
                ExpectValidation(
                    () => LibraryStateReceiptService.Load(
                        fixture.Root,
                        fixture.Inventory,
                        fixture.InventoryBytes),
                    "symlinked state receipt unit directory");
            }
        }
        finally
        {
            if (directoryLinkCreated) Directory.Delete(unitDirectory);
            Directory.Move(savedDirectory, unitDirectory);
        }

        Console.WriteLine($"State receipt symlink fixture: file={fileLinkCreated}, directory={directoryLinkCreated}; originals restored.");

        static bool TryCreateLink(Action create, string kind)
        {
            try
            {
                create();
                return true;
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                Console.WriteLine($"State receipt {kind} symlink rejection not exercised: {exception.Message}");
                return false;
            }
        }
    }

    private static Dictionary<string, string> CreatePackageRevisions(Fixture fixture)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var package in fixture.Inventory.Packages)
        {
            var inputPath = Path.Combine(fixture.Root, package.InputManifestPath);
            var inputBytes = File.ReadAllBytes(inputPath);
            var input = InputManifestService.Parse(inputBytes);
            var initialized = AssessmentService.Initialize(
                "package",
                fixture.Root,
                input,
                inputBytes,
                null,
                []);
            var evidence = BuildEvidence(initialized.Identity, Array.Empty<string>());
            var assessment = Complete(initialized, evidence);
            var revision = Render(
                fixture.Root,
                inputPath,
                assessment,
                evidence,
                Path.Combine(fixture.Root, package.RevisionRoot),
                packageRevision: null);
            result.Add(package.UnitId, revision);
        }

        return result;
    }

    private static void TestMissingOutputPreventsComplete(
        Fixture fixture,
        IReadOnlyDictionary<string, string> packageRevisions)
    {
        var firstPackage = fixture.Inventory.Packages[0];
        var component = firstPackage.Components[0];
        CreateComponentRevision(fixture, component, packageRevisions[firstPackage.UnitId]);
        RunCli(
            fixture,
            [
                "library", "reconcile",
                "--root", fixture.Root,
                "--inventory", fixture.ConfirmedPath,
                "--run-manifest", fixture.RunPath
            ]);
        var manifest = LibraryService.Parse(File.ReadAllBytes(fixture.RunPath));
        AssertEqual("incomplete", manifest.State, "missing component output prevents complete");
        AssertEqual(3, manifest.Units.Count(unit => unit.State == "completed"), "two packages and one component completed");
        AssertEqual(3, manifest.Units.Count(unit => unit.State != "completed"), "remaining component units noncompleted");

        var falseComplete = manifest with { State = "complete" };
        ExpectValidation(
            () => LibraryService.Validate(
                fixture.Root,
                fixture.Inventory,
                fixture.InventoryBytes,
                falseComplete),
            "missing units cannot claim complete");
    }

    private static void CreateComponentRevisions(
        Fixture fixture,
        IReadOnlyDictionary<string, string> packageRevisions)
    {
        foreach (var package in fixture.Inventory.Packages)
        {
            foreach (var component in package.Components)
            {
                if (!Directory.Exists(Path.Combine(fixture.Root, component.RevisionRoot)))
                {
                    CreateComponentRevision(
                        fixture,
                        component,
                        packageRevisions[package.UnitId]);
                }
            }
        }
    }

    private static void TestComponentInputIsolation(
        Fixture fixture,
        IReadOnlyDictionary<string, string> packageRevisions)
    {
        var package = fixture.Inventory.Packages[0];
        var sibling = package.Components[0];
        var target = package.Components[1];
        var siblingInputs = fixture.Packages
            .Single(item => item.Package == package.Package)
            .ComponentInputs[sibling.ComponentId];
        var inputPath = Path.Combine(fixture.Root, target.InputManifestPath);
        var inputBytes = File.ReadAllBytes(inputPath);
        var input = InputManifestService.Parse(inputBytes);
        var binding = RevisionService.LoadPackageBinding(
            fixture.Root,
            packageRevisions[package.UnitId],
            feedbackBytes: null);
        var initialized = AssessmentService.Initialize(
            "component",
            fixture.Root,
            input,
            inputBytes,
            target.ComponentId,
            [],
            binding);
        var leaks = new[]
        {
            new EvidenceProvenance(
                EvidenceIdentity.VendorPublicDocumentation,
                siblingInputs.Documentation.Single().Url,
                "Read sibling documentation.",
                "2026-09-02T21:00:00Z",
                siblingInputs.Documentation.Single().ContentDigest,
                "commitment-only"),
            new EvidenceProvenance(
                EvidenceIdentity.VendorSourceRepository,
                $"source:{sibling.AllowedSourcePaths.Single()}",
                "Read sibling source.",
                "2026-09-02T21:01:00Z",
                Hash("sibling-source"),
                "commitment-only"),
            new EvidenceProvenance(
                EvidenceIdentity.OwnerSuppliedInternalEvidence,
                siblingInputs.OwnerInputs.Single().Basename,
                "Read sibling owner evidence.",
                "2026-09-02T21:02:00Z",
                siblingInputs.OwnerInputs.Single().ContentDigest,
                "commitment-only"),
            new EvidenceProvenance(
                EvidenceIdentity.PackageArtifactMetadata,
                siblingInputs.PackageSources.Single().Locator,
                "Read sibling package entry.",
                "2026-09-02T21:03:00Z",
                Hash("sibling-package-entry"),
                "commitment-only")
        };
        foreach (var leak in leaks)
        {
            var evidence = BuildEvidence(initialized.Identity, [leak]);
            var assessment = Complete(initialized, evidence);
            ExpectValidation(
                () => AssessmentService.Validate(
                    fixture.Root,
                    assessment,
                    AssessmentService.Serialize(assessment),
                    input,
                    inputBytes,
                    evidence,
                    binding),
                $"component sibling {leak.Kind} leakage");
            Assert(
                !Directory.Exists(Path.Combine(fixture.Root, target.RevisionRoot)),
                $"component sibling {leak.Kind} leakage writes no revision");
        }
    }

    private static void CreateComponentRevision(
        Fixture fixture,
        InventoryComponent component,
        string packageRevision)
    {
        var inputPath = Path.Combine(fixture.Root, component.InputManifestPath);
        var inputBytes = File.ReadAllBytes(inputPath);
        var input = InputManifestService.Parse(inputBytes);
        var binding = RevisionService.LoadPackageBinding(
            fixture.Root,
            packageRevision,
            feedbackBytes: null);
        var initialized = AssessmentService.Initialize(
            "component",
            fixture.Root,
            input,
            inputBytes,
            component.ComponentId,
            [],
            binding);
        var evidence = BuildEvidence(initialized.Identity, component.RenderModes, input);
        var assessment = Complete(initialized, evidence);
        _ = Render(
            fixture.Root,
            inputPath,
            assessment,
            evidence,
            Path.Combine(fixture.Root, component.RevisionRoot),
            packageRevision);

        var claims = evidence.SourceLedgers.SelectMany(ledger => ledger.Ledger.Records)
            .Select(record => record.Claim)
            .ToArray();
        Assert(
            component.RenderModes.All(mode => claims.Any(claim => claim.Contains(mode, StringComparison.Ordinal))),
            $"every claimed render mode represented for {component.ComponentId}");
        Assert(
            fixture.Inventory.Packages.SelectMany(item => item.Components)
                .Where(item => item.UnitId != component.UnitId)
                .All(sibling => claims.All(claim =>
                    !claim.Contains(sibling.ComponentId, StringComparison.Ordinal))),
            $"no sibling component evidence leakage for {component.ComponentId}");
    }

    private static void TestCompleteReconcileAndIndex(Fixture fixture)
    {
        var validationPaths = fixture.Inventory.Packages
            .SelectMany(package =>
                new[] { package.RevisionRoot }
                    .Concat(package.Components.Select(component => component.RevisionRoot)))
            .Select(root => Directory.GetFiles(
                Path.Combine(fixture.Root, root),
                "*.validation.json",
                SearchOption.AllDirectories).Single())
            .ToArray();
        var before = validationPaths.ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        RunCli(
            fixture,
            [
                "library", "reconcile",
                "--root", fixture.Root,
                "--inventory", fixture.ConfirmedPath,
                "--run-manifest", fixture.RunPath
            ]);
        var runBytes = File.ReadAllBytes(fixture.RunPath);
        var manifest = LibraryService.Parse(runBytes);
        AssertEqual("complete", manifest.State, "complete library state");
        AssertEqual(6, manifest.Units.Count(unit => unit.State == "completed"), "all units completed");
        Assert(
            manifest.Units.All(unit =>
                unit.StateReceiptPath is null &&
                unit.StateReceiptDigest is null &&
                unit.TransitionReason is null),
            "completed validated revisions override transient receipt state");
        Assert(
            manifest.Units.Where(unit => unit.Kind == "component")
                .All(unit => unit.PackageUnitId is not null &&
                    unit.PackageValidationManifestDigest is not null),
            "every component names exact package unit and package validation revision");
        RunCli(
            fixture,
            [
                "library", "validate",
                "--root", fixture.Root,
                "--inventory", fixture.ConfirmedPath,
                "--run-manifest", fixture.RunPath
            ]);
        foreach (var pair in before)
        {
            AssertBytes(pair.Value, File.ReadAllBytes(pair.Key), "reconcile preserves completed manifest bytes");
        }

        var jsonPath = Path.Combine(fixture.Root, "library-index.json");
        var markdownPath = Path.Combine(fixture.Root, "library-index.md");
        RunCli(
            fixture,
            [
                "library", "index",
                "--root", fixture.Root,
                "--inventory", fixture.ConfirmedPath,
                "--run-manifest", fixture.RunPath,
                "--json", jsonPath,
                "--markdown", markdownPath
            ]);
        var json = File.ReadAllBytes(jsonPath);
        var markdown = File.ReadAllBytes(markdownPath);
        RunCli(
            fixture,
            [
                "library", "index",
                "--root", fixture.Root,
                "--inventory", fixture.ConfirmedPath,
                "--run-manifest", fixture.RunPath,
                "--json", jsonPath,
                "--markdown", markdownPath
            ]);
        AssertBytes(json, File.ReadAllBytes(jsonPath), "deterministic factual library JSON");
        AssertBytes(markdown, File.ReadAllBytes(markdownPath), "deterministic factual library Markdown");
        var text = Encoding.UTF8.GetString(markdown).ToLowerInvariant();
        foreach (var prohibited in new[] { "verdict", "ranking", "priority", "decision guidance", "recommendation" })
        {
            Assert(!text.Contains(prohibited, StringComparison.Ordinal), $"factual index excludes {prohibited}");
        }

        Assert(text.Contains("alpha.controls", StringComparison.Ordinal), "index includes first package");
        Assert(text.Contains("beta.controls", StringComparison.Ordinal), "index includes second package");
        Assert(text.Contains("**overall state:** `complete`", StringComparison.Ordinal), "index factual complete state");
        TestIndexPublicationRecovery(fixture, jsonPath, markdownPath, json, markdown);

        var output = new StringWriter();
        var error = new StringWriter();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(
                [
                    "inventory", "status",
                    "--root", fixture.Root,
                    "--inventory", fixture.ConfirmedPath,
                    "--run-manifest", fixture.RunPath
                ],
                output,
                error),
            $"inventory status: {error}");
        Assert(output.ToString().Contains("\"completed\":6", StringComparison.Ordinal), "status completed count");
    }

    private static void TestIndexPublicationRecovery(
        Fixture fixture,
        string jsonPath,
        string markdownPath,
        byte[] expectedJson,
        byte[] expectedMarkdown)
    {
        var publicationRoot = Path.Combine(fixture.Root, ".readiness-index");
        Directory.Delete(publicationRoot, recursive: true);
        File.Delete(jsonPath);
        File.Delete(markdownPath);

        var args = new[]
        {
            "library", "index",
            "--root", fixture.Root,
            "--inventory", fixture.ConfirmedPath,
            "--run-manifest", fixture.RunPath,
            "--json", jsonPath,
            "--markdown", markdownPath
        };
        try
        {
            LibraryIndexPublicationService.AfterGenerationCommitForTests =
                () => throw new IOException("deterministic failure after generation commit");
            AssertIndexFailure(args, "failure after generation commit");
        }
        finally
        {
            LibraryIndexPublicationService.AfterGenerationCommitForTests = null;
        }

        Assert(
            Directory.GetDirectories(
                Path.Combine(publicationRoot, "generations"),
                "GEN1-*",
                SearchOption.TopDirectoryOnly).Length == 1,
            "generation commit survives interrupted publication");
        Assert(
            !File.Exists(Path.Combine(publicationRoot, "current-generation.json")),
            "failure after generation commit does not publish a pointer");

        try
        {
            LibraryIndexPublicationService.BeforePointerUpdateForTests =
                () => throw new IOException("deterministic failure before pointer update");
            AssertIndexFailure(args, "failure before pointer update");
        }
        finally
        {
            LibraryIndexPublicationService.BeforePointerUpdateForTests = null;
        }

        RunCli(fixture, args);
        var pointerPath = Path.Combine(publicationRoot, "current-generation.json");
        Assert(File.Exists(pointerPath), "recovery publishes current-generation pointer");
        AssertBytes(expectedJson, File.ReadAllBytes(jsonPath), "recovery repairs JSON cache");
        AssertBytes(expectedMarkdown, File.ReadAllBytes(markdownPath), "recovery repairs Markdown cache");

        File.Delete(jsonPath);
        File.Delete(markdownPath);
        try
        {
            LibraryIndexPublicationService.AfterPointerUpdateForTests =
                () => throw new IOException("deterministic failure after pointer update");
            AssertIndexFailure(args, "failure after pointer update");
        }
        finally
        {
            LibraryIndexPublicationService.AfterPointerUpdateForTests = null;
        }

        Assert(File.Exists(pointerPath), "pointer remains authoritative after post-update failure");
        Assert(!File.Exists(jsonPath) && !File.Exists(markdownPath), "failed publish exposes no successful cache pair");
        RunCli(fixture, args);
        AssertBytes(expectedJson, File.ReadAllBytes(jsonPath), "post-pointer recovery repairs JSON");
        AssertBytes(expectedMarkdown, File.ReadAllBytes(markdownPath), "post-pointer recovery repairs Markdown");

        File.WriteAllText(jsonPath, "stale", new UTF8Encoding(false));
        RunCli(fixture, args);
        AssertBytes(expectedJson, File.ReadAllBytes(jsonPath), "stale JSON compatibility cache repaired");
        AssertBytes(expectedMarkdown, File.ReadAllBytes(markdownPath), "paired Markdown remains coherent");
        File.WriteAllText(markdownPath, "stale", new UTF8Encoding(false));
        RunCli(
            fixture,
            [
                "library", "validate",
                "--root", fixture.Root,
                "--inventory", fixture.ConfirmedPath,
                "--run-manifest", fixture.RunPath
            ]);
        AssertBytes(expectedJson, File.ReadAllBytes(jsonPath), "validation preserves JSON cache");
        AssertBytes(expectedMarkdown, File.ReadAllBytes(markdownPath), "validation repairs Markdown cache");

        var pointer = LibraryIndexPublicationService.Parse(File.ReadAllBytes(pointerPath));
        var generationJson = File.ReadAllBytes(Path.Combine(fixture.Root, pointer.JsonPath));
        var generationMarkdown = File.ReadAllBytes(Path.Combine(fixture.Root, pointer.MarkdownPath));
        AssertEqual(ContractJson.RawDigest(generationJson), pointer.JsonDigest, "pointer JSON digest");
        AssertEqual(ContractJson.RawDigest(generationMarkdown), pointer.MarkdownDigest, "pointer Markdown digest");
        AssertBytes(expectedJson, generationJson, "immutable generation JSON");
        AssertBytes(expectedMarkdown, generationMarkdown, "immutable generation Markdown");
    }

    private static void AssertIndexFailure(IReadOnlyList<string> args, string name)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert(
            CliApplication.Run(args, output, error) != ExitCodes.Success,
            $"{name} must not report success");
    }

    private static void TestCorruptManifestReconstruction(Fixture fixture)
    {
        var expected = File.ReadAllBytes(fixture.RunPath);
        File.Delete(fixture.RunPath);
        Reconcile(fixture);
        AssertBytes(expected, File.ReadAllBytes(fixture.RunPath), "missing run manifest reconstructs exactly");

        File.WriteAllText(fixture.RunPath, "{\"schema_version\":1", new UTF8Encoding(false));
        var output = new StringWriter();
        var error = new StringWriter();
        AssertEqual(
            ExitCodes.ValidationFailure,
            CliApplication.Run(
                [
                    "inventory", "status",
                    "--root", fixture.Root,
                    "--inventory", fixture.ConfirmedPath,
                    "--run-manifest", fixture.RunPath
                ],
                output,
                error),
            "truncated run manifest status invalid");
        Assert(output.ToString().Contains("\"state\":\"invalid\"", StringComparison.Ordinal), "status reports invalid state");
        Reconcile(fixture);
        AssertBytes(expected, File.ReadAllBytes(fixture.RunPath), "truncated run manifest reconstructs exactly");

        var parsed = LibraryService.Parse(expected);
        var stale = parsed with
        {
            InventoryDigest = new Sha256Digest("sha256", new string('f', 64))
        };
        File.WriteAllBytes(fixture.RunPath, LibraryService.Serialize(stale));
        Reconcile(fixture);
        AssertBytes(expected, File.ReadAllBytes(fixture.RunPath), "stale run manifest reconstructs exactly");
    }

    private static void TestPackageArtifactCannotSatisfyComponentHandoff(Fixture fixture)
    {
        var package = fixture.Inventory.Packages[0];
        var component = package.Components[0];
        var componentRoot = Path.Combine(fixture.Root, component.RevisionRoot);
        var componentRevision = Path.Combine(componentRoot, "0001");
        var savedComponentRevision = Path.Combine(fixture.Root, "saved-kind-mismatch-revision");
        Directory.Move(componentRevision, savedComponentRevision);
        var inputPath = Path.Combine(fixture.Root, package.InputManifestPath);
        var inputBytes = File.ReadAllBytes(inputPath);
        var input = InputManifestService.Parse(inputBytes);
        var unified = AssessmentService.Initialize(
            "unified",
            fixture.Root,
            input,
            inputBytes,
            component.ComponentId,
            []);
        var unifiedEvidence = BuildEvidence(unified.Identity, component.RenderModes, input);
        var unifiedAssessment = Complete(unified, unifiedEvidence);
        var repositoryEvidenceId = unifiedEvidence.SourceLedgers
            .Single(source => source.Ledger.LedgerKind == "repository")
            .Ledger.Records.Single()
            .StableId;
        unifiedAssessment = unifiedAssessment with
        {
            Rows = unifiedAssessment.Rows
                .Select((row, index) => index == 0
                    ? row with { EvidenceIds = [repositoryEvidenceId] }
                    : row)
                .ToArray()
        };
        _ = Render(
            fixture.Root,
            inputPath,
            unifiedAssessment,
            unifiedEvidence,
            componentRoot,
            packageRevision: null);
        try
        {
            ExpectValidation(
                () => LibraryService.Reconcile(
                    fixture.Root,
                    fixture.Inventory,
                    fixture.InventoryBytes),
                "unified artifact cannot satisfy split component handoff");
        }
        finally
        {
            Directory.Delete(componentRevision, recursive: true);
            Directory.Move(savedComponentRevision, componentRevision);
        }
    }

    private static void TestConflictsAndWrongPackage(Fixture fixture)
    {
        var first = fixture.Inventory.Packages[0].Components[0];
        var second = fixture.Inventory.Packages[1].Components[0];
        var secondRoot = Path.Combine(fixture.Root, second.RevisionRoot);
        var saved = Path.Combine(fixture.Root, "saved-second-revision");
        Directory.Move(Path.Combine(secondRoot, "0001"), saved);
        CopyDirectory(
            Path.Combine(fixture.Root, first.RevisionRoot, "0001"),
            Path.Combine(secondRoot, "0001"));
        try
        {
            ExpectValidation(
                () => LibraryService.Reconcile(
                    fixture.Root,
                    fixture.Inventory,
                    fixture.InventoryBytes),
                "conflicting duplicate manifest and component wrong package fail");
        }
        finally
        {
            Directory.Delete(Path.Combine(secondRoot, "0001"), recursive: true);
            Directory.Move(saved, Path.Combine(secondRoot, "0001"));
        }

        Reconcile(fixture);
        try
        {
            var revisionRoot = Path.Combine(fixture.Root, second.RevisionRoot);
            var revision = Path.Combine(revisionRoot, "0001");
            var savedRevision = Path.Combine(fixture.Root, "saved-linked-revision");
            Directory.Move(revision, savedRevision);
            Directory.CreateSymbolicLink(revision, savedRevision);
            ExpectValidation(
                () => LibraryService.Reconcile(
                    fixture.Root,
                    fixture.Inventory,
                    fixture.InventoryBytes),
                "library revision enumeration rejects linked revision directories");
            Directory.Delete(revision);
            Directory.Move(savedRevision, revision);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
        }
    }

    private static void TestCliSubprocess(Fixture fixture, string pluginRoot)
    {
        var root = Path.Combine(fixture.Root, "cli-library");
        Directory.CreateDirectory(Path.Combine(root, "inputs"));
        foreach (var file in Directory.GetFiles(Path.Combine(fixture.Root, "inputs")))
        {
            File.Copy(file, Path.Combine(root, "inputs", Path.GetFileName(file)));
        }
        foreach (var file in Directory.GetFiles(fixture.Root, "*.owner.txt", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        }

        File.Copy(fixture.CandidatesPath, Path.Combine(root, "inventory-candidates.json"));
        var draft = Path.Combine(root, "inventory.draft.json");
        var confirmed = Path.Combine(root, "inventory.confirmed.json");
        var run = Path.Combine(root, "run-manifest.json");
        var json = Path.Combine(root, "library-index.json");
        var markdown = Path.Combine(root, "library-index.md");
        var validator = Path.Combine(
            pluginRoot,
            "skills",
            "blazor-component-readiness",
            "scripts",
            "validator");
        var external = Path.Combine(root, "launcher-artifacts");
        RunLauncher(
            validator,
            root,
            external,
            [
                "inventory", "discover",
                "--root", root,
                "--candidates", Path.Combine(root, "inventory-candidates.json"),
                "--output", draft
            ]);
        var dll = Path.Combine(
            external,
            "bin",
            "Release",
            "net11.0",
            "BlazorComponentReadiness.Validator.dll");
        RunDll(dll, root, [
            "inventory", "confirm",
            "--root", root,
            "--draft", draft,
            "--output", confirmed
        ]);
        RunDll(dll, root, [
            "library", "reconcile",
            "--root", root,
            "--inventory", confirmed,
            "--run-manifest", run
        ]);
        RunDll(dll, root, [
            "library", "validate",
            "--root", root,
            "--inventory", confirmed,
            "--run-manifest", run
        ]);
        RunDll(dll, root, [
            "library", "index",
            "--root", root,
            "--inventory", confirmed,
            "--run-manifest", run,
            "--json", json,
            "--markdown", markdown
        ]);
        RunDll(dll, root, [
            "inventory", "status",
            "--root", root,
            "--inventory", confirmed,
            "--run-manifest", run
        ]);
        var inventory = InventoryService.Parse(File.ReadAllBytes(confirmed));
        Process first;
        Process second;
        using (LibraryRunLock.Acquire(root, inventory.RunId!))
        {
            first = StartDll(dll, root, [
                "library", "reconcile",
                "--root", root,
                "--inventory", confirmed,
                "--run-manifest", run
            ]);
            second = StartDll(dll, root, [
                "library", "reconcile",
                "--root", root,
                "--inventory", confirmed,
                "--run-manifest", run
            ]);
            Thread.Sleep(250);
            Assert(!first.HasExited && !second.HasExited, "concurrent reconciles wait for cross-process run lock");
        }
        WaitForProcess(first, "first concurrent reconcile");
        WaitForProcess(second, "second concurrent reconcile");
        first.Dispose();
        second.Dispose();

        Process reconcile;
        Process index;
        using (LibraryRunLock.Acquire(root, inventory.RunId!))
        {
            reconcile = StartDll(dll, root, [
                "library", "reconcile",
                "--root", root,
                "--inventory", confirmed,
                "--run-manifest", run
            ]);
            index = StartDll(dll, root, [
                "library", "index",
                "--root", root,
                "--inventory", confirmed,
                "--run-manifest", run,
                "--json", json,
                "--markdown", markdown
            ]);
            Thread.Sleep(250);
            Assert(!reconcile.HasExited && !index.HasExited, "reconcile and index share the cross-process run lock");
        }
        WaitForProcess(reconcile, "concurrent reconcile");
        WaitForProcess(index, "concurrent index");
        reconcile.Dispose();
        index.Dispose();
        using (var indexDocument = JsonDocument.Parse(File.ReadAllBytes(json)))
        {
            var manifestDigest = ContractJson.RawDigest(File.ReadAllBytes(run)).Value;
            var inventoryDigest = ContractJson.RawDigest(File.ReadAllBytes(confirmed)).Value;
            AssertEqual(
                manifestDigest,
                indexDocument.RootElement.GetProperty("run_manifest_sha256").GetProperty("value").GetString(),
                "JSON index binds final run manifest");
            AssertEqual(
                inventoryDigest,
                indexDocument.RootElement.GetProperty("inventory_sha256").GetProperty("value").GetString(),
                "JSON index binds final inventory");
            var markdownText = File.ReadAllText(markdown);
            Assert(
                markdownText.Contains(manifestDigest, StringComparison.Ordinal) &&
                markdownText.Contains(inventoryDigest, StringComparison.Ordinal) &&
                markdownText.Contains(
                    indexDocument.RootElement.GetProperty("generation_id").GetString()!,
                    StringComparison.Ordinal),
                "JSON and Markdown publish one coherent generation");
        }
        Assert(File.Exists(json) && File.Exists(markdown), "CLI subprocess covers all six library commands");
    }

    private static EvidenceBundle BuildEvidence(
        ExactAssessmentIdentity identity,
        IReadOnlyList<string> renderModes,
        InputManifest? input = null)
    {
        var repositoryEvidence = identity.AssessmentKind is "package" or "unified";
        var packageSource = input?.PackageSources.FirstOrDefault();
        var modes = repositoryEvidence
            ? new[] { "package-wide" }
            : renderModes.Count == 0 ? new[] { "package-wide" } : renderModes;
        var drafts = modes.Select((mode, index) => new EvidenceRecordDraft(
            repositoryEvidence
                ? "Exact package evidence supports this package-wide synthetic assessment."
                : $"The {identity.ComponentId} probe exercised render mode {mode}.",
            repositoryEvidence
                ? new EvidenceApplicability("repository-wide", null)
                : new EvidenceApplicability("component-specific", identity.ComponentId),
            new EvidenceProvenance(
                repositoryEvidence
                    ? EvidenceIdentity.PackageArtifactMetadata
                    : EvidenceIdentity.ReproducedRuntimeObservation,
                repositoryEvidence
                    ? packageSource?.Locator ?? NupkgInspector.WholePackageEvidenceLocator
                    : $"probe://{identity.Package.PackageId}/{identity.ComponentId}/render-mode/{mode}",
                identity.ComponentId is null
                    ? "Inspected the exact synthetic package."
                    : $"Ran the bounded synthetic {mode} probe.",
                $"2026-09-02T2{index}:00:00Z",
                new Sha256Digest(
                    "sha256",
                    repositoryEvidence
                        ? (packageSource?.ContentDigest ?? identity.Package.NupkgDigest).Value
                        : Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                            Encoding.UTF8.GetBytes($"{identity.Package.PackageId}\0{identity.ComponentId}\0{mode}")))),
                "commitment-only"),
            [])).ToList();
        if (identity.AssessmentKind == "component" && input is not null)
        {
            drafts.Add(new EvidenceRecordDraft(
                "The component-specific vendor documentation was reviewed.",
                new EvidenceApplicability("component-specific", identity.ComponentId),
                new EvidenceProvenance(
                    EvidenceIdentity.VendorPublicDocumentation,
                    input.Documentation.Single().Url,
                    "Read component documentation.",
                    "2026-09-02T22:00:00Z",
                    input.Documentation.Single().ContentDigest,
                    "commitment-only"),
                []));
            drafts.Add(new EvidenceRecordDraft(
                "The component-specific allowed source path was reviewed.",
                new EvidenceApplicability("component-specific", identity.ComponentId),
                new EvidenceProvenance(
                    EvidenceIdentity.VendorSourceRepository,
                    $"source:{input.Components.Single().AllowedSourcePaths.Single()}",
                    "Read component source.",
                    "2026-09-02T22:01:00Z",
                    input.SourceArtifacts.Single().ContentDigest,
                    "commitment-only"),
                []));
            drafts.Add(new EvidenceRecordDraft(
                "The component-specific owner evidence was reviewed.",
                new EvidenceApplicability("component-specific", identity.ComponentId),
                new EvidenceProvenance(
                    EvidenceIdentity.OwnerSuppliedInternalEvidence,
                    input.OwnerInputs.Single().Basename,
                    "Read component owner evidence.",
                    "2026-09-02T22:02:00Z",
                    input.OwnerInputs.Single().ContentDigest,
                    "commitment-only"),
                []));
            drafts.Add(new EvidenceRecordDraft(
                "The exact component-specific package entry was reviewed.",
                new EvidenceApplicability("component-specific", identity.ComponentId),
                new EvidenceProvenance(
                    EvidenceIdentity.PackageArtifactMetadata,
                    input.PackageSources.Single().Locator,
                    "Read exact component package entry.",
                    "2026-09-02T22:03:00Z",
                    input.PackageSources.Single().ContentDigest,
                    "commitment-only"),
                []));
        }

        IReadOnlyList<EvidenceSourceLedger> ledgers = identity.AssessmentKind == "component"
            ? [EvidenceLedgerBuilder.BuildComponentLedger(identity, drafts)]
            : [
                EvidenceLedgerBuilder.BuildRepositoryLedger(
                    new RepositoryLedgerSubject(
                        identity.AssessmentKind,
                        identity.Package,
                        identity.InputManifestDigest,
                        identity.AssessmentKind == "unified" ? identity.ComponentId : null),
                    drafts)
            ];
        var selectedEvidenceIds = ledgers
            .SelectMany(ledger => ledger.Records)
            .Select(record => record.StableId)
            .ToArray();
        return EvidenceLedgerBuilder.BuildBundle(
            identity,
            ledgers,
            selectedEvidenceIds);
    }

    private static EvidenceBundle BuildEvidence(
        ExactAssessmentIdentity identity,
        IReadOnlyList<EvidenceProvenance> provenances)
    {
        var drafts = provenances.Select((provenance, index) => new EvidenceRecordDraft(
            $"Selected component evidence record {index + 1}.",
            new EvidenceApplicability("component-specific", identity.ComponentId),
            provenance,
            [])).ToArray();
        var ledger = EvidenceLedgerBuilder.BuildComponentLedger(identity, drafts);
        return EvidenceLedgerBuilder.BuildBundle(
            identity,
            [ledger],
            ledger.Records.Select(record => record.StableId).ToArray());
    }

    private static ReadinessAssessment Complete(
        ReadinessAssessment initialized,
        EvidenceBundle evidence)
    {
        var evidenceIds = evidence.Selection.Select(item => item.EvidenceId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var rows = initialized.Rows.Select((row, index) =>
            index == 0
                ? row with
                {
                    Status = "verified",
                    Observation = "The retained deterministic evidence establishes this synthetic observation.",
                    EvidenceIds = evidenceIds
                }
                : row with
                {
                    Status = "not applicable",
                    NotApplicableRationale = "This deterministic fixture records bounded non-applicability."
                }).ToArray();
        return initialized with
        {
            Rows = rows,
            SummaryGroups = initialized.AssessmentKind == "package"
                ?
                [
                    new AssessmentSummaryGroup(
                        "verified",
                        "One package requirement is verified by retained deterministic evidence.",
                        [rows[0].Id],
                        evidenceIds),
                    new AssessmentSummaryGroup(
                        "not applicable",
                        "The remaining package requirements are synthetically not applicable.",
                        rows.Skip(1).Select(row => row.Id).ToArray(),
                        [])
                ]
                : [],
            CompletionState = "complete"
        };
    }

    private static string Render(
        string root,
        string inputPath,
        ReadinessAssessment assessment,
        EvidenceBundle evidence,
        string revisionRoot,
        string? packageRevision)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(revisionRoot)!);
        var stem = assessment.Identity.ComponentId ?? assessment.Identity.Package.PackageId;
        var assessmentPath = Path.Combine(root, $"{stem}.{assessment.AssessmentKind}.assessment.json");
        var evidencePath = Path.Combine(root, $"{stem}.{assessment.AssessmentKind}.evidence.json");
        File.WriteAllBytes(assessmentPath, AssessmentService.Serialize(assessment));
        File.WriteAllBytes(evidencePath, CanonicalEvidenceJson.SerializeBundle(evidence));
        var args = new List<string>
        {
            "report", "render",
            "--root", root,
            "--input", inputPath,
            "--assessment", assessmentPath,
            "--evidence", evidencePath,
            "--output", revisionRoot
        };
        if (packageRevision is not null)
        {
            args.Add("--package-revision");
            args.Add(packageRevision);
        }

        var output = new StringWriter();
        var error = new StringWriter();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(args, output, error),
            $"render {assessment.AssessmentKind}: {error}");
        return Path.Combine(revisionRoot, "0001");
    }

    private static void Reconcile(Fixture fixture, LibraryStateUpdate? update = null)
    {
        var args = new List<string>
        {
            "library", "reconcile",
            "--root", fixture.Root,
            "--inventory", fixture.ConfirmedPath,
            "--run-manifest", fixture.RunPath
        };
        if (update is not null)
        {
            var updatePath = Path.Combine(fixture.Root, "state-update.json");
            File.WriteAllBytes(updatePath, LibraryStateReceiptService.SerializeUpdate(update));
            args.Add("--state-update");
            args.Add(updatePath);
        }

        RunCli(fixture, args);
    }

    private static void RunCli(Fixture fixture, IReadOnlyList<string> args)
    {
        _ = fixture;
        RunCliRaw(args);
    }

    private static void RunCliRaw(IReadOnlyList<string> args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(args, output, error),
            $"CLI {string.Join(' ', args.Take(2))}: {error}");
    }

    private static void RunLauncher(
        string validator,
        string workingDirectory,
        string external,
        IReadOnlyList<string> args)
    {
        var info = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "pwsh" : "bash",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (OperatingSystem.IsWindows())
        {
            info.ArgumentList.Add("-NoLogo");
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-File");
            info.ArgumentList.Add(Path.Combine(validator, "run-validator.ps1"));
        }
        else
        {
            info.ArgumentList.Add(Path.Combine(validator, "run-validator.sh"));
        }

        foreach (var argument in args)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["READINESS_TEMP"] = external;
        RunProcess(info, "library launcher subprocess");
    }

    private static void RunDll(
        string dll,
        string workingDirectory,
        IReadOnlyList<string> args)
    {
        var info = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add(dll);
        foreach (var argument in args)
        {
            info.ArgumentList.Add(argument);
        }

        RunProcess(info, $"CLI subprocess {string.Join(' ', args.Take(2))}");
    }

    private static Process StartDll(
        string dll,
        string workingDirectory,
        IReadOnlyList<string> args)
    {
        var info = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add(dll);
        foreach (var argument in args)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info)
            ?? throw new InvalidOperationException("Could not start concurrent CLI subprocess.");
    }

    private static void WaitForProcess(Process process, string name)
    {
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(milliseconds: 180_000))
        {
            process.Kill(entireProcessTree: true);
            _ = process.WaitForExit(milliseconds: 5_000);
            _ = Task.WaitAll(new Task[] { stdout, stderr }, millisecondsTimeout: 5_000);
            throw new TimeoutException($"{name} did not exit within three minutes.");
        }

        if (!Task.WaitAll(
                new Task[] { stdout, stderr },
                millisecondsTimeout: 5_000))
        {
            throw new TimeoutException(
                $"{name} output streams did not drain within five seconds.");
        }
        Assert(
            process.ExitCode == ExitCodes.Success,
            $"{name} failed with {process.ExitCode}.\nstdout:\n{stdout.Result}\nstderr:\n{stderr.Result}");
    }

    private static void RunProcess(ProcessStartInfo info, string name)
    {
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"Could not start {name}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(milliseconds: 180_000))
        {
            process.Kill(entireProcessTree: true);
            _ = process.WaitForExit(milliseconds: 5_000);
            _ = Task.WaitAll(new Task[] { stdout, stderr }, millisecondsTimeout: 5_000);
            throw new TimeoutException($"{name} did not exit within three minutes.");
        }

        if (!Task.WaitAll(
                new Task[] { stdout, stderr },
                millisecondsTimeout: 5_000))
        {
            throw new TimeoutException(
                $"{name} output streams did not drain within five seconds.");
        }
        Assert(
            process.ExitCode == ExitCodes.Success,
            $"{name} failed with {process.ExitCode}.\nstdout:\n{stdout.Result}\nstderr:\n{stderr.Result}");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void CreatePackage(
        string path,
        string id,
        string version,
        IReadOnlyList<string>? contentEntries = null)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry($"{id}.nuspec", CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using (var stream = entry.Open())
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: false))
        {
            writer.Write($"<package><metadata><id>{id}</id><version>{version}</version></metadata></package>");
        }

        foreach (var contentEntry in contentEntries ?? [])
        {
            var content = archive.CreateEntry(contentEntry, CompressionLevel.NoCompression);
            content.LastWriteTime = entry.LastWriteTime;
            using var contentStream = content.Open();
            using var contentWriter = new StreamWriter(
                contentStream,
                new UTF8Encoding(false),
                leaveOpen: false);
            contentWriter.Write($"Package entry {contentEntry}.");
        }
    }

    private static Sha256Digest Hash(string value) =>
        new(
            "sha256",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(value))));

    private static void ExpectValidation(Action action, string name)
    {
        try
        {
            action();
            throw new InvalidOperationException($"{name}: expected deterministic validation failure.");
        }
        catch (DeterministicValidationException)
        {
        }
    }

    private static void AssertBytes(byte[] expected, byte[] actual, string name) =>
        Assert(expected.AsSpan().SequenceEqual(actual), $"{name}: byte mismatch.");

    private static void AssertEqual<T>(T expected, T actual, string name) =>
        Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"{name}: expected '{expected}', actual '{actual}'.");

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record PackageSet(
        EvidencePackageIdentity Package,
        string Acquisition,
        string PackageInputPath,
        IReadOnlyList<InputComponent> Components,
        IReadOnlyDictionary<string, string> ComponentInputPaths,
        IReadOnlyDictionary<string, ComponentInputs> ComponentInputs);

    private sealed record ComponentInputs(
        IReadOnlyList<InputDocument> Documentation,
        IReadOnlyList<InputPackageSource> PackageSources,
        IReadOnlyList<InputSourceArtifact> SourceArtifacts,
        IReadOnlyList<OwnerInput> OwnerInputs);

    private sealed record Fixture(
        string Root,
        string CandidatesPath,
        string DraftPath,
        string ConfirmedPath,
        string RunPath,
        LibraryInventory Inventory,
        byte[] InventoryBytes,
        IReadOnlyList<PackageSet> Packages);
}
