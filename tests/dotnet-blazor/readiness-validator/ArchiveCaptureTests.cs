using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;

internal static class ArchiveCaptureTests
{
    public static void Run(string repositoryRoot, string pluginRoot)
    {
        var configuredRoot = Environment.GetEnvironmentVariable("READINESS_TEST_ROOT");
        var testRoot = Path.GetFullPath(
            string.IsNullOrWhiteSpace(configuredRoot)
                ? Path.Combine(repositoryRoot, "artifacts", "readiness-validator-archive-tests")
                : Path.Combine(configuredRoot, "archive"));
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }

        Directory.CreateDirectory(testRoot);
        try
        {
            TestTarGzipCapture(testRoot);
            TestCaptureFailures(testRoot);
            TestZipCapture(testRoot);
            TestInventoryCaptureBridge(testRoot);
            TestInventoryCoverage(testRoot);
            TestInventoryHazards(testRoot);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static void TestTarGzipCapture(string testRoot)
    {
        var root = Path.Combine(testRoot, "fixture");
        var sourceRoot = Path.Combine(root, "public-inputs", "source");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, ".git", "HEAD"), "ref: refs/heads/unrelated\n");
        var sourcePath = Path.Combine(sourceRoot, "src", "Component.razor");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, "@* archive capture fixture *@\n", Encoding.UTF8);
        var archivePath = Path.Combine(root, "public-inputs", "source.tar.gz");
        CreateTarGzip(
            archivePath,
            "sample-controls-0123456789abcdef0123456789abcdef01234567/src/Component.razor",
            File.ReadAllBytes(sourcePath));

        var arguments = new[]
        {
            "source", "capture-archive",
            "--root", root,
            "--archive", "public-inputs/source.tar.gz",
            "--source-root", "public-inputs/source",
            "--archive-prefix", "sample-controls-0123456789abcdef0123456789abcdef01234567",
            "--archive-format", "tar.gz",
            "--repository-uri", "https://github.com/example-org/sample-controls",
            "--source-commit", "0123456789abcdef0123456789abcdef01234567",
            "--source-mapping", "owner-selected GitHub codeload archive at the pinned commit",
            "--source-confidence", "high",
            "--acquisition-locator", "https://codeload.github.com/example-org/sample-controls/tar.gz/0123456789abcdef0123456789abcdef01234567",
            "--source-path", "src/Component.razor",
            "--output", "public-inputs/source-capture.json"
        };
        var firstOutput = new StringWriter();
        var firstError = new StringWriter();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(arguments, firstOutput, firstError),
            "tar.gz capture");
        var receiptPath = Path.Combine(root, "public-inputs", "source-capture.json");
        var receiptBytes = File.ReadAllBytes(receiptPath);
        AssertEqual(string.Empty, firstOutput.ToString(), "capture result is retained, not mixed into launcher stdout");
        using (var receipt = JsonDocument.Parse(receiptBytes))
        {
            AssertEqual("source-archive", receipt.RootElement.GetProperty("capture_kind").GetString(), "capture kind");
            AssertEqual(
                "0123456789abcdef0123456789abcdef01234567",
                receipt.RootElement.GetProperty("declared_source").GetProperty("commit").GetString(),
                "declared source commit");
            Assert(
                !receipt.RootElement.TryGetProperty("acquisition", out _),
                "capture does not fabricate a download method or result");
            AssertEqual(
                "public-inputs/source.tar.gz",
                receipt.RootElement.GetProperty("archive").GetProperty("path").GetString(),
                "retained archive path");
            AssertEqual(
                "src/Component.razor",
                receipt.RootElement.GetProperty("source_artifacts")[0].GetProperty("source_path").GetString(),
                "selected source path");
        }

        var secondOutput = Path.Combine(root, "public-inputs", "source-capture-2.json");
        var secondArguments = arguments
            .Select(value => value == "public-inputs/source-capture.json" ? "public-inputs/source-capture-2.json" : value)
            .ToArray();
        var secondStdout = new StringWriter();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(secondArguments, secondStdout, new StringWriter()),
            "deterministic second capture");
        AssertBytes(
            receiptBytes,
            File.ReadAllBytes(secondOutput),
            "deterministic receipt bytes");

        var collision = new StringWriter();
        AssertEqual(
            ExitCodes.EnvironmentFailure,
            CliApplication.Run(arguments, collision, new StringWriter()),
            "capture output is immutable");
        AssertBytes(receiptBytes, File.ReadAllBytes(receiptPath), "immutable receipt content");

        var packagePath = Path.Combine(root, "package.nupkg");
        CreatePackage(packagePath);
        var candidatesPath = Path.Combine(root, "candidates.json");
        var candidateInit = new[]
        {
            "inputs", "candidates", "init",
            "--acquisition", "published",
            "--package-locator", "package.nupkg",
            "--package-method", "local-file",
            "--source-availability", "source-available",
            "--repository-uri", "https://github.com/example-org/sample-controls",
            "--source-commit", "0123456789abcdef0123456789abcdef01234567",
            "--source-mapping", "owner-selected GitHub codeload archive at the pinned commit",
            "--source-confidence", "high",
            "--output", candidatesPath
        };
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(candidateInit, new StringWriter(), new StringWriter()),
            "candidate initialization after capture");
        var retrievalPath = Path.Combine(root, "candidates-retrieval.json");
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(
                [
                    "inputs", "candidates", "add-retrieval",
                    "--input", candidatesPath,
                    "--subject", "package",
                    "--locator", "package.nupkg",
                    "--method", "local-file",
                    "--result", "succeeded",
                    "--output", retrievalPath
                ],
                new StringWriter(),
                new StringWriter()),
            "candidate retrieval after capture");
        var candidateWithArtifact = Path.Combine(root, "candidates-source.json");
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(
                [
                    "inputs", "candidates", "add-source-artifact",
                    "--input", retrievalPath,
                    "--source-path", "src/Component.razor",
                    "--path", "public-inputs/source/src/Component.razor",
                    "--output", candidateWithArtifact
                ],
                new StringWriter(),
                new StringWriter()),
            "candidate source artifact after capture");
        var manifest = InputManifestService.Discover(
            root,
            packagePath,
            File.ReadAllBytes(candidateWithArtifact));
        AssertEqual(1, manifest.SourceArtifacts.Count, "downstream source artifact count");
        AssertEqual(
            "src/Component.razor",
            manifest.SourceArtifacts[0].SourcePath,
            "downstream source artifact path");
        AssertEqual(
            "confirmed",
            InputManifestService.Confirm(manifest, root).State,
            "downstream source artifact confirmation");
        Assert(
            File.ReadAllText(Path.Combine(root, ".git", "HEAD")) == "ref: refs/heads/unrelated\n",
            "unrelated enclosing git metadata is untouched");
    }

    private static void TestCaptureFailures(string testRoot)
    {
        var root = Path.Combine(testRoot, "failures");
        var sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "src"));
        File.WriteAllText(Path.Combine(sourceRoot, "src", "file.txt"), "local");
        var archivePath = Path.Combine(root, "source.tar.gz");
        CreateTarGzip(archivePath, "prefix/src/file.txt", Encoding.UTF8.GetBytes("archive"));
        var args = new[]
        {
            "source", "capture-archive",
            "--root", root,
            "--archive", "source.tar.gz",
            "--source-root", "source",
            "--archive-prefix", "prefix",
            "--archive-format", "tar.gz",
            "--repository-uri", "https://example.com/vendor/repo",
            "--source-commit", "0123456789012345678901234567890123456789",
            "--source-mapping", "test mapping",
            "--source-confidence", "medium",
            "--acquisition-locator", "https://example.com/archive.tar.gz",
            "--source-path", "src/file.txt",
            "--output", "receipt.json"
        };
        AssertEqual(
            ExitCodes.ValidationFailure,
            CliApplication.Run(args, new StringWriter(), new StringWriter()),
            "mismatched extracted file fails closed");
        Assert(!File.Exists(Path.Combine(root, "receipt.json")), "failed capture writes no receipt");

        var invalidPrefix = args
            .Select(value => value == "prefix" ? "../unsafe" : value)
            .ToArray();
        AssertEqual(
            ExitCodes.ValidationFailure,
            CliApplication.Run(invalidPrefix, new StringWriter(), new StringWriter()),
            "unsafe archive prefix fails closed");

        File.WriteAllText(Path.Combine(sourceRoot, "src", "file.txt"), "archive");
        string[] WithOption(string option, string value)
        {
            var changed = args.ToArray();
            changed[Array.IndexOf(changed, option) + 1] = value;
            return changed;
        }

        var controlError = new StringWriter();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(WithOption("--output", "valid-control.json"), new StringWriter(), controlError),
            $"valid counterpart of rejection cases: {controlError}");

        CreateTarGzip(archivePath, "prefix/src/file.txt", []);
        File.WriteAllBytes(Path.Combine(sourceRoot, "src", "file.txt"), []);
        var emptyError = new StringWriter();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(WithOption("--output", "empty-file.json"), new StringWriter(), emptyError),
            $"empty regular source file remains valid: {emptyError}");
        CreateTarGzip(archivePath, "prefix/src/file.txt", Encoding.UTF8.GetBytes("archive"));
        File.WriteAllText(Path.Combine(sourceRoot, "src", "file.txt"), "archive");

        void Reject(string name, string[] command, string message)
        {
            var error = new StringWriter();
            Assert(CliApplication.Run(command, new StringWriter(), error) != ExitCodes.Success,
                $"{name} must fail");
            Assert(error.ToString().Contains(message, StringComparison.OrdinalIgnoreCase),
                $"{name} fails for the expected reason: {error}");
            Assert(!File.Exists(Path.Combine(root, "receipt.json")), $"{name} writes no receipt");
        }

        Reject("missing archive", WithOption("--archive", "missing.tar.gz"), "does not exist");
        Reject("missing selected member", WithOption("--source-path", "src/missing.txt"), "does not contain selected file");
        Reject("outside-root archive", WithOption("--archive", "../outside.tar.gz"), "relative path");
        Reject("traversal source selection", WithOption("--source-path", "../file.txt"), "relative path");
        Reject("missing output parent", WithOption("--output", "missing/receipt.json"), "parent");
        Reject("output inside source", WithOption("--output", "source/receipt.json"), "outside the extracted source");
        Assert(!File.Exists(Path.Combine(sourceRoot, "receipt.json")), "source tree is never an output destination");
        Reject("invalid commit", WithOption("--source-commit", "master"), "commit");
        Reject("non-HTTPS repository", WithOption("--repository-uri", "http://example.com/vendor/repo"), "HTTPS");
        Reject("missing locator", WithOption("--acquisition-locator", ""), "acquisition");
        Reject("invalid confidence", WithOption("--source-confidence", "verified"), "confidence");
        Reject("wrong expected digest", [.. args, "--expected-sha256", new string('0', 64)], "expected SHA-256");
        Reject("duplicate selection", [.. args, "--source-path", "src/file.txt"], "unique");
        Reject("unknown option", [.. args, "--git-clean", "true"], "Unknown option");
        Reject("unsupported source operation", ["source", "checkout", "--help"], "Unknown source command");

        CreateTarGzip(archivePath, "prefix/../file.txt", Encoding.UTF8.GetBytes("archive"));
        Reject("unsafe archive entry", args, "safe regular archive path");

        using (var file = File.Create(archivePath))
        using (var gzip = new GZipStream(file, CompressionLevel.NoCompression))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false))
        {
            for (var index = 0; index < 2; index++)
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "prefix/src/file.txt")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("archive"))
                });
            }
        }

        Reject("duplicate archive member", args, "duplicate entry");
        using (var file = File.Create(archivePath))
        using (var gzip = new GZipStream(file, CompressionLevel.NoCompression))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "prefix/src/file.txt")
            {
                LinkName = "elsewhere"
            });
        }

        Reject("archive symbolic link", args, "not a regular file");
        File.WriteAllText(archivePath, "not an archive");
        Reject("malformed archive", args, "not a valid gzip");
        using (var oversized = File.Create(archivePath))
        {
            oversized.SetLength(ResourceLimits.SourceArchiveBytes + 1);
        }

        Reject("compressed archive size limit", args, "limit");

        CreateTarGzip(archivePath, "prefix/src/file.txt", Encoding.UTF8.GetBytes("archive"));
        if (!OperatingSystem.IsWindows())
        {
            var localPath = Path.Combine(sourceRoot, "src", "file.txt");
            var realPath = Path.Combine(root, "real.txt");
            File.Move(localPath, realPath);
            File.CreateSymbolicLink(localPath, realPath);
            Reject("captured file symbolic link", args, "Symbolic links");
        }
    }

    private static void TestZipCapture(string testRoot)
    {
        var root = Path.Combine(testRoot, "zip");
        Directory.CreateDirectory(Path.Combine(root, "source"));
        var content = Encoding.UTF8.GetBytes("zip source");
        File.WriteAllBytes(Path.Combine(root, "source", "file.txt"), content);
        using (var archive = ZipFile.Open(Path.Combine(root, "source.zip"), ZipArchiveMode.Create))
        {
            using var entry = archive.CreateEntry("prefix/file.txt").Open();
            entry.Write(content);
        }

        var error = new StringWriter();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(
                ["source", "capture-archive", "--root", root, "--archive", "source.zip",
                    "--source-root", "source", "--archive-prefix", "prefix", "--archive-format", "zip",
                    "--repository-uri", "https://example.com/vendor/repo",
                    "--source-commit", "0123456789012345678901234567890123456789",
                    "--source-mapping", "Fixture declared mapping", "--source-confidence", "low",
                    "--acquisition-locator", "https://example.com/source.zip",
                    "--source-path", "file.txt", "--output", "receipt.json"],
                new StringWriter(),
                error),
            $"ZIP source capture: {error}");
    }

    private static void TestInventoryCaptureBridge(string testRoot)
    {
        foreach (var (rootName, sourceRoot) in new[]
        {
            ("inventory-bridge-flat", "extracted"),
            ("inventory-bridge-nested", "extracted/bundle")
        })
        {
            var root = Path.Combine(testRoot, rootName);
            var sourceRootPath = Path.Combine(root, sourceRoot);
            Directory.CreateDirectory(Path.Combine(sourceRootPath, "src"));
            File.WriteAllBytes(Path.Combine(sourceRootPath, "src", "a.txt"), Encoding.UTF8.GetBytes("alpha"));
            File.WriteAllBytes(Path.Combine(sourceRootPath, "src", "b.txt"), Encoding.UTF8.GetBytes("bravo"));
            var archivePath = Path.Combine(root, "archive.tar.gz");
            CreateTarGzip(
                archivePath,
                ("bundle/src/a.txt", Encoding.UTF8.GetBytes("alpha")),
                ("bundle/src/b.txt", Encoding.UTF8.GetBytes("bravo")));

            var inventoryArguments = new[]
            {
                "source", "inventory-archive",
                "--root", root,
                "--archive", "archive.tar.gz",
                "--source-root", sourceRoot,
                "--archive-prefix", "bundle",
                "--archive-format", "tar.gz",
                "--output", "inventory.json"
            };
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(inventoryArguments, new StringWriter(), new StringWriter()),
                $"{rootName} inventory archive");
            var inventoryBytes = File.ReadAllBytes(Path.Combine(root, "inventory.json"));
            using (var inventory = JsonDocument.Parse(inventoryBytes))
            {
                AssertEqual(2, inventory.RootElement.GetProperty("entries").GetArrayLength(), $"{rootName} inventory entry count");
                AssertEqual(
                    "src/a.txt",
                    inventory.RootElement.GetProperty("entries")[0].GetProperty("source_path").GetString(),
                    $"{rootName} inventory first path");
                AssertEqual(
                    "src/b.txt",
                    inventory.RootElement.GetProperty("entries")[1].GetProperty("source_path").GetString(),
                    $"{rootName} inventory second path");
            }

            var common = new[]
            {
                "--repository-uri", "https://example.test/repository",
                "--source-commit", "0123456789012345678901234567890123456789",
                "--source-mapping", "selected retained archive",
                "--source-confidence", "high",
                "--acquisition-locator", "https://example.test/archive.tar.gz"
            };
            var bridgedArguments = new[]
            {
                "source", "capture-inventory",
                "--root", root,
                "--inventory", "inventory.json",
                "--entry-id", "1"
            }.Concat(common).Concat(["--output", "bridged.json"]).ToArray();
            var bridgeError = new StringWriter();
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(bridgedArguments, new StringWriter(), bridgeError),
                $"{rootName} capture inventory: {bridgeError}");
            var directArguments = new[]
            {
                "source", "capture-archive",
                "--root", root,
                "--archive", "archive.tar.gz",
                "--source-root", sourceRoot,
                "--archive-prefix", "bundle",
                "--archive-format", "tar.gz"
            }.Concat(common).Concat(["--source-path", "src/a.txt", "--output", "direct.json"]).ToArray();
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(directArguments, new StringWriter(), new StringWriter()),
                $"{rootName} direct capture counterpart");
            AssertBytes(
                File.ReadAllBytes(Path.Combine(root, "direct.json")),
                File.ReadAllBytes(Path.Combine(root, "bridged.json")),
                $"{rootName} inventory bridge receipt equivalence");
            using (var receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "bridged.json"))))
            {
                AssertEqual(
                    $"{sourceRoot}/src/a.txt",
                    receipt.RootElement.GetProperty("source_artifacts")[0].GetProperty("content_path").GetString(),
                    $"{rootName} local content path");
            }

            File.WriteAllBytes(Path.Combine(sourceRootPath, "src", "a.txt"), Encoding.UTF8.GetBytes("changed"));
            var changedError = new StringWriter();
            AssertEqual(
                ExitCodes.ValidationFailure,
                CliApplication.Run(
                    bridgedArguments.Select(value => value == "bridged.json" ? "changed.json" : value).ToArray(),
                    new StringWriter(),
                    changedError),
                $"{rootName} changed extracted bytes rejected by inventory bridge");
            Assert(!File.Exists(Path.Combine(root, "changed.json")), $"{rootName} changed inventory capture writes no receipt");
        }
    }

    private static void TestInventoryCoverage(string testRoot)
    {
        var root = Path.Combine(testRoot, "inventory-coverage");
        var sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "src"));
        File.WriteAllBytes(Path.Combine(sourceRoot, "src", "A.txt"), Encoding.UTF8.GetBytes("upper"));
        File.WriteAllBytes(Path.Combine(sourceRoot, "src", "a.txt"), Encoding.UTF8.GetBytes("lower"));
        File.WriteAllBytes(Path.Combine(sourceRoot, "src", "z.txt"), Encoding.UTF8.GetBytes("zulu"));

        var tarPath = Path.Combine(root, "prefixed.tar.gz");
        CreateTarGzip(
            tarPath,
            ("bundle/src/z.txt", Encoding.UTF8.GetBytes("zulu")),
            ("bundle/src/a.txt", Encoding.UTF8.GetBytes("lower")),
            ("bundle/src/A.txt", Encoding.UTF8.GetBytes("upper")));
        var tarInventory = RunInventory(
            root,
            "prefixed.tar.gz",
            "source",
            "bundle",
            "tar.gz",
            "prefixed-inventory.json");
        AssertInventoryEntries(tarInventory, ["src/A.txt", "src/a.txt", "src/z.txt"], "prefixed TAR ordering");

        var repeatedInventory = RunInventory(
            root,
            "prefixed.tar.gz",
            "source",
            "bundle",
            "tar.gz",
            "prefixed-inventory-2.json");
        AssertBytes(tarInventory, repeatedInventory, "repeated TAR inventory bytes");
        var inventoryPath = Path.Combine(root, "prefixed-inventory.json");
        var originalInventory = File.ReadAllBytes(inventoryPath);
        var collisionError = new StringWriter();
        AssertEqual(
            ExitCodes.EnvironmentFailure,
            CliApplication.Run(
                [
                    "source", "inventory-archive", "--root", root, "--archive", "prefixed.tar.gz",
                    "--source-root", "source", "--archive-prefix", "bundle", "--archive-format", "tar.gz",
                    "--output", "prefixed-inventory.json"
                ],
                new StringWriter(),
                collisionError),
            "inventory output is immutable");
        AssertBytes(originalInventory, File.ReadAllBytes(inventoryPath), "immutable inventory content");

        var zipPath = Path.Combine(root, "unprefixed.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "src/A.txt", "src/a.txt", "src/z.txt" })
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(File.ReadAllBytes(Path.Combine(sourceRoot, name)));
            }
        }

        var zipInventory = RunInventory(root, "unprefixed.zip", "source", null, "zip", "unprefixed-inventory.json");
        AssertInventoryEntries(zipInventory, ["src/A.txt", "src/a.txt", "src/z.txt"], "unprefixed ZIP ordering");
        using (var document = JsonDocument.Parse(zipInventory))
        {
            AssertEqual(
                string.Empty,
                document.RootElement.GetProperty("archive_entry_prefix").GetString(),
                "omitted prefix is represented canonically");
        }

        var expectedShaError = new StringWriter();
        AssertEqual(
            ExitCodes.ValidationFailure,
            CliApplication.Run(
                [
                    "source", "inventory-archive", "--root", root, "--archive", "unprefixed.zip",
                    "--source-root", "source", "--archive-format", "zip",
                    "--expected-sha256", new string('0', 64), "--output", "wrong-sha.json"
                ],
                new StringWriter(),
                expectedShaError),
            "inventory expected digest mismatch");
        Assert(!File.Exists(Path.Combine(root, "wrong-sha.json")), "wrong digest writes no inventory");

        var common = new[]
        {
            "--repository-uri", "https://example.test/repository",
            "--source-commit", "0123456789012345678901234567890123456789",
            "--source-mapping", "selected retained archive",
            "--source-confidence", "high",
            "--acquisition-locator", "https://example.test/archive.zip"
        };
        var inventoryText = Encoding.UTF8.GetString(zipInventory);
        RejectInventory("missing-property", Encoding.UTF8.GetBytes("{}"), common);
        RejectInventory("wrong-type", Encoding.UTF8.GetBytes(inventoryText.Replace("\"schema_version\":1", "\"schema_version\":\"one\"", StringComparison.Ordinal)), common);
        RejectInventory("null-property", Encoding.UTF8.GetBytes(inventoryText.Replace("\"archive_path\":\"unprefixed.zip\"", "\"archive_path\":null", StringComparison.Ordinal)), common);
        RejectInventory("overflow-id", Encoding.UTF8.GetBytes(inventoryText.Replace("\"id\":1", "\"id\":2147483648", StringComparison.Ordinal)), common);
        RejectInventory("altered-prefix", Encoding.UTF8.GetBytes(inventoryText.Replace("\"archive_entry_prefix\":\"\"", "\"archive_entry_prefix\":\"other\"", StringComparison.Ordinal)), common);
        RejectInventory("altered-format", Encoding.UTF8.GetBytes(inventoryText.Replace("\"archive_format\":\"zip\"", "\"archive_format\":\"tar.gz\"", StringComparison.Ordinal)), common);
        RejectInventory("altered-digest", Encoding.UTF8.GetBytes(inventoryText.Replace("\"archive_sha256\":\"", "\"archive_sha256\":\"0", StringComparison.Ordinal)), common);
        RejectInventory("omitted-entry", Encoding.UTF8.GetBytes(inventoryText.Replace(",{\"id\":3,\"source_path\":\"src/z.txt\",\"archive_entry\":\"src/z.txt\"}", string.Empty, StringComparison.Ordinal)), common);
        RejectInventory("altered-order", Encoding.UTF8.GetBytes(inventoryText.Replace(
            "{\"id\":1,\"source_path\":\"src/A.txt\",\"archive_entry\":\"src/A.txt\"},{\"id\":2,\"source_path\":\"src/a.txt\",\"archive_entry\":\"src/a.txt\"}",
            "{\"id\":2,\"source_path\":\"src/a.txt\",\"archive_entry\":\"src/a.txt\"},{\"id\":1,\"source_path\":\"src/A.txt\",\"archive_entry\":\"src/A.txt\"}",
            StringComparison.Ordinal)), common);
        RejectInventory("remapped-ids", Encoding.UTF8.GetBytes(inventoryText.Replace(
            "{\"id\":1,\"source_path\":\"src/A.txt\",\"archive_entry\":\"src/A.txt\"},{\"id\":2,\"source_path\":\"src/a.txt\",\"archive_entry\":\"src/a.txt\"}",
            "{\"id\":1,\"source_path\":\"src/a.txt\",\"archive_entry\":\"src/a.txt\"},{\"id\":2,\"source_path\":\"src/A.txt\",\"archive_entry\":\"src/A.txt\"}",
            StringComparison.Ordinal)), common, "does not match the retained archive");
        RejectSelection("unknown-id", ["999"], common);
        RejectSelection("duplicate-id", ["1", "1"], common);
        RejectSelection("nonnumeric-id", ["abc"], common);
        RejectSelection("overflow-selection-id", ["999999999999999999999"], common);
        RejectSelection("selection-count", Enumerable.Range(1, ResourceLimits.SourceArchiveSelectionCount + 1).Select(id => id.ToString()).ToArray(), common);

        var archiveBytes = File.ReadAllBytes(zipPath);
        File.Delete(zipPath);
        using (var changedZip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            changedZip.CreateEntry("src/A.txt");
        }

        var staleError = new StringWriter();
        AssertEqual(
            ExitCodes.ValidationFailure,
            CliApplication.Run(
                CaptureArgumentsFor("stale-receipt.json", ["1", "2", "3"]),
                new StringWriter(),
                staleError),
            $"stale archive rejection: {staleError}");
        Assert(!File.Exists(Path.Combine(root, "stale-receipt.json")), "stale archive writes no receipt");
        File.WriteAllBytes(zipPath, archiveBytes);

        var protectedError = new StringWriter();
        AssertEqual(
            ExitCodes.ValidationFailure,
            CliApplication.Run(
                [
                    "source", "inventory-archive", "--root", root, "--archive", "unprefixed.zip",
                    "--source-root", "source", "--archive-format", "zip", "--output", "source/inventory.json"
                ],
                new StringWriter(),
                protectedError),
            $"inventory output source protection: {protectedError}");
        Assert(!File.Exists(Path.Combine(sourceRoot, "inventory.json")), "inventory never writes into source tree");

        void RejectInventory(string name, byte[] bytes, string[] metadata, string? expectedMessage = null)
        {
            var inventoryName = name + ".json";
            File.WriteAllBytes(Path.Combine(root, inventoryName), bytes);
            var outputName = name + "-receipt.json";
            var error = new StringWriter();
            var exitCode = CliApplication.Run(
                new[]
                {
                    "source", "capture-inventory", "--root", root, "--inventory", inventoryName, "--entry-id", "1"
                }.Concat(metadata).Concat(["--output", outputName]).ToArray(),
                new StringWriter(),
                error);
            AssertEqual(ExitCodes.ValidationFailure, exitCode, $"{name} malformed inventory must fail validation: {error}");
            if (expectedMessage is not null)
            {
                Assert(error.ToString().Contains(expectedMessage, StringComparison.Ordinal),
                    $"{name} reaches the expected validation boundary: {error}");
            }

            Assert(!File.Exists(Path.Combine(root, outputName)), $"{name} writes no receipt");
        }

        void RejectSelection(string name, string[] ids, string[] metadata)
        {
            var outputName = name + "-receipt.json";
            var error = new StringWriter();
            var arguments = new[] { "source", "capture-inventory", "--root", root, "--inventory", "unprefixed-inventory.json" }
                .Concat(ids.SelectMany(id => new[] { "--entry-id", id }))
                .Concat(metadata)
                .Concat(["--output", outputName])
                .ToArray();
            var exitCode = CliApplication.Run(arguments, new StringWriter(), error);
            Assert(exitCode == ExitCodes.ValidationFailure, $"{name} must be a validation failure: {error}");
            Assert(!File.Exists(Path.Combine(root, outputName)), $"{name} writes no receipt");
        }

        string[] CaptureArgumentsFor(string outputName, string[] ids) =>
        [
            "source", "capture-inventory", "--root", root, "--inventory", "unprefixed-inventory.json",
            .. ids.SelectMany(id => new[] { "--entry-id", id }),
            .. common, "--output", outputName
        ];

        var captureArguments = CaptureArgumentsFor("source-receipt.json", ["1", "2", "3"]);
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(captureArguments, new StringWriter(), new StringWriter()),
            "ZIP inventory capture");
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(
                CaptureArgumentsFor("reversed-receipt.json", ["3", "2", "1"]),
                new StringWriter(),
                new StringWriter()),
            "reversed inventory selection capture");
        AssertBytes(
            File.ReadAllBytes(Path.Combine(root, "source-receipt.json")),
            File.ReadAllBytes(Path.Combine(root, "reversed-receipt.json")),
            "caller selection order does not change receipt bytes");

        var packagePath = Path.Combine(root, "package.nupkg");
        CreatePackage(packagePath);
        var candidates = Path.Combine(root, "candidates.json");
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(
                [
                    "inputs", "candidates", "init", "--acquisition", "published",
                    "--package-locator", "package.nupkg", "--package-method", "local-file",
                    "--source-availability", "source-available",
                    "--repository-uri", "https://example.test/repository",
                    "--source-commit", "0123456789012345678901234567890123456789",
                    "--source-mapping", "selected retained archive", "--source-confidence", "high",
                    "--output", candidates
                ],
                new StringWriter(),
                new StringWriter()),
            "candidate init from inventory capture");
        var withSource = Path.Combine(root, "candidates-source.json");
        var withRetrieval = Path.Combine(root, "candidates-retrieval.json");
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(
                [
                    "inputs", "candidates", "add-retrieval", "--input", candidates,
                    "--subject", "package", "--locator", "package.nupkg", "--method", "local-file",
                    "--result", "succeeded", "--output", withRetrieval
                ],
                new StringWriter(),
                new StringWriter()),
            "candidate retrieval from inventory capture");
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(
                [
                    "inputs", "candidates", "add-source-artifact", "--input", withRetrieval,
                    "--source-path", "src/A.txt", "--path", "source/src/A.txt", "--output", withSource
                ],
                new StringWriter(),
                new StringWriter()),
            "candidate source artifact from inventory capture");
        var manifest = InputManifestService.Discover(root, packagePath, File.ReadAllBytes(withSource));
        AssertEqual(1, manifest.SourceArtifacts.Count, "inventory candidate source count");
        AssertEqual("confirmed", InputManifestService.Confirm(manifest, root).State, "inventory candidate confirm");
    }

    private static void TestInventoryHazards(string testRoot)
    {
        var root = Path.Combine(testRoot, "inventory-hazards");
        var sourceRoot = Path.Combine(root, "source");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "src"));
        File.WriteAllText(Path.Combine(sourceRoot, "src", "file.txt"), "safe", Encoding.UTF8);

        void RejectArchive(string name, Action<string> createArchive, string expectedMessage)
        {
            var archivePath = Path.Combine(root, name + ".tar.gz");
            createArchive(archivePath);
            var error = new StringWriter();
            AssertEqual(
                ExitCodes.ValidationFailure,
                CliApplication.Run(
                    [
                        "source", "inventory-archive", "--root", root, "--archive", Path.GetFileName(archivePath),
                        "--source-root", "source", "--archive-prefix", "selected", "--archive-format", "tar.gz",
                        "--output", name + ".json"
                    ],
                    new StringWriter(),
                    error),
                $"{name} inventory rejection");
            Assert(
                error.ToString().Contains(expectedMessage, StringComparison.OrdinalIgnoreCase),
                $"{name} reports deterministic validation: {error}");
            Assert(!File.Exists(Path.Combine(root, name + ".json")), $"{name} writes no inventory");
        }

        RejectArchive(
            "unselected-traversal",
            path => CreateTarGzip(path, ("selected/src/file.txt", Encoding.UTF8.GetBytes("safe")), ("../outside.txt", Encoding.UTF8.GetBytes("bad"))),
            "safe regular archive path");
        RejectArchive(
            "out-of-prefix-duplicate",
            path => CreateTarGzip(path, ("selected/src/file.txt", Encoding.UTF8.GetBytes("safe")), ("other/file.txt", Encoding.UTF8.GetBytes("one")), ("other/file.txt", Encoding.UTF8.GetBytes("two"))),
            "duplicate entry");
        RejectArchive(
            "out-of-prefix-symlink",
            path => CreateTarGzipWithEntries(path, writer =>
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "selected/src/file.txt")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("safe"))
                });
                writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "other/link")
                {
                    LinkName = "target"
                });
            }),
            "not a regular file");
        RejectArchive(
            "out-of-prefix-special",
            path => CreateTarGzipWithEntries(path, writer =>
                writer.WriteEntry(new PaxTarEntry(TarEntryType.BlockDevice, "other/device"))),
            "not a regular file");

        var zipTraversal = Path.Combine(root, "unselected-traversal.zip");
        using (var archive = ZipFile.Open(zipTraversal, ZipArchiveMode.Create))
        {
            using (var safe = archive.CreateEntry("selected/src/file.txt").Open())
            {
                safe.Write(Encoding.UTF8.GetBytes("safe"));
            }

            using (var unsafeEntry = archive.CreateEntry("../outside.txt").Open())
            {
                unsafeEntry.Write(Encoding.UTF8.GetBytes("bad"));
            }
        }

        var zipTraversalError = new StringWriter();
        AssertEqual(
            ExitCodes.ValidationFailure,
            CliApplication.Run(
                [
                    "source", "inventory-archive", "--root", root, "--archive", "unselected-traversal.zip",
                    "--source-root", "source", "--archive-format", "zip", "--output", "zip-traversal.json"
                ],
                new StringWriter(),
                zipTraversalError),
            $"ZIP traversal inventory rejection: {zipTraversalError}");
        Assert(!File.Exists(Path.Combine(root, "zip-traversal.json")), "ZIP traversal writes no inventory");

        var zipDuplicate = Path.Combine(root, "unselected-duplicate.zip");
        using (var archive = ZipFile.Open(zipDuplicate, ZipArchiveMode.Create))
        {
            foreach (var value in new[] { "one", "two" })
            {
                using (var duplicate = archive.CreateEntry("other/file.txt").Open())
                {
                    duplicate.Write(Encoding.UTF8.GetBytes(value));
                }
            }
        }

        var zipDuplicateError = new StringWriter();
        AssertEqual(
            ExitCodes.ValidationFailure,
            CliApplication.Run(
                [
                    "source", "inventory-archive", "--root", root, "--archive", "unselected-duplicate.zip",
                    "--source-root", "source", "--archive-format", "zip", "--output", "zip-duplicate.json"
                ],
                new StringWriter(),
                zipDuplicateError),
            $"ZIP duplicate inventory rejection: {zipDuplicateError}");
        Assert(!File.Exists(Path.Combine(root, "zip-duplicate.json")), "ZIP duplicate writes no inventory");

        var zipSymlink = Path.Combine(root, "unselected-symlink.zip");
        using (var archive = ZipFile.Open(zipSymlink, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("other/link");
            entry.ExternalAttributes = unchecked((int)(0xA000 << 16));
        }

        var zipSymlinkError = new StringWriter();
        AssertEqual(
            ExitCodes.ValidationFailure,
            CliApplication.Run(
                [
                    "source", "inventory-archive", "--root", root, "--archive", "unselected-symlink.zip",
                    "--source-root", "source", "--archive-format", "zip", "--output", "zip-symlink.json"
                ],
                new StringWriter(),
                zipSymlinkError),
            $"ZIP symlink inventory rejection: {zipSymlinkError}");
        Assert(!File.Exists(Path.Combine(root, "zip-symlink.json")), "ZIP symlink writes no inventory");

        var oversizedArchive = Path.Combine(root, "oversized.tar.gz");
        using (var file = File.Create(oversizedArchive))
        {
            file.SetLength(ResourceLimits.SourceArchiveBytes + 1);
        }

        var oversizedError = new StringWriter();
        AssertEqual(
            ExitCodes.ValidationFailure,
            CliApplication.Run(
                [
                    "source", "inventory-archive", "--root", root, "--archive", "oversized.tar.gz",
                    "--source-root", "source", "--archive-format", "tar.gz", "--output", "oversized.json"
                ],
                new StringWriter(),
                oversizedError),
            $"inventory archive size rejection: {oversizedError}");
        Assert(!File.Exists(Path.Combine(root, "oversized.json")), "oversized archive writes no inventory");

        var missingInventoryError = new StringWriter();
        var missingInventoryExitCode = CliApplication.Run(
                [
                    "source", "capture-inventory", "--root", root, "--inventory", "missing.json",
                    "--entry-id", "1", "--repository-uri", "https://example.test/repository",
                    "--source-commit", "0123456789012345678901234567890123456789",
                    "--source-mapping", "mapping", "--source-confidence", "high",
                    "--acquisition-locator", "https://example.test/archive.tar.gz", "--output", "missing-receipt.json"
                ],
                new StringWriter(),
                missingInventoryError);
        Assert(
            missingInventoryExitCode != ExitCodes.Success &&
            missingInventoryError.ToString().Contains("does not exist", StringComparison.OrdinalIgnoreCase),
            $"missing inventory rejection is deterministic: {missingInventoryError}");
        Assert(!File.Exists(Path.Combine(root, "missing-receipt.json")), "missing inventory writes no receipt");
    }

    private static byte[] RunInventory(
        string root,
        string archive,
        string sourceRoot,
        string? prefix,
        string format,
        string output)
    {
        var arguments = new List<string>
        {
            "source", "inventory-archive", "--root", root, "--archive", archive,
            "--source-root", sourceRoot, "--archive-format", format, "--output", output
        };
        if (prefix is not null)
        {
            arguments.Add("--archive-prefix");
            arguments.Add(prefix);
        }

        var error = new StringWriter();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(arguments, new StringWriter(), error),
            $"inventory {archive}: {error}");
        return File.ReadAllBytes(Path.Combine(root, output));
    }

    private static void AssertInventoryEntries(byte[] bytes, string[] expected, string name)
    {
        using var document = JsonDocument.Parse(bytes);
        var actual = document.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("source_path").GetString())
            .ToArray();
        Assert(actual.SequenceEqual(expected), $"{name}: paths differ.");
    }

    private static void CreateTarGzip(string path, string entryName, byte[] content)
        => CreateTarGzip(path, [(entryName, content)]);

    private static void CreateTarGzip(string path, params (string EntryName, byte[] Content)[] entries)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.NoCompression);
        using var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false);
        writer.WriteEntry(new PaxGlobalExtendedAttributesTarEntry(
            [new KeyValuePair<string, string>("comment", "Declared archive metadata, not remote identity proof")]));
        foreach (var (entryName, content) in entries)
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
            {
                DataStream = new MemoryStream(content, writable: false)
            };
            writer.WriteEntry(entry);
        }
    }

    private static void CreateTarGzipWithEntries(string path, Action<TarWriter> writeEntries)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.NoCompression);
        using var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false);
        writer.WriteEntry(new PaxGlobalExtendedAttributesTarEntry(
            [new KeyValuePair<string, string>("comment", "Declared archive metadata, not remote identity proof")]));
        writeEntries(writer);
    }

    private static void CreatePackage(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(
            "Fixture.Package.nuspec",
            CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(
            "<package><metadata><id>Fixture.Package</id><version>1.0.0</version></metadata></package>");
    }

    private static void AssertBytes(byte[] expected, byte[] actual, string name) =>
        Assert(expected.SequenceEqual(actual), $"{name}: byte sequences differ.");

    private static void AssertEqual<T>(T expected, T actual, string name) =>
        Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"{name}: expected '{expected}', actual '{actual}'.");

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
