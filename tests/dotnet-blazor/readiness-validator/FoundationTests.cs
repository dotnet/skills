using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

internal static class FoundationTests
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static void Run(string repositoryRoot, string pluginRoot)
    {
        var configuredRoot = Environment.GetEnvironmentVariable("READINESS_TEST_ROOT");
        var testRoot = Path.GetFullPath(
            string.IsNullOrWhiteSpace(configuredRoot)
                ? Path.Combine(repositoryRoot, "artifacts", "readiness-validator-tests")
                : configuredRoot);

        if (IsUnder(testRoot, pluginRoot))
        {
            throw new InvalidOperationException("READINESS_TEST_ROOT must be outside the plugin tree.");
        }

        if (Directory.Exists(testRoot))
        {
            MakeWritable(testRoot);
            Directory.Delete(testRoot, recursive: true);
        }

        Directory.CreateDirectory(testRoot);
        try
        {
            TestProductionProject(pluginRoot);
            TestStrictJson();
            TestDomainSeparatedHash();
            TestResourceLimits(testRoot);
            var package = TestNupkgIdentity(testRoot);
            TestSafePaths(testRoot);
            TestAtomicFiles(testRoot);
            TestCli(package);
            TestLaunchers(pluginRoot, testRoot, package);
        }
        finally
        {
            MakeWritable(testRoot);
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static void TestProductionProject(string pluginRoot)
    {
        var validator = Path.Combine(
            pluginRoot,
            "skills",
            "blazor-component-readiness",
            "scripts",
            "validator");
        var projectPath = Path.Combine(validator, "BlazorComponentReadiness.Validator.csproj");
        var project = File.ReadAllText(projectPath);
        var sdkProps = project.IndexOf("Microsoft.NET.Sdk/Sdk/Sdk.props", StringComparison.Ordinal);

        Assert(sdkProps > 0, "validator imports Sdk.props explicitly");
        foreach (var property in new[]
                 {
                     "ImportDirectoryBuildProps",
                     "ImportDirectoryBuildTargets",
                     "ImportDirectoryPackagesProps"
                 })
        {
            var propertyPosition = project.IndexOf($"<{property}>false</{property}>", StringComparison.Ordinal);
            Assert(
                propertyPosition >= 0 && propertyPosition < sdkProps,
                $"{property} is false before Sdk.props");
        }

        Assert(project.Contains("<TargetFramework>net11.0</TargetFramework>", StringComparison.Ordinal), "net11 target");
        Assert(project.Contains("<LangVersion>13.0</LangVersion>", StringComparison.Ordinal), "C# 13 language version");
        Assert(
            project.Contains($"<Version>{ContractVersions.ValidatorVersion}</Version>", StringComparison.Ordinal),
            "validator assembly version matches the authoritative production project");
        Assert(!project.Contains("<PackageReference", StringComparison.Ordinal), "production project has no PackageReference");
        Assert(project.Contains("VerifyNoPackageReferences", StringComparison.Ordinal), "zero-package verification target");
        Assert(
            project.Contains("obj/**;bin/**", StringComparison.Ordinal),
            "production source glob permanently excludes local build artifacts");

        var restoreConfig = File.ReadAllText(Path.Combine(validator, "restore-offline.config"));
        Assert(restoreConfig.Contains("<packageSources>", StringComparison.Ordinal), "restore config declares sources");
        Assert(restoreConfig.Contains("<clear />", StringComparison.Ordinal), "restore config clears sources");
    }

    private static void TestStrictJson()
    {
        var input = Encoding.UTF8.GetBytes(" { \"z\" : 1, \"a\" : [true, null, \"x\"] } ");
        var expected = Encoding.UTF8.GetBytes("{\"z\":1,\"a\":[true,null,\"x\"]}");
        var canonical = StrictJson.Canonicalize(input, 1024, "test JSON");
        AssertBytes(expected, canonical, "canonical JSON preserves property order");
        AssertBytes(
            canonical,
            StrictJson.Canonicalize(canonical, 1024, "canonical JSON"),
            "canonical JSON byte round-trip");

        ExpectValidation(
            () => StrictJson.Parse(Utf8Bom.Concat(Encoding.UTF8.GetBytes("{}")).ToArray(), 32, "BOM JSON"),
            "JSON BOM");
        ExpectValidation(
            () => StrictJson.Parse(
                new byte[] { 0x7B, 0x22, 0x78, 0x22, 0x3A, 0x22, 0xC3, 0x28, 0x22, 0x7D },
                32,
                "UTF-8"),
            "invalid UTF-8");
        ExpectValidation(
            () => StrictJson.Parse(Encoding.UTF8.GetBytes("{\"x\":1,\"x\":2}"), 64, "duplicate JSON"),
            "duplicate JSON property");
        ExpectValidation(
            () => StrictJson.Parse(Encoding.UTF8.GetBytes("{\"x\":{\"y\":1,\"y\":2}}"), 64, "nested duplicate JSON"),
            "nested duplicate JSON property");
        ExpectValidation(
            () => StrictJson.Parse(Encoding.UTF8.GetBytes("{/*comment*/\"x\":1}"), 64, "comment JSON"),
            "JSON comments");
        ExpectValidation(
            () => StrictJson.Parse(Encoding.UTF8.GetBytes("{\"x\":1,}"), 64, "trailing comma JSON"),
            "JSON trailing comma");
        ExpectValidation(
            () => StrictJson.Parse(Encoding.UTF8.GetBytes("{}{}"), 64, "trailing JSON"),
            "JSON trailing data");
        ExpectValidation(
            () => StrictJson.Parse(Encoding.UTF8.GetBytes("{}"), 1, "bounded JSON"),
            "JSON byte ceiling");
    }

    private static void TestDomainSeparatedHash()
    {
        var content = Encoding.UTF8.GetBytes("known-answer");
        AssertEqual(
            "34a3eef4a20c2ad56c9e530239041a99f1337d4ce1c0c63ed70544e611c7afad",
            DomainSeparatedHash.Compute("evidence.content", content),
            "known-answer domain hash");
        Assert(
            DomainSeparatedHash.Compute("evidence.content", content) !=
            DomainSeparatedHash.Compute("manifest.content", content),
            "hash domains are separated");
        Assert(
            DomainSeparatedHash.Compute("evidence.content", content) !=
            DomainSeparatedHash.Compute("evidence.content", Encoding.UTF8.GetBytes("known-answer!")),
            "hash content is bound");
    }

    private static void TestResourceLimits(string testRoot)
    {
        AssertEqual(64L * 1024 * 1024, ResourceLimits.SerializedArtifactBytes, "serialized limit");
        AssertEqual(4L * 1024 * 1024, ResourceLimits.AuthoredLedgerBytes, "authored ledger limit");
        AssertEqual(256L * 1024 * 1024, ResourceLimits.NupkgBytes, "nupkg limit");
        AssertEqual(1L * 1024 * 1024, ResourceLimits.NuspecBytes, "nuspec limit");
        AssertEqual(32, ResourceLimits.SupplementalInputCount, "supplemental input count");
        AssertEqual(64L * 1024 * 1024, ResourceLimits.SupplementalInputAggregateBytes, "supplemental aggregate limit");
        AssertEqual(64, ResourceLimits.RetrievalAttemptCount, "retrieval attempt count");

        var exactSerialized = Path.Combine(testRoot, "serialized-exact.bin");
        var overSerialized = Path.Combine(testRoot, "serialized-over.bin");
        var exactNupkg = Path.Combine(testRoot, "nupkg-exact.bin");
        var overNupkg = Path.Combine(testRoot, "nupkg-over.bin");
        SetSparseLength(exactSerialized, ResourceLimits.SerializedArtifactBytes);
        SetSparseLength(overSerialized, ResourceLimits.SerializedArtifactBytes + 1);
        SetSparseLength(exactNupkg, ResourceLimits.NupkgBytes);
        SetSparseLength(overNupkg, ResourceLimits.NupkgBytes + 1);

        BoundedIO.EnsureFileLength(exactSerialized, ResourceLimits.SerializedArtifactBytes, "serialized artifact");
        ExpectLimit(
            () => BoundedIO.EnsureFileLength(overSerialized, ResourceLimits.SerializedArtifactBytes, "serialized artifact"),
            ResourceLimits.SerializedArtifactBytes + 1,
            "serialized one-byte-over");
        BoundedIO.EnsureFileLength(exactNupkg, ResourceLimits.NupkgBytes, "nupkg");
        ExpectLimit(
            () => BoundedIO.EnsureFileLength(overNupkg, ResourceLimits.NupkgBytes, "nupkg"),
            ResourceLimits.NupkgBytes + 1,
            "nupkg one-byte-over");
        ExpectLimit(
            () => NupkgInspector.Inspect(overNupkg),
            ResourceLimits.NupkgBytes + 1,
            "NupkgInspector nupkg one-byte-over");

        var exactLedger = new byte[ResourceLimits.AuthoredLedgerBytes];
        AssertEqual(
            exactLedger.Length,
            BoundedIO.ReadAllBytes(new MemoryStream(exactLedger), ResourceLimits.AuthoredLedgerBytes, "ledger").Length,
            "authored ledger exact limit");
        ExpectLimit(
            () => BoundedIO.ReadAllBytes(
                new MemoryStream(new byte[ResourceLimits.AuthoredLedgerBytes + 1]),
                ResourceLimits.AuthoredLedgerBytes,
                "ledger"),
            ResourceLimits.AuthoredLedgerBytes + 1,
            "authored ledger one-byte-over");
    }

    private static string TestNupkgIdentity(string testRoot)
    {
        var package = Path.Combine(testRoot, "Exact.Identity.2.4.6.nupkg");
        CreatePackage(package, "Exact.Identity", "2.4.6-beta.1+build.7");
        var identity = NupkgInspector.Inspect(package);
        AssertEqual("exact.identity", identity.Id, "nupkg ID");
        AssertEqual("2.4.6-beta.1+build.7", identity.Version, "nupkg version");
        AssertEqual("Exact.Identity.nuspec", identity.NuspecEntry, "nuspec entry");
        AssertEqual(new FileInfo(package).Length, identity.NupkgSize, "nupkg byte size");
        AssertEqual(Sha256(File.ReadAllBytes(package)), identity.NupkgSha256, "nupkg digest");

        var exactNuspec = Path.Combine(testRoot, "nuspec-exact.nupkg");
        CreatePackage(exactNuspec, "Limit.Exact", "1.0.0", ResourceLimits.NuspecBytes);
        AssertEqual("limit.exact", NupkgInspector.Inspect(exactNuspec).Id, "exact 1 MiB nuspec");

        var overNuspec = Path.Combine(testRoot, "nuspec-over.nupkg");
        CreatePackage(overNuspec, "Limit.Over", "1.0.0", ResourceLimits.NuspecBytes + 1);
        ExpectLimit(() => NupkgInspector.Inspect(overNuspec), ResourceLimits.NuspecBytes + 1, "nuspec one-byte-over");

        var duplicate = Path.Combine(testRoot, "duplicate.nupkg");
        CreatePackage(duplicate, "Duplicate", "1.0.0", additionalNuspec: true);
        ExpectValidation(() => NupkgInspector.Inspect(duplicate), "multiple nuspecs");

        var nested = Path.Combine(testRoot, "nested.nupkg");
        CreatePackage(nested, "Nested", "1.0.0", nuspecEntry: "nested/Nested.nuspec");
        ExpectValidation(() => NupkgInspector.Inspect(nested), "nested-only nuspec");

        var traversal = Path.Combine(testRoot, "traversal.nupkg");
        CreatePackage(traversal, "Traversal", "1.0.0", unsafeEntry: "../escape.txt");
        ExpectValidation(() => NupkgInspector.Inspect(traversal), "archive traversal");

        var symlinkEntry = Path.Combine(testRoot, "symlink-entry.nupkg");
        CreatePackage(symlinkEntry, "Symlink", "1.0.0", symbolicLinkEntry: "link");
        ExpectValidation(() => NupkgInspector.Inspect(symlinkEntry), "archive symlink");

        var dtd = Path.Combine(testRoot, "dtd.nupkg");
        CreateRawPackage(
            dtd,
            "Dtd.nuspec",
            Encoding.UTF8.GetBytes(
                "<!DOCTYPE package [<!ENTITY x 'bad'>]><package><metadata><id>&x;</id><version>1</version></metadata></package>"));
        ExpectValidation(() => NupkgInspector.Inspect(dtd), "nuspec DTD");

        var packageLink = Path.Combine(testRoot, "package-link.nupkg");
        if (TryCreateFileSymlink(packageLink, package, "package"))
        {
            ExpectValidation(() => NupkgInspector.Inspect(packageLink), "nupkg input symlink");
        }

        var realPackageDirectory = Path.Combine(testRoot, "real-package-directory");
        var aliasPackageDirectory = Path.Combine(testRoot, "alias-package-directory");
        Directory.CreateDirectory(realPackageDirectory);
        var aliasedPackage = Path.Combine(realPackageDirectory, "Aliased.1.0.0.nupkg");
        CreatePackage(aliasedPackage, "Aliased", "1.0.0");
        if (TryCreateDirectorySymlink(aliasPackageDirectory, realPackageDirectory, "package directory"))
        {
            AssertEqual(
                "aliased",
                NupkgInspector.Inspect(Path.Combine(aliasPackageDirectory, "Aliased.1.0.0.nupkg")).Id,
                "nupkg beneath a resolved directory alias");
        }

        return package;
    }

    private static void TestSafePaths(string testRoot)
    {
        var root = Path.Combine(testRoot, "safe-root");
        var child = Path.Combine(root, "child");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "input.json"), "{}");

        AssertEqual(
            Path.Combine(child, "input.json"),
            SafePath.ResolveUnderRoot(root, "child/input.json", requireExisting: true, requireFile: true),
            "safe rooted path");
        ExpectValidation(
            () => SafePath.ResolveUnderRoot(root, "../escape", requireExisting: false),
            "path traversal");
        ExpectValidation(
            () => SafePath.ResolveUnderRoot(root, Path.GetFullPath(Path.Combine(root, "child")), requireExisting: true),
            "absolute path");
        ExpectValidation(
            () => SafePath.ValidateArchiveEntry("a/../b", isSymbolicLink: false),
            "archive dot-dot");
        ExpectValidation(
            () => SafePath.ValidateArchiveEntry("a\\b", isSymbolicLink: false),
            "archive backslash");

        var outside = Path.Combine(testRoot, "outside");
        Directory.CreateDirectory(outside);
        var link = Path.Combine(root, "link");
        if (TryCreateDirectorySymlink(link, outside, "filesystem"))
        {
            ExpectValidation(
                () => SafePath.ResolveUnderRoot(root, "link/escaped.json", requireExisting: false),
                "filesystem symlink");
        }
    }

    private static void TestAtomicFiles(string testRoot)
    {
        var root = Path.Combine(testRoot, "atomic");
        Directory.CreateDirectory(root);
        AtomicFile.WriteNew(root, "new.json", Encoding.UTF8.GetBytes("first"));
        AssertEqual("first", File.ReadAllText(Path.Combine(root, "new.json")), "atomic create-new content");

        ExpectIOException(
            () => AtomicFile.WriteNew(root, "new.json", Encoding.UTF8.GetBytes("second")),
            "atomic create-new collision");
        AssertEqual("first", File.ReadAllText(Path.Combine(root, "new.json")), "create-new preserves existing file");

        AtomicFile.Replace(root, "new.json", Encoding.UTF8.GetBytes("replacement"));
        AssertEqual("replacement", File.ReadAllText(Path.Combine(root, "new.json")), "atomic replacement");

        ExpectException<InvalidOperationException>(
            () => AtomicFile.Replace(root, "new.json", stream =>
            {
                stream.Write(Encoding.UTF8.GetBytes("partial"));
                throw new InvalidOperationException("simulated writer failure");
            }),
            "atomic writer failure");
        AssertEqual("replacement", File.ReadAllText(Path.Combine(root, "new.json")), "failed replacement preserves destination");
        Assert(
            !Directory.EnumerateFiles(root, "*.tmp", SearchOption.TopDirectoryOnly).Any(),
            "atomic temporary files are cleaned");

        var outside = Path.Combine(testRoot, "atomic-outside.json");
        var link = Path.Combine(root, "linked.json");
        File.WriteAllText(outside, "outside");
        if (TryCreateFileSymlink(link, outside, "atomic"))
        {
            ExpectValidation(
                () => AtomicFile.Replace(root, "linked.json", Encoding.UTF8.GetBytes("replacement")),
                "atomic symlink destination");
            AssertEqual("outside", File.ReadAllText(outside), "atomic symlink target remains unchanged");
        }
    }

    private static void TestCli(string package)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        AssertEqual(ExitCodes.Success, CliApplication.Run(["--help"], output, error), "root help exit");
        Assert(output.ToString().Contains("package inspect", StringComparison.Ordinal), "grouped root help");

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        AssertEqual(ExitCodes.Success, CliApplication.Run(["package", "inspect", "--help"], output, error), "package help exit");
        Assert(output.ToString().Contains("--nupkg <path>", StringComparison.Ordinal), "package help");

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        AssertEqual(
            ExitCodes.InvalidUsage,
            CliApplication.Run(["not-a-command"], output, error),
            "invalid usage exit");

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(["package", "inspect", "--nupkg", package], output, error),
            "package inspection exit");
        using (var result = StrictJson.Parse(
                   Encoding.UTF8.GetBytes(output.ToString().TrimEnd()),
                   4096,
                   "CLI output"))
        {
            AssertEqual("exact.identity", result.RootElement.GetProperty("package_id").GetString(), "CLI package ID");
            AssertEqual(
                "2.4.6-beta.1+build.7",
                result.RootElement.GetProperty("package_version").GetString(),
                "CLI package version");
        }

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        var malformed = package + ".malformed";
        File.WriteAllText(malformed, "not a zip archive");
        AssertEqual(
            ExitCodes.ValidationFailure,
            CliApplication.Run(["package", "inspect", "--nupkg", malformed], output, error),
            "invalid package exit");

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        AssertEqual(
            ExitCodes.EnvironmentFailure,
            CliApplication.Run(["package", "inspect", "--nupkg", package + ".missing"], output, error),
            "missing package exit");
    }

    private static void TestLaunchers(string pluginRoot, string testRoot, string package)
    {
        var hostileRoot = Path.Combine(testRoot, "hostile-parent");
        var copiedPlugin = Path.Combine(hostileRoot, "plugins", "dotnet-blazor");
        CopyDirectory(pluginRoot, copiedPlugin);
        WriteHostileAmbientFiles(hostileRoot);
        MakeReadOnly(copiedPlugin);

        try
        {
            var validatorDirectory = Path.Combine(
                copiedPlugin,
                "skills",
                "blazor-component-readiness",
                "scripts",
                "validator");
            var environment = CreateRepositorySdkEnvironment(hostileRoot);
            AssertHostileAmbientFilesEffective(hostileRoot, environment);
            RunLauncher(
                "Bash",
                "bash",
                [
                    Path.Combine(validatorDirectory, "run-validator.sh"),
                    "package",
                    "inspect",
                    "--nupkg",
                    package
                ],
                hostileRoot,
                Path.Combine(testRoot, "launcher-bash"),
                environment);
            RunLauncher(
                "PowerShell",
                "pwsh",
                [
                    "-NoLogo",
                    "-NoProfile",
                    "-File",
                    Path.Combine(validatorDirectory, "run-validator.ps1"),
                    "package",
                    "inspect",
                    "--nupkg",
                    package
                ],
                hostileRoot,
                Path.Combine(testRoot, "launcher-powershell"),
                environment);

            Assert(
                !Directory.EnumerateDirectories(copiedPlugin, "bin", SearchOption.AllDirectories).Any(),
                "read-only plugin tree has no bin directory");
            Assert(
                !Directory.EnumerateDirectories(copiedPlugin, "obj", SearchOption.AllDirectories).Any(),
                "read-only plugin tree has no obj directory");

            TestExternalArtifactGuard(validatorDirectory, hostileRoot, environment);
            TestSdkMajorGuard(validatorDirectory, hostileRoot, testRoot);
            TestReadinessTempParity(validatorDirectory, hostileRoot, testRoot, environment);
            TestLauncherInvocationParity(validatorDirectory, hostileRoot, testRoot);
        }
        finally
        {
            MakeWritable(copiedPlugin);
        }
    }

    private static Dictionary<string, string> CreateRepositorySdkEnvironment(string workingDirectory)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var version = RunProcess("dotnet", ["--version"], workingDirectory, environment, expectedExitCode: 0);
        if (!version.StandardOutput.TrimStart().StartsWith("11.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The repository-selected .NET 11 SDK is required for launcher smoke tests; active SDK was '{version.StandardOutput.Trim()}'.");
        }

        return environment;
    }

    private static void RunLauncher(
        string name,
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string externalArtifacts,
        IReadOnlyDictionary<string, string> environment)
    {
        Directory.CreateDirectory(externalArtifacts);
        var launcherEnvironment = new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase)
        {
            ["READINESS_TEMP"] = externalArtifacts
        };
        var result = RunProcess(executable, arguments, workingDirectory, launcherEnvironment, expectedExitCode: 0);
        var jsonLine = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.StartsWith('{') && line.EndsWith('}'))
            ?? throw new InvalidOperationException($"{name} launcher did not emit package JSON.\n{result}");
        using var json = StrictJson.Parse(Encoding.UTF8.GetBytes(jsonLine), 4096, $"{name} launcher JSON");
        AssertEqual("exact.identity", json.RootElement.GetProperty("package_id").GetString(), $"{name} launcher package ID");

        var dll = Path.Combine(
            externalArtifacts,
            "bin",
            "Release",
            "net11.0",
            "BlazorComponentReadiness.Validator.dll");
        Assert(File.Exists(dll), $"{name} launcher external DLL");
        Assert(!IsUnder(dll, workingDirectory), $"{name} launcher DLL is external");
        Assert(Directory.Exists(Path.Combine(externalArtifacts, "obj")), $"{name} launcher external obj");
        Assert(Directory.Exists(Path.Combine(externalArtifacts, "packages")), $"{name} launcher external packages");

        var assetsPath = Path.Combine(externalArtifacts, "obj", "project.assets.json");
        using var assets = StrictJson.Read(assetsPath, ResourceLimits.SerializedArtifactBytes, $"{name} restore assets");
        AssertEqual(
            0,
            assets.RootElement.GetProperty("libraries").EnumerateObject().Count(),
            $"{name} restore has no package libraries");
        var sources = assets.RootElement
            .GetProperty("project")
            .GetProperty("restore")
            .GetProperty("sources")
            .EnumerateObject()
            .Select(source => source.Name)
            .ToArray();
        Assert(
            sources.All(source =>
                !source.Contains("source-that-does-not-exist", StringComparison.Ordinal) &&
                !source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !source.StartsWith("https://", StringComparison.OrdinalIgnoreCase)),
            $"{name} restore sources must be source-cleared or SDK-local: [{string.Join(", ", sources)}]");
    }

    private static void AssertHostileAmbientFilesEffective(
        string hostileRoot,
        IReadOnlyDictionary<string, string> environment)
    {
        var probe = Path.Combine(hostileRoot, "AmbientProbe.csproj");
        var directoryResponse = Path.Combine(hostileRoot, "Directory.Build.rsp");
        var msbuildResponse = Path.Combine(hostileRoot, "MSBuild.rsp");
        var dotnet = ResolveDotnetExecutable(environment);

        File.Move(msbuildResponse, msbuildResponse + ".disabled");
        try
        {
            var directoryResult = RunProcess(
                dotnet,
                ["msbuild", probe, "-verbosity:minimal"],
                hostileRoot,
                environment,
                expectedExitCode: null);
            Assert(
                directoryResult.ExitCode != 0 &&
                (directoryResult.StandardOutput + directoryResult.StandardError)
                    .Contains("AmbientResponseFileWasLoaded", StringComparison.Ordinal),
                "Directory.Build.rsp hostile control");
        }
        finally
        {
            File.Move(msbuildResponse + ".disabled", msbuildResponse);
        }

        File.Move(directoryResponse, directoryResponse + ".disabled");
        try
        {
            var msbuildResult = RunProcess(
                dotnet,
                ["msbuild", probe, "-verbosity:minimal"],
                hostileRoot,
                environment,
                expectedExitCode: null);
            Assert(
                msbuildResult.ExitCode != 0 &&
                (msbuildResult.StandardOutput + msbuildResult.StandardError)
                    .Contains("AmbientResponseFileWasLoaded", StringComparison.Ordinal),
                "MSBuild.rsp hostile control");
        }
        finally
        {
            File.Move(directoryResponse + ".disabled", directoryResponse);
        }

        var policyResult = RunProcess(
            dotnet,
            ["msbuild", probe, "-noAutoResponse", "-target:DetectAmbientPolicy", "-verbosity:minimal"],
            hostileRoot,
            environment,
            expectedExitCode: 0);
        Assert(
            policyResult.StandardOutput.Contains(
                "AMBIENT_POLICY:true|Hostile.Ambient.Package",
                StringComparison.Ordinal),
            $"Directory build/package hostile control was ineffective.\n{policyResult}");

        var targetsResult = RunProcess(
            dotnet,
            ["msbuild", probe, "-noAutoResponse", "-target:HostileDirectoryBuildTarget", "-verbosity:minimal"],
            hostileRoot,
            environment,
            expectedExitCode: null);
        Assert(
            targetsResult.ExitCode != 0 &&
            (targetsResult.StandardOutput + targetsResult.StandardError)
                .Contains("Hostile Directory.Build.targets was imported.", StringComparison.Ordinal),
            "Directory.Build.targets hostile control");
    }

    private static void TestSdkMajorGuard(string validatorDirectory, string workingDirectory, string testRoot)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var wrapperDirectory = Path.Combine(testRoot, "wrong-sdk-wrapper");
        Directory.CreateDirectory(wrapperDirectory);
        var wrapper = Path.Combine(wrapperDirectory, "dotnet");
        File.WriteAllText(
            wrapper,
            """
            #!/bin/sh
            if [ "$#" -eq 1 ] && [ "$1" = "--version" ]; then
              printf '%s\n' '10.0.203'
              exit 0
            fi
            exit 99
            """,
            new UTF8Encoding(false));
        File.SetUnixFileMode(
            wrapper,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = wrapperDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["READINESS_TEMP"] = Path.Combine(testRoot, "sdk-guard")
        };

        var bash = RunProcess(
            "bash",
            [Path.Combine(validatorDirectory, "run-validator.sh"), "--help"],
            workingDirectory,
            environment,
            expectedExitCode: ExitCodes.EnvironmentFailure);
        Assert(
            bash.StandardError.Contains("requires the repository-selected .NET 11 SDK", StringComparison.Ordinal),
            "Bash SDK major guard message");

        var powershell = RunProcess(
            "pwsh",
            [
                "-NoLogo",
                "-NoProfile",
                "-File",
                Path.Combine(validatorDirectory, "run-validator.ps1"),
                "--help"
            ],
            workingDirectory,
            environment,
            expectedExitCode: ExitCodes.EnvironmentFailure);
        Assert(
            powershell.StandardError.Contains("requires the repository-selected .NET 11 SDK", StringComparison.Ordinal),
            "PowerShell SDK major guard message");
        AssertEqual(
            NormalizeLineEndings(bash.StandardError),
            NormalizeLineEndings(powershell.StandardError),
            "launcher SDK mismatch failure parity");
    }

    private static void TestExternalArtifactGuard(
        string validatorDirectory,
        string workingDirectory,
        IReadOnlyDictionary<string, string> selectedSdkEnvironment)
    {
        var environment = new Dictionary<string, string>(
            selectedSdkEnvironment,
            StringComparer.OrdinalIgnoreCase)
        {
            ["READINESS_TEMP"] = validatorDirectory
        };
        var bash = RunProcess(
            "bash",
            [Path.Combine(validatorDirectory, "run-validator.sh"), "--help"],
            workingDirectory,
            environment,
            expectedExitCode: ExitCodes.EnvironmentFailure);
        Assert(
            bash.StandardError.Contains("outside the plugin tree", StringComparison.Ordinal),
            "Bash external artifact guard");

        var powershell = RunProcess(
            "pwsh",
            [
                "-NoLogo",
                "-NoProfile",
                "-File",
                Path.Combine(validatorDirectory, "run-validator.ps1"),
                "--help"
            ],
            workingDirectory,
            environment,
            expectedExitCode: ExitCodes.EnvironmentFailure);
        Assert(
            powershell.StandardError.Contains("outside the plugin tree", StringComparison.Ordinal),
            "PowerShell external artifact guard");
        AssertEqual(
            NormalizeLineEndings(bash.StandardError),
            NormalizeLineEndings(powershell.StandardError),
            "launcher plugin-membership failure parity");
    }

    private static void TestReadinessTempParity(
        string validatorDirectory,
        string workingDirectory,
        string testRoot,
        IReadOnlyDictionary<string, string> selectedSdkEnvironment)
    {
        var missingParent = Path.Combine(testRoot, "missing-parent", "readiness");
        AssertLauncherFailureParity(
            validatorDirectory,
            workingDirectory,
            selectedSdkEnvironment,
            missingParent,
            "the parent of READINESS_TEMP must already exist",
            "READINESS_TEMP parent");

        var readinessLink = Path.Combine(testRoot, "readiness-link-inside-plugin");
        if (TryCreateDirectorySymlink(readinessLink, validatorDirectory, "launcher root"))
        {
            AssertLauncherFailureParity(
                validatorDirectory,
                workingDirectory,
                selectedSdkEnvironment,
                readinessLink,
                "READINESS_TEMP resolves inside the plugin tree",
                "READINESS_TEMP resolved membership");
        }

        var linkedArtifacts = Path.Combine(testRoot, "linked-artifact-root");
        var packageTarget = Path.Combine(testRoot, "linked-package-target");
        Directory.CreateDirectory(linkedArtifacts);
        Directory.CreateDirectory(packageTarget);
        if (TryCreateDirectorySymlink(
                Path.Combine(linkedArtifacts, "packages"),
                packageTarget,
                "launcher artifact"))
        {
            AssertLauncherFailureParity(
                validatorDirectory,
                workingDirectory,
                selectedSdkEnvironment,
                linkedArtifacts,
                "external artifact directories cannot be symbolic links",
                "READINESS_TEMP artifact symlink",
                offendingArtifactPath: Path.Combine(linkedArtifacts, "packages"));
        }
    }

    private static void AssertLauncherFailureParity(
        string validatorDirectory,
        string workingDirectory,
        IReadOnlyDictionary<string, string> baseEnvironment,
        string readinessTemp,
        string expectedMessage,
        string name,
        string? offendingArtifactPath = null)
    {
        var environment = new Dictionary<string, string>(
            baseEnvironment,
            StringComparer.OrdinalIgnoreCase)
        {
            ["READINESS_TEMP"] = readinessTemp
        };
        var bash = RunProcess(
            "bash",
            [Path.Combine(validatorDirectory, "run-validator.sh"), "--help"],
            workingDirectory,
            environment,
            expectedExitCode: ExitCodes.EnvironmentFailure);
        var powershell = RunProcess(
            "pwsh",
            [
                "-NoLogo",
                "-NoProfile",
                "-File",
                Path.Combine(validatorDirectory, "run-validator.ps1"),
                "--help"
            ],
            workingDirectory,
            environment,
            expectedExitCode: ExitCodes.EnvironmentFailure);

        Assert(
            bash.StandardError.Contains(expectedMessage, StringComparison.Ordinal),
            $"Bash {name} message. Actual stderr: {bash.StandardError}");
        Assert(
            powershell.StandardError.Contains(expectedMessage, StringComparison.Ordinal),
            $"PowerShell {name} message. Actual stderr: {powershell.StandardError}");
        if (offendingArtifactPath is not null)
        {
            var bashPath = offendingArtifactPath.Replace('\\', '/');
            if (OperatingSystem.IsWindows() && bashPath.Length >= 3 && bashPath[1] == ':')
            {
                bashPath = "/" + char.ToLowerInvariant(bashPath[0]) + bashPath[2..];
            }

            AssertEqual($"error: {expectedMessage}: {bashPath}\n",
                NormalizeLineEndings(bash.StandardError), $"Bash {name} exact diagnostic");
            AssertEqual($"error: {expectedMessage}: {offendingArtifactPath}\n",
                NormalizeLineEndings(powershell.StandardError), $"PowerShell {name} exact diagnostic");
            return;
        }

        AssertEqual(
            NormalizeLineEndings(bash.StandardError),
            NormalizeLineEndings(powershell.StandardError),
            $"{name} failure parity");
    }

    private static void TestLauncherInvocationParity(
        string validatorDirectory,
        string workingDirectory,
        string testRoot)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var fakeBin = Path.Combine(testRoot, "fake-dotnet");
        Directory.CreateDirectory(fakeBin);
        var fakeDotnet = Path.Combine(fakeBin, "dotnet");
        File.WriteAllText(
            fakeDotnet,
            """
            #!/bin/sh
            : "${READINESS_DOTNET_LOG:?}"
            {
              printf 'CALL'
              for argument in "$@"; do
                printf '\t%s' "$argument"
              done
              printf '\n'
            } >> "$READINESS_DOTNET_LOG"

            if [ "$#" -eq 1 ] && [ "$1" = "--version" ]; then
              printf '%s\n' "${FAKE_SDK_VERSION:-11.0.100}"
              exit "${FAKE_SDK_EXIT:-0}"
            fi

            if [ "${1:-}" = "msbuild" ]; then
              target=
              output=
              for argument in "$@"; do
                case "$argument" in
                  -target:Restore)
                    target=restore
                    ;;
                  '-target:VerifyNoPackageReferences;Build')
                    target=build
                    ;;
                  -property:BaseOutputPath=*)
                    output="${argument#-property:BaseOutputPath=}"
                    ;;
                esac
              done

              if [ "$target" = "restore" ]; then
                exit "${FAKE_RESTORE_EXIT:-0}"
              fi
              if [ "$target" = "build" ]; then
                build_exit="${FAKE_BUILD_EXIT:-0}"
                if [ "$build_exit" -ne 0 ]; then
                  exit "$build_exit"
                fi
                mkdir -p -- "$output/Release/net11.0"
                : > "$output/Release/net11.0/BlazorComponentReadiness.Validator.dll"
                exit 0
              fi
              exit 97
            fi

            case "${1:-}" in
              */BlazorComponentReadiness.Validator.dll)
                exit "${FAKE_VALIDATOR_EXIT:-0}"
                ;;
            esac
            exit 98
            """,
            new UTF8Encoding(false));
        File.SetUnixFileMode(
            fakeDotnet,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);

        var bashTemp = Path.Combine(testRoot, "parity-bash");
        var powershellTemp = Path.Combine(testRoot, "parity-powershell");
        var bashLog = Path.Combine(testRoot, "parity-bash.log");
        var powershellLog = Path.Combine(testRoot, "parity-powershell.log");
        var bash = RunFakeLauncher(
            "bash",
            [Path.Combine(validatorDirectory, "run-validator.sh")],
            workingDirectory,
            fakeBin,
            bashTemp,
            bashLog,
            validatorExit: ExitCodes.InvalidUsage,
            restoreExit: 0,
            buildExit: 0);
        var powershell = RunFakeLauncher(
            "pwsh",
            [
                "-NoLogo",
                "-NoProfile",
                "-File",
                Path.Combine(validatorDirectory, "run-validator.ps1")
            ],
            workingDirectory,
            fakeBin,
            powershellTemp,
            powershellLog,
            validatorExit: ExitCodes.InvalidUsage,
            restoreExit: 0,
            buildExit: 0);
        AssertEqual(
            ExitCodes.InvalidUsage,
            bash.ExitCode,
            "Bash documented validator exit propagation");
        AssertEqual(
            ExitCodes.InvalidUsage,
            powershell.ExitCode,
            "PowerShell documented validator exit propagation");

        var bashInvocations = ReadNormalizedInvocations(bashLog, bashTemp);
        var powershellInvocations = ReadNormalizedInvocations(powershellLog, powershellTemp);
        Assert(
            bashInvocations.SequenceEqual(powershellInvocations, StringComparer.Ordinal),
            "Bash and PowerShell restore/build/validator arguments must be identical.\n" +
            $"Bash: [{string.Join(" | ", bashInvocations)}]\n" +
            $"PowerShell: [{string.Join(" | ", powershellInvocations)}]");
        AssertEqual(4, bashInvocations.Length, "launcher invocation count");
        AssertEqual("CALL\t--version", bashInvocations[0], "SDK invocation arguments");
        Assert(
            bashInvocations[1].StartsWith("CALL\tmsbuild\t", StringComparison.Ordinal) &&
            bashInvocations[1].Contains(
                Path.Combine(
                    "plugins",
                    "dotnet-blazor",
                    "skills",
                    "blazor-component-readiness",
                    "scripts",
                    "validator",
                    "BlazorComponentReadiness.Validator.csproj") +
                "\t-target:Restore",
                StringComparison.Ordinal),
            "restore invocation arguments");
        Assert(
            bashInvocations[2].Contains(
                "\t-target:VerifyNoPackageReferences;Build",
                StringComparison.Ordinal),
            "build invocation arguments");
        AssertEqual(
            "CALL\t<TEMP>/bin/Release/net11.0/BlazorComponentReadiness.Validator.dll" +
            "\t--parity-token\tparity-value",
            bashInvocations[3],
            "validator DLL path and argument forwarding");

        AssertFakeLauncherPhaseExit(
            validatorDirectory,
            workingDirectory,
            fakeBin,
            testRoot,
            restoreExit: 19,
            buildExit: 0,
            phase: "restore");
        AssertFakeLauncherPhaseExit(
            validatorDirectory,
            workingDirectory,
            fakeBin,
            testRoot,
            restoreExit: 0,
            buildExit: 23,
            phase: "build");
        AssertDocumentedValidatorExit(
            validatorDirectory,
            workingDirectory,
            fakeBin,
            testRoot,
            ExitCodes.ValidationFailure);
        AssertDocumentedValidatorExit(
            validatorDirectory,
            workingDirectory,
            fakeBin,
            testRoot,
            ExitCodes.EnvironmentFailure);
        AssertUndocumentedValidatorExit(
            validatorDirectory,
            workingDirectory,
            fakeBin,
            testRoot);
    }

    private static void AssertDocumentedValidatorExit(
        string validatorDirectory,
        string workingDirectory,
        string fakeBin,
        string testRoot,
        int validatorExit)
    {
        var bash = RunFakeLauncher(
            "bash",
            [Path.Combine(validatorDirectory, "run-validator.sh")],
            workingDirectory,
            fakeBin,
            Path.Combine(testRoot, $"validator-{validatorExit}-bash"),
            Path.Combine(testRoot, $"validator-{validatorExit}-bash.log"),
            validatorExit,
            restoreExit: 0,
            buildExit: 0);
        var powershell = RunFakeLauncher(
            "pwsh",
            [
                "-NoLogo",
                "-NoProfile",
                "-File",
                Path.Combine(validatorDirectory, "run-validator.ps1")
            ],
            workingDirectory,
            fakeBin,
            Path.Combine(testRoot, $"validator-{validatorExit}-powershell"),
            Path.Combine(testRoot, $"validator-{validatorExit}-powershell.log"),
            validatorExit,
            restoreExit: 0,
            buildExit: 0);
        AssertEqual(validatorExit, bash.ExitCode, $"Bash validator exit {validatorExit}");
        AssertEqual(validatorExit, powershell.ExitCode, $"PowerShell validator exit {validatorExit}");
    }

    private static ProcessResult RunFakeLauncher(
        string executable,
        IReadOnlyList<string> launcherArguments,
        string workingDirectory,
        string fakeBin,
        string readinessTemp,
        string logPath,
        int validatorExit,
        int restoreExit,
        int buildExit)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(readinessTemp)!);
        if (Directory.Exists(readinessTemp))
        {
            Directory.Delete(readinessTemp, recursive: true);
        }

        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }

        var arguments = launcherArguments
            .Concat(["--parity-token", "parity-value"])
            .ToArray();
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["READINESS_TEMP"] = readinessTemp,
            ["READINESS_DOTNET_LOG"] = logPath,
            ["FAKE_VALIDATOR_EXIT"] = validatorExit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["FAKE_RESTORE_EXIT"] = restoreExit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["FAKE_BUILD_EXIT"] = buildExit.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return RunProcess(
            executable,
            arguments,
            workingDirectory,
            environment,
            expectedExitCode: null);
    }

    private static void AssertFakeLauncherPhaseExit(
        string validatorDirectory,
        string workingDirectory,
        string fakeBin,
        string testRoot,
        int restoreExit,
        int buildExit,
        string phase)
    {
        var bash = RunFakeLauncher(
            "bash",
            [Path.Combine(validatorDirectory, "run-validator.sh")],
            workingDirectory,
            fakeBin,
            Path.Combine(testRoot, $"{phase}-exit-bash"),
            Path.Combine(testRoot, $"{phase}-exit-bash.log"),
            validatorExit: 0,
            restoreExit: restoreExit,
            buildExit: buildExit);
        var powershell = RunFakeLauncher(
            "pwsh",
            [
                "-NoLogo",
                "-NoProfile",
                "-File",
                Path.Combine(validatorDirectory, "run-validator.ps1")
            ],
            workingDirectory,
            fakeBin,
            Path.Combine(testRoot, $"{phase}-exit-powershell"),
            Path.Combine(testRoot, $"{phase}-exit-powershell.log"),
            validatorExit: 0,
            restoreExit: restoreExit,
            buildExit: buildExit);
        AssertEqual(
            ExitCodes.EnvironmentFailure,
            bash.ExitCode,
            $"Bash {phase} failure mapping");
        AssertEqual(
            ExitCodes.EnvironmentFailure,
            powershell.ExitCode,
            $"PowerShell {phase} failure mapping");
        Assert(
            bash.StandardError.Contains(
                $"readiness validator {phase} failed",
                StringComparison.Ordinal),
            $"Bash {phase} failure message");
        AssertEqual(
            NormalizeLineEndings(bash.StandardError),
            NormalizeLineEndings(powershell.StandardError),
            $"{phase} failure parity");
    }

    private static void AssertUndocumentedValidatorExit(
        string validatorDirectory,
        string workingDirectory,
        string fakeBin,
        string testRoot)
    {
        var bash = RunFakeLauncher(
            "bash",
            [Path.Combine(validatorDirectory, "run-validator.sh")],
            workingDirectory,
            fakeBin,
            Path.Combine(testRoot, "validator-exit-bash"),
            Path.Combine(testRoot, "validator-exit-bash.log"),
            validatorExit: 29,
            restoreExit: 0,
            buildExit: 0);
        var powershell = RunFakeLauncher(
            "pwsh",
            [
                "-NoLogo",
                "-NoProfile",
                "-File",
                Path.Combine(validatorDirectory, "run-validator.ps1")
            ],
            workingDirectory,
            fakeBin,
            Path.Combine(testRoot, "validator-exit-powershell"),
            Path.Combine(testRoot, "validator-exit-powershell.log"),
            validatorExit: 29,
            restoreExit: 0,
            buildExit: 0);
        AssertEqual(
            ExitCodes.EnvironmentFailure,
            bash.ExitCode,
            "Bash undocumented validator exit mapping");
        AssertEqual(
            ExitCodes.EnvironmentFailure,
            powershell.ExitCode,
            "PowerShell undocumented validator exit mapping");
        Assert(
            bash.StandardError.Contains(
                "readiness validator returned undocumented exit code 29",
                StringComparison.Ordinal),
            "Bash undocumented validator exit message");
        AssertEqual(
            NormalizeLineEndings(bash.StandardError),
            NormalizeLineEndings(powershell.StandardError),
            "undocumented validator exit parity");
    }

    private static string[] ReadNormalizedInvocations(string logPath, string readinessTemp)
    {
        var aliases = OperatingSystem.IsMacOS() &&
            readinessTemp.StartsWith("/tmp/", StringComparison.Ordinal)
                ? new[] { "/private" + readinessTemp, readinessTemp }
                : [readinessTemp];
        return File.ReadAllLines(logPath)
            .Select(line => aliases.Aggregate(
                line,
                (current, alias) => current.Replace(alias, "<TEMP>", StringComparison.Ordinal)))
            .ToArray();
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void WriteHostileAmbientFiles(string root)
    {
        File.WriteAllText(
            Path.Combine(root, "Directory.Build.props"),
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <BaseOutputPath>ambient-bin/</BaseOutputPath>
                <HostileDirectoryBuildProps>true</HostileDirectoryBuildProps>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(root, "Directory.Build.targets"),
            """
            <Project>
              <Target Name="HostileDirectoryBuildTarget" BeforeTargets="BeforeBuild">
                <Error Text="Hostile Directory.Build.targets was imported." />
              </Target>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(root, "Directory.Packages.props"),
            """
            <Project>
              <ItemGroup>
                <GlobalPackageReference Include="Hostile.Ambient.Package" Version="99.0.0" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Directory.Build.rsp"), "-target:AmbientResponseFileWasLoaded\n");
        File.WriteAllText(Path.Combine(root, "MSBuild.rsp"), "-target:AmbientResponseFileWasLoaded\n");
        File.WriteAllText(
            Path.Combine(root, "NuGet.config"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="hostile" value="./source-that-does-not-exist" />
              </packageSources>
            </configuration>
            """);
        File.WriteAllText(
            Path.Combine(root, "AmbientProbe.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
              </PropertyGroup>
              <Target Name="DetectAmbientPolicy">
                <Message
                  Importance="high"
                  Text="AMBIENT_POLICY:$(HostileDirectoryBuildProps)|@(GlobalPackageReference)" />
              </Target>
            </Project>
            """);
    }

    private static void CreatePackage(
        string path,
        string id,
        string version,
        long? nuspecLength = null,
        bool additionalNuspec = false,
        string? nuspecEntry = null,
        string? unsafeEntry = null,
        string? symbolicLinkEntry = null)
    {
        var content = Encoding.UTF8.GetBytes(
            $"<?xml version=\"1.0\"?><package><metadata><id>{id}</id><version>{version}</version></metadata></package>");
        if (nuspecLength is not null)
        {
            if (nuspecLength < content.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(nuspecLength));
            }

            Array.Resize(ref content, checked((int)nuspecLength.Value));
            content.AsSpan(content.Length - checked((int)(nuspecLength.Value - Encoding.UTF8.GetByteCount(
                $"<?xml version=\"1.0\"?><package><metadata><id>{id}</id><version>{version}</version></metadata></package>")))).Fill((byte)' ');
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(archive, nuspecEntry ?? $"{id}.nuspec", content);
        if (additionalNuspec)
        {
            WriteEntry(archive, "second.nuspec", content);
        }

        if (unsafeEntry is not null)
        {
            WriteEntry(archive, unsafeEntry, [1]);
        }

        if (symbolicLinkEntry is not null)
        {
            var entry = archive.CreateEntry(symbolicLinkEntry, CompressionLevel.NoCompression);
            entry.ExternalAttributes = 0xA000 << 16;
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false), leaveOpen: false);
            writer.Write("target");
        }
    }

    private static void CreateRawPackage(string path, string entryName, byte[] content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(archive, entryName, content);
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var output = entry.Open();
        output.Write(content);
    }

    private static ProcessResult RunProcess(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        int? expectedExitCode)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            process.StartInfo.Environment[name] = value;
        }

        ResolveWindowsBash(process.StartInfo);
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(milliseconds: 180_000))
        {
            process.Kill(entireProcessTree: true);
            _ = process.WaitForExit(milliseconds: 5_000);
            _ = Task.WaitAll(new Task[] { standardOutput, standardError }, millisecondsTimeout: 5_000);
            throw new TimeoutException($"{executable} did not exit within three minutes.");
        }

        if (!Task.WaitAll(
                new Task[] { standardOutput, standardError },
                millisecondsTimeout: 5_000))
        {
            throw new TimeoutException(
                $"{executable} output streams did not drain within five seconds.");
        }
        var result = new ProcessResult(process.ExitCode, standardOutput.Result, standardError.Result);
        if (expectedExitCode is not null && result.ExitCode != expectedExitCode)
        {
            throw new InvalidOperationException(
                $"{executable} exited {result.ExitCode}, expected {expectedExitCode}.\n{result}");
        }

        return result;
    }

    internal static void ResolveWindowsBash(ProcessStartInfo startInfo)
    {
        if (!OperatingSystem.IsWindows() || startInfo.FileName != "bash")
        {
            return;
        }

        startInfo.Environment.TryGetValue("PATH", out var path);
        var wslShim = Path.Combine(Environment.SystemDirectory, "bash.exe");
        foreach (var directory in (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.GetFullPath(Path.Combine(directory.Trim('"'), "bash.exe"));
            if (File.Exists(candidate) && !string.Equals(candidate, wslShim, StringComparison.OrdinalIgnoreCase))
            {
                startInfo.FileName = candidate;
                Console.WriteLine($"Windows test Bash: {candidate}");
                return;
            }
        }

        throw new InvalidOperationException(
            "Windows launcher tests require an existing non-WSL bash.exe in the effective process PATH.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            File.Copy(file, target);
        }
    }

    private static void MakeReadOnly(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
            }

            return;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Prepend(root))
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead |
                UnixFileMode.UserExecute |
                UnixFileMode.GroupRead |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead |
                UnixFileMode.OtherExecute);
        }
    }

    private static void MakeWritable(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            }

            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Prepend(root))
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void SetSparseLength(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
    }

    private static string ResolveDotnetExecutable(IReadOnlyDictionary<string, string> environment)
    {
        if (environment.TryGetValue("PATH", out var path))
        {
            var first = path.Split(Path.PathSeparator, 2)[0];
            var candidates = OperatingSystem.IsWindows()
                ? new[] { Path.Combine(first, "dotnet.exe"), Path.Combine(first, "dotnet.cmd") }
                : new[] { Path.Combine(first, "dotnet") };
            var candidate = candidates.FirstOrDefault(File.Exists);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return "dotnet";
    }

    private static bool TryCreateFileSymlink(string link, string target, string name)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            Console.WriteLine($"SKIP: {name} file symlink creation is unavailable: {exception.Message}");
            return false;
        }
    }

    private static bool TryCreateDirectorySymlink(string link, string target, string name)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            Console.WriteLine($"SKIP: {name} directory symlink creation is unavailable: {exception.Message}");
            return false;
        }
    }

    private static bool IsUnder(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return fullPath.StartsWith(
            fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar,
            comparison);
    }

    private static string Sha256(byte[] content) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));

    private static void ExpectValidation(Action action, string name) =>
        ExpectException<DeterministicValidationException>(action, name);

    private static void ExpectLimit(Action action, long actual, string name)
    {
        try
        {
            action();
            throw new InvalidOperationException($"{name}: expected a resource-limit failure.");
        }
        catch (ResourceLimitException exception)
        {
            AssertEqual(actual, exception.Actual, $"{name} actual byte count");
        }
    }

    private static void ExpectIOException(Action action, string name) =>
        ExpectException<IOException>(action, name);

    private static void ExpectException<TException>(Action action, string name)
        where TException : Exception
    {
        try
        {
            action();
            throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}.");
        }
        catch (TException)
        {
        }
    }

    private static void AssertBytes(byte[] expected, byte[] actual, string name) =>
        Assert(expected.AsSpan().SequenceEqual(actual), $"{name}: bytes differ.");

    private static void AssertEqual<T>(T expected, T actual, string name) =>
        Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"{name}: expected '{expected}', actual '{actual}'.");

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public override string ToString() =>
            $"stdout:\n{StandardOutput}\nstderr:\n{StandardError}";
    }
}
