using System.Globalization;
using System.Text;
using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Release;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Preparation;

public static partial class PreparationService
{
    internal const string Profile = "authorized-package-48/1.0.0";
    internal const string Version = "package-prepare/1.0.0";
    internal static readonly string[] OperationIds =
        ["input.validate", "package.inspect", "source.inventory", "source.resolve", "release.collect"];
    private static readonly string[] Collectors =
        ["input-manifest/2", "nupkg-inspector/1", "source-inventory/1", "source-layout/1", "release-facts/1.0.0"];
    internal static Action? BeforeSealForTests { get; set; }

    public static PreparationReceipt Prepare(string root, PreparationRequest request)
    {
        root = Path.GetFullPath(root);
        PreparationCommand.ValidateOptions(request);
        request = request with { Input = Relative(root, request.Input), Output = Relative(root, request.Output) };
        var started = Clock();
        var generation = Guid.NewGuid().ToString("N");
        var inputInvocation = new PreparationInvocation(generation, "input.validate", Collector("input.validate"), root, request,
            ResolveCall("input.validate", root, request, null, [], null), null, started);
        var (inputBytes, input, subject, roles) = ValidateInput(root, request);
        var output = FullPath(root, request.Output, false);
        Require(!File.Exists(output) && !Directory.Exists(output), "Preparation requires a NEW output generation.");
        var reservationPath = request.Output + ".preparation-lock";
        var reservation = FullPath(root, reservationPath, false);
        Require(!File.Exists(reservation) && !Directory.Exists(reservation), "Preparation generation is already reserved.");
        var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        _ = FullPath(root, request.Output, false);
        // A persistent exclusive reservation prevents competing invocations from sharing a generation.
        using (var stream = new FileStream(reservation, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(Encoding.UTF8.GetBytes(generation));
            stream.Flush(true);
        }
        Require(!File.Exists(output) && !Directory.Exists(output), "Output appeared after generation reservation.");
        Directory.CreateDirectory(output);
        var snapshot = Write(root, request.Output + "/input.snapshot.json", inputBytes);
        var roleReference = Write(root, request.Output + "/roles.json", PreparationJson.Bytes(roles));
        var operations = new List<PreparationOperation>();
        FullSourceInventory? inventory = null;
        foreach (var id in OperationIds)
        {
            var missing = Missing(request, id, inventory is not null);
            var call = missing is not null ? null : ResolveCall(id, root, request, input, roles, inventory);
            var invocation = id == "input.validate" ? inputInvocation :
                new PreparationInvocation(generation, id, Collector(id), root, request, call,
                    call is null ? null : request.Output + "/" + id + ".json", Clock());
            var invocationRef = Write(root, request.Output + "/" + id + ".invocation.json", PreparationJson.Bytes(invocation));
            string outcome = "succeeded", cause = "completed", detail = "Collector completed; no readiness conclusion.";
            byte[]? bytes = null;
            if (missing is not null)
            {
                outcome = "not-attempted";
                cause = missing.Value.Cause;
                detail = missing.Value.Detail;
            }
            else
            {
                // Only known collector failures are converted to terminal failed operations.
                // Output/retention/seal errors remain fatal and are outside this catch.
                try
                {
                    switch (id)
                    {
                        case "input.validate":
                            detail = "Confirmed current input, authorized scope and exact selected roles validated.";
                            break;
                        case "package.inspect":
                            bytes = NupkgInspector.SerializeInspection(NupkgInspector.Inspect(
                                Argument(call!, "path")));
                            break;
                        case "source.inventory":
                            inventory = SourceArchiveCaptureService.InventoryFullArchive(Argument(call!, "root"),
                                Argument(call!, "archivePath"), Argument(call!, "archiveFormat"), Argument(call!, "expectedSha256"));
                            bytes = PreparationJson.Bytes(inventory);
                            break;
                        case "source.resolve":
                            var layout = SourceLayoutResolver.Resolve(inventory!, input,
                                call!.Arguments.Single(argument => argument.Name == "selectedId").Value);
                            bytes = PreparationJson.Bytes(layout);
                            outcome = layout.Outcome;
                            cause = layout.Cause;
                            detail = layout.Prefix is null ? "No source root selected; inspect retained candidates." :
                                "Selected archive-layout prefix once: " + layout.Prefix;
                            break;
                        case "release.collect":
                            var result = ReleaseFactsService.Collect(Argument(call!, "root"), Argument(call!, "input"),
                                Argument(call!, "releasePackage"), Argument(call!, "spdx22"), Argument(call!, "spdx22Entry"),
                                Argument(call!, "spdx30"), Argument(call!, "spdx30Entry"), Argument(call!, "provenance"),
                                Argument(call!, "sbomStatement"), Argument(call!, "output"));
                            bytes = result.Serialize();
                            break;
                    }
                }
                catch (Exception exception) when (exception is DeterministicValidationException or IOException)
                {
                    // Release publication failures are environmental, not a usable failed collection.
                    if (id == "release.collect" && exception is IOException) throw;
                    outcome = "failed";
                    cause = "collector-failure";
                    detail = exception.Message;
                    bytes = null;
                    if (id == "source.inventory") inventory = null;
                }
            }
            PreparationArtifact? outputRef = null;
            if (bytes is not null)
            {
                var path = request.Output + "/" + id + ".json";
                outputRef = id == "release.collect" ? Reference(root, path) : Write(root, path, bytes);
                Require(Read(root, outputRef).AsSpan().SequenceEqual(bytes), "Collector output bytes changed before retention.");
            }
            var diagnostic = new PreparationDiagnostic(generation, id, outcome, cause, BoundDiagnostic(detail), Clock());
            var diagnosticRef = Write(root, request.Output + "/" + id + ".diagnostic.json", PreparationJson.Bytes(diagnostic));
            operations.Add(new(id, outcome, cause, Dependencies(id), invocationRef, diagnosticRef, outputRef));
        }
        var mappings = new List<PreparationMapping>();
        foreach (var operation in operations.Where(op => op.Outcome == "succeeded" && Kind(op.Id) is not null))
        {
            Require(operation.Output is not null, "A successful collector is missing its output.");
            var basename = RetainedName(generation, operation.Id);
            var retained = Write(root, basename, Read(root, operation.Output!));
            mappings.Add(new(generation, operation.Id, "facts", Kind(operation.Id)!, basename, operation.Output!, retained));
        }
        var map = new PreparationRegistrationMap(1, generation, subject, Hash(inputBytes), mappings);
        var mapRef = Write(root, request.Output + "/registration-map.json", PreparationJson.Bytes(map));
        var receipt = new PreparationReceipt(1, Profile, Version, generation, subject, request,
            new(request.Input, inputBytes.LongLength, Hash(inputBytes)), snapshot, roleReference,
            Reference(root, reservationPath), operations, mapRef, started, Clock());
        BeforeSealForTests?.Invoke();
        ValidateContents(root, receipt);
        Write(root, request.Output + "/preparation.receipt.json", PreparationJson.Bytes(receipt));
        return receipt;
    }

    private static (byte[] Bytes, InputManifest Input, PreparationSubject Subject) LoadInput(string root, string path)
    {
        try
        {
            var bytes = BoundedIO.ReadAllBytes(FullPath(root, path, true), ResourceLimits.SerializedArtifactBytes, "preparation input");
            var input = InputManifestService.Parse(bytes);
            InputManifestService.Validate(input, root, requireConfirmed: true);
            var scope = AuthorizedPackageScope.Load(root, input)
                ?? throw new DeterministicValidationException("package prepare requires the existing pinned authorized-48 package scope.");
            Require(input.Components.Count == 0, "package prepare supports package-only inputs, not component selection.");
            return (bytes, input, new(input.Package, input.Source, scope.ManifestDigest.Value, scope.SelectedSetDigest.Value,
                scope.Requirements.Count));
        }
        catch (FileNotFoundException exception)
        {
            throw new DeterministicValidationException("Missing explicitly bound preparation input: " + exception.Message, exception);
        }
    }

    private static (byte[] Bytes, InputManifest Input, PreparationSubject Subject, PreparationRole[] Roles)
        ValidateInput(string root, PreparationRequest request)
    {
        var (bytes, input, subject) = LoadInput(root, request.Input);
        return (bytes, input, subject, BindRoles(root, input, request));
    }

    internal static PreparationCall ResolveCall(string id, string root, PreparationRequest request,
        InputManifest? input, PreparationRole[] roles, FullSourceInventory? inventory) => id switch
    {
        "input.validate" => new("PreparationService.ValidateInput",
            [new("root", root), new("requestSha256", Hash(PreparationJson.Bytes(request)))]),
        "package.inspect" => new("NupkgInspector.Inspect", [new("path", FullPath(root, input!.Package.NupkgPath, true))]),
        "source.inventory" => new("SourceArchiveCaptureService.InventoryFullArchive",
            [new("root", root), new("archivePath", request.SourceArchive), new("archiveFormat", request.ArchiveFormat),
             new("expectedSha256", roles.Single(role => role.Role == "source-archive").Artifact.Sha256)]),
        // Object arguments are identified by their exact serialized bindings, not invented shell argv.
        "source.resolve" => new("SourceLayoutResolver.Resolve",
            [new("inventoryPath", request.Output + "/source.inventory.json"), new("inventorySha256", Hash(PreparationJson.Bytes(inventory))),
             new("inputPath", request.Input), new("inputSha256", Hash(InputManifestService.Serialize(input!))),
             new("selectedId", request.SourceRootId)]),
        "release.collect" => new("ReleaseFactsService.Collect",
            [new("root", root), new("input", request.Input), new("releasePackage", request.ReleasePackage),
             new("spdx22", request.Spdx22), new("spdx22Entry", request.Spdx22Entry), new("spdx30", request.Spdx30),
             new("spdx30Entry", request.Spdx30Entry), new("provenance", request.Provenance), new("sbomStatement", request.SbomStatement),
             new("output", request.Output + "/release.collect.json")]),
        _ => throw new InvalidOperationException("Unknown producer-owned preparation operation.")
    };

    private static string Argument(PreparationCall call, string name) =>
        call.Arguments.Single(argument => argument.Name == name).Value
        ?? throw new InvalidOperationException("Required resolved collector argument is null: " + name);

    private static PreparationRole[] BindRoles(string root, InputManifest input, PreparationRequest request)
    {
        var roles = new List<PreparationRole>
        {
            new("target", "/package", Reference(root, input.Package.NupkgPath))
        };
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { input.Package.NupkgPath };
        foreach (var (role, selector) in Selections(request))
        {
            if (selector is null) continue;
            Require(Canonicalization.Basename(selector, role) == selector && paths.Add(selector),
                "Raw role selectors must be distinct exact registered basenames.");
            var matches = input.EvidenceInputs.Select((item, index) => (item.Basename, item.Size,
                    Hash: item.ContentDigest.Value, Pointer: $"/evidence_inputs/{index}"))
                .Concat(input.OwnerInputs.Select((item, index) => (item.Basename, item.Size,
                    Hash: item.ContentDigest.Value, Pointer: $"/owner_inputs/{index}")))
                .Where(item => item.Basename == selector).ToArray();
            Require(matches.Length == 1, $"Explicit role '{role}' must select exactly one registered input: {selector}");
            var match = matches[0];
            var expected = new PreparationArtifact(selector, match.Size, match.Hash);
            _ = Read(root, expected);
            roles.Add(new(role, match.Pointer, expected));
        }
        return roles.ToArray();
    }

    private static (string Role, string? Selector)[] Selections(PreparationRequest request) =>
    [
        ("source-archive", request.SourceArchive), ("release-package", request.ReleasePackage),
        ("spdx22", request.Spdx22), ("spdx30", request.Spdx30),
        ("provenance", request.Provenance), ("sbom-statement", request.SbomStatement)
    ];

    private static (string Cause, string Detail)? Missing(PreparationRequest request, string id, bool inventory)
    {
        if (id is "source.inventory" or "source.resolve" && request.SourceArchive is null)
            return ("missing-source-input", "The optional source archive role was not supplied.");
        if (id == "source.resolve" && !inventory)
            return ("source-inventory-failed", "source.inventory did not succeed.");
        var missing = Selections(request).Skip(1).Where(role => role.Selector is null).Select(role => role.Role).ToArray();
        return id == "release.collect" && missing.Length > 0
            ? ("missing-release-roles", "Missing explicit release roles: " + string.Join(", ", missing)) : null;
    }

    private static string Collector(string id) => Collectors[Array.IndexOf(OperationIds, id)];
    private static string[] Dependencies(string id) => id switch
    {
        "input.validate" => [],
        "source.resolve" => ["source.inventory"],
        _ => ["input.validate"]
    };
    private static string? Kind(string id) => id switch
    {
        "package.inspect" => "package-inspection", "source.inventory" => "source-inventory",
        "release.collect" => "offline-release-facts", _ => null
    };
    private static string RetainedName(string generation, string id) => $"prepare-{generation}-{id}.json";
    private static string Clock() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    private static string BoundDiagnostic(string detail) => detail.Length <= 8192 ? detail :
        detail[..8192] + " [diagnostic truncated at 8192 characters]";
    internal static string Hash(byte[] bytes) => ContractJson.RawDigest(bytes).Value;
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new DeterministicValidationException(message);
    }
    internal static string Relative(string root, string path)
    {
        var relative = Path.IsPathRooted(path) ? Path.GetRelativePath(root, path).Replace('\\', '/') : path;
        Require(Canonicalization.RelativePath(relative, "preparation path") == relative, "Preparation path must be canonical.");
        _ = FullPath(root, relative, false);
        return relative;
    }
    internal static string FullPath(string root, string relative, bool existing)
    {
        Require(Canonicalization.RelativePath(relative, "preparation path") == relative, "Preparation path must be canonical.");
        try { return SafePath.ResolveUnderRoot(root, relative, existing, existing); }
        catch (FileNotFoundException exception)
        {
            throw new DeterministicValidationException("Missing preparation binding: " + relative, exception);
        }
    }
    private static PreparationArtifact Write(string root, string path, byte[] bytes)
    {
        BoundedIO.EnsureLength(bytes.Length, ResourceLimits.SerializedArtifactBytes, "preparation output");
        AtomicFile.WriteNew(root, path, bytes);
        var reference = new PreparationArtifact(path, bytes.LongLength, Hash(bytes));
        _ = Read(root, reference);
        return reference;
    }
    private static PreparationArtifact Reference(string root, string path)
    {
        var bytes = BoundedIO.ReadAllBytes(FullPath(root, path, true),
            path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ? ResourceLimits.NupkgBytes :
            ResourceLimits.SerializedArtifactBytes, "preparation reference");
        return new(path, bytes.LongLength, Hash(bytes));
    }
    private static byte[] Read(string root, PreparationArtifact reference)
    {
        var path = FullPath(root, reference.Path, true);
        Require(reference.Size >= 0 && reference.Size <= ResourceLimits.NupkgBytes, "Invalid preparation reference size.");
        var bytes = BoundedIO.ReadAllBytes(path, Math.Max(reference.Size, 1), "preparation binding");
        Require(bytes.LongLength == reference.Size && Hash(bytes) == reference.Sha256,
            $"Preparation binding changed: {reference.Path}");
        return bytes;
    }
}
