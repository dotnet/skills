using System.Text.Json;
using GitHub.Copilot.SDK;

namespace SkillEvaluator;

/// <summary>
/// Uses the GitHub Copilot SDK to evaluate agent skill transcripts via an LLM judge.
/// </summary>
public sealed class JudgeEvaluator : IAsyncDisposable
{
    private readonly CopilotClient _client;
    private readonly string _model;
    private bool _started;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <param name="model">Model to use for judging (e.g. "claude-opus-4.6", "gpt-4.1").</param>
    public JudgeEvaluator(string model = "claude-opus-4.6")
    {
        _model = model;
        _client = new CopilotClient();
    }

    /// <summary>Start the Copilot CLI server.</summary>
    public async Task StartAsync()
    {
        if (!_started)
        {
            await _client.StartAsync();
            _started = true;
        }
    }

    /// <summary>
    /// Evaluate a transcript against good/bad references using a rubric.
    /// </summary>
    public async Task<EvaluationResult> EvaluateAsync(
        string inputPrompt,
        Rubric rubric,
        string goodTranscript,
        string badTranscript,
        string candidateTranscript)
    {
        await StartAsync();

        var judgePrompt = BuildJudgePrompt(inputPrompt, rubric, goodTranscript, badTranscript, candidateTranscript);

        // Create a session with a replaced system message so the model acts purely as a JSON evaluator.
        await using var session = await _client.CreateSessionAsync(new SessionConfig
        {
            Model = _model,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = "You are an expert evaluator of AI coding agent behavior. Respond ONLY in valid JSON. No markdown fences, no commentary."
            },
            // Disable all built-in tools — we only want a chat completion, not an agentic session.
            AvailableTools = [],
        });

        // Send the judge prompt and wait for the full response.
        var response = await session.SendAndWaitAsync(new MessageOptions
        {
            Prompt = judgePrompt
        });

        var content = response?.Data.Content
            ?? throw new InvalidOperationException("Judge returned no response content.");

