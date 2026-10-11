#:package Microsoft.CodeAnalysis.CSharp@4.14.0
#:property PublishAot=false

// Find-UntestedSources.cs
//
// Static, parse-only analysis (no compilation, no metadata, no test execution)
// that produces a JSON report of which C# source files have at least one C#
// test file referencing one of their declared types, and which do not.
//
// Pipeline:
//   1. Walk the repo for *.cs (skip bin/obj/.g.cs/.Designer.cs).
//   2. Classify each file as test vs source by walking up to the nearest .csproj
//      and checking for Microsoft.NET.Test.Sdk / MSTest.Sdk / Microsoft.Testing.Platform
//      references, or a project name ending in .Tests / .Test / .UnitTests /
//      .IntegrationTests / .E2E / .EndToEnd.
//   3. Build a source index in parallel: per source file, parse with
//      CSharpSyntaxTree.ParseText (NO Compilation, NO MetadataReferences),
//      record every class/record/struct/interface/enum/delegate declaration
//      as (ShortName, Namespace, File).
//   4. Scan each test file in parallel: parse, collect `using` directives and
//      enclosing namespace, walk identifier tokens, look them up in the index,
//      disambiguate by namespace match against the test file's usings.
//   5. Invert into source -> [tests] map, identify unreferenced source files,
//      and emit JSON sorted by declaration count descending (highest API
//      surface first).
//
// Output: JSON to stdout. Diagnostics to stderr.
//
// Usage:
//   dotnet run Find-UntestedSources.cs -- <repo-root> [--top N]
//
// Output schema:
//   {
//     "repo": "<abs path>",
//     "counts": { "source_files", "test_files", "untested_files", "paired_files" },
//     "untested": [ { "source", "decl_count", "suggested_test_path" }, ... ],
//     "source_to_tests": { "<source>": ["<test1>", "<test2>", ...], ... }
//   }

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

bool jsonV1 = args.Contains("json-v1", StringComparer.Ordinal);
int topN = int.MaxValue;
bool includeTested = false;
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: dotnet run Find-UntestedSources.cs -- <repo-root> [--top N] [--include-tested] [--output-format legacy|json-v1]");
    return 1;
}

for (int i = 1; i < args.Length; i++)
{
    if (args[i] == "--include-tested") { includeTested = true; continue; }
    if (args[i] == "--output-format" && i + 1 < args.Length && args[i + 1] is "legacy" or "json-v1")
    {
        jsonV1 = args[++i] == "json-v1";
        continue;
    }
    if ((args[i] == "--top" || args[i] == "--limit-untested") && i + 1 < args.Length &&
        int.TryParse(args[i + 1], out int n) && n >= 0)
    {
        topN = n == 0 ? int.MaxValue : n;
        i++;
        continue;
    }
    return InputError($"Invalid argument: {args[i]}", jsonV1);
}

string root;
try { root = Path.GetFullPath(args[0]); }
catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
{
    return InputError(ex.Message, jsonV1);
}
if (!Directory.Exists(root)) { return InputError($"Not a directory: {root}", jsonV1); }

var swTotal = System.Diagnostics.Stopwatch.StartNew();

// 1. Discover .cs files
var allCs = new List<string>();
foreach (var f in EnumerateSourceFiles(root, "*.cs"))
{
    allCs.Add(f);
}
Log($"Discovered {allCs.Count} .cs files");

// 2. Classify by walking up to nearest .csproj
var projectForDir = new ConcurrentDictionary<string, ProjectLookup>(StringComparer.OrdinalIgnoreCase);
var isTestProject = new ConcurrentDictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);

