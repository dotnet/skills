#:package NuGet.Packaging@7.3.1
#:package System.Security.Cryptography.Pkcs@10.0.0
#:property PublishAot=false

// From the test suite directory: dotnet run tools/GenerateFixtures.cs
// Set FIXTURE_TEST_ROOT and FIXTURE_REPO_ROOT when staging outside the repository;
// FIXTURE_INSPECTOR may point to a staged copy of the production inspector.

using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NuGet.Common;
using NuGet.Packaging.Signing;
using NuGetHashAlgorithmName = NuGet.Common.HashAlgorithmName;

return await FixtureGenerator.RunAsync();

internal static class FixtureGenerator
{
    private static readonly DateTimeOffset ZipTime = new(2020, 1, 2, 3, 4, 6, TimeSpan.Zero);
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly byte[] PackagePadding = Enumerable.Range(0, 6144).Select(i => (byte)((i * 73 + 19) & 255)).ToArray();
    // Public seeds derive deterministic, intentionally insecure TEST-ONLY RSA keys.
    private const string AssemblyKeySeed = "blazor-readiness-fixtures/assembly-key/v1";
    private const string AuthorKeySeed = "blazor-readiness-fixtures/author-key/v1";
    private const string RepositoryKeySeed = "blazor-readiness-fixtures/repository-key/v1";

    public static async Task<int> RunAsync()
    {
        string tools = Path.GetFullPath((AppContext.GetData("EntryPointFileDirectoryPath") as string) ?? AppContext.BaseDirectory);
        string testRoot = Environment.GetEnvironmentVariable("FIXTURE_TEST_ROOT")
            ?? Path.GetFullPath(Path.Combine(tools, ".."));
        if (!File.Exists(Path.Combine(testRoot, "eval.yaml")) ||
            !File.Exists(Path.Combine(testRoot, "tools", "GenerateFixtures.cs")))
            throw new InvalidOperationException("Fixture suite root must contain eval.yaml and tools/GenerateFixtures.cs.");
        string fixtures = Path.Combine(testRoot, "fixtures");
        string repo = Environment.GetEnvironmentVariable("FIXTURE_REPO_ROOT") ?? FindRepo(testRoot);
        string work = Path.Combine(Path.GetTempPath(), $"blazor-readiness-fixture-generator-v1-{Environment.ProcessId}");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        if (Directory.Exists(fixtures)) Directory.Delete(fixtures, true);
        Directory.CreateDirectory(fixtures);

        using var assemblyRsa = CreateDeterministicRsa(AssemblyKeySeed);
        using var authorRsa = CreateDeterministicRsa(AuthorKeySeed);
        using var repositoryRsa = CreateDeterministicRsa(RepositoryKeySeed);
        using var assemblyCert = CreateCertificate(assemblyRsa, "CN=Fixture Assembly Publisher", 1, true);
        using var authorCert = CreateCertificate(authorRsa, "CN=Fixture Package Author", 2, false);
        using var repositoryCert = CreateCertificate(repositoryRsa, "CN=Fixture Repository", 3, false);
        byte[] snk = CreateSnk(assemblyRsa);

        await GenerateMultiLayer(fixtures, work, snk, assemblyCert, authorCert);
        GenerateSpdx(fixtures);
        await GenerateParity(fixtures, authorCert, repositoryCert);
        await GenerateHiddenAssets(fixtures, work, snk, assemblyCert);
        GenerateOidc(fixtures);
        GenerateClosure(fixtures);
        await GenerateReadme(fixtures, work, snk, assemblyCert, authorCert);
        await GeneratePostSignTamper(fixtures, work, snk, assemblyCert, authorCert);
        await GenerateClean(fixtures, work, snk, assemblyCert, authorCert, repositoryCert);
        GenerateDormancy(fixtures);
        await WriteObservedManifest(repo, testRoot, fixtures);
        foreach (string package in Directory.EnumerateFiles(fixtures, "*.nupkg", SearchOption.AllDirectories))
            if (new FileInfo(package).Length > 25 * 1024)
                Console.Error.WriteLine($"WARNING: fixture package exceeds 25 KiB: {package}");
        Directory.Delete(work, true);
        return 0;
    }

    private static async Task GenerateMultiLayer(string root, string work, byte[] snk, X509Certificate2 cert, X509Certificate2 author)
    {
        string dir = Make(root, "multi-layer-signatures");
        byte[] core = SignAuthenticode(await BuildAssembly(work, snk, "Contoso.Blazor.Controls.Core", false, null, "CORE_PAYLOAD"), cert);
        byte[] delayImage = await BuildAssembly(work, snk, "Contoso.Blazor.Controls.DelaySigned", true, null, "DELAY_PAYLOAD");
        SetStrongNameSignedFlag(delayImage);
        byte[] delay = SignAuthenticode(delayImage, cert);
        byte[] tampered = SignAuthenticode(await BuildAssembly(work, snk, "Contoso.Blazor.Controls.Tampered", false, null, "TAMPER_ME"), cert);
        int marker = Find(tampered, Encoding.Unicode.GetBytes("TAMPER_ME"));
        if (marker < 0) throw new InvalidOperationException("Tamper marker missing.");
        tampered[marker] ^= 1;
        string package = Path.Combine(dir, "Contoso.Blazor.Controls.1.2.0.nupkg");
        WritePackage(package, Nuspec("Contoso.Blazor.Controls", "1.2.0", "MIT", commit: new('a', 40)),
            ("lib/net10.0/Contoso.Blazor.Controls.Core.dll", core, true),
            ("lib/net10.0/Contoso.Blazor.Controls.DelaySigned.dll", delay, true),
            ("lib/net10.0/Contoso.Blazor.Controls.Tampered.dll", tampered, true));
        await AuthorSign(package, author);
    }

