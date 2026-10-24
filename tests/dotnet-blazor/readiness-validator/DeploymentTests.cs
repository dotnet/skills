using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.Validation;

internal static class DeploymentTests
{
    private static readonly List<object> Commands = [];

    public static void Run(string repositoryRoot, string pluginRoot)
    {
        Commands.Clear();
        var artifacts = Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS")
            ?? throw new InvalidOperationException("Deployment tests require external READINESS_TEST_ARTIFACTS.");
        var artifactRoot = ValidateArtifactRoot(artifacts, repositoryRoot,
            Environment.GetEnvironmentVariable("READINESS_STAGED_SKILL"));
        var root = Path.Combine(artifactRoot, $"deployment-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sourceSkill = Path.Combine(pluginRoot, "skills", "blazor-component-readiness");
        var sourceBefore = Inventory(pluginRoot);
        var installed = Path.Combine(root, "installed", "dotnet-blazor");
        Copy(pluginRoot, installed);
        var isolated = Environment.GetEnvironmentVariable("READINESS_STAGED_SKILL");
        if (string.IsNullOrWhiteSpace(isolated))
        {
            isolated = Path.Combine(root, "trial", "blazor-component-readiness");
            Copy(sourceSkill, isolated);
        }
        using var sourceManifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(pluginRoot, "plugin.json")));
        var sourceVersion = sourceManifest.RootElement.GetProperty("version").GetString()!;
        var isolatedBefore = Inventory(isolated);
        var installedBefore = Inventory(installed);
        var inputInventories = new Dictionary<string, object>(StringComparer.Ordinal);
        var stagingReceipt = Environment.GetEnvironmentVariable("READINESS_STAGING_RECEIPT");
        var stagingReceiptHash = string.IsNullOrWhiteSpace(stagingReceipt) ? null : FileHash(stagingReceipt);
        if (stagingReceiptHash is not null)
        {
            using var staging = JsonDocument.Parse(File.ReadAllBytes(stagingReceipt!));
            Assert(staging.RootElement.GetProperty("stagedSkill").GetString() == isolated,
                "helper consumes the exact staged skill named by its bound Vally receipt");
        }
        try
        {
            MetadataControls(root, sourceSkill, File.ReadAllBytes(Path.Combine(pluginRoot, "plugin.json")), sourceVersion);
            foreach (var (layout, skill) in new[]
                     {
                         ("installed", Path.Combine(installed, "skills", "blazor-component-readiness")),
                         ("isolated", isolated)
                     })
            {
                foreach (var shell in new[] { "pwsh", "bash" })
                {
                    var caseRoot = Path.Combine(root, layout + "-" + shell);
                    Directory.CreateDirectory(caseRoot);
                    var scratch = layout == "isolated"
                        ? Path.Combine(Path.GetDirectoryName(skill)!, "out", shell + "-validator-cache")
                        : Path.Combine(root, "dotnet-blazor-sibling", shell);
                    Directory.CreateDirectory(Path.GetDirectoryName(scratch)!);
                    var environment = new Dictionary<string, string>
                    {
                        ["READINESS_TEMP"] = scratch,
                        ["READINESS_SKILL_ROOT"] = skill
                    };
                    var inputs = Path.Combine(caseRoot, "inputs");
                    Directory.CreateDirectory(inputs);
                    var fixture = AssessmentTests.CreateInputFixture(inputs);
                    var publicInput = Path.Combine(inputs, "input.public-confirmed.json");
                    File.WriteAllBytes(publicInput, InputManifestService.Serialize(fixture.Confirmed with { OwnerInputs = [] }));
                    var inputBefore = Inventory(inputs);
                    Launch(shell, skill, caseRoot, environment, 0, "package", "inspect", "--nupkg", fixture.NupkgPath);
                    var dll = Path.Combine(scratch, "bin", "Release", "net11.0", "BlazorComponentReadiness.Validator.dll");
                    foreach (var kind in new[] { "package", "component" })
                    {
                        Produce(shell, skill, caseRoot, inputs, publicInput, dll, kind, environment, sourceVersion);
                    }
                    RuntimeMetadataControls(dll, sourceSkill, caseRoot, sourceVersion);
                    ScratchControls(shell, skill, caseRoot, root, environment);
                    var inputAfter = Inventory(inputs, "out");
                    Assert(inputBefore.SequenceEqual(inputAfter), $"{layout}/{shell} immutable assessed inputs");
                    inputInventories.Add(layout + "/" + shell, new { before = inputBefore, after = inputAfter });
                    Console.WriteLine($"PASS deployment {layout}/{shell}: package/component report and reader render/verify; scratch controls.");
                }
            }
        }
        finally
        {
            AssertInventory(sourceBefore, pluginRoot, "reviewed plugin");
            AssertInventory(installedBefore, installed, "installed helper");
            AssertInventory(isolatedBefore, isolated, "isolated helper");
        }
        if (stagingReceiptHash is not null)
            Assert(stagingReceiptHash == FileHash(stagingReceipt!), "bound Vally receipt remains unchanged");
        File.WriteAllText(Path.Combine(root, "helper-receipt.json"), JsonSerializer.Serialize(new
        {
            result = "PASS",
            command = Environment.GetCommandLineArgs(),
            sdkClients = 0,
            modelCalls = 0,
            stagingReceipt,
            stagingReceiptSha256 = stagingReceiptHash,
            stagedSkill = isolated,
            sourceInventory = new { before = sourceBefore, after = Inventory(pluginRoot) },
            installedInventory = new { before = installedBefore, after = Inventory(installed) },
            isolatedInventory = new { before = isolatedBefore, after = Inventory(isolated) },
            inputInventories,
            commands = Commands,
            unrun = new[] { "Native POSIX platform", "Cross-drive scratch" }
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Deployment acceptance artifacts: {root}");
    }

    public static void RunAdmission(string repositoryRoot, string pluginRoot)
    {
        var artifacts = Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS")
            ?? throw new InvalidOperationException("Admission tests require external READINESS_TEST_ARTIFACTS.");
        var stagedSkill = Environment.GetEnvironmentVariable("READINESS_STAGED_SKILL");
        var root = Path.Combine(ValidateArtifactRoot(artifacts, repositoryRoot, stagedSkill), $"admission-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sourceBefore = Inventory(pluginRoot);
        var sourceEntriesBefore = Directory.GetFileSystemEntries(repositoryRoot).Order(StringComparer.Ordinal).ToArray();
        var previous = Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS");
        try
        {
            foreach (var target in new[] { repositoryRoot, stagedSkill }.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                var alias = Path.Combine(root, "alias-" + Guid.NewGuid().ToString("N"));
                CreateDirectoryAlias(alias, target!);
                var rejected = Path.Combine(alias, "admission-prohibited");
                Assert(!Directory.Exists(rejected), "admission negative requires a nonexistent leaf");
                Environment.SetEnvironmentVariable("READINESS_TEST_ARTIFACTS", rejected);
                var failure = false;
                try
                {
                    Run(repositoryRoot, pluginRoot);
                }
                catch (InvalidOperationException exception) when (
                    exception.Message.Contains("physically remain outside", StringComparison.Ordinal))
                {
                    failure = true;
                }
                Assert(failure && !Directory.Exists(rejected), "external alias is rejected before any allocation");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("READINESS_TEST_ARTIFACTS", previous);
        }
        AssertInventory(sourceBefore, pluginRoot, "admission-only reviewed plugin");
        Assert(sourceEntriesBefore.SequenceEqual(
            Directory.GetFileSystemEntries(repositoryRoot).Order(StringComparer.Ordinal)), "no new checkout entry");
        Console.WriteLine("PASS physical contract admission: external aliases rejected before allocation; source inventory unchanged.");
    }

    private static string ValidateArtifactRoot(string artifacts, string repositoryRoot, string? stagedSkill)
    {
        var prospective = ResolveProspectiveDirectory(artifacts);
        foreach (var boundary in new[] { repositoryRoot, stagedSkill }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var physicalBoundary = ResolveProspectiveDirectory(boundary!);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            Assert(!string.Equals(prospective, physicalBoundary, comparison) &&
                !prospective.StartsWith(physicalBoundary.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison),
                "Test artifacts must physically remain outside reviewed source and staged helpers before allocation.");
        }
        return prospective;
    }

    private static string ResolveProspectiveDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)
            ?? throw new InvalidOperationException("Artifact directory has no filesystem root.");
        foreach (var segment in Path.GetRelativePath(current, full).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = new DirectoryInfo(Path.Combine(current, segment));
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = directory.ResolveLinkTarget(true)
                    ?? throw new IOException($"Could not resolve admission directory alias: {directory.FullName}");
                current = ResolveProspectiveDirectory(target.FullName);
            }
            else current = directory.FullName;
        }
        return current;
    }

    private static void RuntimeMetadataControls(string dll, string sourceSkill, string root, string version)
    {
        var plugin = Path.Combine(root, "runtime-metadata");
        var skill = Path.Combine(plugin, OperatingSystem.IsWindows() ? "Skills" : "skills", "blazor-component-readiness");
        Copy(sourceSkill, skill);
        var source = File.ReadAllBytes(Path.Combine(sourceSkill, "plugin-manifest.json"));
        var manifest = Path.Combine(plugin, "plugin.json");
        var snapshot = Path.Combine(skill, "plugin-manifest.json");
        var env = new Dictionary<string, string> { ["READINESS_SKILL_ROOT"] = skill };
        File.WriteAllBytes(manifest, source);
        Execute("dotnet", [dll, "--help"], root, env, 0);
        foreach (var selected in new[] { manifest, snapshot })
        {
            foreach (var (name, bytes, exit) in new (string, byte[]?, int)[]
                     {
                         ("missing", null, 3),
                         ("malformed", "{"u8.ToArray(), 1),
                         ("identity", System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(source)
                             .Replace("dotnet-blazor", "other-plugin")), 1),
                         ("version", System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(source)
                             .Replace(version, "00.1.2")), 1),
                         ("non-version mismatch", System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(source)
                             .Replace("Skills and agents", "Changed description")), 1)
                     })
            {
                if (bytes is null) File.Delete(selected);
                else File.WriteAllBytes(selected, bytes);
                var result = Execute("dotnet", [dll, "--help"], root, env, exit);
                Assert(!result.Contains("Usage:", StringComparison.Ordinal), $"{name} actual startup is fail-closed");
                File.WriteAllBytes(selected, source);
            }
        }
        foreach (var shell in new[] { "pwsh", "bash" })
        {
            var scratch = Path.Combine(root, "case-check-" + shell);
            Launch(shell, skill, root, new() { ["READINESS_TEMP"] = scratch }, 0, "--help");
            Launch(shell, skill, root, new() { ["READINESS_TEMP"] = Path.Combine(plugin, "rejected") }, 3, "--help");
            File.Delete(manifest);
            var before = Inventory(plugin);
            Launch(shell, skill, root, new() { ["READINESS_TEMP"] = scratch }, 3, "--help");
            AssertInventory(before, plugin, "case-varied broken installed layout");
            File.WriteAllBytes(manifest, source);
        }
        var isolatedSkill = Path.Combine(root, "runtime-isolated", "blazor-component-readiness");
        Copy(sourceSkill, isolatedSkill);
        env["READINESS_SKILL_ROOT"] = isolatedSkill;
        var isolatedSnapshot = Path.Combine(isolatedSkill, "plugin-manifest.json");
        foreach (var (bytes, exit) in new (byte[]?, int)[]
                 {
                     (null, 3),
                     ("{"u8.ToArray(), 1),
                     (System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(source)
                         .Replace("dotnet-blazor", "other-plugin")), 1),
                     (System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(source)
                         .Replace(version, "00.1.2")), 1)
                 })
        {
            if (bytes is null) File.Delete(isolatedSnapshot);
            else File.WriteAllBytes(isolatedSnapshot, bytes);
            Execute("dotnet", [dll, "--help"], root, env, exit);
            File.WriteAllBytes(isolatedSnapshot, source);
        }
    }

    private static void MetadataControls(string root, string sourceSkill, byte[] sourceManifest, string version)
    {
        var skill = Path.Combine(root, "metadata", "blazor-component-readiness");
        Copy(sourceSkill, skill);
        var snapshot = Path.Combine(skill, "plugin-manifest.json");
        var previous = Environment.GetEnvironmentVariable("READINESS_SKILL_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", skill);
            Assert(ContractVersions.PluginVersion == version, "isolated version equals independent canonical source");
            foreach (var (name, bytes) in new (string, byte[]?)[]
                     {
                         ("missing", null),
                         ("malformed", "{"u8.ToArray()),
                         ("wrong identity", System.Text.Encoding.UTF8.GetBytes(
                             System.Text.Encoding.UTF8.GetString(sourceManifest).Replace("dotnet-blazor", "other-plugin"))),
                         ("noncanonical version", System.Text.Encoding.UTF8.GetBytes(
                             System.Text.Encoding.UTF8.GetString(sourceManifest).Replace(version, "00.1.2")))
                     })
            {
                if (bytes is null) File.Delete(snapshot);
                else File.WriteAllBytes(snapshot, bytes);
                Assert(CliApplication.Run(["--help"], new StringWriter(), new StringWriter(), validateDeployment: true) != 0,
                    $"isolated {name} metadata fails without fallback");
                File.WriteAllBytes(snapshot, sourceManifest);
            }

            var plugin = Path.Combine(root, "metadata-installed");
            var installedSkill = Path.Combine(plugin, "skills", "blazor-component-readiness");
            Copy(sourceSkill, installedSkill);
            var manifest = Path.Combine(plugin, "plugin.json");
            File.WriteAllBytes(manifest, sourceManifest);
            Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", installedSkill);
            Assert(ContractVersions.PluginVersion == version, "installed authority agrees with source");
            foreach (var bytes in new[]
                     {
                         System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(sourceManifest)
                             .Replace(version, "99.0.0")),
                         System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(sourceManifest)
                             .Replace("Skills and agents", "Changed description"))
                     })
            {
                File.WriteAllBytes(Path.Combine(installedSkill, "plugin-manifest.json"), bytes);
                var error = new StringWriter();
                Assert(CliApplication.Run(["--help"], new StringWriter(), error, validateDeployment: true) == 1 &&
                    error.ToString().Contains("does not match the authoritative", StringComparison.Ordinal),
                    "installed version/non-version mismatch fails, rather than selecting snapshot authority");
            }
            File.WriteAllBytes(Path.Combine(installedSkill, "plugin-manifest.json"), sourceManifest);
            File.Delete(manifest);
            Assert(CliApplication.Run(["--help"], new StringWriter(), new StringWriter(), validateDeployment: true) == 3,
                "missing installed manifest never becomes isolated");
            foreach (var shell in new[] { "pwsh", "bash" })
            {
                var temp = Path.Combine(root, "broken-installed-" + shell);
                var failure = Launch(shell, installedSkill, root,
                    new() { ["READINESS_TEMP"] = temp }, 3, "--help");
                Assert(failure.Contains("authoritative plugin.json", StringComparison.Ordinal) &&
                    !Directory.Exists(temp), "broken installed launcher fails before allocating scratch");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", previous);
        }
    }

    private static void Produce(
        string shell, string skill, string caseRoot, string inputs, string input, string dll, string kind,
        Dictionary<string, string> environment, string version)
    {
        var output = Path.Combine(inputs, "out", kind);
        Directory.CreateDirectory(output);
        var assessmentPath = Path.Combine(output, "assessment.json");
        string[] component = kind == "component" ? ["--component", "fancy-tree"] : [];
        Execute("dotnet", [dll, "assessment", "init", "--kind", kind, "--root", inputs, "--input", input,
            "--output", assessmentPath, .. component], caseRoot, environment, 0);
        var initialized = AssessmentService.Parse(File.ReadAllBytes(assessmentPath));
        var assessment = SeparationTests.Complete(initialized);
        var evidence = AssessmentTests.BuildEvidence(assessment.Identity);
        File.WriteAllBytes(assessmentPath, AssessmentService.Serialize(assessment));
        var evidencePath = Path.Combine(output, "evidence.json");
        File.WriteAllBytes(evidencePath, CanonicalEvidenceJson.SerializeBundle(evidence));
        Execute("dotnet", [dll, "assessment", "validate", "--root", inputs, "--input", input,
            "--assessment", assessmentPath, "--evidence", evidencePath], caseRoot, environment, 0);
        var revisions = Path.Combine(output, "revisions");
        Launch(shell, skill, caseRoot, environment, 0, "report", "render", "--root", inputs, "--input", input,
            "--assessment", assessmentPath, "--evidence", evidencePath, "--output", revisions);
        var revision = Path.Combine(revisions, "0001");
        Execute("dotnet", [dll, "report", "verify", "--root", inputs, "--revision", revision],
            caseRoot, environment, 0);
        var reader = Path.Combine(output, "reader");
        Execute("dotnet", [dll, "reader", "render", "--root", inputs, "--revision", revision, "--output", reader],
            caseRoot, environment, 0);
        Execute("dotnet", [dll, "reader", "verify", "--root", inputs, "--revision", revision, "--output", reader],
            caseRoot, environment, 0);
        using var receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(revision, kind + ".validation.json")));
        Assert(receipt.RootElement.GetProperty("plugin_version").GetString() == version,
            "actual rendered report retains source-bound plugin version");
        Assert(File.ReadAllText(Path.Combine(revision, kind + ".report.md")).Length > 0,
            "canonical delivered report is readable");
        var report = Path.Combine(revision, kind + ".report.md");
        var original = File.ReadAllBytes(report);
        File.AppendAllText(report, "tampered");
        Execute("dotnet", [dll, "report", "verify", "--root", inputs, "--revision", revision],
            caseRoot, environment, 1);
        File.WriteAllBytes(report, original);
        Execute("dotnet", [dll, "report", "verify", "--root", inputs, "--revision", revision],
            caseRoot, environment, 0);
    }

    private static void ScratchControls(
        string shell, string skill, string caseRoot, string root, Dictionary<string, string> environment)
    {
        var protectedRoot = Path.GetFileName(Path.GetDirectoryName(skill)) == "skills"
            ? Path.GetFullPath(Path.Combine(skill, "..", "..")) : skill;
        foreach (var temp in new[]
                 {
                     protectedRoot,
                     Path.Combine(protectedRoot, "new-forbidden-scratch"),
                     Path.Combine(skill, "scripts", "..", "new-forbidden-scratch")
                 })
        {
            var failure = Launch(shell, skill, caseRoot, new(environment) { ["READINESS_TEMP"] = temp }, 3, "--help");
            Assert(failure.Contains("outside the plugin tree", StringComparison.Ordinal), "inside-tree scratch rejection");
            Assert(!Directory.Exists(Path.Combine(protectedRoot, "new-forbidden-scratch")) &&
                !Directory.Exists(Path.Combine(skill, "new-forbidden-scratch")), "no rejected scratch creation");
        }
        var defaultEnv = new Dictionary<string, string>(environment)
        {
            ["READINESS_TEMP"] = "",
            ["TMPDIR"] = protectedRoot,
            ["TEMP"] = protectedRoot,
            ["TMP"] = protectedRoot
        };
        var before = Inventory(protectedRoot);
        Launch(shell, skill, caseRoot, defaultEnv, 3, "--help");
        AssertInventory(before, protectedRoot, "forbidden default temporary parent");
        if (OperatingSystem.IsWindows())
        {
            Launch(shell, skill, caseRoot, new(environment) { ["READINESS_TEMP"] = protectedRoot.ToUpperInvariant() },
                3, "--help");
        }
        var alias = Path.Combine(caseRoot, "alias-into-helper");
        CreateDirectoryAlias(alias, protectedRoot);
        Launch(shell, skill, caseRoot, new(environment) { ["READINESS_TEMP"] = Path.Combine(alias, "rejected") },
            3, "--help");
        Assert(!Directory.Exists(Path.Combine(protectedRoot, "rejected")), "physical parent alias rejected before creation");
        Launch(shell, skill, caseRoot, new(environment) { ["READINESS_TEMP"] = alias }, 3, "--help");
        var artifactRoot = Path.Combine(caseRoot, "linked-artifacts");
        Directory.CreateDirectory(artifactRoot);
        CreateDirectoryAlias(Path.Combine(artifactRoot, "packages"), protectedRoot);
        var result = Launch(shell, skill, caseRoot, new(environment) { ["READINESS_TEMP"] = artifactRoot }, 3, "--help");
        Assert(result.Contains("cannot be symbolic links", StringComparison.Ordinal), "linked build-artifact rejection");
        AssertInventory(before, protectedRoot, "positive and negative launcher source immutability");
        var helperAlias = Path.Combine(caseRoot, "helper-alias");
        CreateDirectoryAlias(helperAlias, skill);
        Launch(shell, helperAlias, caseRoot, environment, 0, "--help");
        Launch(shell, skill, caseRoot, new(environment) { ["READINESS_TEMP"] = Path.Combine(protectedRoot, "..") },
            0, "--help");
        Launch(shell, skill, caseRoot, new(environment) { ["READINESS_TEMP"] = protectedRoot + "-sibling-scratch" },
            0, "--help");
        AssertInventory(before, protectedRoot, "normalized parent and sibling-prefix scratch acceptance");
        if (OperatingSystem.IsWindows())
            Console.WriteLine("UNRUN: native POSIX symlinks and cross-drive scratch; Windows junction and same-drive case controls exercised.");
    }

    private static void CreateDirectoryAlias(string link, string target)
    {
        if (OperatingSystem.IsWindows())
            Execute("pwsh", ["-NoProfile", "-Command",
                $"New-Item -ItemType Junction -Path '{link.Replace("'", "''")}' -Target '{target.Replace("'", "''")}' | Out-Null"],
                Path.GetDirectoryName(link)!, new(), 0);
        else
            Directory.CreateSymbolicLink(link, target);
    }

    private static string Launch(
        string shell, string skill, string cwd, Dictionary<string, string> environment, int exit, params string[] args)
    {
        var launcher = Path.Combine(skill, "scripts", "validator", shell == "bash" ? "run-validator.sh" : "run-validator.ps1");
        string[] prefix = shell == "bash" ? [launcher] : ["-NoLogo", "-NoProfile", "-File", launcher];
        return Execute(shell, [.. prefix, .. args], cwd, environment, exit);
    }

    private static string Execute(
        string executable, string[] args, string cwd, Dictionary<string, string> environment, int exit)
    {
        using var process = new Process
        {
            StartInfo = new()
            {
                FileName = executable, WorkingDirectory = cwd, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        foreach (var (key, value) in environment) process.StartInfo.Environment[key] = value;
        FoundationTests.ResolveWindowsBash(process.StartInfo);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Deployment check timed out.");
        }
        var result = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        Assert(process.ExitCode == exit, $"{executable} {string.Join(' ', args)} expected {exit}, got {process.ExitCode}:\n{result}");
        Commands.Add(new { executable, arguments = args, cwd, expectedExitCode = exit, actualExitCode = process.ExitCode });
        return result;
    }

    private static SortedDictionary<string, string> Inventory(string root, string? excludedDirectory = null)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, directory);
            if (excludedDirectory is null || (relative != excludedDirectory &&
                !relative.StartsWith(excludedDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                result.Add("D:" + relative, "");
        }
        foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path);
            if (excludedDirectory is null ||
                !relative.StartsWith(excludedDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                result.Add("F:" + relative, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }
        return result;
    }

    private static void AssertInventory(SortedDictionary<string, string> before, string root, string subject) =>
        Assert(before.SequenceEqual(Inventory(root)), $"{subject}: complete inventory and content hashes remain unchanged");

    private static string FileHash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Copy(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var directory in Directory.GetDirectories(source))
            Copy(directory, Path.Combine(dest, Path.GetFileName(directory)));
        foreach (var path in Directory.GetFiles(source))
            File.Copy(path, Path.Combine(dest, Path.GetFileName(path)));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