ProjectLookup ProjectFor(string filePath)
{
    var dir = Path.GetDirectoryName(filePath)!;
    return projectForDir.GetOrAdd(dir, d =>
    {
        var cur = d;
        while (cur is not null && cur.Length >= root.Length)
        {
            string[] csprojs;
            try { csprojs = Directory.GetFiles(cur, "*.csproj"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Report("PROJECT_DISCOVERY_FAILED", cur, ex.Message);
                return new ProjectLookup(null, false);
            }
            if (csprojs.Length > 0)
            {
                if (csprojs.Length > 1)
                {
                    Report("PROJECT_AMBIGUOUS", cur, "Multiple nearest project files; classification is unresolved.");
                    return new ProjectLookup(null, false);
                }
                return new ProjectLookup(csprojs[0], true);
            }
            cur = Path.GetDirectoryName(cur);
        }
        return new ProjectLookup(null, true);
    });
}

bool? IsTest(string filePath)
{
    var lookup = ProjectFor(filePath);
    if (!lookup.IsResolved)
    {
        return null;
    }
    if (lookup.Path is null)
    {
        return false;
    }
    return isTestProject.GetOrAdd(lookup.Path, ClassifyProjectAsTest);
}

var sourceFiles = new List<string>();
var testFiles = new List<string>();
foreach (var f in allCs)
{
    var classification = IsTest(f);
    if (classification is null)
    {
        Report("CLASSIFICATION_UNKNOWN", f, "Nearest project discovery or classification could not be completed.");
        continue;
    }
    if (classification.Value)
    {
        testFiles.Add(f);
    }
    else
    {
        sourceFiles.Add(f);
    }
}
Log($"Classified: {sourceFiles.Count} source, {testFiles.Count} test");

// 3. Build source index (parse-only, no Compilation)
var declsByShort = new ConcurrentDictionary<string, List<Decl>>(StringComparer.Ordinal);
var declsByFile = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

var swIndex = System.Diagnostics.Stopwatch.StartNew();
Parallel.ForEach(sourceFiles, file =>
{
    var rootNode = ReadSyntax(file);
    if (rootNode is null) { return; }
    int count = 0;
    foreach (var node in rootNode.DescendantNodes())
    {
        string? name = node switch
        {
            BaseTypeDeclarationSyntax t => t.Identifier.ValueText,
            DelegateDeclarationSyntax d => d.Identifier.ValueText,
            _ => null,
        };
        if (name is null || name.Length == 0)
        {
            continue;
        }
        var ns = GetNamespaceFor(node);
        var decl = new Decl(name, ns, file);
        var list = declsByShort.GetOrAdd(name, _ => new List<Decl>());
        lock (list)
        {
            list.Add(decl);
        }
        count++;
    }
    declsByFile[file] = count;
});
swIndex.Stop();
Log($"Indexed {sourceFiles.Count} source files in {swIndex.ElapsedMilliseconds} ms ({declsByShort.Count} distinct short names)");

// 4. Scan test files
var sourceToTests = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.Ordinal);
foreach (var s in sourceFiles)
{
    sourceToTests[s] = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
}

var swScan = System.Diagnostics.Stopwatch.StartNew();
Parallel.ForEach(testFiles, file =>
{
    var rootNode = ReadSyntax(file);
    if (rootNode is null) { return; }

    var usings = new HashSet<string>(StringComparer.Ordinal);
    foreach (var u in rootNode.DescendantNodes().OfType<UsingDirectiveSyntax>())
    {
        var nm = u.Name?.ToString();
        if (!string.IsNullOrEmpty(nm))
        {
            usings.Add(nm);
        }
    }
    var localNs = rootNode.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()
        .FirstOrDefault()?.Name.ToString() ?? "";

    var ids = new HashSet<string>(StringComparer.Ordinal);
    foreach (var tok in rootNode.DescendantTokens())
    {
        if (tok.IsKind(SyntaxKind.IdentifierToken))
        {
            ids.Add(tok.ValueText);
        }
    }

    var refs = new HashSet<string>(StringComparer.Ordinal);
    foreach (var id in ids)
    {
        if (!declsByShort.TryGetValue(id, out var decls))
        {
            continue;
        }
        List<Decl>? preferred = null;
        foreach (var d in decls)
        {
            // Declarations in the global namespace are visible without a
            // using directive, so accept them unconditionally. (Skipping
            // them previously meant types declared without a namespace were
            // never attributed to any test file.)
            if (d.Namespace.Length == 0
                || usings.Contains(d.Namespace)
                || d.Namespace == localNs
                || (localNs.Length > 0 && localNs.StartsWith(d.Namespace + ".", StringComparison.Ordinal))
                || IsUsingPrefix(usings, d.Namespace))
            {
                (preferred ??= new List<Decl>()).Add(d);
            }
        }
        // Strict: skip identifiers whose declarations don't match any namespace
        // visible to this test file. Avoids the noise where a common identifier
        // (e.g., a type name shared across unrelated projects) gets attributed
        // to every file declaring something with the same short name.
        if (preferred is null)
        {
            continue;
        }
        foreach (var d in preferred)
        {
            refs.Add(d.File);
        }
    }

    foreach (var src in refs)
    {
        sourceToTests[src][file] = 0;
    }
});
swScan.Stop();
Log($"Scanned {testFiles.Count} test files in {swScan.ElapsedMilliseconds} ms");

