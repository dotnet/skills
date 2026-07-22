namespace SkillValidator.Evaluate;

public static class EvalSchema
{
    /// <summary>Default scenario timeout in seconds when not specified in eval.yaml.</summary>
    public const int DefaultScenarioTimeoutSeconds = 120;

    public static EvalConfig ParseEvalConfig(string yamlContent)
    {
        var raw = SkillValidatorYamlContext.UnderscoredDeserializer.Deserialize<RawEvalConfig>(yamlContent)
            ?? throw new InvalidOperationException("Failed to parse eval config YAML");

        var scenarios = raw.Scenarios?.Select(ParseScenario).ToList();

        if (scenarios is not { Count: > 0 })
            throw new InvalidOperationException("Eval config must have at least one scenario");

        return new EvalConfig(
            scenarios,
            MaxParallelScenarios: raw.Config?.MaxParallelScenarios,
            MaxParallelRuns: raw.Config?.MaxParallelRuns);
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

    /// <summary>
    /// Parse an eval.yaml in either the current Vally-native format
    /// (<c>stimuli:</c>/<c>graders:</c>) or the legacy skill-validator format
    /// (<c>scenarios:</c>), returning null when neither yields any scenario.
    ///
    /// Unlike <see cref="ParseEvalConfig"/>, this never throws on an unrecognized
    /// or empty schema — the standalone overfitting judge treats an unparseable
    /// eval as "skip, don't fail". The Vally format is tried first because it is
    /// the schema every eval.yaml in this repo now uses.
    /// </summary>
    public static EvalConfig? ParseEvalConfigFlexible(string yamlContent)
    {
        var vally = TryParseVallyEvalConfig(yamlContent);
        if (vally is not null)
            return vally;

        // Legacy scenarios format (retained for back-compat).
        RawEvalConfig? legacy;
        try
        {
            legacy = SkillValidatorYamlContext.UnderscoredDeserializer.Deserialize<RawEvalConfig>(yamlContent);
        }
        catch
        {
            return null;
        }

        var scenarios = legacy?.Scenarios?.Select(ParseScenario).ToList();
        return scenarios is { Count: > 0 }
            ? new EvalConfig(scenarios, legacy!.Config?.MaxParallelScenarios, legacy.Config?.MaxParallelRuns)
            : null;
    }

    /// <summary>
    /// Map a Vally-native eval (<c>stimuli</c> with per-stimulus <c>prompt</c>,
    /// <c>graders</c>, and <c>rubric</c>) onto the internal <see cref="EvalConfig"/>
    /// shape the overfitting judge consumes. Each stimulus becomes a scenario
    /// (name + prompt + rubric), and recognized output graders map to assertions.
    /// Returns null when the YAML has no <c>stimuli</c>.
    /// </summary>
    internal static EvalConfig? TryParseVallyEvalConfig(string yamlContent)
    {
        RawVallyEvalConfig? raw;
        try
        {
            raw = SkillValidatorYamlContext.UnderscoredDeserializer.Deserialize<RawVallyEvalConfig>(yamlContent);
        }
        catch
        {
            return null;
        }

        if (raw?.Stimuli is not { Count: > 0 })
            return null;

        var scenarios = new List<EvalScenario>();
        foreach (var stimulus in raw.Stimuli)
        {
            if (string.IsNullOrWhiteSpace(stimulus.Name) || string.IsNullOrWhiteSpace(stimulus.Prompt))
                continue;

            List<Assertion>? assertions = null;
            if (stimulus.Graders is not null)
            {
                foreach (var grader in stimulus.Graders)
                {
                    var assertion = MapVallyGrader(grader);
                    if (assertion is not null)
                        (assertions ??= []).Add(assertion);
                }
            }

            scenarios.Add(new EvalScenario(
                Name: stimulus.Name,
                Prompt: stimulus.Prompt,
                Assertions: assertions,
                Rubric: stimulus.Rubric is { Count: > 0 } ? stimulus.Rubric : null));
        }

        return scenarios.Count > 0 ? new EvalConfig(scenarios) : null;
    }

    /// <summary>
    /// Best-effort map of a Vally output grader onto an <see cref="Assertion"/>.
    /// The overfitting judge sends the raw eval YAML to the LLM regardless, so
    /// unrecognized grader types (e.g. <c>prompt</c>, the LLM-rubric grader) are
    /// simply skipped rather than treated as errors.
    /// </summary>
    private static Assertion? MapVallyGrader(RawVallyGrader grader) => grader.Type switch
    {
        "output-contains" => new Assertion(AssertionType.OutputContains, Value: grader.Config?.Substring),
        "output-not-contains" => new Assertion(AssertionType.OutputNotContains, Value: grader.Config?.Substring),
        "output-matches" => new Assertion(AssertionType.OutputMatches, Pattern: grader.Config?.Pattern),
        "output-not-matches" => new Assertion(AssertionType.OutputNotMatches, Pattern: grader.Config?.Pattern),
        _ => null,
    };

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
            setup = new SetupConfig(
                raw.Setup.CopyTestFiles,
                files,
                raw.Setup.Commands,
                raw.Setup.AdditionalRequiredSkills,
                raw.Setup.AdditionalRequiredAgents);
        }