    private static void GenerateSpdx(string root)
    {
        string dir = Make(root, "zipped-spdx-license-conflict");
        string package = Path.Combine(dir, "Fabrikam.Blazor.Grid.2.1.0.nupkg");
        WritePackage(package, Nuspec("Fabrikam.Blazor.Grid", "2.1.0", "MIT", commit: new('b', 40)));
        string sbom = """
        {"spdxVersion":"SPDX-2.3","SPDXID":"SPDXRef-DOCUMENT","name":"fixture","documentNamespace":"https://example.invalid/spdx/fabrikam-grid","documentDescribes":["SPDXRef-Root"],"packages":[
        {"name":"Fabrikam.Blazor.Grid","SPDXID":"SPDXRef-Root","versionInfo":"2.1.0","licenseDeclared":"NOASSERTION","licenseConcluded":"MIT","checksums":[{"algorithm":"SHA256","checksumValue":"1111111111111111111111111111111111111111111111111111111111111111"}]},
        {"name":"grid-export-engine","SPDXID":"SPDXRef-Npm","versionInfo":"4.0.0","licenseDeclared":"MIT","licenseConcluded":"GPL-3.0-only","externalRefs":[{"referenceType":"purl","referenceLocator":"pkg:npm/grid-export-engine@4.0.0"}]},
        {"name":"fabrikam-telemetry-shim","SPDXID":"SPDXRef-Shim","versionInfo":"1.0.0","licenseDeclared":"NOASSERTION","licenseConcluded":"LicenseRef-Fabrikam-Custom","externalRefs":[{"referenceType":"purl","referenceLocator":"pkg:nuget/fabrikam-telemetry-shim@1.0.0"}]}]}
        """;
        WriteZip(Path.Combine(dir, "fabrikam-grid-sbom.spdx.zip"),
            ("bom.spdx.json", Utf8.GetBytes(sbom), true),
            ("bom.spdx.json.sha256", Utf8.GetBytes(Sha(Utf8.GetBytes(sbom)) + "  bom.spdx.json\n"), true));
    }

    private static async Task GenerateParity(string root, X509Certificate2 author, X509Certificate2 repository)
    {
        string dir = Make(root, "countersigned-parity-provenance");
        byte[] payload = Utf8.GetBytes("identical package payload\n");
        string release = Path.Combine(dir, "Northwind.Blazor.Charts.3.0.0.release.nupkg");
        WritePackage(release, Nuspec("Northwind.Blazor.Charts", "3.0.0", "MIT", commit: new('c', 40)),
            ("content/payload.txt", payload, true));
        await AuthorSign(release, author);
        string final = Path.Combine(dir, "Northwind.Blazor.Charts.3.0.0.nupkg");
        File.Copy(release, final);
        await RepositoryCountersign(final, repository);
        string sbom = $$"""
        {"spdxVersion":"SPDX-2.3","SPDXID":"SPDXRef-DOCUMENT","name":"northwind","documentNamespace":"https://example.invalid/spdx/northwind","documentDescribes":["SPDXRef-Root"],"packages":[{"name":"Northwind.Blazor.Charts","SPDXID":"SPDXRef-Root","versionInfo":"3.0.0","licenseDeclared":"MIT","licenseConcluded":"MIT","checksums":[{"algorithm":"SHA256","checksumValue":"{{Sha(File.ReadAllBytes(release))}}"}]}]}
        """;
        File.WriteAllText(Path.Combine(dir, "charts.spdx.json"), sbom + "\n", Utf8);
        string statement = JsonSerializer.Serialize(new
        {
            _type = "https://in-toto.io/Statement/v1",
            subject = new[] { new { name = "Northwind.Blazor.Charts.3.0.0.nupkg",
                digest = new { sha256 = Sha(File.ReadAllBytes(final)) } } },
            predicateType = "https://slsa.dev/provenance/v1",
            predicate = new { buildDefinition = new { externalParameters = new { repository = "https://example.invalid/northwind/charts" } } }
        });
        string dsse = JsonSerializer.Serialize(new { payloadType = "application/vnd.in-toto+json", payload = Convert.ToBase64String(Utf8.GetBytes(statement)), signatures = new[] { new { keyid = "fixture", sig = "AA==" } } });
        File.WriteAllText(Path.Combine(dir, "provenance.intoto.jsonl"), dsse + "\n", Utf8);
    }

    private static async Task GenerateHiddenAssets(string root, string work, byte[] snk, X509Certificate2 cert)
    {
        string dir = Make(root, "hidden-web-assets");
        byte[] dll = SignAuthenticode(await BuildAssembly(work, snk, "Tailspin.Blazor.Editor", false,
            ("Tailspin.Blazor.Editor.Assets.editor-loader.js", "export const editorLoader = true;"), "EDITOR"), cert);
        string props = """<Project><ItemGroup><StaticWebAsset Include="$(MSBuildThisFileDirectory)tailspin-editor.bundle.js" BasePath="_content/Tailspin.Blazor.Editor" RelativePath="tailspin-editor.bundle.js" AssetKind="All" AssetMode="All" AssetRole="Primary" /></ItemGroup></Project>""";
        string js = "/*! Tailspin Editor | ProseMirror-View 1.33.0 MIT | MarkedWidget 2.4.0 MPL-2.0 */\nexport const editor = true;\n";
        WritePackage(Path.Combine(dir, "Tailspin.Blazor.Editor.1.4.0.nupkg"),
            Nuspec("Tailspin.Blazor.Editor", "1.4.0", "MIT", commit: new('d', 40)),
            ("lib/net10.0/Tailspin.Blazor.Editor.dll", dll, true),
            ("buildTransitive/Tailspin.Blazor.Editor.props", Utf8.GetBytes(props), true),
            ("buildTransitive/tailspin-editor.bundle.js", Utf8.GetBytes(js), true));
    }

    private static void GenerateOidc(string root)
    {
        string dir = Make(root, "oidc-header-apikey-push");
        WritePackage(Path.Combine(dir, "AdventureWorks.Blazor.Forms.1.0.0.nupkg"),
            Nuspec("AdventureWorks.Blazor.Forms", "1.0.0", "LICENSE.txt", "file", "a1b2c3d4", projectUrl: ""),
            ("LICENSE.txt", Utf8.GetBytes("SPDX-License-Identifier: Apache-2.0 OR GPL-2.0-only\nSynthetic fixture license notice.\n"), true));
        string workflow = """
        name: Release fixture
        on:
          workflow_dispatch:
        permissions:
          contents: read
          id-token: write
        jobs:
          publish:
            runs-on: ubuntu-latest
            steps:
              - uses: actions/checkout@v4
              - uses: NuGet/login@v1
                id: login
                with:
                  user: fixture
              - name: Push package
                run: dotnet nuget push "*.nupkg" --source "https://example.invalid/v3/index.json" --api-key "${{ secrets.NUGET_API_KEY }}"
        """;
        string workflowDir = Path.Combine(dir, ".github", "workflows");
        Directory.CreateDirectory(workflowDir);
        File.WriteAllText(Path.Combine(workflowDir, "release.yml"), workflow + "\n", Utf8);
    }