// 5. Build suggested test paths for untested files
var untested = sourceToTests
    .Where(kv => kv.Value.IsEmpty && !AnalysisState.FailedFiles.ContainsKey(kv.Key))
    .Select(kv => kv.Key)
    .OrderBy(s => s, StringComparer.Ordinal)
    .ToList();

// Cache: source proj -> candidate test project paths
var testProjectsByProductionProject = BuildProductionToTestProjectMap(root);

string? SuggestTestPath(string srcFile)
{
    var lookup = ProjectFor(srcFile);
    var proj = lookup.Path;
    if (!lookup.IsResolved || proj is null)
    {
        return null;
    }
    if (!testProjectsByProductionProject.TryGetValue(proj, out var testProj))
    {
        return null;
    }
    var srcProjDir = Path.GetDirectoryName(proj)!;
    var testProjDir = Path.GetDirectoryName(testProj)!;
    var rel = Path.GetRelativePath(srcProjDir, srcFile);
    var relDir = Path.GetDirectoryName(rel) ?? "";
    var fname = Path.GetFileNameWithoutExtension(rel);
    return Path.Combine(testProjDir, relDir, fname + "Tests.cs");
}

// 6. Emit JSON
var untestedReport = untested.Select(s => new
{
    source = ToRel(root, s),
    decl_count = declsByFile.TryGetValue(s, out var c) ? c : 0,
    suggested_test_path = SuggestTestPath(s) is string p ? ToRel(root, p) : null,
})
.OrderByDescending(x => x.decl_count)
.ThenBy(x => x.source, StringComparer.Ordinal)
.Take(topN)
.ToList();

var pairedReport = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
foreach (var kv in sourceToTests)
{
    if (kv.Value.IsEmpty)
    {
        continue;
    }
    pairedReport[ToRel(root, kv.Key)] = kv.Value.Keys
        .Select(t => ToRel(root, t))
        .OrderBy(s => s, StringComparer.Ordinal)
        .ToList();
}

swTotal.Stop();
var result = new
{
    repo = root,
    elapsed_ms = swTotal.ElapsedMilliseconds,
    counts = new
    {
        source_files = sourceFiles.Count,
        test_files = testFiles.Count,
        untested_files = untested.Count,
        paired_files = sourceFiles.Count - untested.Count,
    },
    untested = untestedReport,
    source_to_tests = pairedReport,
};

var opts = new JsonSerializerOptions
{
    WriteIndented = true,
    TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
};
var diagnostics = AnalysisState.Diagnostics
    .OrderBy(d => d.path, StringComparer.Ordinal).ThenBy(d => d.code, StringComparer.Ordinal)
    .ThenBy(d => d.message, StringComparer.Ordinal).Distinct().ToArray();
