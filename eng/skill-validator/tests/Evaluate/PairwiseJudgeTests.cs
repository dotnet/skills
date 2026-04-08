using System.Text.Json;
using SkillValidator.Evaluate;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

public class PairwiseToQualityScoreTests
{
    private static PairwiseJudgeResult MakePairwiseResult(
        string overallWinner = "skill",
        PairwiseMagnitude overallMagnitude = PairwiseMagnitude.SlightlyBetter,
        string overallReasoning = "Skill is slightly better overall",
        bool positionSwapConsistent = true,
        IReadOnlyList<PairwiseRubricResult>? rubricResults = null)
    {
        rubricResults ??= [new PairwiseRubricResult("Quality", "skill", PairwiseMagnitude.SlightlyBetter, "Better quality")];
        return new PairwiseJudgeResult(rubricResults, overallWinner, overallMagnitude, overallReasoning, positionSwapConsistent);
    }

    [Fact]
    public void ReturnsPositiveScoresWhenSkillWins()
    {
        var result = MakePairwiseResult(
            overallWinner: "skill",
            overallMagnitude: PairwiseMagnitude.MuchBetter,
            rubricResults: [new PairwiseRubricResult("Q", "skill", PairwiseMagnitude.MuchBetter, "")]);
        var scores = PairwiseJudge.PairwiseToQualityScore(result);
        Assert.Equal(1.0, scores.OverallImprovement);
        Assert.Equal(1.0, scores.QualityImprovement);
    }

    [Fact]
    public void ReturnsNegativeScoresWhenBaselineWins()
    {
        var result = MakePairwiseResult(
            overallWinner: "baseline",
            overallMagnitude: PairwiseMagnitude.SlightlyBetter,
            rubricResults: [new PairwiseRubricResult("Q", "baseline", PairwiseMagnitude.SlightlyBetter, "")]);
        var scores = PairwiseJudge.PairwiseToQualityScore(result);
        Assert.Equal(-0.4, scores.OverallImprovement);
        Assert.Equal(-0.4, scores.QualityImprovement);
    }

    [Fact]
    public void ReturnsZeroForTie()
    {
        var result = MakePairwiseResult(
            overallWinner: "tie",
            overallMagnitude: PairwiseMagnitude.Equal,
            rubricResults: [new PairwiseRubricResult("Q", "tie", PairwiseMagnitude.Equal, "")]);
        var scores = PairwiseJudge.PairwiseToQualityScore(result);
        Assert.Equal(0, scores.OverallImprovement);
        Assert.Equal(0, scores.QualityImprovement);
    }

    [Fact]
    public void AveragesRubricScoresCorrectly()
    {
        var result = MakePairwiseResult(
            overallWinner: "skill",
            overallMagnitude: PairwiseMagnitude.SlightlyBetter,
            rubricResults:
            [
                new PairwiseRubricResult("A", "skill", PairwiseMagnitude.MuchBetter, ""),
                new PairwiseRubricResult("B", "tie", PairwiseMagnitude.Equal, ""),
                new PairwiseRubricResult("C", "baseline", PairwiseMagnitude.SlightlyBetter, ""),
            ]);
        var scores = PairwiseJudge.PairwiseToQualityScore(result);
        // (1.0 + 0 + -0.4) / 3 = 0.2
        Assert.Equal(0.2, scores.QualityImprovement, 5);
    }

    [Fact]
    public void HandlesEmptyRubricResults()
    {
        var result = MakePairwiseResult(rubricResults: []);
        var scores = PairwiseJudge.PairwiseToQualityScore(result);
        Assert.Equal(0, scores.QualityImprovement);
    }

    [Fact]
    public void MapsAllMagnitudesCorrectlyForSkillWinner()
    {
        var magnitudes = new[]
        {
            PairwiseMagnitude.MuchBetter,
            PairwiseMagnitude.SlightlyBetter,
            PairwiseMagnitude.Equal,
            PairwiseMagnitude.SlightlyWorse,
            PairwiseMagnitude.MuchWorse,
        };
        var expected = new[] { 1.0, 0.4, 0, 0.4, 1.0 };

        for (int i = 0; i < magnitudes.Length; i++)
        {
            var result = MakePairwiseResult(
                overallWinner: "skill",
                overallMagnitude: magnitudes[i]);
            var scores = PairwiseJudge.PairwiseToQualityScore(result);
            // When winner is "skill", score = Math.Abs(magnitude_score)
            Assert.Equal(expected[i], scores.OverallImprovement);
        }
    }
}

