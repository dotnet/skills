using SkillValidator.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SkillValidator.Services;

public static class EvalSchema
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static EvalConfig ParseEvalConfig(string yamlContent)
    {
        var raw = YamlDeserializer.Deserialize<RawEvalConfig>(yamlContent)
            ?? throw new InvalidOperationException("Failed to parse eval config YAML");

        var scenarios = raw.Scenarios?.Select(ParseScenario).ToList();

        if (scenarios is not { Count: > 0 })
            throw new InvalidOperationException("Eval config must have at least one scenario");

        return new EvalConfig(scenarios);
    }

    public static (bool Success, EvalConfig? Data, IReadOnlyList<string>? Errors) ValidateEvalConfig(string yamlContent)
    {
        try
        {
            var config = ParseEvalConfig(yamlContent);
            return (true, config, null);
        }
        catch (Exception ex)
        {
            return (false, null, [ex.Message]);
        }
    }

    private static EvalScenario ParseScenario(RawScenario raw)
    {
        if (string.IsNullOrWhiteSpace(raw.Name))
            throw new InvalidOperationException("Scenario name is required");
        if (string.IsNullOrWhiteSpace(raw.Prompt))
            throw new InvalidOperationException("Scenario prompt is required");

        var assertions = raw.Assertions?.Select(ParseAssertion).ToList();

        SetupConfig? setup = null;
        if (raw.Setup is not null)
        {
            var files = raw.Setup.Files?.Select(f =>
                new SetupFile(f.Path, f.Source, f.Content)).ToList();
            setup = new SetupConfig(raw.Setup.CopyTestFiles, files, raw.Setup.Commands);
        }

        return new EvalScenario(
            Name: raw.Name,
            Prompt: raw.Prompt,
            Setup: setup,
            Assertions: assertions,
            Rubric: raw.Rubric,
            Timeout: raw.Timeout ?? 120,
            ExpectTools: raw.ExpectTools,
            RejectTools: raw.RejectTools,
            MaxTurns: raw.MaxTurns,
            MaxTokens: raw.MaxTokens,
            ExpectActivation: raw.ExpectActivation ?? true);
    }

    private static Assertion ParseAssertion(RawAssertion raw)
    {
        var type = raw.Type switch
        {
            "file_exists" => AssertionType.FileExists,
            "file_not_exists" => AssertionType.FileNotExists,
            "file_contains" => AssertionType.FileContains,
            "file_not_contains" => AssertionType.FileNotContains,
            "output_contains" => AssertionType.OutputContains,
            "output_not_contains" => AssertionType.OutputNotContains,
            "output_matches" => AssertionType.OutputMatches,
            "output_not_matches" => AssertionType.OutputNotMatches,
            "exit_success" => AssertionType.ExitSuccess,
            _ => throw new InvalidOperationException($"Unknown assertion type: {raw.Type}"),
        };

        // Validate required fields per assertion type
        switch (type)
        {
            case AssertionType.FileExists or AssertionType.FileNotExists:
                if (string.IsNullOrWhiteSpace(raw.Path))
                    throw new InvalidOperationException($"Assertion '{raw.Type}' requires 'path'");
                break;
            case AssertionType.FileContains or AssertionType.FileNotContains:
                if (string.IsNullOrWhiteSpace(raw.Path))
                    throw new InvalidOperationException($"Assertion '{raw.Type}' requires 'path'");
                if (string.IsNullOrWhiteSpace(raw.Value))
                    throw new InvalidOperationException($"Assertion '{raw.Type}' requires 'value'");
                break;
            case AssertionType.OutputContains or AssertionType.OutputNotContains:
                if (string.IsNullOrWhiteSpace(raw.Value))
                    throw new InvalidOperationException($"Assertion '{raw.Type}' requires 'value'");
                break;
            case AssertionType.OutputMatches or AssertionType.OutputNotMatches:
                if (string.IsNullOrWhiteSpace(raw.Pattern))
                    throw new InvalidOperationException($"Assertion '{raw.Type}' requires 'pattern'");
                break;
        }

        return new Assertion(type, raw.Path, raw.Value, raw.Pattern);
    }

    // Raw YAML deserialization models

    private sealed class RawEvalConfig
    {
        public List<RawScenario>? Scenarios { get; set; }
    }

    private sealed class RawScenario
    {
        public string Name { get; set; } = "";
        public string Prompt { get; set; } = "";
        public RawSetup? Setup { get; set; }
        public List<RawAssertion>? Assertions { get; set; }
        public List<string>? Rubric { get; set; }
        public int? Timeout { get; set; }
        public List<string>? ExpectTools { get; set; }
        public List<string>? RejectTools { get; set; }
        public int? MaxTurns { get; set; }
        public int? MaxTokens { get; set; }
        public bool? ExpectActivation { get; set; }
    }

    private sealed class RawSetup
    {
        public bool CopyTestFiles { get; set; }
        public List<RawSetupFile>? Files { get; set; }
        public List<string>? Commands { get; set; }
    }

    private sealed class RawSetupFile
    {
        public string Path { get; set; } = "";
        public string? Source { get; set; }
        public string? Content { get; set; }
    }

    private sealed class RawAssertion
    {
        public string Type { get; set; } = "";
        public string? Path { get; set; }
        public string? Value { get; set; }
        public string? Pattern { get; set; }
    }
}
