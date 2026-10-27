const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

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
        model: 'executor',
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
      }],
      SkillValue: [{
        commit: { id: 'commit-a' },
        date,
        executionId: 'run-1',
        model: 'executor',
        judgeModel: 'judge',
        skills: [
          { skill: 'regressed-agent' },
          { skill: 'clean-agent' },
          { skill: 'activation-failed-agent' },
          { skill: 'unmatched-legacy-agent' },
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
    const skills = Object.fromEntries(
      index.entries[0].skills.map(skill => [skill.skill, skill]),
    );

    assert.equal(skills['regressed-agent'].completionRegressed, true);
    assert.equal(skills['regressed-agent'].targetActivationFailed, false);
    assert.equal(skills['clean-agent'].completionRegressed, false);
    assert.equal(skills['clean-agent'].targetActivationFailed, false);
    assert.equal(skills['activation-failed-agent'].completionRegressed, false);
    assert.equal(skills['activation-failed-agent'].targetActivationFailed, true);
    assert.equal(
      Object.hasOwn(skills['unmatched-legacy-agent'], 'completionRegressed'),
      false,
    );
    assert.equal(
      Object.hasOwn(skills['unmatched-legacy-agent'], 'targetActivationFailed'),
      false,
    );
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});