        return new EvalScenario(
            Name: raw.Name,
            Prompt: raw.Prompt,
            Setup: setup,
            Assertions: assertions,
            Rubric: raw.Rubric,
            Timeout: raw.Timeout ?? DefaultScenarioTimeoutSeconds,
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
            "run_command_and_assert" => AssertionType.RunCommandAndAssert,
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
            case AssertionType.RunCommandAndAssert:
                if (string.IsNullOrWhiteSpace(raw.CommandToRun))
                    throw new InvalidOperationException($"Assertion '{raw.Type}' requires 'command_to_run'");

                if (raw.ExpectedExitCode is null &&
                    string.IsNullOrWhiteSpace(raw.ExpectedStdOutputContains) &&
                    string.IsNullOrWhiteSpace(raw.ExpectedStdErrorContains) &&
                    string.IsNullOrWhiteSpace(raw.ExpectedStdOutputMatches) &&
                    string.IsNullOrWhiteSpace(raw.ExpectedStdErrorMatches))
                    throw new InvalidOperationException($"Assertion '{raw.Type}' requires one or more of 'expected_exit_code', 'expected_std_output_contains', 'expected_std_error_contains', 'expected_std_output_matches', or 'expected_std_error_matches'");
                break;
        }

        CommandAssertionArgs? commandArgs = type == AssertionType.RunCommandAndAssert
            ? new CommandAssertionArgs(
                raw.CommandToRun!,
                NullIfWhiteSpace(raw.CommandArguments),
                raw.ExpectedExitCode,
                NullIfWhiteSpace(raw.ExpectedStdOutputContains),
                NullIfWhiteSpace(raw.ExpectedStdErrorContains),
                NullIfWhiteSpace(raw.ExpectedStdOutputMatches),
                NullIfWhiteSpace(raw.ExpectedStdErrorMatches),
                raw.CommandTimeout)
            : null;

        return new Assertion(type, raw.Path, raw.Value, raw.Pattern, commandArgs);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    // Raw YAML deserialization models

    internal sealed class RawEvalConfig
    {
        public RawEvalSettings? Config { get; set; }
        public List<RawScenario>? Scenarios { get; set; }
    }

    internal sealed class RawEvalSettings
    {
        public int? MaxParallelScenarios { get; set; }
        public int? MaxParallelRuns { get; set; }
    }

    internal sealed class RawScenario
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

    internal sealed class RawSetup
    {
        public bool CopyTestFiles { get; set; }
        public List<RawSetupFile>? Files { get; set; }
        public List<string>? Commands { get; set; }
        public List<string>? AdditionalRequiredSkills { get; set; }
        public List<string>? AdditionalRequiredAgents { get; set; }
    }

    internal sealed class RawSetupFile
    {
        public string Path { get; set; } = "";
        public string? Source { get; set; }
        public string? Content { get; set; }
    }

    internal sealed class RawAssertion
    {
        public string Type { get; set; } = "";
        public string? Path { get; set; }
        public string? Value { get; set; }
        public string? Pattern { get; set; }

        public string? CommandToRun { get; set; }
        public string? CommandArguments { get; set; }
        public int? ExpectedExitCode { get; set; }
        public string? ExpectedStdOutputContains { get; set; }
        public string? ExpectedStdErrorContains { get; set; }
        public string? ExpectedStdOutputMatches { get; set; }
        public string? ExpectedStdErrorMatches { get; set; }
        public int? CommandTimeout { get; set; }
    }

    // Raw YAML deserialization models for the Vally-native eval format
    // (stimuli/graders). Only the fields the overfitting judge needs are
    // modeled; the deserializer ignores unmatched properties.

    internal sealed class RawVallyEvalConfig
    {
        public List<RawVallyStimulus>? Stimuli { get; set; }
    }

    internal sealed class RawVallyStimulus
    {
        public string Name { get; set; } = "";
        public string Prompt { get; set; } = "";
        public List<RawVallyGrader>? Graders { get; set; }
        public List<string>? Rubric { get; set; }
    }

    internal sealed class RawVallyGrader
    {
        public string Type { get; set; } = "";
        public RawVallyGraderConfig? Config { get; set; }
    }

    internal sealed class RawVallyGraderConfig
    {
        public string? Substring { get; set; }
        public string? Pattern { get; set; }
    }
}