foreach (var diagnostic in diagnostics) { Console.Error.WriteLine($"{diagnostic.code}: {diagnostic.path}: {diagnostic.message}"); }
if (jsonV1)
{
    var declarations = declsByShort.Values.SelectMany(values => values)
        .GroupBy(d => ToRel(root, d.File), StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Select(d => d.ShortName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
    var pairedTests = pairedReport.Values.SelectMany(tests => tests).ToHashSet(StringComparer.Ordinal);
    var orphanTests = testFiles.Where(file => !AnalysisState.FailedFiles.ContainsKey(file) && !pairedTests.Contains(ToRel(root, file)))
        .Order(StringComparer.Ordinal).Select(file => new { path = ToRel(root, file), language = "csharp" }).ToArray();
    var unassessed = allCs.Where(AnalysisState.FailedFiles.ContainsKey).Order(StringComparer.Ordinal)
        .Select(file => new { path = ToRel(root, file), language = "csharp" }).ToArray();
    var data = new Dictionary<string, object?>
    {
        ["repo_root"] = root,
        ["summary"] = new
        {
            files = allCs.Count, source_files = sourceFiles.Count, test_files = testFiles.Count,
            tested_source_files = pairedReport.Count, untested_source_files = untested.Count,
            orphan_test_files = orphanTests.Length, unassessed_files = unassessed.Length,
            languages = allCs.Count > 0 ? new[] { "csharp" } : Array.Empty<string>(),
        },
        ["untested_sources"] = untestedReport.Select(entry => new
        {
            path = entry.source, language = "csharp", declaration_count = entry.decl_count,
            declarations = declarations.GetValueOrDefault(entry.source, Array.Empty<string>()),
            suggested_test_path = entry.suggested_test_path,
        }).ToArray(),
        ["orphan_tests"] = orphanTests,
        ["unassessed_files"] = unassessed,
        ["test_regions"] = Array.Empty<object>(),
    };
    if (includeTested)
    {
        data["tested_sources"] = pairedReport.Select(entry => new
        {
            path = entry.Key, language = "csharp",
            declaration_count = declsByFile.GetValueOrDefault(Path.GetFullPath(Path.Combine(root, entry.Key))),
            declarations = declarations.GetValueOrDefault(entry.Key, Array.Empty<string>()),
            covering_tests = entry.Value,
        }).ToArray();
    }
    var envelope = new
    {
        schema_version = "1", tool = "Find-UntestedSources", tool_version = "1.0",
        status = diagnostics.Length > 0 ? "partial" : "complete",
        scope = new
        {
            root, source_revision = (string?)null, evidence = "static-reference-pairing",
            engine = "Roslyn", engine_version = typeof(CSharpSyntaxTree).Assembly.GetName().Version?.ToString(),
            declarations = "C# type and delegate syntax declarations; not behavioral coverage",
            languages = new[] { "csharp" }, exclude_symlinks = true,
            untested_limit = topN == int.MaxValue ? 0 : topN, truncated = untestedReport.Count < untested.Count,
        },
        inputs = AnalysisState.Inputs.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new { path = ToRel(root, entry.Key), sha256 = entry.Value }).ToArray(),
        diagnostics, data,
    };
    Console.WriteLine(JsonSerializer.Serialize(envelope, opts));
}
else if (diagnostics.Length == 0)
{
    Console.WriteLine(JsonSerializer.Serialize(result, opts));
}
Log($"Total time: {swTotal.ElapsedMilliseconds} ms");
return diagnostics.Length > 0 ? 3 : 0;

// ============================================================================
// Helpers
// ============================================================================

static IEnumerable<string> EnumerateSourceFiles(string root, string pattern)
{
    var stack = new Stack<string>();
    stack.Push(root);
    while (stack.Count > 0)
    {
        var dir = stack.Pop();
        IEnumerable<string> subdirs;
        try { subdirs = Directory.GetDirectories(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report("WALK_FAILED", dir, ex.Message);
            continue;
        }
        foreach (var sub in subdirs)
        {
            if (IsLink(sub)) { continue; }
            var name = Path.GetFileName(sub);
            if (name.Length == 0)
            {
                continue;
            }
            if (string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, ".vs", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "packages", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }
            stack.Push(sub);
        }
        IEnumerable<string> files;
        try { files = Directory.GetFiles(dir, pattern); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report("WALK_FAILED", dir, ex.Message);
            continue;
        }
        foreach (var f in files)
        {
            if (IsSkippedFile(f) || IsLink(f))
            {
                continue;
            }
            yield return f;
        }
    }
}

static bool IsSkippedFile(string path)
{
    string[] skippedSuffixes =
    {
        ".g.cs",
        ".Designer.cs",
        ".AssemblyInfo.cs",
        ".AssemblyAttributes.cs",
        ".GlobalUsings.g.cs",
    };
    foreach (var suffix in skippedSuffixes)
    {
        if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }
    return false;
}

static bool? ClassifyProjectAsTest(string csproj)
{
    try
    {
        var content = ReadSnapshot(csproj);
        var name = Path.GetFileNameWithoutExtension(csproj);
        string[] testSuffixes = { ".Tests", ".Test", ".UnitTests", ".IntegrationTests", ".E2E", ".EndToEnd", ".Spec", ".Specs" };
        if (testSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) { return true; }
        if (content.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (content.Contains("MSTest.Sdk", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (content.Contains("Microsoft.Testing.Platform", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (content.Contains("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (content.Contains("xunit", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (content.Contains("NUnit", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (content.Contains("TUnit", StringComparison.OrdinalIgnoreCase)) { return true; }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
    {
        Report("PROJECT_READ_FAILED", csproj, ex.Message);
        return null;
    }
    return false;
}

static string GetNamespaceFor(SyntaxNode node)
{
    // Walk all enclosing namespace declarations and join from outermost to
    // innermost so nested blocks (namespace A { namespace B { ... } }) yield
    // "A.B" rather than just "B".
    List<string>? parts = null;
    foreach (var ns in node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>())
    {
        (parts ??= new List<string>()).Add(ns.Name.ToString());
    }
    if (parts is null)
    {
        return "";
    }
    parts.Reverse();
    return string.Join(".", parts);
}

static bool IsUsingPrefix(HashSet<string> usings, string ns)
{
    if (ns.Length == 0)
    {
        return false;
    }
    foreach (var u in usings)
    {
        if (ns.StartsWith(u + ".", StringComparison.Ordinal))
        {
            return true;
        }
    }
    return false;
}

static Dictionary<string, string> BuildProductionToTestProjectMap(string root)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    List<string> allCsproj = EnumerateSourceFiles(root, "*.csproj").ToList();
    // Sort so the deterministic "first write wins" rule below selects the
    // same test project across runs/machines regardless of filesystem
    // enumeration order.
    allCsproj.Sort(StringComparer.Ordinal);
    foreach (var testProj in allCsproj)
    {
        if (ClassifyProjectAsTest(testProj) != true)
        {
            continue;
        }
        string content;
        try { content = ReadSnapshot(testProj); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            Report("PROJECT_READ_FAILED", testProj, ex.Message);
            continue;
        }
        var testDir = Path.GetDirectoryName(testProj)!;
        var matches = System.Text.RegularExpressions.Regex.Matches(content,
            @"<ProjectReference\s+Include=""([^""]+)""");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var rel = m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
            var abs = Path.GetFullPath(Path.Combine(testDir, rel));
            if (File.Exists(abs) && !map.ContainsKey(abs))
            {
                map[abs] = testProj;
            }
        }
    }
    return map;
}

static string ToRel(string root, string path)
{
    return Path.GetRelativePath(root, path).Replace('\\', '/');
}

static void Log(string msg) => Console.Error.WriteLine($"[find-untested-sources] {msg}");

static int InputError(string message, bool jsonV1)
{
    Console.Error.WriteLine($"INVALID_INPUT: {message}");
    if (jsonV1)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema_version = "1", tool = "Find-UntestedSources", tool_version = "1.0", status = "error",
            scope = new { }, inputs = Array.Empty<object>(), data = new { },
            diagnostics = new[] { new { code = "INVALID_INPUT", severity = "error", message } },
        }));
    }
    return jsonV1 ? 2 : 1;
}

static void Report(string code, string path, string message)
{
    AnalysisState.Diagnostics.Enqueue(new DiagnosticRecord(code, "warning", path, message));
    AnalysisState.FailedFiles[path] = 0;
}

static bool IsLink(string path)
{
    try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Report("STAT_FAILED", path, ex.Message);
        return true;
    }
}

static string ReadSnapshot(string path)
{
    byte[] bytes = File.ReadAllBytes(path);
    string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    if (AnalysisState.Inputs.TryGetValue(path, out string? previous) && previous != hash)
    {
        Report("INPUT_CHANGED", path, "Input changed during analysis.");
    }
    AnalysisState.Inputs[path] = hash;
    using var stream = new MemoryStream(bytes, writable: false);
    using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
    return reader.ReadToEnd();
}

static SyntaxNode? ReadSyntax(string path)
{
    try
    {
        var tree = CSharpSyntaxTree.ParseText(ReadSnapshot(path));
        var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Take(3).ToArray();
        if (errors.Length > 0)
        {
            Report("SYNTAX_ERROR", path, string.Join("; ", errors.Select(d => d.ToString())));
            return null;
        }
        return tree.GetRoot();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
    {
        Report("READ_FAILED", path, ex.Message);
        return null;
    }
}

internal readonly record struct Decl(string ShortName, string Namespace, string File);
internal readonly record struct ProjectLookup(string? Path, bool IsResolved);
internal sealed record DiagnosticRecord(string code, string severity, string path, string message);
internal static class AnalysisState
{
    internal static readonly ConcurrentQueue<DiagnosticRecord> Diagnostics = new();
    internal static readonly ConcurrentDictionary<string, string> Inputs = new(StringComparer.Ordinal);
    internal static readonly ConcurrentDictionary<string, byte> FailedFiles = new(StringComparer.Ordinal);
}
