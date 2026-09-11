using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Preparation;
using BlazorComponentReadiness.Validator.Release;

internal static class PreparationTests
{
    private const string Package = "Sample.Controls.0.1.2-alpha.3.nupkg";
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Wrapper = "sample-controls-" + Commit;
    private const string Source = Wrapper + ".tar.gz";
    private const string Spdx22 = "Sample.Controls.0.1.2-alpha.3.spdx-2.2.zip";
    private const string Spdx30 = "Sample.Controls.0.1.2-alpha.3.spdx-3.0.zip";
    private static int assertions;

    public static void Run(string repositoryRoot, string pluginRoot)
    {
        var parent = Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ?? Path.Combine(repositoryRoot, "artifacts");
        var root = Path.Combine(parent, $"preparation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("READINESS_SKILL_ROOT");
        Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", Path.Combine(pluginRoot, "skills", "blazor-component-readiness"));
        try
        {
            ArchiveAndLayout(root);
            StrictContracts();
            var fixture = new Fixture(Path.Combine(root, "synthetic"));
            Integration(fixture);
            OmissionsAndFailures(fixture);
            MutationAndRetention(fixture);
            Limits(fixture);
            fixture.Preserved();
            Console.WriteLine($"Preparation synthetic contract root: {fixture.Root}; no product assessment.");
            Console.WriteLine($"Preparation: {assertions} assertions passed.");
        }
        finally
        {
            PreparationService.BeforeSealForTests = null;
            Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", previous);
        }
    }

    private static void ArchiveAndLayout(string root)
    {
        var seed = new InputManifest(2, "confirmed", "published",
            new("unused", "1.0.0", new("sha256", new string('a', 64)), "unused.nupkg", "unused.nupkg", "local-file"),
            new("source-available", "https://github.com/example-org/sample-controls", Commit, "local correspondence", "high"),
            [new("source", $"https://codeload.github.com/example-org/sample-controls/zip/{Commit}", "direct-download", "succeeded", null)],
            [], [], [], [], [], [], []);
        FullSourceInventory Scan(string name, params (string Path, string Content)[] entries)
        {
            var path = Path.Combine(root, name);
            Zip(path, entries);
            return SourceArchiveCaptureService.InventoryFullArchive(root, name, "zip", Digest(path));
        }
        var inventory = Scan("wrapped.zip", (Wrapper + "/", ""), (Wrapper + "/src/", ""),
            (Wrapper + "/src/lib/", ""), (Wrapper + "/src/lib/a.cs", "a"), (Wrapper + "/src/lib/b.cs", "bc"));
        Assert(inventory.RegularFiles == 2 && inventory.Directories == 3 && inventory.LogicalMembers == 5 &&
            inventory.ExpandedFileBytes == 3 && inventory.TarStreamBytes is null, "full file/directory/byte accounting");
        var layout = SourceLayoutResolver.Resolve(inventory, seed, null);
        Assert(layout.Prefix == Wrapper && layout.Outcome == "succeeded", "strip exactly one recognized wrapper, not src/lib");
        Assert(layout.Candidates.Select(candidate => candidate.Prefix).SequenceEqual(new[] { "", Wrapper, Wrapper + "/src", Wrapper + "/src/lib" }),
            "ordinal full-coverage candidates");
        Assert(SourceLayoutResolver.Resolve(inventory, seed, layout.Candidates[0].Id).Cause == "invalid-source-root-id",
            "explicit root cannot conflict with packaging convention");
        Assert(SourceLayoutResolver.Resolve(inventory, seed, layout.Candidates[1].Id).Prefix == Wrapper,
            "explicit matching convention allowed");
        var unknown = seed with { RetrievalAttempts = [] };
        var ambiguous = SourceLayoutResolver.Resolve(inventory, unknown, null);
        Assert(ambiguous.Cause == "ambiguous-source-root" && ambiguous.Prefix is null, "no acquisition match means no guessed strip");
        Assert(SourceLayoutResolver.Resolve(inventory, unknown, ambiguous.Candidates.Last().Id).Prefix == Wrapper + "/src/lib",
            "explicit valid unknown-layout choice");
        var stale = SourceLayoutResolver.Resolve(inventory with { ArchiveSha256 = new string('b', 64) }, unknown, ambiguous.Candidates[1].Id);
        Assert(stale.Cause == "invalid-source-root-id", "root handle bound to exact archive digest");
        var flat = Scan("flat.zip", ("a.txt", "a"), ("src/b.txt", "b"));
        Assert(SourceLayoutResolver.Resolve(flat, seed, null).Prefix == "", "top-level regular files select flat root");
        Assert(SourceLayoutResolver.Resolve(flat, seed, "root-" + new string('0', 64)).Cause == "invalid-source-root-id", "wrong flat ID rejected");
        var split = Scan("split.zip", ("left/a", "a"), ("right/b", "b"));
        var splitLayout = SourceLayoutResolver.Resolve(split, unknown, null);
        Assert(splitLayout.Cause == "ambiguous-source-root" && splitLayout.Candidates.Count == 1, "no subtree-truncating candidate");
        Assert(SourceLayoutResolver.Resolve(split, unknown, splitLayout.Candidates.Single().Id).Prefix == "", "operator can choose covering archive root");
        var empty = Scan("empty.zip");
        Assert(empty.LogicalMembers == 0 && SourceLayoutResolver.Resolve(empty, seed, null).Cause == "empty-source-archive",
            "empty archive is inventory, not successful root");
        var directories = Scan("directories.zip", ("src/", ""));
        Assert(SourceLayoutResolver.Resolve(directories, seed, null).Cause == "empty-source-archive", "directory-only root unresolved");
        var outside = Scan("outside.zip", (Wrapper + "/a", "a"), ("other/", ""));
        Assert(SourceLayoutResolver.Resolve(outside, seed, null).Cause == "ambiguous-source-root", "all logical members must match codeload wrapper");

        var bad = new (string Path, string Content)[][]
        {
            [("../escape", "a")], [("/absolute", "a")], [("a\\b", "a")], [("a//b", "a")],
            [("a/./b", "a")], [("a", "a"), ("a/b", "b")], [("a/b", "b"), ("a", "a")],
            [("a/", ""), ("a", "a")], [("A/a", "a"), ("a/b", "b")], [("a", "a"), ("A", "a")],
            [("caf\u00e9", "a"), ("cafe\u0301", "b")], [("duplicate", "a"), ("duplicate", "b")],
            [("directory/", "not empty")], [(" space ", "a")], [("dir /a", "a")], [("dir./a", "a")],
            [(string.Join("/", Enumerable.Repeat("d", 65)), "a")], [(new string('a', 1025), "a")]
        };
        for (var index = 0; index < bad.Length; index++)
        {
            var current = index;
            Reject(() => Scan($"bad-{current}.zip", bad[current]), "new full-inventory path/collision guard");
        }
        foreach (var kind in new[] { 0xA000, 0x1000, 0x2000, 0x6000 })
        {
            var path = Path.Combine(root, $"kind-{kind}.zip");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                zip.CreateEntry("special").ExternalAttributes = kind << 16;
            Reject(() => SourceArchiveCaptureService.InventoryFullArchive(root, Path.GetFileName(path), "zip", Digest(path)),
                "unsupported ZIP native type");
        }
        foreach (var kind in new[] { TarEntryType.SymbolicLink, TarEntryType.HardLink, TarEntryType.Fifo })
        {
            var name = $"tar-{kind}.tar.gz";
            using (var file = File.Create(Path.Combine(root, name)))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            using (var tar = new TarWriter(gzip))
            {
                var entry = new PaxTarEntry(kind, "special");
                if (kind is TarEntryType.SymbolicLink or TarEntryType.HardLink) entry.LinkName = "target";
                tar.WriteEntry(entry);
            }
            Reject(() => SourceArchiveCaptureService.InventoryFullArchive(root, name, "tar.gz", Digest(Path.Combine(root, name))),
                "unsupported TAR kind");
        }
        var tarPath = Path.Combine(root, "metadata.tar.gz");
        using (var file = File.Create(tarPath))
        using (var gzip = new GZipStream(file, CompressionMode.Compress))
        using (var tar = new TarWriter(gzip))
        {
            tar.WriteEntry(new PaxGlobalExtendedAttributesTarEntry([new KeyValuePair<string, string>("comment", "fixture")]));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "src/"));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "src/a") { DataStream = new MemoryStream("abc"u8.ToArray()) });
        }
        var tarInventory = SourceArchiveCaptureService.InventoryFullArchive(root, "metadata.tar.gz", "tar.gz", Digest(tarPath));
        Assert(tarInventory.ReaderMetadataEntries == 1 && tarInventory.LogicalMembers == 2 &&
            tarInventory.TarNonFileBytes == tarInventory.TarStreamBytes - 3, "TAR metadata/framing separated truthfully from logical members");
        var capPath = Path.Combine(root, "entry-limit.zip");
        using (var zip = ZipFile.Open(capPath, ZipArchiveMode.Create))
            for (var index = 0; index <= ResourceLimits.SourceArchiveEntryCount; index++) zip.CreateEntry($"d{index}/");
        Reject(() => SourceArchiveCaptureService.InventoryFullArchive(root, "entry-limit.zip", "zip", Digest(capPath)), "archive entry ceiling");
        var largePath = Path.Combine(root, "expanded-limit.zip");
        using (var zip = ZipFile.Open(largePath, ZipArchiveMode.Create))
        using (var stream = zip.CreateEntry("payload").Open())
        {
            var buffer = new byte[1024 * 1024];
            for (var index = 0; index < 257; index++) stream.Write(buffer);
        }
        Reject(() => SourceArchiveCaptureService.InventoryFullArchive(root, "expanded-limit.zip", "zip", Digest(largePath)), "expanded byte ceiling");
        Reject(() => SourceArchiveCaptureService.InventoryFullArchive(root, "flat.zip", "zip", new string('0', 64)), "archive digest binding");
        var missingTree = Invoke("source", "inventory-archive", "--root", root, "--archive", "flat.zip",
            "--source-root", "no-extracted-tree", "--archive-format", "zip", "--output", "old-inventory.json");
        Assert(missingTree.Exit != 0, "old manual inventory still requires extracted source tree");
    }

    private static void StrictContracts()
    {
        var bytes = PreparationJson.Bytes(new PreparationArtifact("a.json", 1, new string('a', 64)));
        foreach (var text in new[]
        {
            Encoding.UTF8.GetString(bytes).Replace("\"size\":1,", ""),
            Encoding.UTF8.GetString(bytes).Replace("\"size\":1", "\"size\":1,\"size\":1"),
            Encoding.UTF8.GetString(bytes).Replace("\"size\":1", "\"size\":null"),
            Encoding.UTF8.GetString(bytes).Replace("\"size\":1", "\"size\":1,\"unknown\":0"),
            Encoding.UTF8.GetString(bytes).Replace("\"path\":\"a.json\"", "\"path\":null"),
            "null", "{}"
        })
            Reject(() => PreparationJson.Parse<PreparationArtifact>(Encoding.UTF8.GetBytes(text)), "closed strict canonical artifact");
        Reject(() => PreparationJson.Parse<PreparationArtifact[]>(" [null]"u8.ToArray()), "null array element");
        foreach (var args in new[]
        {
            new[] { "--source-archive", "a.zip" }, ["--archive-format", "zip"], ["--source-root-id", "root-x"],
            ["--spdx22", "a.zip"], ["--spdx22-entry", "a.json"], ["--spdx30", "a.zip"], ["--spdx30-entry", "a.json"],
            ["--source-archive", "a.zip", "--archive-format", "rar"]
        })
            Assert(Invoke(["package", "prepare", "--root", ".", "--input", "missing", "--output", "new", .. args]).Exit == 2,
                "paired selector usage error before filesystem collection");
        Assert(Invoke("package", "prepare", "--help").Output.Contains("accounting/correspondence"), "prepare help disclaims readiness");
    }

    private static void Integration(Fixture f)
    {
        f.Success = f.Prepare(f.Arguments("success"));
        Assert(f.Success.Operations.All(op => op.Outcome == "succeeded"), "all synthetic collectors succeed");
        var inventory = f.Read<FullSourceInventory>(f.Success.Operations[2].Output!.Path);
        var layout = f.Read<SourceLayout>(f.Success.Operations[3].Output!.Path);
        Assert(inventory.RegularFiles == 1332 && inventory.Directories == 160 && inventory.LogicalMembers == 1492,
            "synthetic FULL archive counts 1332/160/1492");
        Assert(layout.Prefix == Wrapper && inventory.Members.All(member => member.Path.StartsWith(Wrapper + "/", StringComparison.Ordinal)),
            "synthetic single wrapper and no omitted members");
        Console.WriteLine($"Synthetic source inventory: regular-files={inventory.RegularFiles} directories={inventory.Directories} logical-members={inventory.LogicalMembers} reader-metadata={inventory.ReaderMetadataEntries}; prefix={layout.Prefix}");
        var facts = f.Read<ReleaseFactsResult>(f.Success.Operations[4].Output!.Path);
        Assert(facts.Comparisons.Count == 11 && facts.Comparisons.Any(item => item.Outcome == "mismatch") &&
            facts.Comparisons.Any(item => item.Outcome == "not-comparable"), "adverse facts remain succeeded usable collection");
        var calls = f.Success.Operations.ToDictionary(op => op.Id, op => f.Read<PreparationInvocation>(op.Invocation.Path));
        Assert(calls["package.inspect"].Call!.Arguments.Single().Value == Path.GetFullPath(f.Path("target/" + Package)),
            "resolved package call retains actual manifest target path");
        Assert(calls["source.inventory"].Call!.Arguments.Single(arg => arg.Name == "expectedSha256").Value == inventory.ArchiveSha256 &&
            calls["source.inventory"].Call!.Arguments.Single(arg => arg.Name == "archivePath").Value == Source,
            "resolved full inventory call retains exact archive and expected digest");
        Assert(calls["release.collect"].Call!.Arguments.Single(arg => arg.Name == "output").Value == "success/release.collect.json" &&
            calls["release.collect"].OutputPath == "success/release.collect.json" && calls["release.collect"].WrapperRequest.Output == "success",
            "resolved release call output distinguished from wrapper generation directory");
        Assert(calls["source.resolve"].Call!.Arguments.Single(arg => arg.Name == "inventorySha256").Value == f.Success.Operations[2].Output!.Sha256,
            "layout object argument bound to exact serialized full inventory");
        var map = f.Read<PreparationRegistrationMap>(f.Success.RegistrationMap.Path);
        Assert(map.Entries.Count == 3 && map.Entries.All(entry => !entry.Retained.Path.Contains('/') &&
            File.ReadAllBytes(f.Path(entry.Original.Path)).SequenceEqual(File.ReadAllBytes(f.Path(entry.Retained.Path)))),
            "every success output retained at a real independent root basename");
        f.Validate(f.Success, f.Input);
        var repeated = f.Prepare(f.Arguments("repeat"));
        Assert(f.Success.GenerationId != repeated.GenerationId &&
            PreparationJson.Same(inventory, f.Read<FullSourceInventory>(repeated.Operations[2].Output!.Path)) &&
            PreparationJson.Same(layout, f.Read<SourceLayout>(repeated.Operations[3].Output!.Path)),
            "fresh generations repeat facts/layout without a normalized subsystem");
        var subset = f.ConfirmAdditions(map.Entries.Take(1));
        f.Validate(f.Success, subset);
        var final = f.ConfirmAdditions(map.Entries);
        f.Validate(f.Success, final);
        f.Validate(f.Success, final);
        f.Evidence(final, map.Entries.Single(entry => entry.Kind == "offline-release-facts"));
        var packageOutput = f.New("manual-inspection");
        f.Ok("package", "inspect", "--nupkg", f.Path("target/" + Package), "--output", packageOutput);
        using var manual = JsonDocument.Parse(File.ReadAllBytes(packageOutput));
        using var prepared = JsonDocument.Parse(File.ReadAllBytes(f.Path(f.Success.Operations[1].Output!.Path)));
        Assert(JsonElement.DeepEquals(manual.RootElement, prepared.RootElement), "manual package inspection JSON unchanged");
        Assert(f.Invoke(f.Arguments("success")).Exit != 0, "existing generation never overwritten");
    }

    private static void OmissionsAndFailures(Fixture f)
    {
        var none = f.Prepare(["package", "prepare", "--root", f.Root, "--input", f.Input, "--output", "no-optional-roles"]);
        Assert(none.Operations.Count(op => op.Outcome == "succeeded") == 2 &&
            none.Operations.Count(op => op.Outcome == "not-attempted") == 3, "no optional roles still gives usable package facts and complete accounting");
        var missingScope = f.WriteManifest(f.Manifest(f.Input) with
        {
            EvidenceInputs = f.Manifest(f.Input).EvidenceInputs.Where(item => item.Kind != "authorized-package-report-scope").ToArray()
        });
        Assert(f.Invoke(Replace(f.Arguments("missing-scope"), "--input", missingScope)).Exit == 1, "pinned scope required, no synthetic authority bypass");
        var draft = f.WriteManifest(f.Manifest(f.Input) with { State = "draft" });
        Assert(f.Invoke(Replace(f.Arguments("unconfirmed"), "--input", draft)).Exit == 1, "confirmation required");
        var omittedSource = f.Prepare(Remove(Remove(f.Arguments("no-source"), "--source-archive"), "--archive-format"));
        Assert(omittedSource.Operations[2].Cause == "missing-source-input" && omittedSource.Operations[3].Outcome == "not-attempted" &&
            omittedSource.Operations[4].Outcome == "succeeded", "omitted source does not block release");
        foreach (var role in new[] { "--release-package", "--spdx22", "--spdx30", "--provenance", "--sbom-statement" })
        {
            var args = Remove(f.Arguments("missing-role-" + role[2..]), role);
            if (role is "--spdx22" or "--spdx30") args = Remove(args, role + "-entry");
            var receipt = f.Prepare(args);
            var diagnostic = f.Read<PreparationDiagnostic>(receipt.Operations[4].Diagnostic.Path);
            Assert(receipt.Operations[4].Outcome == "not-attempted" && diagnostic.Detail.Contains(role[2..], StringComparison.Ordinal),
                "missing release role named, independent inventory kept");
        }
        foreach (var role in new[] { "--source-archive", "--release-package", "--spdx22", "--spdx30", "--provenance", "--sbom-statement" })
        {
            var args = Replace(f.Arguments("unregistered-" + role[2..]), role, "unregistered.bin");
            Assert(f.Invoke(args).Exit == 1 && !File.Exists(f.Path(Value(args, "--output") + "/preparation.receipt.json")),
                "explicit unregistered role fatal");
        }
        foreach (var role in new[] { "--spdx22-entry", "--spdx30-entry" })
        {
            var partial = f.Prepare(Replace(f.Arguments("missing-inner-" + role[2..]), role, "missing.json"));
            Assert(partial.Operations[4].Outcome == "failed" && partial.Operations[2].Outcome == "succeeded" &&
                f.Read<PreparationRegistrationMap>(partial.RegistrationMap.Path).Entries.Count == 2, "inner missing entry records actual failure and keeps independent inventory");
            Assert(f.Read<PreparationInvocation>(partial.Operations[4].Invocation.Path).WrapperRequest.Spdx22Entry == (role == "--spdx22-entry" ? "missing.json" : "spdx_2.2/manifest.spdx.json"),
                "actual failure selectors retained");
            f.Validate(partial, f.Input);
        }
        var wrong = f.Prepare([.. f.Arguments("wrong-root"), "--source-root-id", "root-" + new string('0', 64)]);
        Assert(wrong.Operations[3].Cause == "invalid-source-root-id" && wrong.Operations[4].Outcome == "succeeded" &&
            f.Read<PreparationRegistrationMap>(wrong.RegistrationMap.Path).Entries.Count == 3, "wrong root preserves full inventory and release evidence");
        f.Validate(wrong, f.Input);
        foreach (var (name, entries, expected) in new[]
        {
            ("unknown.zip", new[] { ("meaningful/src/a.cs", "a") }, "ambiguous-source-root"),
            ("empty-source.zip", Array.Empty<(string, string)>(), "empty-source-archive"),
            ("bad-source.zip", new[] { ("../a", "a") }, "source-inventory-failed")
        })
        {
            Zip(f.Path(name), entries);
            var input = f.WithSupplement(name);
            var args = Replace(Replace(Replace(f.Arguments("case-" + name), "--source-archive", name), "--archive-format", "zip"), "--input", input);
            var receipt = f.Prepare(args);
            Assert(receipt.Operations[3].Cause == expected && receipt.Operations[4].Outcome == "succeeded", "independent release continues after source failure/ambiguity");
            f.Validate(receipt, input);
            if (expected == "ambiguous-source-root")
            {
                var layout = f.Read<SourceLayout>(receipt.Operations[3].Output!.Path);
                var selected = f.Prepare([.. Replace(args, "--output", "selected-root"), "--source-root-id", layout.Candidates.Last().Id]);
                Assert(selected.Operations[3].Outcome == "succeeded", "listed unknown-layout candidate works in fresh generation");
            }
        }
        File.WriteAllText(f.Path("malformed.json"), "{}");
        var malformedInput = f.WithSupplement("malformed.json", owner: true);
        var malformed = f.Prepare(Replace(Replace(f.Arguments("malformed-payload"), "--provenance", "malformed.json"), "--input", malformedInput));
        Assert(malformed.Operations[4].Outcome == "failed" && malformed.Operations[2].Outcome == "succeeded", "registered owner role accepted; malformed inner DSSE is collector failure");
        var malformedSpdx = f.Path("malformed-spdx.zip");
        Zip(malformedSpdx, ("bad.json", "{}"));
        var malformedSpdxInput = f.WithSupplement("malformed-spdx.zip");
        var badSpdx = f.Prepare(Replace(Replace(Replace(f.Arguments("malformed-spdx"), "--input", malformedSpdxInput),
            "--spdx22", "malformed-spdx.zip"), "--spdx22-entry", "bad.json"));
        Assert(badSpdx.Operations[4].Outcome == "failed" && badSpdx.Operations[2].Outcome == "succeeded", "malformed selected SPDX document is attempted failure");
        var ownerInput = f.AsOwner("provenance.sigstore.json");
        var owner = f.Prepare(Replace(f.Arguments("owner-role"), "--input", ownerInput));
        Assert(owner.Operations[4].Outcome == "succeeded", "release owner-input role semantics preserved");
        foreach (var name in new[] { Source, Package, Spdx22, Spdx30, "provenance.sigstore.json", "sbom.sigstore.json" })
        {
            var path = f.Path(name);
            var bytes = File.ReadAllBytes(path);
            try
            {
                File.WriteAllBytes(path, [.. bytes, (byte)0]);
                Assert(f.Invoke(f.Arguments("mutated-" + name)).Exit == 1, "mutated bound artifact fatal preflight");
                File.Delete(path);
                Assert(f.Invoke(f.Arguments("absent-" + name)).Exit == 1, "missing bound artifact fatal preflight");
            }
            finally { File.WriteAllBytes(path, bytes); }
        }
        var collision = Replace(f.Arguments("role-alias"), "--spdx30", Spdx22);
        Assert(f.Invoke(collision).Exit == 1, "same registered input cannot fill ambiguous roles");
        var unsafePath = Replace(f.Arguments("unsafe"), "--source-archive", "../" + Source);
        Assert(f.Invoke(unsafePath).Exit == 1, "unsafe outer selection fatal");
    }

    private static void MutationAndRetention(Fixture f)
    {
        var receipt = f.Success;
        var receiptPath = receipt.Request.Output + "/preparation.receipt.json";
        var original = File.ReadAllBytes(f.Path(receiptPath));
        foreach (var mutation in new Action<JsonObject>[]
        {
            node => node["schema_version"] = 2,
            node => node["profile"] = "other",
            node => node["generation_id"] = Guid.NewGuid().ToString("N"),
            node => node.Remove("roles"),
            node => node["extra"] = true,
            node => node["operations"]![0]!["outcome"] = "not-applicable",
            node => node["operations"]![1]!["outcome"] = "not-attempted",
            node => node["operations"]![1]!["cause"] = "invented",
            node => node["operations"]!.AsArray().RemoveAt(2),
            node => node["operations"]!.AsArray().Add(node["operations"]![0]!.DeepClone()),
            node => node["request"]!["spdx22_entry"] = "wrong.json",
            node => node["subject"]!["source"]!["commit"] = new string('a', 40),
            node => node["input_snapshot"]!["path"] = "../escape",
            node => node["completed_at_utc"] = "bad"
        })
        {
            var node = JsonNode.Parse(original)!.AsObject();
            mutation(node);
            File.WriteAllBytes(f.Path(receiptPath), Encoding.UTF8.GetBytes(node.ToJsonString()));
            Assert(f.ValidateExit(receipt, f.Input) == 1, "mutated receipt rejected");
        }
        File.WriteAllBytes(f.Path(receiptPath), original);
        var inventory = f.Read<FullSourceInventory>(receipt.Operations[2].Output!.Path);
        var roles = f.Read<PreparationRole[]>(receipt.Roles.Path);
        var invocations = receipt.Operations.ToDictionary(op => op.Id, op => File.ReadAllBytes(f.Path(op.Invocation.Path)));
        foreach (var mode in new[] { "spdx22-entry", "spdx30-entry", "call-output", "call-target", "call-archive-digest" })
        {
            var request = mode switch
            {
                "spdx22-entry" => receipt.Request with { Spdx22Entry = "different.spdx.json" },
                "spdx30-entry" => receipt.Request with { Spdx30Entry = "different.spdx.json" },
                _ => receipt.Request
            };
            var operations = new List<PreparationOperation>();
            foreach (var operation in receipt.Operations)
            {
                var invocation = PreparationJson.Parse<PreparationInvocation>(invocations[operation.Id]) with
                {
                    WrapperRequest = request,
                    Call = PreparationService.ResolveCall(operation.Id, f.Root, request, f.Manifest(f.Input), roles, inventory)
                };
                var argument = (mode, operation.Id) switch
                {
                    ("call-output", "release.collect") => "output",
                    ("call-target", "package.inspect") => "path",
                    ("call-archive-digest", "source.inventory") => "expectedSha256",
                    _ => null
                };
                if (argument is not null)
                    invocation = invocation with { Call = invocation.Call! with
                    {
                        Arguments = invocation.Call!.Arguments.Select(arg => arg.Name == argument ? arg with { Value = "incorrect" } : arg).ToArray()
                    } };
                var bytes = PreparationJson.Bytes(invocation);
                File.WriteAllBytes(f.Path(operation.Invocation.Path), bytes);
                operations.Add(operation with { Invocation = new(operation.Invocation.Path, bytes.Length, PreparationService.Hash(bytes)) });
            }
            File.WriteAllBytes(f.Path(receiptPath), PreparationJson.Bytes(receipt with { Request = request, Operations = operations }));
            var result = f.Invoke("inputs", "validate", "--root", f.Root, "--manifest", f.Input, "--preparation", f.Path(receiptPath));
            Assert(result.Exit == 1 && result.Error.Contains(mode.EndsWith("-entry", StringComparison.Ordinal) ?
                "SPDX fact containers" : "Resolved collector call", StringComparison.Ordinal),
                "coherently rehashed invocation/request must match actual call and selected-entry result locators: " + mode);
            foreach (var operation in receipt.Operations) File.WriteAllBytes(f.Path(operation.Invocation.Path), invocations[operation.Id]);
            File.WriteAllBytes(f.Path(receiptPath), original);
        }
        foreach (var reference in new[]
        {
            receipt.InputSnapshot, receipt.Roles, receipt.RegistrationMap, receipt.Reservation,
            receipt.Operations[2].Output!, receipt.Operations[3].Output!,
            receipt.Operations[4].Invocation, receipt.Operations[4].Diagnostic
        })
        {
            var path = f.Path(reference.Path);
            var bytes = File.ReadAllBytes(path);
            try
            {
                File.WriteAllBytes(path, [.. bytes, (byte)0]);
                Assert(f.ValidateExit(receipt, f.Input) == 1, "post-seal dependency mutation rejected");
                File.Delete(path);
                Assert(f.ValidateExit(receipt, f.Input) == 1, "post-seal missing dependency rejected");
            }
            finally { File.WriteAllBytes(path, bytes); }
        }
        var map = f.Read<PreparationRegistrationMap>(receipt.RegistrationMap.Path);
        foreach (var mutated in new[]
        {
            map with { Entries = map.Entries.Take(2).ToArray() },
            map with { Entries = [map.Entries[0], map.Entries[0], map.Entries[2]] },
            map with { Entries = map.Entries.Select(entry => entry with { Kind = "root-context" }).ToArray() },
            map with { Entries = map.Entries.Select(entry => entry with { GenerationId = new string('a', 32) }).ToArray() },
            map with { Entries = map.Entries.Select(entry => entry with { Original = entry.Original with { Path = receipt.Operations[3].Output!.Path } }).ToArray() }
        })
        {
            f.RewriteDependency(receipt, receipt.RegistrationMap, PreparationJson.Bytes(mutated));
            Assert(f.ValidateExit(receipt, f.Input) == 1, "coherently rehashed missing/duplicate/arbitrary/mixed-generation map rejected");
            File.WriteAllBytes(f.Path(receipt.RegistrationMap.Path), PreparationJson.Bytes(map));
            File.WriteAllBytes(f.Path(receiptPath), original);
        }
        var retained = f.Path(map.Entries[0].Retained.Path);
        var retainedBytes = File.ReadAllBytes(retained);
        File.WriteAllBytes(retained, [.. retainedBytes, (byte)0]);
        Assert(f.ValidateExit(receipt, f.Input) == 1, "root-level retained copy mutation rejected");
        File.WriteAllBytes(retained, retainedBytes);
        var final = f.Manifest(f.Input);
        var arbitrary = f.WriteManifest(final with
        {
            EvidenceInputs = [.. final.EvidenceInputs, new("malformed.json", "offline-release-facts", new("sha256", Digest(f.Path("malformed.json"))), 2)]
        });
        Assert(f.ValidateExit(receipt, arbitrary) == 1, "arbitrary final evidence additions rejected");
        var removal = f.WriteManifest(final with { EvidenceInputs = final.EvidenceInputs.Skip(1).ToArray() });
        Assert(f.ValidateExit(receipt, removal) == 1, "original registration removal rejected");
        var changed = f.WriteManifest(final with { Source = final.Source with { Mapping = "changed" } });
        Assert(f.ValidateExit(receipt, changed) == 1, "changed final input source rejected");
        var failed = f.Read<PreparationReceipt>("missing-inner-spdx22-entry/preparation.receipt.json");
        var failedMap = f.Read<PreparationRegistrationMap>(failed.RegistrationMap.Path);
        var failedAddition = failedMap with { Entries = [.. failedMap.Entries, map.Entries[2]] };
        f.RewriteDependency(failed, failed.RegistrationMap, PreparationJson.Bytes(failedAddition));
        Assert(f.ValidateExit(failed, f.Input) == 1, "failed collector cannot acquire another generation's successful registration");
        File.WriteAllBytes(f.Path(failed.RegistrationMap.Path), PreparationJson.Bytes(failedMap));
        File.WriteAllBytes(f.Path(failed.Request.Output + "/preparation.receipt.json"), PreparationJson.Bytes(failed));
        PreparationService.BeforeSealForTests = () =>
        {
            Assert(!File.Exists(f.Path("interrupt/preparation.receipt.json")), "receipt absent until seal");
            throw new InvalidOperationException("Deliberate pre-seal interruption");
        };
        Assert(f.Invoke(Remove(Remove(f.Arguments("interrupt"), "--source-archive"), "--archive-format")).Exit == 3 &&
            !File.Exists(f.Path("interrupt/preparation.receipt.json")), "unexpected interruption never seals or becomes successful accounting");
        PreparationService.BeforeSealForTests = null;
        Assert(f.Invoke(f.Arguments("interrupt")).Exit == 1, "unsealed generation cannot resume");
        PreparationService.BeforeSealForTests = () => File.AppendAllText(f.Path("seal-mutation/input.snapshot.json"), " ");
        Assert(f.Invoke(f.Arguments("seal-mutation")).Exit == 1 && !File.Exists(f.Path("seal-mutation/preparation.receipt.json")),
            "mutation at seal boundary rejected");
        PreparationService.BeforeSealForTests = null;
        PreparationService.BeforeSealForTests = () => File.WriteAllText(f.Path("seal-race/preparation.receipt.json"), "occupied");
        Assert(f.Invoke(f.Arguments("seal-race")).Exit == 3 &&
            File.ReadAllText(f.Path("seal-race/preparation.receipt.json")) == "occupied", "atomic receipt publication refuses a racing file without overwrite");
        PreparationService.BeforeSealForTests = null;
        int nestedExit = -1;
        PreparationService.BeforeSealForTests = () =>
        {
            PreparationService.BeforeSealForTests = null;
            nestedExit = f.Invoke(f.Arguments("race")).Exit;
        };
        _ = f.Prepare(f.Arguments("race"));
        Assert(nestedExit == 1, "competing same-generation invocation refused by reservation");
        File.WriteAllText(f.Path("success/unreferenced.json"), "{}");
        Assert(f.ValidateExit(receipt, f.Input) == 1, "unreferenced generation additions rejected");
        File.Delete(f.Path("success/unreferenced.json"));
        if (!OperatingSystem.IsWindows())
        {
            File.Delete(retained);
            File.CreateSymbolicLink(retained, f.Path(map.Entries[0].Original.Path));
            Assert(f.ValidateExit(receipt, f.Input) == 1, "retention symlink rejected even with matching bytes");
            File.Delete(retained);
            File.WriteAllBytes(retained, retainedBytes);
        }
        f.Validate(receipt, f.Input);
        var preInputBytes = File.ReadAllBytes(f.Input);
        try
        {
            File.WriteAllBytes(f.Input, [.. preInputBytes, (byte)'\n']);
            Assert(f.ValidateExit(receipt, f.Input) == 1, "changed pre-input bytes reject reuse");
        }
        finally { File.WriteAllBytes(f.Input, preInputBytes); }
    }

    private static void Limits(Fixture f)
    {
        var input = f.Manifest(f.Input);
        var additions = new List<InputEvidenceArtifact>();
        for (var index = input.EvidenceInputs.Count + input.OwnerInputs.Count; index < 32; index++)
        {
            var basename = $"cap-{index}.txt";
            File.WriteAllText(f.Path(basename), "x");
            additions.Add(new(basename, "raw", new("sha256", Digest(f.Path(basename))), 1));
        }
        var full = input with { EvidenceInputs = [.. input.EvidenceInputs, .. additions] };
        var capInput = f.WriteManifest(full);
        var receipt = f.Prepare(Replace(f.Arguments("full-32"), "--input", capInput));
        f.Validate(receipt, capInput);
        var mapping = f.Read<PreparationRegistrationMap>(receipt.RegistrationMap.Path).Entries[0];
        var over = f.WriteManifest(full with
        {
            EvidenceInputs = [.. full.EvidenceInputs, new(mapping.Basename, mapping.Kind, new("sha256", mapping.Retained.Sha256), mapping.Retained.Size)]
        });
        Assert(f.ValidateExit(receipt, over) == 1, "base 32 plus mapped addition exceeds combined count");
        var ownerOver = f.WriteManifest(full with
        {
            OwnerInputs = [new(mapping.Basename, EvidenceIdentity.OwnerSuppliedPublicEvidence, new("sha256", mapping.Retained.Sha256), mapping.Retained.Size)]
        });
        Assert(f.ValidateExit(receipt, ownerOver) == 1, "owner/evidence share the same count ceiling");
        var remaining = ResourceLimits.SupplementalInputAggregateBytes - input.EvidenceInputs.Sum(item => item.Size) -
            input.OwnerInputs.Sum(item => item.Size);
        var large = f.Path("byte-cap.bin");
        using (var stream = File.Create(large)) stream.SetLength(remaining);
        var capped = input with { EvidenceInputs = [.. input.EvidenceInputs, new("byte-cap.bin", "raw", new("sha256", Digest(large)), remaining)] };
        var byteInput = f.WriteManifest(capped);
        var minimal = new[] { "package", "prepare", "--root", f.Root, "--input", byteInput, "--output", "full-bytes" };
        var byteReceipt = f.Prepare(minimal);
        f.Validate(byteReceipt, byteInput);
        var byteMapping = f.Read<PreparationRegistrationMap>(byteReceipt.RegistrationMap.Path).Entries.Single();
        var byteOver = f.WriteManifest(capped with
        {
            EvidenceInputs = [.. capped.EvidenceInputs, new(byteMapping.Basename, byteMapping.Kind, new("sha256", byteMapping.Retained.Sha256), byteMapping.Retained.Size)]
        });
        Assert(f.ValidateExit(byteReceipt, byteOver) == 1, "base exact 64MiB plus mapped addition exceeds aggregate bytes");
    }

    private sealed class Fixture
    {
        private int sequence;
        private string candidates = "";
        private readonly Dictionary<string, string> originals = [];
        private readonly string baseCandidates;
        public string Root { get; }
        public string Input { get; }
        public PreparationReceipt Success { get; set; } = null!;

        public Fixture(string root)
        {
            Root = root;
            Directory.CreateDirectory(Path("target"));
            WriteSyntheticInputs();
            foreach (var relative in new[] { "target/" + Package, Source,
                         Package, Spdx22, Spdx30, "provenance.sigstore.json", "sbom.sigstore.json" })
            {
                originals[relative] = Digest(Path(relative));
            }
            Assert(originals["target/" + Package] != originals[Package],
                "coherent synthetic target and release retain distinct exact bytes");
            Candidate("init", "--acquisition", "published", "--package-locator", "target/" + Package, "--package-method", "local-file",
                "--source-availability", "source-available", "--repository-uri", "https://github.com/example-org/sample-controls",
                "--source-commit", Commit, "--source-mapping", "Synthetic codeload layout declared at the confirmed full commit; local fixture only.",
                "--source-confidence", "high");
            Candidate("add-retrieval", "--subject", "package", "--locator", "target/" + Package, "--method", "local-file", "--result", "succeeded");
            Candidate("add-retrieval", "--subject", "source", "--locator", $"https://codeload.github.com/example-org/sample-controls/tar.gz/{Commit}",
                "--method", "direct-download", "--result", "succeeded", "--detail", "Synthetic acquisition metadata; this test generates local bytes without downloading.");
            foreach (var (name, kind) in new[]
            {
                (Source, "source-archive"), (Package, "release-package"), (Spdx22, "sbom"), (Spdx30, "sbom"),
                ("provenance.sigstore.json", "release-provenance"), ("sbom.sigstore.json", "release-sbom-statement")
            })
                Candidate("add-evidence", "--path", name, "--kind", kind);
            var unscoped = Confirm();
            Ok("inputs", "scope", "--root", Root, "--manifest", unscoped,
                "--output", Path(AuthorizedPackageScope.Filename));
            originals[AuthorizedPackageScope.Filename] = Digest(Path(AuthorizedPackageScope.Filename));
            Candidate("add-evidence", "--path", AuthorizedPackageScope.Filename, "--kind", AuthorizedPackageScope.Kind);
            baseCandidates = candidates;
            Input = Confirm();
        }

        private void WriteSyntheticInputs()
        {
            AssessmentTests.CreatePackage(Path("target/" + Package), "Sample.Controls", "0.1.2-alpha.3");
            AssessmentTests.CreatePackage(Path(Package), "Sample.Controls", "0.1.2-alpha.3");
            using (var release = ZipFile.Open(Path(Package), ZipArchiveMode.Update))
            {
                var marker = release.CreateEntry("release-marker.txt", CompressionLevel.NoCompression);
                marker.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var content = marker.Open();
                content.Write("Synthetic release bytes, distinct from the target."u8);
            }
            using (var file = File.Create(Path(Source)))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            using (var tar = new TarWriter(gzip))
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, Wrapper + "/"));
                for (var index = 0; index < 159; index++)
                    tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, $"{Wrapper}/directory-{index:D3}/"));
                for (var index = 0; index < 1332; index++)
                    tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile,
                        $"{Wrapper}/directory-{index % 159:D3}/source-{index:D4}.txt")
                    {
                        DataStream = new MemoryStream(Encoding.UTF8.GetBytes($"Synthetic source entry {index}."))
                    });
            }
            var releaseHash = Digest(Path(Package));
            var spdx22 = JsonNode.Parse($$$"""
                {"spdxVersion":"SPDX-2.2","SPDXID":"document","name":"Synthetic package inventory",
                 "documentNamespace":"https://example.test/sbom","documentDescribes":["package"],
                 "packages":[{"SPDXID":"package","name":"Sample.Controls","versionInfo":"0.1.2-alpha.3",
                   "hasFiles":["file"],"packageVerificationCode":{"packageVerificationCodeValue":"{{{new string('a', 40)}}}"}}],
                 "files":[{"SPDXID":"file","fileName":"./{{{Package}}}","checksums":[
                   {"algorithm":"SHA256","checksumValue":"{{{releaseHash}}}"}]}]}
                """)!;
            var spdx30 = JsonNode.Parse($$$"""
                {"@context":["https://spdx.org/rdf/3.0.1/spdx-context.json"],"@graph":[
                 {"spdxId":"document","name":"Synthetic package inventory","type":"SpdxDocument"},
                 {"spdxId":"file","name":"./{{{Package}}}","type":"software_File","verifiedUsing":[
                   {"spdxId":"verification","type":"PackageVerificationCode","algorithm":"sha256","hashValue":"{{{releaseHash}}}"}]}]}
                """)!;
            Zip(Path(Spdx22), ("spdx_2.2/manifest.spdx.json", spdx22.ToJsonString()));
            Zip(Path(Spdx30), ("spdx_3.0/manifest.spdx.json", spdx30.ToJsonString()));
            var provenance = JsonNode.Parse($$$"""
                {"buildDefinition":{"buildType":"https://actions.github.io/buildtypes/workflow/v1",
                 "externalParameters":{"workflow":{"repository":"https://github.com/example-org/sample-controls",
                   "ref":"refs/tags/v0.1.2-alpha.3","path":".github/workflows/release.yml"}},
                 "resolvedDependencies":[{"uri":"git+https://github.com/example-org/sample-controls@refs/tags/v0.1.2-alpha.3",
                   "digest":{"gitCommit":"{{{Commit}}}"}}]}}
                """)!;
            Bundle("provenance.sigstore.json", "https://slsa.dev/provenance/v1", provenance);
            Bundle("sbom.sigstore.json", "https://spdx.dev/Document/v2.2", spdx22);

            void Bundle(string filename, string predicateType, JsonNode predicate)
            {
                var statement = new JsonObject
                {
                    ["_type"] = "https://in-toto.io/Statement/v1",
                    ["subject"] = new JsonArray(new JsonObject
                    {
                        ["name"] = Package, ["digest"] = new JsonObject { ["sha256"] = releaseHash }
                    }),
                    ["predicateType"] = predicateType, ["predicate"] = predicate.DeepClone()
                };
                File.WriteAllText(Path(filename), new JsonObject
                {
                    ["mediaType"] = "application/vnd.dev.sigstore.bundle.v0.3+json",
                    ["verificationMaterial"] = new JsonObject(),
                    ["dsseEnvelope"] = new JsonObject
                    {
                        ["payloadType"] = "application/vnd.in-toto+json",
                        ["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(statement.ToJsonString())),
                        ["signatures"] = new JsonArray(new JsonObject { ["sig"] = "AA==" })
                    }
                }.ToJsonString());
            }
        }

        public string Path(string relative) => System.IO.Path.Combine(Root, relative);
        public string New(string label) => Path($"{label}-{sequence++}.json");
        public T Read<T>(string relative) => PreparationJson.Parse<T>(File.ReadAllBytes(Path(relative)));
        public InputManifest Manifest(string path) => InputManifestService.Parse(File.ReadAllBytes(path));
        public string WriteManifest(InputManifest input)
        {
            var path = New("mutation-input");
            input = input with
            {
                EvidenceInputs = input.EvidenceInputs.OrderBy(item => item.Basename, StringComparer.Ordinal).ToArray(),
                OwnerInputs = input.OwnerInputs.OrderBy(item => item.Basename, StringComparer.Ordinal).ToArray()
            };
            File.WriteAllBytes(path, InputManifestService.Serialize(input));
            return path;
        }
        public (int Exit, string Output, string Error) Invoke(params string[] args)
        {
            var result = PreparationTests.Invoke(args);
            File.AppendAllText(Path("cli.log"), JsonSerializer.Serialize(new { args, result.Exit, result.Output, result.Error }) + "\n");
            return result;
        }
        public void Ok(params string[] args)
        {
            var result = Invoke(args);
            Assert(result.Exit == 0, $"CLI failed ({result.Exit}): {string.Join(" ", args)}\n{result.Error}");
        }
        public string[] Arguments(string generation) =>
        [
            "package", "prepare", "--root", Root, "--input", Input, "--output", generation,
            "--source-archive", Source, "--archive-format", "tar.gz", "--release-package", Package,
            "--spdx22", Spdx22, "--spdx22-entry", "spdx_2.2/manifest.spdx.json",
            "--spdx30", Spdx30, "--spdx30-entry", "spdx_3.0/manifest.spdx.json",
            "--provenance", "provenance.sigstore.json", "--sbom-statement", "sbom.sigstore.json"
        ];
        public PreparationReceipt Prepare(string[] args)
        {
            var result = Invoke(args);
            Assert(result.Exit == 0, $"prepare failed ({result.Exit}): {result.Error}");
            Assert(result.Output.Contains("accounting/correspondence only", StringComparison.Ordinal) &&
                result.Output.Contains("not-applicable=0", StringComparison.Ordinal), "sealed receipt stdout has accounting-only counts");
            return Read<PreparationReceipt>(Value(args, "--output") + "/preparation.receipt.json");
        }
        public int ValidateExit(PreparationReceipt receipt, string input) => Invoke("inputs", "validate", "--root", Root,
            "--manifest", input, "--preparation", Path(receipt.Request.Output + "/preparation.receipt.json")).Exit;
        public void Validate(PreparationReceipt receipt, string input) => Assert(ValidateExit(receipt, input) == 0, "valid receipt/final-input subset");
        private void Candidate(string command, params string[] args)
        {
            var next = New("candidates");
            Ok(["inputs", "candidates", command, .. (command == "init" ? Array.Empty<string>() : new[] { "--input", candidates }),
                .. args, "--output", next]);
            candidates = next;
        }
        private string Confirm()
        {
            var draft = New("draft-input");
            var confirmed = New("confirmed-input");
            Ok("inputs", "discover", "--root", Root, "--nupkg", Path("target/" + Package), "--candidates", candidates, "--output", draft);
            Ok("inputs", "confirm", "--root", Root, "--draft", draft, "--output", confirmed);
            return confirmed;
        }
        public string ConfirmAdditions(IEnumerable<PreparationMapping> entries)
        {
            candidates = baseCandidates;
            foreach (var entry in entries) Candidate("add-evidence", "--path", entry.Basename, "--kind", entry.Kind);
            return Confirm();
        }
        public string WithSupplement(string name, bool owner = false)
        {
            candidates = baseCandidates;
            if (owner) Candidate("add-owner-input", "--path", name, "--provenance", EvidenceIdentity.OwnerSuppliedPublicEvidence);
            else Candidate("add-evidence", "--path", name, "--kind", "raw");
            return Confirm();
        }
        public string AsOwner(string name)
        {
            var input = Manifest(Input);
            var evidence = input.EvidenceInputs.Single(item => item.Basename == name);
            return WriteManifest(input with
            {
                EvidenceInputs = input.EvidenceInputs.Where(item => item.Basename != name).ToArray(),
                OwnerInputs = [new(name, EvidenceIdentity.OwnerSuppliedPublicEvidence, evidence.ContentDigest, evidence.Size)]
            });
        }
        public void Evidence(string final, PreparationMapping entry)
        {
            var skeleton = New("identity-only-skeleton");
            var identity = New("identity");
            var draft = New("evidence-draft");
            var ledger = New("evidence-ledger");
            var bundle = New("evidence-bundle");
            Ok("assessment", "init", "--kind", "package", "--root", Root, "--input", final, "--output", skeleton);
            Ok("assessment", "export-identity", "--assessment", skeleton, "--output", identity);
            var capture = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
            Ok("evidence", "draft-add", "--root", Root, "--manifest", final, "--evidence-input", entry.Basename,
                "--claim", "Preparation retained eleven literal release comparisons; target-release mismatch and typed not-comparable are observations, not readiness conclusions.",
                "--scope", "repository-wide", "--kind", EvidenceIdentity.ReviewerGeneratedAnalysis,
                "--method", "Offline package prepare; references comparisons/target-release and comparisons/spdx30-release. No authentication.",
                "--captured-at", capture, "--output", draft);
            Ok("evidence", "ledger-build", "--kind", "repository", "--subject", identity, "--draft", draft,
                "--nupkg", Path("target/" + Package), "--output", ledger);
            var produced = CanonicalEvidenceJson.ParseSourceLedger(File.ReadAllBytes(ledger));
            Ok("evidence", "bundle", "--assessment", identity, "--source-ledger", ledger, "--ids", produced.Records.Single().StableId,
                "--root", Root, "--manifest", final, "--output", bundle);
            var assessment = AssessmentService.Parse(File.ReadAllBytes(skeleton));
            Assert(assessment.Rows.All(row => row.Status is null && row.EvidenceIds.Count == 0), "identity skeleton unscored; no vendor report");
            Assert(CanonicalEvidenceJson.ParseBundle(File.ReadAllBytes(bundle)).Selection.Count == 1, "ordinary bound evidence bundle accepted");
        }
        public void RewriteDependency(PreparationReceipt receipt, PreparationArtifact reference, byte[] bytes)
        {
            File.WriteAllBytes(Path(reference.Path), bytes);
            var updated = new PreparationArtifact(reference.Path, bytes.LongLength, PreparationService.Hash(bytes));
            File.WriteAllBytes(Path(receipt.Request.Output + "/preparation.receipt.json"),
                PreparationJson.Bytes(receipt with { RegistrationMap = updated }));
        }
        public void Preserved()
        {
            foreach (var (relative, hash) in originals) Assert(Digest(Path(relative)) == hash, "independent synthetic raw input preserved");
        }
    }

    private static void Zip(string path, params (string Path, string Content)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }
    }
    private static string Digest(string path) => PreparationService.Hash(File.ReadAllBytes(path));
    private static (int Exit, string Output, string Error) Invoke(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        return (CliApplication.Run(args, output, error), output.ToString(), error.ToString());
    }
    private static string Value(string[] args, string name) => args[Array.IndexOf(args, name) + 1];
    private static string[] Replace(string[] args, string name, string value)
    {
        var copy = args.ToArray();
        copy[Array.IndexOf(copy, name) + 1] = value;
        return copy;
    }
    private static string[] Remove(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return [.. args.Take(index), .. args.Skip(index + 2)];
    }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (DeterministicValidationException) { assertions++; return; }
        throw new InvalidOperationException("Expected validation failure: " + message);
    }
    private static void Assert(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(message);
    }
}
