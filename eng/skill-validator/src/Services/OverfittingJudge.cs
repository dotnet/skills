using System.Text.Json;
using System.Text.RegularExpressions;
using SkillValidator.Models;
using SkillValidator.Utilities;
using GitHub.Copilot.SDK;

namespace SkillValidator.Services;

public static partial class OverfittingJudge
{
    private const int MaxRetries = 2;
    private const int MaxSkillContentChars = 48_000; // ~12K tokens

    public static async Task<OverfittingResult?> Analyze(EvalSkillInfo evalSkill, OverfittingJudgeOptions options)
    {
        if (evalSkill.EvalConfig is null || evalSkill.EvalPath is null)
            return null;

        Exception? lastError = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                    Console.Error.WriteLine($"      🔄 Overfitting judge retry {attempt}/{MaxRetries} for \"{evalSkill.Skill.Name}\"");
                return await AnalyzeOnce(evalSkill, options);
            }
            catch (Exception error)
            {
                lastError = error;
                Console.Error.WriteLine($"      ⚠️  Overfitting judge attempt {attempt + 1} failed: {error.Message[..Math.Min(200, error.Message.Length)]}");
            }
        }

        throw new InvalidOperationException(
            $"Overfitting judge failed for \"{evalSkill.Skill.Name}\" after {MaxRetries + 1} attempts: {lastError}");
    }

    private static async Task<OverfittingResult> AnalyzeOnce(EvalSkillInfo evalSkill, OverfittingJudgeOptions options)
    {
        // Run deterministic prompt checks first — these are high-confidence signals
        // that don't need LLM judgment.
        var deterministicPromptAssessments = DetectPromptOverfitting(evalSkill);

        var client = await AgentRunner.GetSharedClient(options.Verbose);

        await using var session = await client.CreateSessionAsync(new SessionConfig
        {
            Model = options.Model,
            Streaming = true,
            WorkingDirectory = options.WorkDir,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = BuildSystemPrompt(),
            },
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            // No tools needed — all content is passed inline
            OnPermissionRequest = (_, _) => Task.FromResult(new PermissionRequestResult
            {
                Kind = PermissionRequestResultKind.DeniedByRules,
            }),
        });

        var userPrompt = await BuildUserPromptAsync(evalSkill);

        using var cts = new CancellationTokenSource(options.Timeout);
        using var timer = new Timer(_ =>
        {
            Console.Error.WriteLine(
                $"      ⏰ Overfitting judge timed out after {options.Timeout / 1000}s.");
        }, null, options.Timeout, Timeout.Infinite);

        var done = new TaskCompletionSource<string>();
        string responseContent = "";

        session.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageEvent msg:
                    responseContent = msg.Data.Content ?? "";
                    break;
                case SessionIdleEvent:
                    done.TrySetResult(responseContent);
                    break;
                case SessionErrorEvent err:
                    done.TrySetException(new InvalidOperationException(err.Data.Message ?? "Session error"));
                    break;
            }
        });

        await session.SendAsync(new MessageOptions { Prompt = userPrompt });

        var content = await done.Task.WaitAsync(cts.Token);

        if (!string.IsNullOrEmpty(content))
            return ParseOverfittingResponse(content, deterministicPromptAssessments);

        throw new InvalidOperationException("Overfitting judge returned no content");
    }

    public static async Task GenerateFix(EvalSkillInfo evalSkill, OverfittingResult result, OverfittingJudgeOptions options)
    {
        if (evalSkill.EvalPath is null) return;

        var client = await AgentRunner.GetSharedClient(options.Verbose);

        await using var session = await client.CreateSessionAsync(new SessionConfig
        {
            Model = options.Model,
            Streaming = true,
            WorkingDirectory = options.WorkDir,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = BuildFixSystemPrompt(),
            },
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            OnPermissionRequest = (_, _) => Task.FromResult(new PermissionRequestResult
            {
                Kind = PermissionRequestResultKind.DeniedByRules,
            }),
        });

        var evalYaml = await File.ReadAllTextAsync(evalSkill.EvalPath);
        var fixPrompt = BuildFixUserPrompt(evalYaml, result);

        using var cts = new CancellationTokenSource(options.Timeout);
        var done = new TaskCompletionSource<string>();
        string responseContent = "";

        session.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageEvent msg:
                    responseContent = msg.Data.Content ?? "";
                    break;
                case SessionIdleEvent:
                    done.TrySetResult(responseContent);
                    break;
                case SessionErrorEvent err:
                    done.TrySetException(new InvalidOperationException(err.Data.Message ?? "Session error"));
                    break;
            }
        });

        await session.SendAsync(new MessageOptions { Prompt = fixPrompt });
        var content = await done.Task.WaitAsync(cts.Token);

        if (!string.IsNullOrEmpty(content))
        {
            // Extract YAML from response (might be in a code block)
            var yaml = ExtractYaml(content);
            var fixedPath = Path.Combine(Path.GetDirectoryName(evalSkill.EvalPath)!, "eval.fixed.yaml");
            await File.WriteAllTextAsync(fixedPath, yaml);
        }
    }

    internal static OverfittingResult ParseOverfittingResponse(string content, IReadOnlyList<PromptOverfitAssessment>? deterministicPromptAssessments = null)
    {
        var jsonStr = LlmJson.ExtractJson(content)
            ?? throw new InvalidOperationException(
                $"Overfitting judge response contained no JSON. Raw response:\n{content[..Math.Min(500, content.Length)]}");

        var parsed = LlmJson.ParseLlmJson(jsonStr, "overfitting judge response");

        var rubricAssessments = new List<RubricOverfitAssessment>();
        if (parsed.TryGetProperty("rubric_assessments", out var rubricEl) && rubricEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in rubricEl.EnumerateArray())
            {
                var scenario = item.TryGetProperty("scenario", out var s) ? s.GetString() ?? "" : "";
                var criterion = item.TryGetProperty("criterion", out var c) ? c.GetString() ?? "" : "";
                var classification = item.TryGetProperty("classification", out var cl) ? cl.GetString() ?? "outcome" : "outcome";
                var confidence = item.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0.5;
                var reasoning = item.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";
                rubricAssessments.Add(new RubricOverfitAssessment(scenario, criterion, classification, confidence, reasoning));
            }
        }

        var assertionAssessments = new List<AssertionOverfitAssessment>();
        if (parsed.TryGetProperty("assertion_assessments", out var assertEl) && assertEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in assertEl.EnumerateArray())
            {
                var scenario = item.TryGetProperty("scenario", out var s) ? s.GetString() ?? "" : "";
                var summary = item.TryGetProperty("assertion_summary", out var as_) ? as_.GetString() ?? "" : "";
                var classification = item.TryGetProperty("classification", out var cl) ? cl.GetString() ?? "broad" : "broad";
                var confidence = item.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0.5;
                var reasoning = item.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";
                assertionAssessments.Add(new AssertionOverfitAssessment(scenario, summary, classification, confidence, reasoning));
            }
        }

        var crossScenarioIssues = new List<string>();
        if (parsed.TryGetProperty("cross_scenario_issues", out var crossEl) && crossEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in crossEl.EnumerateArray())
            {
                crossScenarioIssues.Add(item.GetString() ?? "");
            }
        }

        // Parse LLM prompt assessments
        var llmPromptAssessments = new List<PromptOverfitAssessment>();
        if (parsed.TryGetProperty("prompt_assessments", out var promptEl) && promptEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in promptEl.EnumerateArray())
            {
                var scenario = item.TryGetProperty("scenario", out var s) ? s.GetString() ?? "" : "";
                var issue = item.TryGetProperty("issue", out var i) ? i.GetString() ?? "" : "";
                var confidence = item.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0.5;
                var reasoning = item.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";
                llmPromptAssessments.Add(new PromptOverfitAssessment(scenario, issue, confidence, reasoning));
            }
        }

        // Merge deterministic prompt assessments (high priority) with LLM-detected ones.
        // Deterministic detections are authoritative — they have confidence 1.0.
        // Only add LLM detections for (scenario, issue) pairs not already covered by deterministic checks.
        var promptAssessments = new List<PromptOverfitAssessment>(deterministicPromptAssessments ?? []);
        var coveredScenarioIssues = new HashSet<string>(
            promptAssessments.Select(p => $"{p.Scenario}\u0001{p.Issue}".ToLowerInvariant())
        );
        foreach (var llmAssessment in llmPromptAssessments)
        {
            var key = $"{llmAssessment.Scenario}\u0001{llmAssessment.Issue}".ToLowerInvariant();
            if (!coveredScenarioIssues.Contains(key))
            {
                promptAssessments.Add(llmAssessment);
                coveredScenarioIssues.Add(key);
            }
        }

        double llmOverallScore = 0.0;
        if (parsed.TryGetProperty("overall_overfitting_score", out var overallEl))
            llmOverallScore = Math.Clamp(overallEl.GetDouble(), 0.0, 1.0);

        string overallReasoning = "";
        if (parsed.TryGetProperty("overall_reasoning", out var reasonEl))
            overallReasoning = reasonEl.GetString() ?? "";

        // Compute score from per-element classifications
        double computedScore = ComputeOverfittingScore(rubricAssessments, assertionAssessments, promptAssessments);

        // Blend: 60% computed (systematic) + 40% LLM holistic
        double finalScore = Math.Clamp(0.6 * computedScore + 0.4 * llmOverallScore, 0.0, 1.0);

        var severity = finalScore switch
        {
            < 0.20 => OverfittingSeverity.Low,
            < 0.50 => OverfittingSeverity.Moderate,
            _ => OverfittingSeverity.High,
        };

        return new OverfittingResult(
            Math.Round(finalScore, 2),
            severity,
            rubricAssessments,
            assertionAssessments,
            promptAssessments,
            crossScenarioIssues,
            overallReasoning);
    }

    internal static double ComputeOverfittingScore(
        IReadOnlyList<RubricOverfitAssessment> rubricAssessments,
        IReadOnlyList<AssertionOverfitAssessment> assertionAssessments,
        IReadOnlyList<PromptOverfitAssessment>? promptAssessments = null)
    {
        // Rubric scoring: weight by classification and confidence
        double rubricScore = 0;
        int rubricCount = 0;
        foreach (var item in rubricAssessments)
        {
            double weight = item.Classification switch
            {
                "outcome" => 0.0,
                "technique" => 0.5,
                "vocabulary" => 1.0,
                _ => 0.0,
            };
            rubricScore += weight * item.Confidence;
            rubricCount++;
        }

        // Assertion scoring
        double assertionScore = 0;
        int assertionCount = 0;
        foreach (var item in assertionAssessments)
        {
            double weight = item.Classification switch
            {
                "broad" => 0.0,
                "narrow" => 1.0,
                _ => 0.0,
            };
            assertionScore += weight * item.Confidence;
            assertionCount++;
        }

        // Prompt scoring — explicit skill references in prompts are a severe signal.
        // Each prompt issue is scored at full weight (1.0) * confidence.
        double promptScore = 0;
        int promptCount = promptAssessments?.Count ?? 0;
        if (promptAssessments is not null)
        {
            foreach (var item in promptAssessments)
            {
                promptScore += 1.0 * item.Confidence;
            }
        }

        double rubricAvg = rubricCount > 0 ? rubricScore / rubricCount : 0;
        double assertionAvg = assertionCount > 0 ? assertionScore / assertionCount : 0;
        double promptAvg = promptCount > 0 ? promptScore / promptCount : 0;

        if (promptCount > 0)
        {
            // When prompt issues exist, they dominate the score because explicit skill
            // references in the prompt are the strongest form of overfitting — they
            // directly bias which agent gets advantage.
            // Weight: 40% prompt, 40% rubric, 20% assertion
            return Math.Clamp(0.4 * promptAvg + 0.4 * rubricAvg + 0.2 * assertionAvg, 0.0, 1.0);
        }

        // Original weighting when no prompt issues (rubric matters more — assertions are secondary gates)
        return Math.Clamp(0.7 * rubricAvg + 0.3 * assertionAvg, 0.0, 1.0);
    }

    /// <summary>
    /// Deterministic pre-check: scans scenario prompts for explicit skill references
    /// that bias the evaluation. These patterns are unambiguously overfitted — they
    /// give the skilled agent a direct advantage by name-dropping the skill.
    /// </summary>
    internal static IReadOnlyList<PromptOverfitAssessment> DetectPromptOverfitting(EvalSkillInfo evalSkill)
    {
        var assessments = new List<PromptOverfitAssessment>();
        if (evalSkill.EvalConfig is null || string.IsNullOrWhiteSpace(evalSkill.Skill.Name))
            return assessments;

        foreach (var scenario in evalSkill.EvalConfig.Scenarios)
        {
            var prompt = scenario.Prompt;
            if (string.IsNullOrWhiteSpace(prompt)) continue;

            // Check 1: Prompt explicitly contains the skill name (e.g., "migrate-dotnet10-to-dotnet11")
            if (prompt.Contains(evalSkill.Skill.Name, StringComparison.OrdinalIgnoreCase))
            {
                assessments.Add(new PromptOverfitAssessment(
                    scenario.Name,
                    "explicit_skill_reference",
                    1.0,
                    $"Prompt explicitly mentions skill name '{evalSkill.Skill.Name}' — this directly tells the skilled agent which skill to activate and disadvantages the baseline agent."));
                continue; // One assessment per scenario is enough
            }

            // Check 2: Prompt uses "use the ... skill" or "use ... skill to" phrasing
            // even if the exact skill name isn't used (e.g., "use the migration skill")
            if (UseSkillPattern().IsMatch(prompt))
            {
                assessments.Add(new PromptOverfitAssessment(
                    scenario.Name,
                    "skill_instruction",
                    0.9,
                    "Prompt explicitly instructs the agent to 'use' a skill — this biases toward the skilled agent and creates an unfair comparison with the baseline."));
            }
        }

        return assessments;
    }

    [GeneratedRegex(@"\buse\s+(?:the\s+)?[\w-]+\s+skill\b", RegexOptions.IgnoreCase)]
    private static partial Regex UseSkillPattern();

    internal static string BuildSystemPrompt() =>
        """
        You are an expert evaluator assessing whether an AI skill's evaluation
        definition (eval) is overfitted.

        A skill teaches an LLM new knowledge or techniques. The eval tests whether
        loading the skill produces better outcomes. An eval is OVERFITTED when it
        rewards the agent for repeating the skill's specific phrasing, syntax, or
        methodology — rather than for producing a genuinely better result that the
        agent could NOT have produced without the skill.

        TWO KEY QUESTIONS to ask for every rubric item and assertion:

        1. DOMAIN EXPERT TEST: "Would a knowledgeable developer who gives a correct,
           high-quality answer — but has NOT read this specific skill document — fail
           this rubric item or assertion?" If yes → overfitted.

        2. LLM KNOWLEDGE TEST: "Does this eval item test for knowledge the LLM likely
           already has from its training data, rather than something genuinely new
           that the skill teaches?" If it tests pre-existing LLM knowledge
           (common APIs, standard syntax, general best practices, language escaping
           rules) → overfitted. The skill's value should be in teaching something
           the LLM wouldn't reliably do on its own.

        IMPORTANT DISTINCTION: Some skills teach a GENUINELY NOVEL technique that has
        no practical alternative — testing for that technique is NOT overfitting.
        The test is: are there other valid approaches that would solve the problem
        equally well? If not, the item tests a real outcome, not memorization.

        Both the skill document and the eval definition will be provided inline
        in the user prompt, clearly delimited.

        YOUR TASK: Classify each rubric item and each assertion.

        ### Rubric item classifications

        - "outcome" — Tests whether the agent reached a correct, useful result.
          The criterion describes WHAT should be achieved, not HOW.
          A different valid approach would also score well.

        - "technique" — Tests whether the agent used a specific method or diagnostic
          procedure taught in the skill. A correct answer using a different valid
          method would score poorly.

        - "vocabulary" — Tests whether the agent used specific terminology, labels,
          or syntax from the skill. The phrasing rewards the skill's exact wording
          rather than equivalent correct alternatives.

        ### Assertion classifications

        - "broad" — Tests for a generally-correct behavior. Multiple valid approaches
          would pass.

        - "narrow" — Tests for a skill-specific pattern. Correct alternative approaches
          would fail this assertion.

        ### Scoring guidance

        - Does the rubric item contain quoted syntax or commands copied from the skill? → likely "vocabulary"
        - Does the rubric item describe a diagnostic STEP rather than a FINDING? → likely "technique"
        - Does the assertion regex match only one specific syntax when alternatives exist? → likely "narrow"
        - Does the item test knowledge any good LLM already has (language escaping, common APIs, general best practices)? → overfitted
        - Does the skill teach something genuinely novel with no practical alternative? → testing for it is NOT overfitting
        - Is the item testing a CONCLUSION or a PATH to the conclusion?

        ### Few-shot examples

        #### Example 1: HIGH overfitting — testing shell escaping the LLM already knows

        SKILL teaches: Use -bl:{} for unique binlog names in MSBuild. In PowerShell,
        escape as -bl:{{}}.

        OVERFITTED assertion (narrow) — HIGH:
          output_matches: "(-bl:\{\{\}\}|/bl:\{\{\}\})"
          → Tests PowerShell escaping of curly braces. This is general shell knowledge
          the LLM already has in training data. Testing for it measures pre-existing
          LLM capability, not skill value.

        WELL-DESIGNED assertion (broad):
          file_exists: "*.binlog"
          → Tests the actual outcome regardless of the syntax used.

        #### Example 2: MODERATE overfitting — testing skill vocabulary labels

        SKILL teaches: Measure builds in three tiers — "cold build", "warm build",
        "no-op build".

        OVERFITTED rubric (vocabulary) — MODERATE:
          "Measured three build scenarios: cold build, warm/incremental build, and no-op build"
          → Tests the skill's specific labeling system. A developer who measures
          "clean build, cached build, null build" is doing the same thing with
          different vocabulary.

        WELL-DESIGNED rubric (outcome):
          "Established a reproducible baseline by measuring builds under different cache states"
          → Tests the concept without requiring exact labels.

        #### Example 3: LOW overfitting — testing a diagnostic step

        SKILL teaches: Build twice and compare binlogs to verify incrementality.

        BORDERLINE rubric (technique) — LOW:
          "Mentioned building twice to verify incrementality"
          → This is a technique from the skill, but an agent that directly analyzes
          the binlog for missing Inputs/Outputs attributes could give an equally
          valid answer without mentioning this step.

        WELL-DESIGNED rubric (outcome):
          "Identified the root cause of incremental build failure"
          → Tests the finding, not the diagnostic path.

        #### Example 4: NOT overfitted — testing a genuinely novel technique

        SKILL teaches: Use -bl:{} for unique binlog filenames (MSBuild 17.8+).
        The {} placeholder makes MSBuild auto-generate a unique name per invocation,
        preventing overwrites when running multiple builds in sequence.

        NOT OVERFITTED rubric (outcome):
          "Used /bl:{} or equivalent to ensure each build produces a unique binlog
          that does not overwrite previous binlogs"
          → The {} placeholder is genuinely the only reliable way to prevent binlog
          collision across sequential MSBuild invocations in the same directory.
          There is no practical alternative — this IS the outcome, not just vocabulary.

        #### Example 5: NOT overfitted — testing correctness, not style

        SKILL teaches: Use LibraryImport for .NET 7+, DllImport for .NET Framework.

        NOT OVERFITTED rubric (outcome):
          "Uses LibraryImport instead of DllImport since the target is .NET 8"
          → Tests a genuine correctness decision. LibraryImport is the right API
          for .NET 7+ — this is not a stylistic preference.

        #### Example 6: MODERATE overfitting — artificial example

        SKILL teaches: Use `dotnet trace collect` with `--clreventlevel` flag for
        targeted perf tracing.

        OVERFITTED rubric (vocabulary) — MODERATE:
          "Used --clreventlevel flag with dotnet trace collect"
          → Tests for the specific CLI flag name from the skill. An agent that
          correctly uses the EventPipe API directly or another profiling tool to
          achieve the same targeted trace would score poorly.

        WELL-DESIGNED rubric (outcome):
          "Collected a performance trace scoped to the relevant events"
          → Tests the result regardless of tool selection.

        #### Example 7: HIGH overfitting — artificial example

        SKILL teaches: Run `dotnet-counters monitor` and look for "GC Heap Size"
        exceeding 500MB as a memory leak indicator.

        OVERFITTED rubric (vocabulary) — HIGH:
          "Checked if GC Heap Size counter exceeds 500MB threshold"
          → Tests for the skill's specific counter name and threshold. A developer
          who checks Gen2 GC frequency, allocations/sec, or uses a memory profiler
          to find the leak is solving the same problem differently.

        WELL-DESIGNED rubric (outcome):
          "Identified evidence of a memory leak and proposed a diagnosis path"
          → Tests whether the agent found the issue, not which metric it checked.

        ### Scenario prompt classifications

        ALSO assess each scenario's PROMPT for bias that unfairly advantages the
        skilled agent. This is a CRITICAL and often-overlooked form of overfitting:

        - "explicit_skill_reference" — The prompt mentions the skill by name
          (e.g., "Use the migrate-dotnet10-to-dotnet11 skill"). This directly
          tells the skilled agent which skill to activate and creates an unfair
          disadvantage for the baseline agent, which cannot follow this instruction.

        - "skill_instruction" — The prompt instructs the agent to "use a skill"
          or references skill-specific concepts that only make sense if the skill
          is loaded (e.g., "use the migration skill to help me").

        - "neutral" — The prompt describes the task naturally without referencing
          skills. A developer might write this prompt regardless of whether a
          skill exists.

        If the prompt is "neutral", do NOT include it in prompt_assessments.

        #### Example 8: HIGH overfitting — prompt explicitly names the skill

        SKILL name: migrate-dotnet10-to-dotnet11

        OVERFITTED prompt — HIGH:
          "Use the migrate-dotnet10-to-dotnet11 skill to help me migrate my
          .NET 10 console app to .NET 11."
          → Tells the skilled agent exactly which skill to activate. The baseline
          agent wastes time looking for a skill it doesn't have. This is the
          strongest form of overfitting.

        WELL-DESIGNED prompt:
          "I need to migrate my .NET 10 console app to .NET 11. What breaks?"
          → Describes the task naturally. Both agents get the same fair prompt.

        Respond ONLY with JSON. No markdown, no commentary outside the JSON.
        """;

    internal static async Task<string> BuildUserPromptAsync(EvalSkillInfo evalSkill)
    {
        // Prepare skill content (truncate if needed)
        var skillContent = evalSkill.Skill.SkillMdContent;
        if (skillContent.Length > MaxSkillContentChars)
        {
            skillContent = skillContent[..MaxSkillContentChars] +
                $"\n\n[TRUNCATED — skill document was {skillContent.Length} characters, showing first {MaxSkillContentChars}.\n Full document at: {evalSkill.Skill.SkillMdPath}]";
        }

        // Read raw eval YAML
        string evalYaml = "(eval.yaml not available)";
        if (evalSkill.EvalPath is not null && File.Exists(evalSkill.EvalPath))
        {
            evalYaml = await File.ReadAllTextAsync(evalSkill.EvalPath);
        }

        return $$"""
            Assess the following skill and its eval definition for overfitting.

            === SKILL DOCUMENT (from: {{evalSkill.Skill.SkillMdPath}}) ===
            <<<SKILL_CONTENT_START>>>
            {{skillContent}}
            <<<SKILL_CONTENT_END>>>

            === EVAL DEFINITION (raw YAML) ===
            <<<EVAL_CONTENT_START>>>
            {{evalYaml}}
            <<<EVAL_CONTENT_END>>>

            === CLASSIFICATION REQUEST ===

            Classify every rubric item and every assertion across all scenarios.
            Also assess each scenario prompt for bias (skill name references, skill
            instructions). Then provide an overall overfitting score from 0.0 to 1.0.

            Respond in this exact JSON schema:

            {
              "rubric_assessments": [
                {
                  "scenario": "<scenario name>",
                  "criterion": "<exact rubric text>",
                  "classification": "outcome" | "technique" | "vocabulary",
                  "confidence": <0.0-1.0>,
                  "reasoning": "<1-2 sentence explanation>"
                }
              ],
              "assertion_assessments": [
                {
                  "scenario": "<scenario name>",
                  "assertion_summary": "<type: pattern_or_value>",
                  "classification": "broad" | "narrow",
                  "confidence": <0.0-1.0>,
                  "reasoning": "<1-2 sentence explanation>"
                }
              ],
              "prompt_assessments": [
                {
                  "scenario": "<scenario name>",
                  "issue": "explicit_skill_reference" | "skill_instruction",
                  "confidence": <0.0-1.0>,
                  "reasoning": "<1-2 sentence explanation>"
                }
              ],
              "cross_scenario_issues": [
                "<description of any cross-scenario overfitting patterns>"
              ],
              "overall_overfitting_score": <0.0-1.0>,
              "overall_reasoning": "<2-3 sentence summary>"
            }
            """;
    }

    private static string BuildFixSystemPrompt() =>
        """
        You are an expert evaluator helping improve AI skill evaluation definitions.
        You will receive an eval.yaml file and a classification of its rubric items
        and assertions. Your job is to produce a revised eval.yaml that replaces
        overfitted items with outcome-focused alternatives.

        Rules:
        - Keep the YAML structure identical (same scenarios, same number of items)
        - Replace flagged "vocabulary" and "technique" items with "outcome" alternatives
        - Add a `# CHANGED: <reason>` YAML comment before each modified item
        - Keep items classified as "outcome" or "broad" unchanged
        - The replacement should test the same CONCEPT but allow different valid approaches
        - Output ONLY valid YAML. No markdown, no commentary.
        """;

    private static string BuildFixUserPrompt(string evalYaml, OverfittingResult result)
    {
        var flagged = new List<string>();
        foreach (var r in result.RubricAssessments.Where(a => a.Classification != "outcome"))
            flagged.Add($"- [{r.Classification}] \"{r.Criterion}\" — {r.Reasoning}");
        foreach (var a in result.AssertionAssessments.Where(a => a.Classification != "broad"))
            flagged.Add($"- [{a.Classification}] {a.AssertionSummary} — {a.Reasoning}");

        return $"""
            Original eval.yaml:
            ```yaml
            {evalYaml}
            ```

            Flagged items to improve:
            {string.Join("\n", flagged)}

            Produce a revised eval.yaml with CHANGED comments. Output ONLY YAML.
            """;
    }

    private static string ExtractYaml(string content)
    {
        // Try to extract from markdown code block
        var match = System.Text.RegularExpressions.Regex.Match(content, @"```(?:yaml)?\s*([\s\S]*?)```");
        return match.Success ? match.Groups[1].Value.Trim() : content.Trim();
    }
}
