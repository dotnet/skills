const test = require('node:test');
const assert = require('node:assert/strict');

const {
  buildReport,
  classify,
  isDifferentiated,
  isPreferenceLoss,
  isoWeekKey,
} = require('./weekly-eval-insights.js');

function skill(overrides = {}) {
  return {
    skill: 'example',
    timedOut: false,
    passTotal: 10,
    baselineFail: 6,
    treatmentFail: 2,
    baseline: { n: 10, timeMs: 1000, tokens: 100 },
    treatment: { n: 10, timeMs: 800, tokens: 80 },
    activationContract: { evaluated: true, passed: true, count: 1, violated: 0 },
    preference: {
      count: 8,
      wins: 8,
      ties: 0,
      losses: 0,
      direction: 'better',
      pValue: 0.00390625,
      alpha: 0.05,
      netWin: 1,
      underpowered: false,
      conclusive: true,
      minCredibleStimuli: 5,
      practicalPassed: true,
    },
    ...overrides,
  };
}

function entry(date, skillValue, model = 'executor', judgeModel = 'judge') {
  return {
    plugin: 'plugin',
    date: Date.parse(date),
    model,
    judgeModel,
    skills: [skillValue],
  };
}

test('differentiation requires authoritative gate evidence', () => {
  assert.equal(isDifferentiated(skill()), true);
  assert.equal(isDifferentiated(skill({
    preference: { ...skill().preference, pValue: 0.06 },
  })), false);
  assert.equal(isDifferentiated(skill({
    activationContract: { evaluated: true, passed: false, count: 1, violated: 1 },
  })), false);
});

test('credible reverse preference is reported as a preference loss', () => {
  const loss = skill({
    preference: {
      ...skill().preference,
      wins: 0,
      losses: 8,
      direction: 'worse',
      netWin: -1,
    },
  });

  assert.equal(isPreferenceLoss(loss), true);
  assert.equal(classify(loss), 'preference-loss');
  assert.equal(isDifferentiated(loss), false);
});

test('weekly report separates reliability, differentiation, and pass rate', () => {
  const report = buildReport({
    dashboard: {
      entries: [
        entry('2026-10-17T00:00:00Z', skill()),
        entry('2026-10-18T00:00:00Z', skill({
          preference: { ...skill().preference, conclusive: false },
          treatmentFail: 5,
        })),
        entry('2026-10-10T00:00:00Z', skill()),
      ],
    },
    workflowRuns: [
      {
        id: 1,
        created_at: '2026-10-17T00:00:00Z',
        run_started_at: '2026-10-17T00:01:00Z',
        updated_at: '2026-10-17T00:11:00Z',
        conclusion: 'success',
        html_url: 'https://github.com/dotnet/skills/actions/runs/1',
      },
      {
        id: 2,
        created_at: '2026-10-18T00:00:00Z',
        run_started_at: '2026-10-18T00:02:00Z',
        updated_at: '2026-10-18T00:12:00Z',
        conclusion: 'failure',
        html_url: 'https://github.com/dotnet/skills/actions/runs/2',
      },
    ],
    windowEnd: '2026-10-21T17:00:00Z',
  });

  assert.equal(report.weekKey, '2026-W43');
  assert.equal(report.workflowRuns.count, 2);
  assert.equal(report.workflowRuns.averageDurationMs, 600000);
  assert.equal(report.current.expectedResults, 2);
  assert.equal(report.current.conclusiveResults, 1);
  assert.equal(report.current.reliabilityRate, 0.5);
  assert.equal(report.current.differentiatedResults, 1);
  assert.equal(report.current.differentiationRate, 1);
  assert.equal(report.current.plugins[0].skills[0].treatmentPassRate, 0.65);
  assert.equal(report.current.plugins[0].skills[0].flaky, true);
  assert.equal(report.current.plugins[0].skills[0].modelSensitive, false);
  assert.equal(report.current.plugins[0].skills[0].consistentlyPassing, false);
  assert.equal(report.previous.differentiatedResults, 1);
  assert.equal(report.aiCredits.available, false);
});

test('resource metrics are not double counted across judges', () => {
  const report = buildReport({
    dashboard: {
      entries: [
        {
          ...entry('2026-10-17T00:00:00Z', skill(), 'executor', 'judge-a'),
          commit: { id: 'same-run' },
        },
        {
          ...entry('2026-10-17T00:00:00Z', skill(), 'executor', 'judge-b'),
          commit: { id: 'same-run' },
        },
      ],
    },
    workflowRuns: [],
    windowEnd: '2026-10-21T17:00:00Z',
  });

  const result = report.current.plugins[0].skills[0];
  assert.equal(result.expectedResults, 2);
  assert.equal(result.metricObservationCount, 1);
  assert.equal(result.pairedScenarioCount, 10);
  assert.equal(result.averageTreatmentTokens, 80);
  assert.equal(result.flaky, false);
  assert.equal(result.modelSensitive, false);
});

test('resource metrics preserve independent runs on the same day', () => {
  const report = buildReport({
    dashboard: {
      entries: [
        {
          ...entry('2026-10-17T00:00:00Z', skill(), 'executor', 'judge'),
          commit: { id: 'commit-a' },
        },
        {
          ...entry('2026-10-17T12:00:00Z', skill({
            treatmentFail: 10,
          }), 'executor', 'judge'),
          commit: { id: 'commit-b' },
        },
      ],
    },
    workflowRuns: [],
    windowEnd: '2026-10-21T17:00:00Z',
  });

  const result = report.current.plugins[0].skills[0];
  assert.equal(result.metricObservationCount, 2);
  assert.equal(result.treatmentPassRate, 0.4);
  assert.equal(result.consistentlyPassing, false);
});

test('stable outcome differences across models are model sensitivity, not flakiness', () => {
  const report = buildReport({
    dashboard: {
      entries: [
        entry('2026-10-17T00:00:00Z', skill(), 'executor-a', 'judge'),
        entry('2026-10-17T00:00:01Z', skill({
          preference: { ...skill().preference, direction: 'tie', pValue: 1, practicalPassed: false },
        }), 'executor-b', 'judge'),
      ],
    },
    workflowRuns: [],
    windowEnd: '2026-10-21T17:00:00Z',
  });

  const result = report.current.plugins[0].skills[0];
  assert.equal(result.flaky, false);
  assert.equal(result.modelSensitive, true);
});

test('week keys use the ISO week year', () => {
  assert.equal(isoWeekKey(new Date('2025-12-30T00:00:00Z')), '2026-W01');
});
