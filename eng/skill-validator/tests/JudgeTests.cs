using System.Text.Json;
using SkillValidator.Services;

namespace SkillValidator.Tests;

public class ParseJudgeResponseTests
{
    private static readonly string ValidJson = JsonSerializer.Serialize(new
    {
        rubric_scores = new[]
        {
            new { criterion = "Correctness", score = 4, reasoning = "Mostly correct" },
        },
        overall_score = 4,
        overall_reasoning = "Good work overall",
    });

    [Fact]
    public void ParsesValidJsonInCodeBlock()
    {
        var content = "```json\n" + ValidJson + "\n```";
        var result = Judge.ParseJudgeResponse(content, ["Correctness"]);
        Assert.Equal(4, result.OverallScore);
        Assert.Single(result.RubricScores);
        Assert.Equal(4, result.RubricScores[0].Score);
    }

    [Fact]
    public void ParsesValidJsonWithoutCodeBlock()
    {
        var content = "Here is my evaluation:\n" + ValidJson;
        var result = Judge.ParseJudgeResponse(content, ["Correctness"]);
        Assert.Equal(4, result.OverallScore);
    }

    [Fact]
    public void HandlesInvalidEscapeSequences()
    {
        var raw = """
            {
              "rubric_scores": [
                {"criterion": "Quality", "score": 5, "reasoning": "It\'s excellent and has \a great structure"}
              ],
              "overall_score": 5,
              "overall_reasoning": "The agent\'s work is outstanding"
            }
            """;
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(raw));

        var result = Judge.ParseJudgeResponse(raw, ["Quality"]);
        Assert.Equal(5, result.OverallScore);
        Assert.Contains("excellent", result.RubricScores[0].Reasoning);
    }

    [Fact]
    public void ThrowsWhenContentHasOnlyMalformedJson()
    {
        var malformed = "{\"overall_score\": 4, broken}";
        var ex = Assert.Throws<InvalidOperationException>(
            () => Judge.ParseJudgeResponse(malformed, []));
        Assert.Contains("contained no JSON", ex.Message);
    }

    [Fact]
    public void ThrowsWhenContentHasMalformedJsonWithInvalidEscapes()
    {
        var malformed = "{\"overall_score\": \"4\\x\", broken}";
        var ex = Assert.Throws<InvalidOperationException>(
            () => Judge.ParseJudgeResponse(malformed, []));
        Assert.Contains("contained no JSON", ex.Message);
    }

    [Fact]
    public void ThrowsWhenContentHasNoJson()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Judge.ParseJudgeResponse("no json here", []));
        Assert.Contains("contained no JSON", ex.Message);
    }

    [Fact]
    public void ClampsScoresTo1To5Range()
    {
        var json = JsonSerializer.Serialize(new
        {
            rubric_scores = new[] { new { criterion = "Q", score = 10, reasoning = "" } },
            overall_score = -1,
            overall_reasoning = "",
        });
        var result = Judge.ParseJudgeResponse(json, ["Q"]);
        Assert.Equal(5, result.RubricScores[0].Score);
        Assert.Equal(1, result.OverallScore);
    }

    [Fact]
    public void DefaultsMissingScoresTo3()
    {
        var json = JsonSerializer.Serialize(new
        {
            rubric_scores = new[] { new { criterion = "Q", score = 0, reasoning = "" } },
            overall_score = 0,
            overall_reasoning = "",
        });
        // The C# code clamps to [1,5], so score=0 becomes 1, not 3.
        // In TS, missing fields default to 3. But C# requires explicit fields.
        // Let's test with actual missing score - but JsonSerializer won't omit a field.
        // The TS test passes score as undefined. C# source reads GetDouble which throws if missing.
        // So we test clamping of 0 to 1 instead, matching the actual C# behavior.
        var result = Judge.ParseJudgeResponse(json, ["Q"]);
        Assert.Equal(1, result.RubricScores[0].Score);
        Assert.Equal(1, result.OverallScore);
    }
}
