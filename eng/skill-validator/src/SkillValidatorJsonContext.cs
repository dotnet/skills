using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SkillValidator.Models;

namespace SkillValidator;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ResultsOutput))]
[JsonSerializable(typeof(ConsolidateData))]
[JsonSerializable(typeof(SkillVerdict))]
[JsonSerializable(typeof(ScenarioComparison))]
[JsonSerializable(typeof(RunResult))]
[JsonSerializable(typeof(RunMetrics))]
[JsonSerializable(typeof(JudgeResult))]
[JsonSerializable(typeof(RubricScore))]
[JsonSerializable(typeof(AssertionResult))]
[JsonSerializable(typeof(Assertion))]
[JsonSerializable(typeof(AgentEvent))]
[JsonSerializable(typeof(MetricBreakdown))]
[JsonSerializable(typeof(ConfidenceInterval))]
[JsonSerializable(typeof(PairwiseJudgeResult))]
[JsonSerializable(typeof(PairwiseRubricResult))]
[JsonSerializable(typeof(SkillActivationInfo))]
[JsonSerializable(typeof(OverfittingResult))]
[JsonSerializable(typeof(RubricOverfitAssessment))]
[JsonSerializable(typeof(AssertionOverfitAssessment))]
[JsonSerializable(typeof(OverfittingSeverity))]
[JsonSerializable(typeof(NoiseScenarioResult))]
[JsonSerializable(typeof(NoiseTestResult))]
[JsonSerializable(typeof(PairwiseMagnitude))]
[JsonSerializable(typeof(AssertionType))]
[JsonSerializable(typeof(MCPServerDef))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(Dictionary<string, JsonNode?>))]
[JsonSerializable(typeof(List<SkillVerdict>))]
[JsonSerializable(typeof(IReadOnlyList<SkillVerdict>))]
internal partial class SkillValidatorJsonContext : JsonSerializerContext;