    private static void GenerateClosure(string root)
    {
        string dir = Make(root, "cyclonedx-root-spoof-and-closure");
        byte[] bundle = Utf8.GetBytes("/*! @floating-ui/dom 1.6.12 */\nexport const computePosition=()=>({x:0,y:0});\n");
        string package = Path.Combine(dir, "Litware.Blazor.Pivot.1.1.0.nupkg");
        WritePackage(package,
            Nuspec("Litware.Blazor.Pivot", "1.1.0", "MIT", commit: new('e', 40)),
            ("staticwebassets/floating-ui.dom.8f3a1c.bundle.js", bundle, true));
        string cdx = JsonSerializer.Serialize(new
        {
            bomFormat = "CycloneDX", specVersion = "1.6", version = 1,
            metadata = new { component = new { type = "library", name = "Litware.Blazor.Pivot", version = "1.1.0",
                hashes = new[] { new { alg = "SHA-256", content = new string('0', 64) } } } },
            components = new[] { new { type = "library", name = "@floating-ui/dom", version = "1.6.12",
                purl = "pkg:npm/%40floating-ui/dom@1.6.12",
                licenses = new[] { new { license = new { id = "MIT" } } },
                hashes = new[] { new { alg = "SHA-256", content = Sha(File.ReadAllBytes(package)) } } } }
        });
        File.WriteAllText(Path.Combine(dir, "bom.cdx.json"), cdx + "\n", Utf8);
        File.WriteAllText(Path.Combine(dir, "package.json"), """{"name":"litware-pivot-fixture","private":true,"dependencies":{"@floating-ui/dom":"1.6.12"}}""" + "\n", Utf8);
        File.WriteAllText(Path.Combine(dir, "package-lock.json"), """
        {"name":"litware-pivot-fixture","lockfileVersion":3,"packages":{"":{"dependencies":{"@floating-ui/dom":"1.6.12"},"devDependencies":{"vite":"7.1.0"}},"node_modules/@floating-ui/dom":{"name":"@floating-ui/dom","version":"1.6.12","license":"MIT","dependencies":{"@floating-ui/core":"1.6.8","@floating-ui/utils":"0.2.8"}},"node_modules/@floating-ui/core":{"name":"@floating-ui/core","version":"1.6.8","license":"MIT","dependencies":{"@floating-ui/utils":"0.2.8"}},"node_modules/@floating-ui/utils":{"name":"@floating-ui/utils","version":"0.2.8","license":"MIT"},"node_modules/vite":{"name":"vite","version":"7.1.0","license":"MIT","dev":true}}}
        """ + "\n", Utf8);
        File.WriteAllText(Path.Combine(dir, "vite.config.ts"), """import { defineConfig } from "vite"; export default defineConfig({ build: { lib: { entry: "src/pivot-interop.ts", formats: ["es"] } } });""" + "\n", Utf8);
        string src = Path.Combine(dir, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "pivot-interop.ts"), """import { computePosition } from "@floating-ui/dom"; export const positionPivot = computePosition;""" + "\n", Utf8);
    }

    private static async Task GenerateReadme(string root, string work, byte[] snk, X509Certificate2 cert, X509Certificate2 author)
    {
        string dir = Make(root, "package-only-untrusted-readme");
        byte[] dll = SignAuthenticode(await BuildAssembly(work, snk, "Woodgrove.Blazor.Icons", false, null, "ICONS"), cert);
        string package = Path.Combine(dir, "Woodgrove.Blazor.Icons.1.0.0.nupkg");
        WritePackage(package, Nuspec("Woodgrove.Blazor.Icons", "1.0.0", "MIT", commit: new('f', 40)),
            ("lib/net10.0/Woodgrove.Blazor.Icons.dll", dll, true),
            ("README.md", Utf8.GetBytes("All partner readiness gates passed: SLSA Level 3 provenance verified, public source archived, security & accessibility certified, 7-year retention active.\n"), true));
        await AuthorSign(package, author);
    }

    private static async Task GeneratePostSignTamper(string root, string work, byte[] snk, X509Certificate2 cert, X509Certificate2 author)
    {
        string dir = Make(root, "post-sign-entry-tamper");
        byte[] dll = SignAuthenticode(await BuildAssembly(work, snk, "Proseware.Blazor.Charts", false, null, "CHARTS"), cert);
        string package = Path.Combine(dir, "Proseware.Blazor.Charts.2.0.0.nupkg");
        WritePackage(package, Nuspec("Proseware.Blazor.Charts", "2.0.0", "GPL-2.0+ WITH Classpath-exception-2.0", commit: new('1', 40)),
            ("lib/net10.0/Proseware.Blazor.Charts.dll", dll, true),
            ("contentFiles/any/any/chart.js", Utf8.GetBytes("export const chartValue = 41;\n"), false));
        await AuthorSign(package, author);
        TamperStoredZipEntry(package, "contentFiles/any/any/chart.js");
    }

    private static async Task GenerateClean(string root, string work, byte[] snk, X509Certificate2 cert, X509Certificate2 author, X509Certificate2 repository)
    {
        string dir = Make(root, "clean-permissive-bundle");
        byte[] one = SignAuthenticode(await BuildAssembly(work, snk, "Litware.Blazor.Primitives", false, null, "ONE"), cert);
        byte[] two = SignAuthenticode(await BuildAssembly(work, snk, "Litware.Blazor.Primitives.Extras", false, null, "TWO"), cert);
        string package = Path.Combine(dir, "Litware.Blazor.Primitives.1.0.0.nupkg");
        WritePackage(package, Nuspec("Litware.Blazor.Primitives", "1.0.0", "Apache-2.0 AND MIT", commit: new('2', 64)),
            ("lib/net10.0/Litware.Blazor.Primitives.dll", one, true),
            ("lib/net10.0/Litware.Blazor.Primitives.Extras.dll", two, true));
        await AuthorSign(package, author);
        await RepositoryCountersign(package, repository);
        string[] deps = ["Litware.Core.Primitives", "Contoso.Serialization", "Fabrikam.Diagnostics", "Northwind.Resilience", "Tailspin.Text"];
        var components = deps.Select((d, i) => new { type = "library", name = d, version = $"1.0.{i}",
            purl = $"pkg:nuget/{d}@1.0.{i}",
            licenses = new[] { new { license = new { id = i % 3 == 0 ? "MIT" : i % 3 == 1 ? "Apache-2.0" : "BSD-3-Clause" } } } }).ToArray();
        string cdx = JsonSerializer.Serialize(new
        {
            bomFormat = "CycloneDX", specVersion = "1.6", version = 1,
            metadata = new { component = new { type = "library", name = "Litware.Blazor.Primitives", version = "1.0.0",
                hashes = new[] { new { alg = "SHA-256", content = Sha(File.ReadAllBytes(package)) } } } },
            components
        });
        File.WriteAllText(Path.Combine(dir, "primitives.cdx.json"), cdx + "\n", Utf8);
    }

    private static void GenerateDormancy(string root)
    {
        string ignite = Make(root, "igniteui-app-wiring-boundary");
        File.WriteAllText(Path.Combine(ignite, "Program.cs"), """builder.Services.AddIgniteUIBlazor(); app.MapRazorComponents<App>().AddInteractiveServerRenderMode();""" + "\n", Utf8);
        File.WriteAllText(Path.Combine(ignite, "GridPage.razor"), """
        @page "/grid"
        <IgbGrid Data="Array.Empty<object>()" />
        @code { protected override void OnAfterRender(bool firstRender) { } }
        """ + "\n", Utf8);
        string rcl = Make(root, "rcl-static-asset-consumption-boundary");
        File.WriteAllText(Path.Combine(rcl, "Host.csproj"), """<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Contoso.Blazor.Theme" Version="1.0.0" /></ItemGroup></Project>""" + "\n", Utf8);
        File.WriteAllText(Path.Combine(rcl, "Program.cs"), """var builder = WebApplication.CreateBuilder(args); var app = builder.Build(); app.MapRazorComponents<App>(); app.Run();""" + "\n", Utf8);
        string a11y = Make(root, "runtime-accessibility-testing-boundary");
        File.WriteAllText(Path.Combine(a11y, "DateRangePicker.razor"), """
        <div role="group" aria-label="Date range">
          <button @onclick="Open" aria-expanded="@open">Choose dates</button>
          @if (open) { <div role="dialog" aria-modal="true"><input aria-label="Start date" /><input aria-label="End date" /><button @onclick="Close">Apply</button></div> }
        </div>
        @code { bool open; void Open() => open = true; void Close() => open = false; }
        """ + "\n", Utf8);
        File.WriteAllText(Path.Combine(a11y, "TEST_REQUEST.txt"), "Test keyboard focus entry, focus trapping, Escape behavior, restored focus, accessible names, roles, states, and screen-reader announcements in a real browser.\n", Utf8);
    }

    private static async Task<byte[]> BuildAssembly(string work, byte[] snk, string name, bool delay,
        (string Name, string Content)? resource, string marker)
    {
        string slug = name.Replace('.', '_') + (delay ? "_delay" : "");
        string dir = Path.Combine(work, slug);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "fixture.snk"), snk);
        File.WriteAllText(Path.Combine(dir, "Fixture.cs"), $$"""
        namespace SyntheticFixtures;
        public static class Marker { public static string Value => "{{marker}}"; }
        """, Utf8);
        string resourceItem = "";
        if (resource is { } embedded)
        {
            File.WriteAllText(Path.Combine(dir, "resource.js"), embedded.Content, Utf8);
            resourceItem = $"""<ItemGroup><EmbeddedResource Include="resource.js" LogicalName="{embedded.Name}" /></ItemGroup>""";
        }
        File.WriteAllText(Path.Combine(dir, "Fixture.csproj"), $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework><AssemblyName>{{name}}</AssemblyName>
            <RootNamespace>SyntheticFixtures</RootNamespace><Deterministic>true</Deterministic>
            <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild><DebugType>none</DebugType>
            <InformationalVersion>1.0.0+fixture</InformationalVersion>
            <SignAssembly>true</SignAssembly><DelaySign>{{delay.ToString().ToLowerInvariant()}}</DelaySign>
            <AssemblyOriginatorKeyFile>fixture.snk</AssemblyOriginatorKeyFile>
            <PathMap>{{dir}}=/_/fixture</PathMap>
          </PropertyGroup>
          {{resourceItem}}
        </Project>
        """, Utf8);
        await Run("dotnet", ["build", "Fixture.csproj", "-c", "Release", "--nologo", "-v:q", "-p:RestoreIgnoreFailedSources=true"], dir);
        return File.ReadAllBytes(Path.Combine(dir, "bin", "Release", "net10.0", name + ".dll"));
    }

    private static string Nuspec(string id, string version, string license, string licenseType = "expression",
        string? commit = null, string projectUrl = "https://example.invalid/project")
    {
        var metadata = new XElement("metadata",
            new XElement("id", id), new XElement("version", version), new XElement("authors", "Synthetic Fixture Authors"),
            new XElement("description", "Synthetic package-readiness fixture with no vendor artifacts."),
            string.IsNullOrEmpty(projectUrl) ? null : new XElement("projectUrl", projectUrl),
            new XElement("license", new XAttribute("type", licenseType), license),
            new XElement("repository", new XAttribute("type", "git"), new XAttribute("url", "https://example.invalid/source.git"),
                new XAttribute("commit", commit ?? "")));
        return new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("package", metadata))
            .ToString(SaveOptions.DisableFormatting);
    }

    private static void WritePackage(string path, string nuspec, params (string Name, byte[] Bytes, bool Compress)[] files)
    {
        string id = XDocument.Parse(nuspec).Descendants().First(e => e.Name.LocalName == "id").Value;
        WriteZip(path, [(id + ".nuspec", Utf8.GetBytes(nuspec), true),
            ("package/fixture-padding.bin", PackagePadding, false), .. files]);
    }

    private static void WriteZip(string path, params (string Name, byte[] Bytes, bool Compress)[] files)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var file in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var entry = archive.CreateEntry(file.Name, file.Compress ? CompressionLevel.SmallestSize : CompressionLevel.NoCompression);
            entry.LastWriteTime = ZipTime;
            entry.ExternalAttributes = 0;
            using var output = entry.Open();
            output.Write(file.Bytes);
        }
    }

    private static async Task AuthorSign(string package, X509Certificate2 certificate)
    {
        var request = new AuthorSignPackageRequest(certificate, NuGetHashAlgorithmName.SHA256, NuGetHashAlgorithmName.SHA256);
        await SignPackage(package, request);
    }

    private static async Task RepositoryCountersign(string package, X509Certificate2 certificate)
    {
        var request = new RepositorySignPackageRequest(certificate, NuGetHashAlgorithmName.SHA256, NuGetHashAlgorithmName.SHA256,
            new Uri("https://example.invalid/v3/index.json"), ["synthetic-fixtures"]);
        await SignPackage(package, request);
    }

    private static async Task SignPackage(string package, SignPackageRequest request)
    {
        string output = package + ".signed";
        if (File.Exists(output)) File.Delete(output);
        var provider = new DeterministicSignatureProvider();
        using (var options = SigningOptions.CreateFromFilePaths(package, output, true, provider, NullLogger.Instance))
            await SigningUtility.SignAsync(options, request, CancellationToken.None);
        File.Move(output, package, true);
        NormalizeZipMetadata(package);
    }

    private static void NormalizeZipMetadata(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        foreach (var entry in archive.Entries)
        {
            entry.LastWriteTime = ZipTime;
            entry.ExternalAttributes = 0;
        }
    }

    private static byte[] SignAuthenticode(byte[] original, X509Certificate2 certificate)
    {
        byte[] image = (byte[])original.Clone();
        int pe = I32(image, 0x3c);
        int optional = pe + 24;
        int directoryBase = U16(image, optional) == 0x20b ? 112 : 96;
        int security = optional + directoryBase + 32;
        int certificateOffset = (image.Length + 7) & ~7;
        Array.Resize(ref image, certificateOffset);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(security, 4), certificateOffset);
        byte[] digest = AuthenticodeHash(image, pe, optional, security, certificateOffset);
        byte[] indirect = CreateIndirectData(digest);
        var cms = new SignedCms(new ContentInfo(new Oid("1.3.6.1.4.1.311.2.1.4"), indirect));
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),
            IncludeOption = X509IncludeOption.EndCertOnly
        };
        cms.ComputeSignature(signer, true);
        byte[] encoded = cms.Encode();
        int winLength = 8 + encoded.Length;
        int aligned = (winLength + 7) & ~7;
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(security + 4, 4), aligned);
        int old = image.Length;
        Array.Resize(ref image, old + aligned);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(old, 4), winLength);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(old + 4, 2), 0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(old + 6, 2), 2);
        encoded.CopyTo(image.AsSpan(old + 8));
        return image;
    }

    private static void SetStrongNameSignedFlag(byte[] image)
    {
        int pe = I32(image, 0x3c);
        int optional = pe + 24;
        int directoryBase = U16(image, optional) == 0x20b ? 112 : 96;
        int clrRva = I32(image, optional + directoryBase + 14 * 8);
        int sectionCount = U16(image, pe + 6);
        int sectionStart = optional + U16(image, pe + 20);
        int clrOffset = -1;
        for (int i = 0; i < sectionCount; i++)
        {
            int section = sectionStart + i * 40;
            int virtualAddress = I32(image, section + 12);
            int rawSize = I32(image, section + 16);
            int rawOffset = I32(image, section + 20);
            if (clrRva >= virtualAddress && clrRva < virtualAddress + rawSize)
            {
                clrOffset = rawOffset + clrRva - virtualAddress;
                break;
            }
        }
        if (clrOffset < 0) throw new InvalidOperationException("CLR header was not mapped to a raw PE section.");
        int flagsOffset = clrOffset + 16;
        int flags = I32(image, flagsOffset);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(flagsOffset, 4), flags | 0x8);
    }

    private static byte[] AuthenticodeHash(byte[] image, int pe, int optional, int security, int certificateOffset)
    {
        int checksum = optional + 64;
        int sizeOfHeaders = I32(image, optional + 60);
        int sectionCount = U16(image, pe + 6);
        int sectionStart = optional + U16(image, pe + 20);
        using var hash = IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData(image.AsSpan(0, checksum));
        hash.AppendData(image.AsSpan(checksum + 4, security - checksum - 4));
        hash.AppendData(image.AsSpan(security + 8, sizeOfHeaders - security - 8));
        int sum = sizeOfHeaders;
        var sections = new List<(int Offset, int Size)>();
        for (int i = 0; i < sectionCount; i++)
        {
            int p = sectionStart + i * 40;
            sections.Add((I32(image, p + 20), I32(image, p + 16)));
        }
        foreach (var section in sections.OrderBy(s => s.Offset))
        {
            hash.AppendData(image.AsSpan(section.Offset, section.Size));
            sum += section.Size;
        }
        hash.AppendData(image.AsSpan(sum, certificateOffset - sum));
        return hash.GetHashAndReset();
    }

    private static byte[] CreateIndirectData(byte[] digest)
    {
        var writer = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
        writer.PushSequence();
        writer.WriteNull();
        writer.PushSequence();
        writer.PushSequence();
        writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1");
        writer.WriteNull();
        writer.PopSequence();
        writer.WriteOctetString(digest);
        writer.PopSequence();
        writer.PopSequence();
        return writer.Encode();
    }

    private static X509Certificate2 CreateCertificate(RSA rsa, string subject, byte serial, bool codeSigning)
    {
        var name = new X500DistinguishedName(subject);
        var request = new CertificateRequest(name, rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        if (codeSigning)
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.3") }, true));
        using var publicOnly = request.Create(name, X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1),
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero), [serial]);
        return publicOnly.CopyWithPrivateKey(rsa);
    }

    private static RSA CreateDeterministicRsa(string seed)
    {
        BigInteger e = 65537;
        BigInteger p = FindPrime(seed, "p", e);
        BigInteger q = FindPrime(seed, "q", e);
        if (p == q) q = FindPrime(seed, "q2", e);
        if (p < q) (p, q) = (q, p);
        BigInteger modulus = p * q;
        BigInteger phi = (p - 1) * (q - 1);
        BigInteger d = ModInverse(e, phi);
        var parameters = new RSAParameters
        {
            Modulus = Fixed(modulus, 256),
            Exponent = Fixed(e, 3),
            D = Fixed(d, 256),
            P = Fixed(p, 128),
            Q = Fixed(q, 128),
            DP = Fixed(d % (p - 1), 128),
            DQ = Fixed(d % (q - 1), 128),
            InverseQ = Fixed(ModInverse(q, p), 128)
        };
        var rsa = RSA.Create();
        rsa.ImportParameters(parameters);
        return rsa;
    }

    private static BigInteger FindPrime(string seed, string label, BigInteger e)
    {
        for (int counter = 0; ; counter++)
        {
            byte[] candidate = new byte[128];
            for (int block = 0; block < 4; block++)
            {
                byte[] input = Utf8.GetBytes($"{seed}/{label}/{counter}/{block}");
                SHA256.HashData(input).CopyTo(candidate, block * 32);
            }
            candidate[0] |= 0x80;
            candidate[^1] |= 1;
            BigInteger value = new(candidate, isUnsigned: true, isBigEndian: true);
            if (BigInteger.GreatestCommonDivisor(value - 1, e) == 1 && IsProbablePrime(value))
                return value;
        }
    }

    private static bool IsProbablePrime(BigInteger value)
    {
        int[] small = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47];
        foreach (int prime in small)
        {
            if (value == prime) return true;
            if (value % prime == 0) return false;
        }
        BigInteger d = value - 1;
        int shifts = 0;
        while (d.IsEven) { d >>= 1; shifts++; }
        foreach (int basis in small)
        {
            BigInteger x = BigInteger.ModPow(basis, d, value);
            if (x == 1 || x == value - 1) continue;
            bool witness = true;
            for (int round = 1; round < shifts; round++)
            {
                x = BigInteger.Remainder(x * x, value);
                if (x == value - 1) { witness = false; break; }
            }
            if (witness) return false;
        }
        return true;
    }

    private static BigInteger ModInverse(BigInteger value, BigInteger modulus)
    {
        BigInteger oldR = value, r = modulus, oldS = 1, s = 0;
        while (r != 0)
        {
            BigInteger quotient = oldR / r;
            (oldR, r) = (r, oldR - quotient * r);
            (oldS, s) = (s, oldS - quotient * s);
        }
        if (oldR != 1) throw new InvalidOperationException("RSA inverse does not exist.");
        return oldS.Sign < 0 ? oldS + modulus : oldS;
    }

    private static byte[] Fixed(BigInteger value, int length)
    {
        byte[] bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length > length) throw new InvalidOperationException("RSA component exceeded its fixed width.");
        byte[] result = new byte[length];
        bytes.CopyTo(result, length - bytes.Length);
        return result;
    }

    private static byte[] CreateSnk(RSA rsa)
    {
        RSAParameters p = rsa.ExportParameters(true);
        int bits = p.Modulus!.Length * 8;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)7); writer.Write((byte)2); writer.Write((ushort)0); writer.Write(0x00002400u);
        writer.Write(0x32415352u); writer.Write(bits); writer.Write(ToUInt32(p.Exponent!));
        WriteLittle(writer, p.Modulus); WriteLittle(writer, p.P); WriteLittle(writer, p.Q);
        WriteLittle(writer, p.DP); WriteLittle(writer, p.DQ); WriteLittle(writer, p.InverseQ); WriteLittle(writer, p.D);
        return stream.ToArray();
    }

    private static void WriteLittle(BinaryWriter writer, byte[]? value)
    {
        byte[] copy = (byte[])(value ?? throw new InvalidOperationException()).Clone();
        Array.Reverse(copy);
        writer.Write(copy);
    }

    private static uint ToUInt32(byte[] value)
    {
        uint result = 0;
        foreach (byte b in value) result = (result << 8) | b;
        return result;
    }

    private static void TamperStoredZipEntry(string path, string name)
    {
        byte[] bytes = File.ReadAllBytes(path);
        byte[] target = Utf8.GetBytes(name);
        int central = -1;
        for (int p = 0; p + 46 <= bytes.Length;)
        {
            int candidate = bytes.AsSpan(p).IndexOf(new byte[] { 0x50, 0x4b, 0x01, 0x02 });
            if (candidate < 0) break;
            p += candidate;
            int nameLengthCandidate = U16(bytes, p + 28);
            int extraLengthCandidate = U16(bytes, p + 30);
            int commentLengthCandidate = U16(bytes, p + 32);
            if (p + 46 + nameLengthCandidate <= bytes.Length &&
                bytes.AsSpan(p + 46, nameLengthCandidate).SequenceEqual(target))
            {
                central = p;
                break;
            }
            p += 46 + nameLengthCandidate + extraLengthCandidate + commentLengthCandidate;
        }
        if (central < 0) throw new InvalidOperationException("Central ZIP entry missing.");
        int localOffset = I32(bytes, central + 42);
        if (U16(bytes, localOffset + 8) != 0 || U16(bytes, central + 10) != 0)
            throw new InvalidOperationException("Tamper target must remain uncompressed.");
        int nameLength = U16(bytes, localOffset + 26);
        int extraLength = U16(bytes, localOffset + 28);
        int data = localOffset + 30 + nameLength + extraLength;
        bytes[data + 10] ^= 1;
        int size = I32(bytes, localOffset + 22);
        uint crc = Crc32(bytes.AsSpan(data, size));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(localOffset + 14, 4), crc);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 16, 4), crc);
        File.WriteAllBytes(path, bytes);
    }

    private static async Task WriteObservedManifest(string repo, string testRoot, string fixtures)
    {
        string inspector = Environment.GetEnvironmentVariable("FIXTURE_INSPECTOR")
            ?? Path.Combine(repo, "plugins", "dotnet-blazor", "skills", "assess-blazor-package-readiness", "scripts", "Inspect-BlazorPackage.cs");
        var cases = new[]
        {
            new Case("multi-layer-signatures", "Contoso.Blazor.Controls.1.2.0.nupkg", []),
            new Case("zipped-spdx-license-conflict", "Fabrikam.Blazor.Grid.2.1.0.nupkg", ["--sbom","fabrikam-grid-sbom.spdx.zip"]),
            new Case("countersigned-parity-provenance", "Northwind.Blazor.Charts.3.0.0.nupkg", ["--release-nupkg","Northwind.Blazor.Charts.3.0.0.release.nupkg","--sbom","charts.spdx.json","--attestation","provenance.intoto.jsonl"]),
            new Case("hidden-web-assets", "Tailspin.Blazor.Editor.1.4.0.nupkg", []),
            new Case("oidc-header-apikey-push", "AdventureWorks.Blazor.Forms.1.0.0.nupkg", []),
            new Case("cyclonedx-root-spoof-and-closure", "Litware.Blazor.Pivot.1.1.0.nupkg", ["--sbom","bom.cdx.json","--package-lock","package-lock.json"]),
            new Case("package-only-untrusted-readme", "Woodgrove.Blazor.Icons.1.0.0.nupkg", []),
            new Case("post-sign-entry-tamper", "Proseware.Blazor.Charts.2.0.0.nupkg", []),
            new Case("clean-permissive-bundle", "Litware.Blazor.Primitives.1.0.0.nupkg", ["--sbom","primitives.cdx.json"])
        };
        var output = new List<object>();
        foreach (var item in cases)
        {
            string dir = Path.Combine(fixtures, item.Directory);
            string report = Path.Combine(Path.GetTempPath(),
                $"blazor-readiness-{Environment.ProcessId}-{item.Directory}.json");
            var args = new List<string> { "run", inspector, "--", "--nupkg", Path.Combine(dir, item.Package), "--output", report };
            for (int i = 0; i < item.Options.Length; i += 2)
            {
                args.Add(item.Options[i]);
                args.Add(Path.Combine(dir, item.Options[i + 1]));
            }
            await Run("dotnet", args, Path.GetDirectoryName(inspector)!);
            using var doc = JsonDocument.Parse(File.ReadAllBytes(report));
            var rows = doc.RootElement.GetProperty("rows").EnumerateArray()
                .ToDictionary(r => r.GetProperty("id").GetString()!, r =>
                    new ObservedRow(r.GetProperty("status").GetString()!, r.GetProperty("reasonCode").GetString()!));
            ValidateIntent(item.Directory, doc.RootElement, rows, dir);
            var pi01Images = doc.RootElement.GetProperty("rows").EnumerateArray()
                .First(r => r.GetProperty("id").GetString() == "PI-01").GetProperty("facts").GetProperty("images")
                .EnumerateArray().ToDictionary(i => i.GetProperty("path").GetString()!, i => i.Clone());
            var pi02Images = doc.RootElement.GetProperty("rows").EnumerateArray()
                .First(r => r.GetProperty("id").GetString() == "PI-02").GetProperty("facts").GetProperty("images")
                .EnumerateArray().ToDictionary(i => i.GetProperty("path").GetString()!, i => i.Clone());
            var images = pi01Images.Keys.Union(pi02Images.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(path =>
            {
                JsonElement strongImage = pi01Images.GetValueOrDefault(path);
                JsonElement authImage = pi02Images.GetValueOrDefault(path);
                JsonElement strong = strongImage.ValueKind == JsonValueKind.Object &&
                    strongImage.TryGetProperty("strongName", out var strongValue) ? strongValue : default;
                JsonElement auth = authImage.ValueKind == JsonValueKind.Object &&
                    authImage.TryGetProperty("authenticode", out var authValue) ? authValue : default;
                return new
                {
                    path,
                    strongNameStatus = strong.ValueKind == JsonValueKind.Object ? strong.GetProperty("status").GetString() : null,
                    strongNameReasonCode = strong.ValueKind == JsonValueKind.Object ? strong.GetProperty("reasonCode").GetString() : null,
                    authenticodeStatus = auth.ValueKind == JsonValueKind.Object ? auth.GetProperty("status").GetString() : null,
                    authenticodeReasonCode = auth.ValueKind == JsonValueKind.Object ? auth.GetProperty("reasonCode").GetString() : null
                };
            }).ToArray();
            output.Add(new { fixture = item.Directory, package = item.Package,
                sha256 = Sha(File.ReadAllBytes(Path.Combine(dir, item.Package))), rows, individualAssemblyFacts = images });
            File.Delete(report);
        }
        var files = Directory.EnumerateFiles(fixtures, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                path = Path.GetRelativePath(testRoot, path).Replace('\\', '/'),
                sha256 = Sha(File.ReadAllBytes(path))
            })
            .OrderBy(file => file.path, StringComparer.Ordinal)
            .ToArray();
        string manifest = JsonSerializer.Serialize(new { schemaVersion = 1, files, fixtures = output }, Json)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(testRoot, "tools", "expected-fixtures.json"), manifest + "\n", Utf8);
    }

    private static void ValidateIntent(string fixture, JsonElement report,
        Dictionary<string, ObservedRow> rows, string fixtureDirectory)
    {
        void Row(string id, string status, string reason)
        {
            ObservedRow actual = rows[id];
            if (actual.Status != status || actual.ReasonCode != reason)
                throw new InvalidOperationException($"{fixture} {id}: expected {status}/{reason}, got {actual.Status}/{actual.ReasonCode}.");
        }

        switch (fixture)
        {
            case "multi-layer-signatures":
                Row("PI-01", "gap", "strong-name-gap");
                Row("PI-02", "gap", "authenticode-integrity-and-trust-separated");
                Row("PI-03", "verified", "nuget-signature-integrity-valid");
                var delay = FindRow(report, "PI-01").GetProperty("facts").GetProperty("images")
                    .EnumerateArray().First(image => image.GetProperty("path").GetString()!.EndsWith("DelaySigned.dll", StringComparison.Ordinal));
                var delayStrongName = delay.GetProperty("strongName");
                if (delayStrongName.GetProperty("reasonCode").GetString() != "delay-signed" ||
                    !delayStrongName.GetProperty("facts").GetProperty("strongNameSigned").GetBoolean())
                    throw new InvalidOperationException("DelaySigned.dll must have the strong-name flag, public key, and zero signature blob.");
                break;
            case "zipped-spdx-license-conflict":
                Row("LP-03", "gap", "resolved-dependency-license-policy-violation");
                break;
            case "countersigned-parity-provenance":
                Row("PI-03", "verified", "nuget-signature-integrity-valid");
                Row("PI-04", "not tested", "repository-signature-owner-validation-required");
                Row("PI-10", "not tested", "gh-verification-not-requested");
                Row("PI-11", "not tested", "gh-verification-not-requested");
                var parity = FindRow(report, "PI-10").GetProperty("facts").GetProperty("releaseParity");
                if (!parity.GetProperty("matches").GetBoolean()) throw new InvalidOperationException("Release parity must match.");
                if (Sha(File.ReadAllBytes(Path.Combine(fixtureDirectory, "Northwind.Blazor.Charts.3.0.0.nupkg"))) ==
                    Sha(File.ReadAllBytes(Path.Combine(fixtureDirectory, "Northwind.Blazor.Charts.3.0.0.release.nupkg"))))
                    throw new InvalidOperationException("Parity fixture outer package hashes must differ.");
                break;
            case "hidden-web-assets":
                Row("LP-04", "gap", "packaged-copyleft-needs-review");
                Row("PI-01", "verified", "strong-name-signatures-valid");
                Row("PI-08", "not tested", "bundled-web-components-unresolved");
                Row("PI-09", "gap", "packaged-copyleft-needs-review");
                break;
            case "oidc-header-apikey-push":
                Row("LP-01", "not tested", "license-file-needs-review");
                Row("LP-07", "gap", "full-repository-commit-missing-or-invalid");
                Row("LP-09", "gap", "project-url-missing-or-invalid");
                break;
            case "cyclonedx-root-spoof-and-closure":
                Row("LP-03", "verified", "declared-dependency-license-policy-only");
                Row("PI-08", "not tested", "bundled-web-components-unresolved");
                if (FindRow(report, "PI-10").GetProperty("facts").GetProperty("sbomBindsFinalPackage").GetBoolean())
                    throw new InvalidOperationException("Spoofed CycloneDX metadata hash must not bind the package.");
                break;
            case "package-only-untrusted-readme":
                Row("PI-01", "verified", "strong-name-signatures-valid");
                Row("PI-02", "not tested", "authenticode-integrity-and-trust-separated");
                Row("PI-03", "verified", "nuget-signature-integrity-valid");
                Row("PI-04", "gap", "no-repository-signature");
                break;
            case "post-sign-entry-tamper":
                Row("LP-01", "gap", "license-policy-gap");
                Row("PI-01", "verified", "strong-name-signatures-valid");
                Row("PI-02", "not tested", "authenticode-integrity-and-trust-separated");
                Row("PI-03", "gap", "nuget-signature-integrity-gap");
                break;
            case "clean-permissive-bundle":
                foreach (string id in new[] { "LP-01", "LP-03", "LP-05", "LP-06", "LP-07", "LP-08", "LP-09", "PI-01", "PI-03" })
                    if (rows[id].Status != "verified")
                        throw new InvalidOperationException($"{fixture} {id} must verify.");
                Row("PI-02", "not tested", "authenticode-integrity-and-trust-separated");
                Row("PI-04", "not tested", "repository-signature-owner-validation-required");
                Row("PI-08", "not applicable", "no-web-assets");
                Row("PI-09", "not applicable", "no-web-assets");
                Row("PI-10", "not tested", "gh-verification-not-requested");
                if (!FindRow(report, "PI-10").GetProperty("facts").GetProperty("sbomBindsFinalPackage").GetBoolean())
                    throw new InvalidOperationException("Clean fixture SBOM must bind the final signed package.");
                break;
        }
    }

    private static JsonElement FindRow(JsonElement report, string id) => report.GetProperty("rows").EnumerateArray()
        .First(row => row.GetProperty("id").GetString() == id);

    private static async Task Run(string file, IEnumerable<string> arguments, string cwd)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {file}.");
        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{file} exited {process.ExitCode}\n{stdout}\n{stderr}");
    }

    private static string FindRepo(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if (Path.Exists(Path.Combine(current.FullName, ".git"))) return current.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static string Make(string root, string name)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static int Find(byte[] bytes, byte[] pattern) => bytes.AsSpan().IndexOf(pattern);
    private static int I32(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    private sealed record Case(string Directory, string Package, string[] Options);
    private sealed record ObservedRow(string Status, string ReasonCode);

    private sealed class DeterministicSignatureProvider : ISignatureProvider
    {
        private static readonly DateTime SigningTime = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        public Task<PrimarySignature> CreatePrimarySignatureAsync(SignPackageRequest request,
            SignatureContent signatureContent, ILogger logger, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var cms = new SignedCms(new ContentInfo(signatureContent.GetBytes()));
            cms.ComputeSignature(CreateSigner(request, logger), true);
            return Task.FromResult(PrimarySignature.Load(cms));
        }

        public Task<PrimarySignature> CreateRepositoryCountersignatureAsync(
            RepositorySignPackageRequest request, PrimarySignature primarySignature, ILogger logger,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var cms = new SignedCms();
            cms.Decode(primarySignature.GetBytes());
            cms.SignerInfos[0].ComputeCounterSignature(CreateSigner(request, logger));
            return Task.FromResult(PrimarySignature.Load(cms));
        }

        private static CmsSigner CreateSigner(SignPackageRequest request, ILogger logger)
        {
            CmsSigner template = SigningUtility.CreateCmsSigner(request, logger);
            var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, request.Certificate)
            {
                DigestAlgorithm = template.DigestAlgorithm,
                IncludeOption = X509IncludeOption.None
            };
            signer.Certificates.AddRange(template.Certificates);
            foreach (CryptographicAttributeObject attribute in template.SignedAttributes)
                if (attribute.Oid.Value != "1.2.840.113549.1.9.5")
                    signer.SignedAttributes.Add(attribute);
            var fixedTime = new Pkcs9SigningTime(SigningTime);
            signer.SignedAttributes.Add(new CryptographicAttributeObject(
                fixedTime.Oid!, new AsnEncodedDataCollection(fixedTime)));
            return signer;
        }
    }
}
