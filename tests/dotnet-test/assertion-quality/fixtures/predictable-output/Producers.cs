using System.Text;
using System.Text.Json;

namespace Output;

public sealed record TestResult(string Id, string Outcome);

public sealed record Event(string TestId, string Stream, string Text);

public sealed record Provider(string Name, params string[] Extensions);

public static class Producers
{
    private static readonly TestResult[] Results = [new("Alpha", "passed"), new("Beta", "failed")];

    // A filter selects these three test IDs; summaries alone are not the selection contract.
    public static string RunFiltered(string filter = "Alpha|Beta|Gamma")
    {
        TestResult[] all = [new("Alpha", "passed"), new("Beta", "passed"), new("Gamma", "skipped"), new("Delta", "passed")];
        TestResult[] selected = all.Where(result => filter.Split('|').Contains(result.Id)).ToArray();
        var lines = selected.Select(result => $"{result.Id} {result.Outcome}").ToList();
        lines.Add($" total: {selected.Length}; passed: {selected.Count(r => r.Outcome == "passed")}; skipped: {selected.Count(r => r.Outcome == "skipped")}");
        return string.Join("\n", lines);
    }

    public static string Render()
    {
        string json = JsonSerializer.Serialize(new
        {
            passed = Results.Count(r => r.Outcome == "passed"),
            failed = Results.Count(r => r.Outcome == "failed"),
            tests = Results.Select(r => new { id = r.Id, outcome = r.Outcome }),
        });
        return $"""<html><script id="results" type="application/json">{json}</script></html>""";
    }

    // Public CLI contract: provider sections and their extensions appear in this order.
    public static string[] Info(params Provider[] providers)
    {
        if (providers.Length == 0)
        {
            providers = [new("Alpha", "Retry"), new("Beta", "Html")];
        }

        return providers
            .SelectMany(provider => provider.Extensions.Select(extension => $"Extension: {extension}").Prepend($"Provider: {provider.Name}"))
            .ToArray();
    }

    // Contract: exactly one complete generated file registering only the supplied classes.
    public static string GenerateRegistry(params string[] classes)
    {
        var builder = new StringBuilder("public static class Registry\n{\n    public static void Register()\n    {\n");
        foreach (string name in classes)
        {
            builder.Append($"        Register(typeof({name}));\n");
        }

        return builder.Append("    }\n}\n").ToString();
    }

    public static string Summary()
    {
        var builder = new StringBuilder("# Test results\n| Test | Outcome |\n| --- | --- |\n");
        foreach (TestResult result in Results)
        {
            builder.Append($"| {result.Id} | {result.Outcome} |\n");
        }

        return builder.Append($"Total: {Results.Length}; Passed: {Results.Count(r => r.Outcome == "passed")}; Failed: {Results.Count(r => r.Outcome == "failed")}").ToString();
    }

    public static string InvalidOption(string value) =>
        $"Unknown option '{value}'. Expected one of: json, html.";

    // Events for this sequential run must keep their original test, stream, and order.
    public static Event[] Events() =>
        [new("Alpha", "stdout", "hello"), new("Beta", "stderr", "warning")];

    public static string Log() => $"startup {DateTime.UtcNow:O}\nWARNING: low disk space\nshutdown";

    // Stable result records are emitted in test-ID order; timestamp and property order may vary.
    public static string RenderResults() => JsonSerializer.Serialize(new
    {
        timestamp = DateTime.UtcNow,
        results = Results.Select(r => new { id = r.Id, status = r.Outcome }),
    });

    public static string[] DiscoverTestIds() => Results.Select(r => r.Id).ToArray();
}
