import test from "node:test";
import assert from "node:assert/strict";
import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const script = join(dirname(fileURLToPath(import.meta.url)), "consolidate.mjs");

function render(verdicts, options = {}) {
  const documents = options.documents ?? [
    {
      model: "test-model",
      judgeModel: "test-judge",
      verdicts,
    },
  ];
  const root = mkdtempSync(join(tmpdir(), "vally-consolidate-"));
  try {
    documents.forEach((document, index) => {
      const directory = join(root, `result-${index}`);
      mkdirSync(directory, { recursive: true });
      writeFileSync(join(directory, "results.json"), JSON.stringify(document));
    });
    (options.summaries ?? []).forEach((summary, index) => {
      const directory = join(root, `summary-${index}`);
      mkdirSync(directory, { recursive: true });
      writeFileSync(join(directory, "adapter-summary.json"), JSON.stringify(summary));
    });
    const output = join(root, "summary.md");
    const args = [
      script,
      "--format",
      options.format ?? "simple",
      "--output",
      output,
      "--root",
      root,
    ];
    if (options.commit) args.push("--commit", options.commit);
    const result = spawnSync(process.execPath, args, { encoding: "utf8" });
    assert.equal(result.status, 0, result.stderr);
    return readFileSync(output, "utf8");
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

test("separates preference loss from the disabled objective regression gate", () => {
  const markdown = render([
    {
      skillName: "preference-loss",
      state: "VALID_NO_CHANGE",
      stateReason: { code: "preference_regression_report_only" },
      preferenceRegressed: true,
      regressed: true,
      reason: "credible preference loss",
      recoveredErrors: [{ code: "judge_session_idle_timeout" }],
      scenarioEvidence: {
        gateEligible: true,
        count: 1,
        wins: 0,
        ties: 0,
        losses: 1,
      },
      comparisonTrialEvidence: {
        gateEligible: false,
        count: 3,
        wins: 0,
        ties: 1,
        losses: 2,
      },
      scenarios: [],
    },
    {
      skillName: "judge-failure",
      state: "INVALID_INCONCLUSIVE",
      stateReason: { code: "comparison_errors" },
      conclusive: false,
      underpowered: false,
      reason: "comparison failed",
      errors: [
        { code: "judge_organization_disabled" },
        { code: "judge_organization_disabled" },
      ],
      scenarios: [],
    },
  ]);

  assert.match(markdown, /Objective completion gate:\*\* not enabled/);
  assert.doesNotMatch(markdown, /0 objective regressions/);
  assert.match(markdown, /\*\*1 preference losses \(report only\)\*\*/);
  assert.match(markdown, /<code>VALID_NO_CHANGE<\/code> \(<code>preference_regression_report_only<\/code>\)/);
  assert.match(markdown, /<code>judge_organization_disabled<\/code>=2/);
  assert.match(markdown, /successful first-attempt judgments stayed fixed/);
  assert.match(markdown, /Repeated-run reliability \(not used by the gate\):\*\* 3 paired runs/);
});

test("renders legacy preference regressions as report-only", () => {
  const markdown = render([
    {
      skillName: "legacy-regression",
      regressed: true,
      passed: false,
      conclusive: true,
      reason: "legacy credible loss",
      scenarios: [],
    },
  ]);

  assert.match(markdown, /\*\*1 preference losses \(report only\)\*\*/);
  assert.match(markdown, /<code>VALID_NO_CHANGE<\/code>/);
});

test("keeps routine passing details out of the PR comment but in Full Results", () => {
  const verdict = {
    skillName: "passing-skill",
    state: "VALID_PASS",
    passed: true,
    conclusive: true,
    reason: "credible preference improvement",
    scenarios: [],
  };

  const compact = render([verdict]);
  assert.doesNotMatch(compact, /passing-skill \(test-model\)<\/summary>/);
  assert.match(compact, /Routine passing details for 1 result are in Full Results/);

  const full = render([verdict], { format: "full" });
  assert.match(full, /passing-skill \(test-model\)<\/summary>/);
  assert.match(full, /<code>VALID_PASS<\/code>/);
});

test("uses target-agent activation evidence for agent verdicts", () => {
  const markdown = render([{
    skillName: "agent.router",
    skillKind: "agent",
    state: "VALID_PASS",
    passed: true,
    conclusive: true,
    reason: "credible preference improvement",
    scenarios: [{
      scenarioName: "route request",
      expectActivation: true,
      agentActivationIsolated: { activated: true },
      agentActivationPlugin: { activated: false },
    }],
  }], { format: "full" });

  assert.match(markdown, /Activation: isolated 1\/1; plugin 0\/1/);
  assert.doesNotMatch(markdown, /Activation: isolated 0\/1/);
});

test("selects weak scenarios using only target-agent activation", () => {
  const markdown = render([{
    skillName: "agent.router",
    skillKind: "agent",
    state: "VALID_PASS",
    passed: true,
    conclusive: true,
    reason: "credible preference improvement",
    scenarios: [
      {
        scenarioName: "missing target agent",
        expectActivation: true,
        netWin: 1,
        skillActivationIsolated: { activated: true },
        agentActivationIsolated: { activated: false },
      },
      {
        scenarioName: "correctly dormant target agent",
        expectActivation: false,
        netWin: 1,
        skillActivationIsolated: { activated: true },
        agentActivationIsolated: { activated: false },
      },
    ],
  }]);

  assert.match(markdown, /\*\*Weak or warning scenarios:\*\*/);
  assert.match(markdown, /missing target agent/);
  assert.doesNotMatch(markdown, /correctly dormant target agent/);
});

test("preserves execution model identity and aggregates measurement health", () => {
  const verdict = {
    skillName: "same-skill",
    state: "VALID_PASS",
    passed: true,
    conclusive: true,
    scenarios: [],
  };
  const markdown = render([], {
    documents: [
      { model: "model-a", judgeModel: "judge-a", verdicts: [verdict] },
      { model: "model-b", judgeModel: "judge-a", verdicts: [verdict] },
    ],
    summaries: [
      {
        expectedEvalCount: 1,
        observedEvalCount: 1,
        writtenResultCount: 1,
        missingEvalCount: 0,
        unexpectedEvalCount: 0,
        invalidEvalCount: 0,
      },
      {
        expectedEvalCount: 1,
        observedEvalCount: 1,
        writtenResultCount: 1,
        missingEvalCount: 0,
        unexpectedEvalCount: 0,
        invalidEvalCount: 0,
      },
    ],
    commit: "abc123",
  });

  assert.match(markdown, /2 model\/target results across 1 target and 2 models/);
  assert.match(markdown, /\| same-skill \| model-a \| ✅ Improved \|/);
  assert.match(markdown, /\| same-skill \| model-b \| ✅ Improved \|/);
  assert.match(markdown, /evaluated commit `abc123`; judge `judge-a`/);
  assert.match(markdown, /2 expected \/ 2 observed \/ 2 written/);
  assert.match(markdown, /0 missing, 0 unexpected, 0 invalid/);
});

test("escapes table identity cells exactly once", () => {
  const markdown = render([
    {
      skillName: "skill<T> & name|next\nline",
      model: "model<U> & name|next\nline",
      state: "VALID_PASS",
      passed: true,
      conclusive: true,
      scenarios: [],
    },
  ]);

  assert.ok(
    markdown.includes(
      "| skill&lt;T&gt; &amp; name\\|next line | model&lt;U&gt; &amp; name\\|next line |",
    ),
  );
  assert.equal(markdown.includes("skill<T>"), false);
  assert.equal(markdown.includes("model<U>"), false);
  assert.equal(markdown.includes("&amp;lt;"), false);
  assert.equal(markdown.includes("&amp;amp;"), false);
  assert.equal(markdown.includes("name\\\\|next"), false);
});

test("escapes scenario names in markdown tables exactly once", () => {
  const markdown = render(
    [
      {
        skillName: "scenario-escaping",
        state: "VALID_NO_CHANGE",
        passed: false,
        conclusive: true,
        reason: "not proven improved",
        scenarios: [
          {
            scenarioName: "List<T> & map|next\nline",
            netWin: 0,
            meanScore: 0,
            wins: 0,
            ties: 1,
            losses: 0,
          },
        ],
      },
    ],
    { format: "full" },
  );

  assert.ok(markdown.includes("| = List&lt;T&gt; &amp; map\\|next line |"));
  assert.equal(markdown.includes("List<T>"), false);
  assert.equal(markdown.includes("&amp;lt;"), false);
  assert.equal(markdown.includes("&amp;amp;"), false);
});

test("keeps Overfit visible and gives actionable evidence for a non-pass", () => {
  const markdown = render([
    {
      skillName: "tie-sensitive",
      state: "VALID_NO_CHANGE",
      stateReason: { code: "insufficient_discordant_stimulus_votes" },
      reason: "not credible",
      netWin: 0.4,
      signTest: {
        wins: 4,
        ties: 1,
        losses: 0,
        discordant: 4,
        pValue: 0.0625,
      },
      stimulusVoteCount: 5,
      overfittingResult: { severity: "Moderate", score: 0.51 },
      scenarios: [
        {
          scenarioName: "tied-case",
          expectActivation: true,
          skillActivationIsolated: { activated: false },
          netWin: 0,
          wins: 0,
          ties: 1,
          losses: 0,
          trials: [{ evidence: "tie evidence <unsafe>", score: "tie" }],
        },
      ],
    },
  ]);

  assert.match(markdown, /\| Target \| Model \| Verdict \| Gate evidence \| Overfit \| Warnings \| Next action \|/);
  assert.match(markdown, /n=5; 4W\/1T\/0L; d=4; p=0.063; net \+40.0%/);
  assert.match(markdown, /🟡 0.51/);
  assert.match(markdown, /Activation: isolated 0\/1/);
  assert.match(markdown, /predeclare added breadth before a new experiment/);
  assert.match(markdown, /\| = tied-case \|/);
  assert.match(markdown, /tie evidence &lt;unsafe&gt;/);
});

test("prefers eligible judge evidence over a correctly dormant tie", () => {
  const markdown = render([
    {
      skillName: "evidence-order",
      state: "VALID_NO_CHANGE",
      stateReason: { code: "insufficient_discordant_stimulus_votes" },
      reason: "not credible",
      netWin: 0,
      signTest: {
        wins: 0,
        ties: 2,
        losses: 0,
        discordant: 0,
        pValue: 1,
      },
      stimulusVoteCount: 1,
      activationContract: {
        unmatchedDormancyStimuli: ["renamed-dormancy-scenario"],
      },
      scenarios: [
        {
          scenarioName: "a-dormant",
          expectActivation: false,
          preferenceGateEligible: false,
          skillActivationIsolated: { activated: false },
          netWin: 0,
          trials: [{ evidence: "excluded dormant evidence", score: "tie" }],
        },
        {
          scenarioName: "z-eligible",
          expectActivation: true,
          preferenceGateEligible: true,
          skillActivationIsolated: { activated: true },
          netWin: 0,
          trials: [{ evidence: "eligible tied evidence", score: "tie" }],
        },
      ],
    },
  ]);

  assert.match(markdown, /eligible tied evidence/);
  assert.doesNotMatch(markdown, /excluded dormant evidence/);
  assert.match(markdown, /1 dormancy annotation unmatched/);
});

test("surfaces failed activation-only completions before generic content advice", () => {
  const markdown = render([
    {
      skillName: "premature-stop",
      state: "VALID_NO_CHANGE",
      stateReason: { code: "no_credible_preference_change" },
      passed: false,
      conclusive: true,
      reason: "not proven improved",
      signTest: {
        wins: 1,
        ties: 3,
        losses: 1,
        discordant: 2,
        pValue: 0.75,
      },
      stimulusVoteCount: 5,
      scenarios: [
        {
          scenarioName: "implementation task",
          expectActivation: true,
          skillActivationIsolated: {
            activated: true,
            failedActivationOnlyCompletions: 2,
          },
          skillActivationPlugin: {
            activated: true,
            failedActivationOnlyCompletions: 1,
          },
          netWin: 0,
          wins: 0,
          ties: 1,
          losses: 0,
          trials: [{ evidence: "The model stopped after loading the skill.", score: "tie" }],
        },
      ],
    },
  ]);

  assert.match(markdown, /Activation-only stop: isolated 2 failed runs/);
  assert.match(markdown, /Activation-only stop: plugin 1 failed run/);
  assert.match(markdown, /Inspect activation-only failed runs before rewriting skill content/);
  assert.match(markdown, /\| = implementation task \|/);
});

test("prioritizes activation-only diagnosis over preference-loss guidance", () => {
  const markdown = render([
    {
      skillName: "runtime-failure",
      state: "VALID_NO_CHANGE",
      stateReason: { code: "preference_regression_report_only" },
      preferenceRegressed: true,
      passed: false,
      conclusive: true,
      reason: "credible preference loss",
      scenarios: [
        {
          scenarioName: "implementation task",
          expectActivation: true,
          skillActivationIsolated: {
            activated: true,
            failedActivationOnlyCompletions: 1,
          },
          netWin: -1,
          wins: 0,
          ties: 0,
          losses: 1,
          trials: [],
        },
      ],
    },
  ]);

  assert.match(markdown, /Inspect activation-only failed runs before rewriting skill content/);
  assert.doesNotMatch(markdown, /Inspect losing stimuli and fix skill behavior/);
});

test("counts plugin activation-only failures on target-dormancy scenarios", () => {
  const markdown = render([
    {
      skillName: "plugin-runtime-failure",
      state: "VALID_PASS",
      passed: true,
      conclusive: true,
      reason: "credible preference improvement",
      scenarios: [
        {
          scenarioName: "target should stay dormant",
          expectActivation: false,
          skillActivationIsolated: { activated: false },
          skillActivationPlugin: {
            activated: true,
            failedActivationOnlyCompletions: 1,
          },
          preferenceGateEligible: false,
          netWin: 0,
          wins: 0,
          ties: 1,
          losses: 0,
          trials: [],
        },
      ],
    },
  ]);

  assert.match(markdown, /Activation-only stop: plugin 1 failed run/);
  assert.match(markdown, /Inspect activation-only failed runs before rewriting skill content/);
});

test("does not warn when an activation-only completion passes its graders", () => {
  const markdown = render([
    {
      skillName: "advice-skill",
      state: "VALID_PASS",
      passed: true,
      conclusive: true,
      reason: "credible preference improvement",
      signTest: {
        wins: 5,
        ties: 0,
        losses: 0,
        discordant: 5,
        pValue: 0.03125,
      },
      stimulusVoteCount: 5,
      scenarios: [
        {
          scenarioName: "answer an advisory question",
          expectActivation: true,
          skillActivationIsolated: {
            activated: true,
            activationOnlyCompletions: 1,
            failedActivationOnlyCompletions: 0,
          },
          netWin: 1,
          wins: 1,
          ties: 0,
          losses: 0,
          trials: [],
        },
      ],
    },
  ]);

  assert.doesNotMatch(markdown, /Activation-only stop/);
  assert.match(markdown, /\| advice-skill \| test-model \| ✅ Improved \|/);
});

test("reports activation-contract failures ahead of underpowered preference evidence", () => {
  const markdown = render([
    {
      skillName: "over-eager-skill",
      state: "VALID_NO_CHANGE",
      stateReason: { code: "activation_contract_failed", phase: "activation" },
      underpowered: true,
      passed: false,
      conclusive: true,
      reason: "preference improved but dormancy failed",
      netWin: 1,
      signTest: {
        wins: 5,
        ties: 0,
        losses: 0,
        discordant: 5,
        pValue: 0.03125,
      },
      stimulusVoteCount: 5,
      excludedScenarioEvidence: {
        gateEligible: false,
        count: 1,
        wins: 0,
        ties: 1,
        losses: 0,
      },
      activationContract: {
        evaluated: true,
        passed: false,
        violated: 1,
      },
      scenarios: [
        {
          scenarioName: "off-target request",
          expectActivation: false,
          preferenceGateEligible: false,
          skillActivationIsolated: { activated: true },
          netWin: 0,
          wins: 0,
          ties: 1,
          losses: 0,
          trials: [],
        },
      ],
    },
  ]);

  assert.match(markdown, /⛔ \*\*1 activation contract failure\*\*/);
  assert.match(markdown, /⛔ Activation contract failed/);
  assert.match(markdown, /1 dormancy excluded/);
  assert.match(markdown, /Dormancy contract: 1 unexpected activation/);
  assert.match(markdown, /Excluded \(activation contract\)/);
  assert.match(markdown, /Narrow skill routing so the listed off-target scenarios stay dormant/);
});

test("explains that repeated runs cannot repair an underpowered eval", () => {
  const markdown = render([
    {
      skillName: "too-small",
      state: "INVALID_INCONCLUSIVE",
      underpowered: true,
      reason: "fewer than five distinct stimuli",
      stimulusVoteCount: 4,
      signTest: {
        wins: 4,
        ties: 0,
        losses: 0,
        discordant: 4,
        pValue: 0.0625,
      },
      scenarios: [],
    },
  ]);

  assert.match(markdown, /⚠️ Underpowered/);
  assert.match(markdown, /Predeclare more independent, discriminating stimuli; repeated runs do not add power/);
});