        return ParseJudgeResponse(content);
    }

    /// <summary>
    /// Run calibration: evaluate the known good and bad transcripts and report whether the rubric discriminates.
    /// </summary>
    public async Task<(EvaluationResult Good, EvaluationResult Bad)> CalibrateAsync(
        string inputPrompt,
        Rubric rubric,
        string goodTranscript,
        string badTranscript)
    {
        Console.WriteLine("  Evaluating GOOD transcript (expect >= 0.85)...");
        var goodResult = await EvaluateAsync(inputPrompt, rubric, goodTranscript, badTranscript, goodTranscript);

        Console.WriteLine("  Evaluating BAD transcript (expect <= 0.40)...");
        var badResult = await EvaluateAsync(inputPrompt, rubric, goodTranscript, badTranscript, badTranscript);

        return (goodResult, badResult);
    }

    /// <summary>
    /// Grade the quality of a SKILL.md prompt against a prompt quality rubric.
    /// </summary>
    public async Task<PromptGradeResult> GradePromptAsync(string skillMdContent, Rubric promptQualityRubric)
    {
        await StartAsync();

        var rubricJson = JsonSerializer.Serialize(promptQualityRubric.Criteria, new JsonSerializerOptions { WriteIndented = true });
        var threshold = promptQualityRubric.Scoring.PassThreshold;

        var jsonTemplate = """
            {
              "scores": [
                {
                  "criterion": "<name>",
                  "score": 0.0,
                  "justification": "<one sentence>"
                }
              ],
              "weighted_score": 0.0,
              "pass": true,
              "summary": "<2-3 sentence assessment>"
            }
            """;

        var prompt = $"""
            ## TASK
            Evaluate the quality of the following agent skill definition (SKILL.md) against
            a rubric of best practices. You are scoring the *prompt itself*, not the agent's
            behavior. A high quality skill definition gives an agent clear, unambiguous,
            actionable instructions that lead to good outcomes.

            ## RUBRIC
            {rubricJson}

            Pass threshold: {threshold}
            Scoring: 0.0 = not met, 0.5 = partially met, 1.0 = fully met

            ## SKILL.MD TO EVALUATE
            {skillMdContent}

            Score each criterion. Respond ONLY in this JSON format (no markdown fences):
            {jsonTemplate}
            """;

        await using var session = await _client.CreateSessionAsync(new SessionConfig
        {
            Model = _model,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = "You are an expert evaluator of AI agent skill definitions. Respond ONLY in valid JSON. No markdown fences, no commentary."
            },
            AvailableTools = [],
        });

        var response = await session.SendAndWaitAsync(new MessageOptions
        {
            Prompt = prompt
        });

        var content = response?.Data.Content
            ?? throw new InvalidOperationException("Judge returned no response content.");

        return ParsePromptGradeResponse(content);
    }

    // ── Private helpers ────────────────────────────────────────────────

    private static string BuildJudgePrompt(
        string inputPrompt,
        Rubric rubric,
        string goodTranscript,
        string badTranscript,
        string candidateTranscript)
    {
        var rubricJson = JsonSerializer.Serialize(rubric.Criteria, new JsonSerializerOptions { WriteIndented = true });
        var threshold = rubric.Scoring.PassThreshold;

        var jsonTemplate = """
            {
              "scores": [
                {
                  "criterion": "<name>",
                  "score": 0.0,
                  "justification": "<one sentence>"
                }
              ],
              "weighted_score": 0.0,
              "pass": true,
              "classification": "correct",
              "summary": "<2-3 sentence assessment>"
            }
            """;

        return $"""
            ## INPUT PROMPT (what the user asked)
            {inputPrompt}

            ## EVALUATION RUBRIC
            {rubricJson}

            Pass threshold: {threshold}
            Scoring: 0.0 = not met, 0.5 = partially met, 1.0 = fully met

            ## REFERENCE GOOD (ideal behavior WITH the skill)
            {goodTranscript}

            ## REFERENCE BAD (typical behavior WITHOUT the skill)
            {badTranscript}

            ## TRANSCRIPT TO EVALUATE
            {candidateTranscript}

            Score each criterion. Respond ONLY in this JSON format (no markdown fences):
            {jsonTemplate}

            Classification guide:
            - "correct": Transcript resembles REFERENCE GOOD (skill helped) and score >= threshold
            - "incorrect": Transcript resembles REFERENCE BAD despite skill being loaded
            - "ineffective": Skill was not needed (agent would succeed without it)
            """;
    }

    private static EvaluationResult ParseJudgeResponse(string content)
    {
        // Strip markdown JSON fences if the model wraps them anyway.
        content = content.Trim();
        if (content.StartsWith("```"))
        {
            var firstNewline = content.IndexOf('\n');
            if (firstNewline >= 0) content = content[(firstNewline + 1)..];
        }
        if (content.EndsWith("```"))
        {
            content = content[..^3];
        }
        content = content.Trim();

        try
        {
            return JsonSerializer.Deserialize<EvaluationResult>(content, JsonOptions)
                ?? throw new InvalidOperationException("Deserialized to null");
        }
        catch (JsonException ex)
        {
            // Try to find JSON object within a larger response.
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                var jsonSlice = content[start..(end + 1)];
                return JsonSerializer.Deserialize<EvaluationResult>(jsonSlice, JsonOptions)
                    ?? throw new InvalidOperationException($"Failed to parse judge JSON: {ex.Message}");
            }
            throw new InvalidOperationException($"Failed to parse judge response as JSON: {ex.Message}\nRaw:\n{content}");
        }
    }

    private static PromptGradeResult ParsePromptGradeResponse(string content)
    {
        content = content.Trim();
        if (content.StartsWith("```"))
        {
            var firstNewline = content.IndexOf('\n');
            if (firstNewline >= 0) content = content[(firstNewline + 1)..];
        }
        if (content.EndsWith("```"))
        {
            content = content[..^3];
        }
        content = content.Trim();

        try
        {
            return JsonSerializer.Deserialize<PromptGradeResult>(content, JsonOptions)
                ?? throw new InvalidOperationException("Deserialized to null");
        }
        catch (JsonException ex)
        {
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                var jsonSlice = content[start..(end + 1)];
                return JsonSerializer.Deserialize<PromptGradeResult>(jsonSlice, JsonOptions)
                    ?? throw new InvalidOperationException($"Failed to parse prompt grade JSON: {ex.Message}");
            }
            throw new InvalidOperationException($"Failed to parse prompt grade response as JSON: {ex.Message}\nRaw:\n{content}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            try { await _client.StopAsync(); }
            catch { await _client.ForceStopAsync(); }
        }
        _client.Dispose();
    }
}
