#:package Microsoft.CodeAnalysis.CSharp@4.14.0

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
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: dotnet run Find-UntestedSources.cs -- <repo-root> [--top N]");
    return 1;
}

string root = Path.GetFullPath(args[0]);
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"Not a directory: {root}");
    return 1;
}

int topN = int.MaxValue;
for (int i = 1; i < args.Length - 1; i++)
{
    if (args[i] == "--top" && int.TryParse(args[i + 1], out var n))
    {
        topN = n;
    }
}

var swTotal = System.Diagnostics.Stopwatch.StartNew();

// 1. Discover .cs files
var allCs = new List<string>();
foreach (var f in EnumerateCsFiles(root))
{
    allCs.Add(f);
}
Log($"Discovered {allCs.Count} .cs files");

// 2. Classify by walking up to nearest .csproj
var projectForDir = new ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
var isTestProject = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

string? ProjectFor(string filePath)
{
    var dir = Path.GetDirectoryName(filePath)!;
    return projectForDir.GetOrAdd(dir, d =>
    {
        var cur = d;
        while (cur is not null && cur.Length >= root.Length)
        {
            string[] csprojs;
            try { csprojs = Directory.GetFiles(cur, "*.csproj"); }
            catch { csprojs = Array.Empty<string>(); }
            if (csprojs.Length > 0)
            {
                // Sort to keep selection deterministic when multiple .csproj
                // files coexist in the same directory.
                Array.Sort(csprojs, StringComparer.Ordinal);
                return csprojs[0];
            }
            cur = Path.GetDirectoryName(cur);
        }
        return null;
    });
}

bool IsTest(string filePath)
{
    var proj = ProjectFor(filePath);
    if (proj is null)
    {
        return false;
    }
    return isTestProject.GetOrAdd(proj, ClassifyProjectAsTest);
}

var sourceFiles = new List<string>();
var testFiles = new List<string>();
foreach (var f in allCs)
{
    if (IsTest(f))
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
    string text;
    try { text = File.ReadAllText(file); }
    catch { return; }
    var tree = CSharpSyntaxTree.ParseText(text);
    var rootNode = tree.GetRoot();
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
    string text;
    try { text = File.ReadAllText(file); }
    catch { return; }
    var tree = CSharpSyntaxTree.ParseText(text);
    var rootNode = tree.GetRoot();

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
    .Where(kv => kv.Value.IsEmpty)
    .Select(kv => kv.Key)
    .OrderBy(s => s, StringComparer.Ordinal)
    .ToList();

// Cache: source proj -> candidate test project paths
var testProjectsByProductionProject = BuildProductionToTestProjectMap(root);

string? SuggestTestPath(string srcFile)
{
    var proj = ProjectFor(srcFile);
    if (proj is null)
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
Console.WriteLine(JsonSerializer.Serialize(result, opts));
Log($"Total time: {swTotal.ElapsedMilliseconds} ms");
return 0;

// ============================================================================
// Helpers
// ============================================================================

static IEnumerable<string> EnumerateCsFiles(string root)
{
    var stack = new Stack<string>();
    stack.Push(root);
    while (stack.Count > 0)
    {
        var dir = stack.Pop();
        IEnumerable<string> subdirs;
        try { subdirs = Directory.EnumerateDirectories(dir); }
        catch { continue; }
        foreach (var sub in subdirs)
        {
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
        try { files = Directory.EnumerateFiles(dir, "*.cs"); }
        catch { continue; }
        foreach (var f in files)
        {
            if (IsSkippedFile(f))
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

static bool ClassifyProjectAsTest(string csproj)
{
    var name = Path.GetFileNameWithoutExtension(csproj);
    string[] testSuffixes = { ".Tests", ".Test", ".UnitTests", ".IntegrationTests", ".E2E", ".EndToEnd", ".Spec", ".Specs" };
    foreach (var suf in testSuffixes)
    {
        if (name.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }
    try
    {
        var content = File.ReadAllText(csproj);
        if (content.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (content.Contains("MSTest.Sdk", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (content.Contains("Microsoft.Testing.Platform", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (content.Contains("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (content.Contains("xunit", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (content.Contains("NUnit", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (content.Contains("TUnit", StringComparison.OrdinalIgnoreCase)) { return true; }
    }
    catch
    {
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
    List<string> allCsproj;
    try
    {
        allCsproj = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).ToList();
    }
    catch
    {
        return map;
    }
    // Sort so the deterministic "first write wins" rule below selects the
    // same test project across runs/machines regardless of filesystem
    // enumeration order.
    allCsproj.Sort(StringComparer.Ordinal);
    foreach (var testProj in allCsproj)
    {
        if (!ClassifyProjectAsTest(testProj))
        {
            continue;
        }
        string content;
        try { content = File.ReadAllText(testProj); }
        catch { continue; }
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
    try { return Path.GetRelativePath(root, path).Replace('\\', '/'); }
    catch { return path; }
}

static void Log(string msg) => Console.Error.WriteLine($"[find-untested-sources] {msg}");

internal readonly record struct Decl(string ShortName, string Namespace, string File);
