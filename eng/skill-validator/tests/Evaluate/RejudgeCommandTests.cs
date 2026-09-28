using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

[TestClass]
public class RejudgeCommandTests
{
    private static SessionRecord Rec(
        string id,
        string role,
        int runIndex,
        string? baselineKey,
        string skill = "skill",
        string scenario = "scn",
        string model = "model-x",
        string? metrics = "{}") =>
        new(
            Id: id,
            SkillName: skill,
            SkillPath: "/path/" + skill,
            ScenarioName: scenario,
            RunIndex: runIndex,
            Role: role,
            Model: model,
            ConfigDir: "cfg",
            WorkDir: "/work",
            Prompt: "prompt",
            SkillSha: "sha",
            RubricJson: null,
            Status: "completed",
            MetricsJson: metrics,
            JudgeJson: null,
            PairwiseJson: null,
            BaselineKey: baselineKey);

    [TestMethod]
    public void PairCrossDir_MatchesByBaselineKeyAndRunIndex()
    {
        var baseline = new[]
        {
            Rec("b0", "baseline", 0, "K1"),
            Rec("b1", "baseline", 1, "K1"),
        };
        var treatment = new[]
        {
            Rec("t0", "with-skill-isolated", 0, "K1"),
            Rec("t1", "with-skill-isolated", 1, "K1"),
        };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        Assert.IsEmpty(pairing.Unmatched);
        Assert.AreEqual(2, pairing.Pairs.Count);
        Assert.AreEqual("b0", pairing.Pairs.Single(p => p.RunIndex == 0).Baseline.Id);
        Assert.AreEqual("b1", pairing.Pairs.Single(p => p.RunIndex == 1).Baseline.Id);
        Assert.AreEqual("t0", pairing.Pairs.Single(p => p.RunIndex == 0).Isolated.Id);
    }

    [TestMethod]
    public void PairCrossDir_FallsBackToFirstBaseline_WhenRunIndexMissing()
    {
        var baseline = new[] { Rec("b0", "baseline", 0, "K1") };
        var treatment = new[] { Rec("t2", "with-skill-isolated", 2, "K1") };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        var pair = Assert.ContainsSingle(pairing.Pairs);
        Assert.AreEqual("b0", pair.Baseline.Id);
        Assert.AreEqual(2, pair.RunIndex);
    }

    [TestMethod]
    public void PairCrossDir_ReportsUnmatched_WhenNoBaselineKeyMatches()
    {
        var baseline = new[] { Rec("b0", "baseline", 0, "K1") };
        var treatment = new[] { Rec("t0", "with-skill-isolated", 0, "K2") };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        Assert.IsEmpty(pairing.Pairs);
        var unmatched = Assert.ContainsSingle(pairing.Unmatched);
        Assert.Contains("scn", unmatched);
    }

    [TestMethod]
    public void PairCrossDir_IncludesPluginRole()
    {
        var baseline = new[] { Rec("b0", "baseline", 0, "K1") };
        var treatment = new[]
        {
            Rec("iso", "with-skill-isolated", 0, "K1"),
            Rec("plug", "with-skill-plugin", 0, "K1"),
        };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        var pair = Assert.ContainsSingle(pairing.Pairs);
        Assert.AreEqual("iso", pair.Isolated.Id);
        Assert.IsNotNull(pair.Plugin);
        Assert.AreEqual("plug", pair.Plugin!.Id);
    }

    [TestMethod]
    public void PairCrossDir_SupportsAgentRolesAndReusedBaseline()
    {
        var baseline = new[] { Rec("b0", "baseline-reused", 0, "K1") };
        var treatment = new[] { Rec("a0", "with-agent-isolated", 0, "K1") };

        var pairing = RejudgeCommand.PairCrossDir(baseline, treatment);

        var pair = Assert.ContainsSingle(pairing.Pairs);
        Assert.AreEqual("b0", pair.Baseline.Id);
        Assert.AreEqual("a0", pair.Isolated.Id);
    }

    [TestMethod]
    public void ValidateCrossDirCompat_RejectsModelMismatch()
    {
        var (ok, effective, error) = RejudgeCommand.ValidateCrossDirCompat(
            baselineModel: "model-a", treatmentModel: "model-b",
            baselineJudgeModel: "judge", treatmentJudgeModel: "judge", explicitJudgeModel: null);

        Assert.IsFalse(ok);
        Assert.IsNull(effective);
        Assert.Contains("model-a", error!);
        Assert.Contains("model-b", error!);
    }

    [TestMethod]
    public void ValidateCrossDirCompat_RejectsJudgeModelMismatch()
    {
        var (ok, effective, error) = RejudgeCommand.ValidateCrossDirCompat(
            "model-x", "model-x", "judge-a", "judge-b", explicitJudgeModel: null);

        Assert.IsFalse(ok);
        Assert.IsNull(effective);
        Assert.Contains("judge-a", error!);
        Assert.Contains("judge-b", error!);
    }

    [TestMethod]
    public void ValidateCrossDirCompat_ExplicitJudgeOverridesMismatch()
    {
        var (ok, effective, error) = RejudgeCommand.ValidateCrossDirCompat(
            "model-x", "model-x", "judge-a", "judge-b", explicitJudgeModel: "judge-c");

        Assert.IsTrue(ok);
        Assert.AreEqual("judge-c", effective);
        Assert.IsNull(error);
    }

    [TestMethod]
    public void ValidateCrossDirCompat_PrefersTreatmentJudgeModel()
    {
        var (ok, effective, _) = RejudgeCommand.ValidateCrossDirCompat(
            "model-x", "model-x", baselineJudgeModel: null, treatmentJudgeModel: "judge-t", explicitJudgeModel: null);

        Assert.IsTrue(ok);
        Assert.AreEqual("judge-t", effective);
    }

    [TestMethod]
    public void ValidateCrossDirCompat_FallsBackToBaselineJudgeModel()
    {
        var (ok, effective, _) = RejudgeCommand.ValidateCrossDirCompat(
            "model-x", "model-x", baselineJudgeModel: "judge-b", treatmentJudgeModel: null, explicitJudgeModel: null);

        Assert.IsTrue(ok);
        Assert.AreEqual("judge-b", effective);
    }

    [TestMethod]
    public void ValidateCrossDirCompat_FailsWhenNoJudgeModelAvailable()
    {
        var (ok, effective, error) = RejudgeCommand.ValidateCrossDirCompat(
            "model-x", "model-x", baselineJudgeModel: null, treatmentJudgeModel: null, explicitJudgeModel: null);

        Assert.IsFalse(ok);
        Assert.IsNull(effective);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void ValidateCrossDirCompat_AcceptsMatchingJudgeModels()
    {
        var (ok, effective, error) = RejudgeCommand.ValidateCrossDirCompat(
            "model-x", "model-x", "judge", "judge", explicitJudgeModel: null);

        Assert.IsTrue(ok);
        Assert.AreEqual("judge", effective);
        Assert.IsNull(error);
    }
}
