using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkillEvaluator;

// ── Rubric schema ──────────────────────────────────────────────────────

public sealed class RubricCriterion
{
    [JsonPropertyName("criterion")]
    public string Criterion { get; set; } = "";

    [JsonPropertyName("weight")]
    public double Weight { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}

public sealed class ScoringConfig
{
    [JsonPropertyName("per_criterion")]
    public string PerCriterion { get; set; } = "";

    [JsonPropertyName("pass_threshold")]
    public double PassThreshold { get; set; } = 0.75;

    [JsonPropertyName("formula")]
    public string Formula { get; set; } = "";
}

public sealed class Rubric
{
    [JsonPropertyName("rubric")]
    public List<RubricCriterion> Criteria { get; set; } = [];

    [JsonPropertyName("scoring")]
    public ScoringConfig Scoring { get; set; } = new();
}

// ── Judge response schema ──────────────────────────────────────────────

public sealed class CriterionScore
{
    [JsonPropertyName("criterion")]
    public string Criterion { get; set; } = "";

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("justification")]
    public string Justification { get; set; } = "";
}

public sealed class EvaluationResult
{
    [JsonPropertyName("scores")]
    public List<CriterionScore> Scores { get; set; } = [];

    [JsonPropertyName("weighted_score")]
    public double WeightedScore { get; set; }

    [JsonPropertyName("pass")]
    public bool Pass { get; set; }

    [JsonPropertyName("classification")]
    public string Classification { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";
}

// ── Prompt quality result ──────────────────────────────────────────────

public sealed class PromptGradeResult
{
    [JsonPropertyName("scores")]
    public List<CriterionScore> Scores { get; set; } = [];

    [JsonPropertyName("weighted_score")]
    public double WeightedScore { get; set; }

    [JsonPropertyName("pass")]
    public bool Pass { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";
}

// ── Batch result container ─────────────────────────────────────────────

public sealed class SkillResult
{
    public string SkillName { get; set; } = "";
    public EvaluationResult? Result { get; set; }
    public PromptGradeResult? PromptGrade { get; set; }
    public string? Error { get; set; }
}