public class PairwisePositionSwapConsistencyTests
{
    [Fact]
    public void ConsistentResultPreservesWinner()
    {
        var result = new PairwiseJudgeResult(
            [new PairwiseRubricResult("Quality", "skill", PairwiseMagnitude.SlightlyBetter, "Better quality")],
            "skill", PairwiseMagnitude.SlightlyBetter, "Skill is slightly better overall", true);
        Assert.True(result.PositionSwapConsistent);
        Assert.Equal("skill", result.OverallWinner);
    }

    [Fact]
    public void InconsistentResultCanBeDetected()
    {
        var result = new PairwiseJudgeResult(
            [new PairwiseRubricResult("Quality", "skill", PairwiseMagnitude.SlightlyBetter, "Better quality")],
            "tie", PairwiseMagnitude.Equal, "Position-swap inconsistent", false);
        Assert.False(result.PositionSwapConsistent);
        Assert.Equal("tie", result.OverallWinner);
    }
}

public class ParsePairwiseResponseTests
{
    private static readonly string ValidJson = JsonSerializer.Serialize(new
    {
        rubric_results = new[]
        {
            new { criterion = "Quality", winner = "A", magnitude = "slightly-better", reasoning = "Good" },
        },
        overall_winner = "A",
        overall_magnitude = "slightly-better",
        overall_reasoning = "A is better",
    });

    [Fact]
    public void ParsesValidJsonInCodeBlock()
    {
        var content = "```json\n" + ValidJson + "\n```";
        var result = PairwiseJudge.ParsePairwiseResponse(content, ["Quality"], "forward");
        // In forward: A=baseline, B=skill. A winning means baseline wins.
        Assert.Equal("baseline", result.OverallWinner);
        Assert.Single(result.RubricResults);
    }

    [Fact]
    public void ParsesValidJsonWithoutCodeBlock()
    {
        var content = "Here is my evaluation:\n" + ValidJson;
        var result = PairwiseJudge.ParsePairwiseResponse(content, ["Quality"], "forward");
        Assert.Equal("baseline", result.OverallWinner);
    }

    [Fact]
    public void HandlesInvalidEscapeSequences()
    {
        var raw = """
            {
              "rubric_results": [
                {"criterion": "Quality", "winner": "B", "magnitude": "slightly-better", "reasoning": "It\'s much better and has \a good structure"}
              ],
              "overall_winner": "B",
              "overall_magnitude": "slightly-better",
              "overall_reasoning": "Response B\'s approach is cleaner"
            }
            """;
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(raw));

        var result = PairwiseJudge.ParsePairwiseResponse(raw, ["Quality"], "forward");
        // In forward: B=skill
        Assert.Equal("skill", result.OverallWinner);
        Assert.Contains("much better", result.RubricResults[0].Reasoning);
    }

    [Fact]
    public void ThrowsWhenContentHasOnlyMalformedJson()
    {
        var malformed = "{\"overall_winner\": \"A\", broken}";
        var ex = Assert.Throws<InvalidOperationException>(
            () => PairwiseJudge.ParsePairwiseResponse(malformed, [], "forward"));
        Assert.Contains("contained no JSON", ex.Message);
    }

    [Fact]
    public void ThrowsWhenContentHasMalformedJsonWithInvalidEscapes()
    {
        var malformed = "{\"overall_winner\": \"A\\x\", broken}";
        var ex = Assert.Throws<InvalidOperationException>(
            () => PairwiseJudge.ParsePairwiseResponse(malformed, [], "forward"));
        Assert.Contains("contained no JSON", ex.Message);
    }

    [Fact]
    public void ThrowsWhenContentHasNoJson()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => PairwiseJudge.ParsePairwiseResponse("no json here", [], "forward"));
        Assert.Contains("contained no JSON", ex.Message);
    }

    [Fact]
    public void ReversesWinnersInReverseDirection()
    {
        var result = PairwiseJudge.ParsePairwiseResponse(ValidJson, ["Quality"], "reverse");
        // In reverse: A=skill, B=baseline. A winning means skill wins.
        Assert.Equal("skill", result.OverallWinner);
        Assert.Equal("skill", result.RubricResults[0].Winner);
    }
}
