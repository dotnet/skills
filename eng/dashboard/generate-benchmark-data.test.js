const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

test('activation evidence separates implementation stops after activation', () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'activation-stops-'));
  const script = path.join(__dirname, 'generate-benchmark-data.ps1');
  const results = {
    model: 'executor',
    verdicts: [{
      skillName: 'demo-skill',
      skillPath: 'plugins/demo/skills/demo-skill',
      passed: false,
      reason: 'test',
      scenarios: [{
        scenarioName: 'implement-feature',
        expectActivation: true,
        stimulusWork: { kind: 'implementation' },
        skillActivationIsolated: {
          activated: true,
          detectedSkills: ['demo-skill'],
          failedActivationOnlyCompletions: 3,
          implementationActivationOnlyFailures: 2,
        },
        skillActivationPlugin: {
          activated: true,
          detectedSkills: ['demo-skill'],
          failedActivationOnlyCompletions: 1,
          implementationActivationOnlyFailures: 0,
        },
      }, {
        scenarioName: 'legacy-scenario',
        expectActivation: true,
        skillActivationIsolated: {
          activated: true,
          detectedSkills: ['demo-skill'],
          failedActivationOnlyCompletions: 1,
        },
      }],
    }],
  };

  try {
    const resultsFile = path.join(tempDir, 'results.json');
    fs.writeFileSync(resultsFile, `${JSON.stringify(results, null, 2)}\n`);
    const result = spawnSync('pwsh', [
      '-NoProfile',
      '-NonInteractive',
      '-File',
      script,
      '-ResultsFile',
      resultsFile,
      '-PluginName',
      'demo',
      '-OutputDir',
      tempDir,
      '-SkipTokenUsage',
    ], {
      encoding: 'utf8',
    });

    assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`);
    const data = JSON.parse(fs.readFileSync(path.join(tempDir, 'demo.json'), 'utf8'));
    const [scenario, legacy] = data.entries.Quality[0].verdictEvidence[0].activationScenarios;

    assert.equal(scenario.workKind, 'implementation');
    assert.equal(scenario.isolatedActivationOnlyFailedRuns, 3);
    assert.equal(scenario.isolatedImplementationStopRuns, 2);
    assert.equal(scenario.pluginActivationOnlyFailedRuns, 1);
    assert.equal(scenario.pluginImplementationStopRuns, 0);
    assert.equal(legacy.workKind, null);
    assert.equal(legacy.isolatedActivationOnlyFailedRuns, 1);
    assert.equal(legacy.isolatedImplementationStopRuns, 0);
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});

test('skill value index backfills retained native outcome evidence', () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'skill-value-index-'));
  const script = path.join(__dirname, 'generate-benchmark-data.ps1');
  const date = Date.now();
  const pluginData = {
    entries: {
      Quality: [{
        commit: { id: 'commit-a' },
        date,
        executionId: 'run-1',
        model: 'executor-a',
        verdictEvidence: [
          {
            skillName: 'regressed-agent',
            stateReason: { code: 'native_completion_regression' },
          },
          {
            skillName: 'clean-agent',
            stateReason: { code: 'none' },
          },
          {
            skillName: 'activation-failed-agent',
            stateReason: { code: 'target_agent_not_activated' },
          },
        ],
      }, {
        commit: { id: 'commit-a' },
        date,
        executionId: 'run-1',
        model: 'executor-b',
        verdictEvidence: [
          {
            skillName: 'clean-agent',
            stateReason: { code: 'none' },
          },
          {
            skillName: 'judge-specific-regression',
            stateReason: { code: 'none' },
          },
        ],
      }],
      SkillValue: [{
        commit: { id: 'commit-a' },
        date,
        executionId: 'run-1',
        model: 'executor-a',
        judgeModel: 'judge',
        skills: [
          { skill: 'regressed-agent' },
          { skill: 'activation-failed-agent' },
          { skill: 'unmatched-legacy-agent' },
        ],
      }, {
        commit: { id: 'commit-a' },
        date,
        executionId: 'run-1',
        model: 'executor-b',
        judgeModel: 'judge',
        skills: [
          { skill: 'clean-agent' },
          {
            skill: 'judge-specific-regression',
            completionRegressed: true,
            targetActivationFailed: false,
          },
        ],
      }],
    },
  };

  try {
    fs.writeFileSync(
      path.join(tempDir, 'dotnet-test.json'),
      `${JSON.stringify(pluginData, null, 2)}\n`,
    );
    const result = spawnSync('pwsh', [
      '-NoProfile',
      '-File',
      script,
      '-PurgeStaleFiles',
      '-DataDir',
      tempDir,
      '-RetentionDays',
      '30',
    ], {
      encoding: 'utf8',
    });

    assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`);
    const index = JSON.parse(
      fs.readFileSync(path.join(tempDir, 'skill-value.json'), 'utf8'),
    );
    const entries = Object.fromEntries(
      index.entries.map(entry => [entry.model, entry]),
    );
    const executorASkills = Object.fromEntries(
      entries['executor-a'].skills.map(skill => [skill.skill, skill]),
    );
    const executorBSkills = Object.fromEntries(
      entries['executor-b'].skills.map(skill => [skill.skill, skill]),
    );

    assert.equal(executorASkills['regressed-agent'].completionRegressed, true);
    assert.equal(executorASkills['regressed-agent'].targetActivationFailed, false);
    assert.equal(executorASkills['activation-failed-agent'].completionRegressed, false);
    assert.equal(executorASkills['activation-failed-agent'].targetActivationFailed, true);
    assert.equal(executorBSkills['clean-agent'].completionRegressed, false);
    assert.equal(executorBSkills['clean-agent'].targetActivationFailed, false);
    assert.equal(executorBSkills['judge-specific-regression'].completionRegressed, true);
    assert.equal(executorBSkills['judge-specific-regression'].targetActivationFailed, false);
    assert.equal(
      Object.hasOwn(executorASkills['unmatched-legacy-agent'], 'completionRegressed'),
      false,
    );
    assert.equal(
      Object.hasOwn(executorASkills['unmatched-legacy-agent'], 'targetActivationFailed'),
      false,
    );
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});
