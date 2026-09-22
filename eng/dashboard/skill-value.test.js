const test = require('node:test');
const assert = require('node:assert/strict');

test('skill value table uses preference evidence for install guidance', async (t) => {
  const previousGlobals = {
    document: globalThis.document,
    window: globalThis.window,
    fetch: globalThis.fetch,
  };
  const hadGlobal = {
    document: Object.hasOwn(globalThis, 'document'),
    window: Object.hasOwn(globalThis, 'window'),
    fetch: Object.hasOwn(globalThis, 'fetch'),
  };
  const modulePath = require.resolve('./skill-value.js');
  t.after(() => {
    for (const name of Object.keys(previousGlobals)) {
      if (hadGlobal[name]) {
        globalThis[name] = previousGlobals[name];
      } else {
        delete globalThis[name];
      }
    }
    delete require.cache[modulePath];
  });

  const elements = new Map();
  const wrap = {
    innerHTML: '',
    querySelector: () => null,
    querySelectorAll: () => [],
  };
  const container = {
    innerHTML: '',
    querySelector(selector) {
      if (selector === '#sv-table-wrap') return wrap;
      if (!elements.has(selector)) {
        elements.set(selector, {
          value: '',
          addEventListener: () => {},
        });
      }
      return elements.get(selector);
    },
  };

  globalThis.document = {
    createElement: () => ({
      set textContent(value) { this.innerHTML = String(value); },
      innerHTML: '',
    }),
    getElementById: () => container,
  };
  globalThis.window = {};
  globalThis.fetch = async () => ({
    ok: true,
    json: async () => ({
      entries: [{
        plugin: 'plugin',
        model: 'executor',
        judgeModel: 'judge',
        date: 1,
        skills: [{
          skill: 'skill',
          baseline: { n: 100, timeMs: 1000, tokens: 100 },
          treatment: { n: 100, timeMs: 800, tokens: 80 },
          activationExpected: 100,
          activationFired: 100,
          passTotal: 100,
          baselineFail: 60,
          treatmentFail: 10,
          bothPass: 40,
          bothFail: 10,
          baselineOnlyPass: 0,
          treatmentOnlyPass: 50,
          hasPassData: true,
          activationContract: { evaluated: true, passed: true, count: 100, violated: 0 },
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
        }],
      }],
    }),
  });

  require(modulePath);
  await window.initSkillValue();

  const headerMatch = wrap.innerHTML.match(/<thead><tr>(.*?)<\/tr><\/thead>/s);
  assert.ok(headerMatch, 'rendered table contains a header row');
  const header = headerMatch[1];
  assert.equal((header.match(/<th/g) || []).length, 7);
  assert.match(header, /Reliability N \/ CI/);
  assert.match(header, /Reliability pass rate \(base→skill\)/);
  assert.match(header, /What the results suggest/);
  assert.match(wrap.innerHTML, /colspan="7"/);
  assert.match(container.innerHTML, /Reliability N \/ CI/);
  assert.match(container.innerHTML, /Strong install or avoid recommendations require consistent side-by-side task results/);
  assert.match(wrap.innerHTML, /\+34 pp to \+61 pp/);
  assert.match(wrap.innerHTML, /preference 8W\/0T\/0L/);
  assert.match(wrap.innerHTML, /diagnostic only/);
  assert.match(wrap.innerHTML, /Worth installing/);
  assert.match(wrap.innerHTML, /side-by-side task comparisons consistently favored the skill/);
  assert.match(wrap.innerHTML, /20% fewer tokens and 20% faster/);
  const valueCell = wrap.innerHTML.match(/<td class="sv-value positive">(.*?)<\/td>/s);
  assert.ok(valueCell, 'rendered model row contains a value cell');
  assert.doesNotMatch(valueCell[1], /pass rate|grader|reliability|provisional|95% interval/i);
});

test('value assessment uses preference evidence and treats pass telemetry as irrelevant', (t) => {
  const previousWindow = globalThis.window;
  const hadWindow = Object.hasOwn(globalThis, 'window');
  const modulePath = require.resolve('./skill-value.js');
  globalThis.window = {};
  delete require.cache[modulePath];
  const {
    costMultiplier,
    deltaCell,
    drilldown,
    metricCell,
    provisionalReliabilityAssessment,
    reliabilityEvidence,
    valueAssessment,
    valueSentence,
  } = require(modulePath);

  t.after(() => {
    if (hadWindow) globalThis.window = previousWindow;
    else delete globalThis.window;
    delete require.cache[modulePath];
  });

  const credibleWin = {
    count: 8,
    wins: 7,
    ties: 1,
    losses: 0,
    direction: 'better',
    pValue: 0.0078125,
    alpha: 0.05,
    netWin: 0.875,
    underpowered: false,
    conclusive: true,
    minCredibleStimuli: 5,
    practicalPassed: true,
  };
  const row = (tokens, timeMs, preference = credibleWin) => ({
    preference,
    activationContract: { evaluated: true, passed: true, count: 1, violated: 0 },
    passTotal: 100,
    baseFail: 0,
    treatFail: 100,
    hasPass: true,
    baseline: { n: 100, tokens: 100, timeMs: 1000 },
    treatment: { n: 100, tokens, timeMs },
    activation: 1,
  });

  const worth = valueAssessment(row(80, 900));
  assert.equal(worth.status, 'worth');

  const fallbackZeroMetrics = row(0, 0);
  assert.equal(costMultiplier(fallbackZeroMetrics), null);
  assert.equal(valueAssessment(fallbackZeroMetrics).status, 'preference-only');
  assert.match(valueSentence(fallbackZeroMetrics).text, /cost information is unavailable/);
  assert.doesNotMatch(valueSentence(fallbackZeroMetrics).text, /Worth installing|100% fewer/);
  assert.match(deltaCell(100, 0, String, false), /telemetry unavailable/);
  assert.doesNotMatch(deltaCell(100, 0, String, false), /100%/);

  const activationFailure = {
    ...row(80, 900),
    activationContract: { evaluated: true, passed: false, count: 1, violated: 1 },
  };
  assert.equal(valueAssessment(activationFailure).status, 'activation-contract-failed');
  assert.equal(provisionalReliabilityAssessment(activationFailure), null);
  const activationFailureDescription = valueSentence(activationFailure);
  assert.equal(activationFailureDescription.status, 'activation-contract-failed');
  assert.match(activationFailureDescription.text, /Guidance withheld/);
  assert.match(activationFailureDescription.text, /expected to stay off/);
  assert.doesNotMatch(activationFailureDescription.text, /Worth installing|Looks helpful/);

  assert.equal(valueAssessment({ ...row(80, 900), activationContract: null }).status, 'insufficient');
  assert.equal(valueAssessment({
    ...row(80, 900),
    activationContract: { evaluated: false, passed: null, count: 0, violated: 0 },
  }).status, 'insufficient');

  const telemetryOnly = valueAssessment({ ...row(80, 900), preference: null });
  assert.equal(telemetryOnly.status, 'insufficient');
  const legacyReliabilityRow = {
    ...row(80, 900, null),
    bothPass: 40,
    bothFail: 10,
    baselineOnlyPass: 0,
    treatmentOnlyPass: 50,
    baseFail: 60,
    treatFail: 10,
  };
  const interval = reliabilityEvidence(legacyReliabilityRow);
  assert.equal(interval.method, 'paired');
  assert.ok(interval.low > 0.34 && interval.low < 0.35);
  assert.match(metricCell(legacyReliabilityRow), /\+34 pp to \+61 pp/);
  assert.doesNotMatch(metricCell(legacyReliabilityRow), /preference unavailable|N\/A/);
  assert.equal(provisionalReliabilityAssessment(legacyReliabilityRow).status, 'reliability-promising');
  const legacyDescription = valueSentence(legacyReliabilityRow);
  assert.equal(legacyDescription.status, 'reliability-promising');
  assert.match(legacyDescription.text, /Looks helpful/);
  assert.match(legacyDescription.text, /completed more successfully/);
  assert.doesNotMatch(legacyDescription.text, /provisional|95% interval|reliability telemetry/i);
  assert.doesNotMatch(legacyDescription.text, /Insufficient signal/);

  const legacyTradeoff = {
    ...legacyReliabilityRow,
    treatment: { n: 100, tokens: 120, timeMs: 1100 },
  };
  assert.equal(provisionalReliabilityAssessment(legacyTradeoff).status, 'reliability-tradeoff');
  assert.match(valueSentence(legacyTradeoff).text, /May help, but costs more/);

  const uncertainReliability = {
    ...legacyReliabilityRow,
    baseFail: 32,
    treatFail: 28,
    bothPass: 60,
    bothFail: 20,
    baselineOnlyPass: 8,
    treatmentOnlyPass: 12,
  };
  assert.equal(provisionalReliabilityAssessment(uncertainReliability).status, 'reliability-uncertain');
  assert.match(valueSentence(uncertainReliability).text, /No clear result yet/);

  const expensive = valueAssessment(row(120, 1100));
  assert.equal(expensive.status, 'tradeoff');

  const underSampledCost = {
    ...row(80, 900),
    baseline: { n: 4, tokens: 100, timeMs: 1000 },
    treatment: { n: 4, tokens: 80, timeMs: 900 },
  };
  assert.equal(costMultiplier(underSampledCost), null);
  assert.equal(valueAssessment(underSampledCost).status, 'preference-only');
  assert.match(valueSentence(underSampledCost).text, /Looks helpful/);
  assert.match(valueSentence(underSampledCost).text, /cost information is unavailable/);
  assert.doesNotMatch(valueSentence(underSampledCost).text, /Insufficient signal/);
  assert.match(deltaCell(100, 80, String, false, false), /telemetry unavailable/);
  assert.doesNotMatch(deltaCell(100, 80, String, false, false), /20%/);

  const missingArmMetrics = {
    ...row(80, 900),
    baseline: {
      n: 0,
      timeMs: null,
      tokens: null,
      tokensIn: null,
      tokensOut: null,
      cacheRead: null,
      cacheWrite: null,
    },
  };
  assert.match(drilldown(missingArmMetrics), /Latest run without skill<\/span> <span class="sv-sub">no metrics/);
  assert.doesNotMatch(drilldown(missingArmMetrics), /time 0\.0s/);

  const inconclusive = valueAssessment(row(80, 900, {
    ...credibleWin,
    wins: 4,
    ties: 3,
    losses: 1,
    pValue: 0.1875,
  }));
  assert.equal(inconclusive.status, 'unproven');

  const regression = valueAssessment(row(80, 900, {
    ...credibleWin,
    wins: 0,
    ties: 1,
    losses: 7,
    direction: 'worse',
  }));
  assert.equal(regression.status, 'regression');
  const underSampledRegression = {
    ...row(80, 900, {
      ...credibleWin,
      wins: 0,
      ties: 1,
      losses: 7,
      direction: 'worse',
    }),
    baseline: { n: 4, tokens: 100, timeMs: 1000 },
    treatment: { n: 4, tokens: 80, timeMs: 900 },
  };
  assert.equal(valueAssessment(underSampledRegression).status, 'regression');
  assert.match(valueSentence(underSampledRegression).text, /Not recommended/);
  assert.match(valueSentence(underSampledRegression).text, /cost information is unavailable/);

  const underpowered = valueAssessment(row(80, 900, {
    ...credibleWin,
    count: 4,
    wins: 4,
    ties: 0,
    underpowered: true,
  }));
  assert.equal(underpowered.status, 'insufficient');
});

test('aggregation preserves a null preference from the newest run', (t) => {
  const previousWindow = globalThis.window;
  const hadWindow = Object.hasOwn(globalThis, 'window');
  const modulePath = require.resolve('./skill-value.js');
  globalThis.window = {};
  delete require.cache[modulePath];
  const { aggregate, valueAssessment } = require(modulePath);

  t.after(() => {
    if (hadWindow) globalThis.window = previousWindow;
    else delete globalThis.window;
    delete require.cache[modulePath];
  });

  const skill = (preference, activationContract = null, overrides = {}) => ({
    skill: 'skill',
    baseline: { n: 5, timeMs: 1000, tokens: 100 },
    treatment: { n: 5, timeMs: 900, tokens: 90 },
    activationExpected: 5,
    activationFired: 5,
    preference,
    activationContract,
    ...overrides,
  });
  const crediblePreference = {
    count: 8,
    wins: 8,
    ties: 0,
    losses: 0,
    direction: 'better',
    pValue: 0.00390625,
    alpha: 0.05,
    underpowered: false,
    conclusive: true,
    practicalPassed: true,
  };
  const rows = aggregate([
    { plugin: 'plugin', model: 'model', judgeModel: 'judge', date: 1, skills: [skill(crediblePreference)] },
    {
      plugin: 'plugin',
      model: 'model',
      judgeModel: 'judge',
      date: 2,
      skills: [skill(null, { evaluated: true, passed: false, count: 1, violated: 1 })],
    },
  ]);

  assert.equal(rows.length, 1);
  assert.equal(rows[0].preference, null);
  assert.deepEqual(rows[0].activationContract, {
    evaluated: true,
    passed: false,
    count: 1,
    violated: 1,
  });
  assert.equal(valueAssessment(rows[0]).status, 'activation-contract-failed');
});

test('headline and recommendation use only the latest run', (t) => {
  const previousWindow = globalThis.window;
  const hadWindow = Object.hasOwn(globalThis, 'window');
  const modulePath = require.resolve('./skill-value.js');
  globalThis.window = {};
  delete require.cache[modulePath];
  const { aggregate, valueSentence } = require(modulePath);

  t.after(() => {
    if (hadWindow) globalThis.window = previousWindow;
    else delete globalThis.window;
    delete require.cache[modulePath];
  });

  const crediblePreference = {
    count: 8,
    wins: 8,
    ties: 0,
    losses: 0,
    direction: 'better',
    pValue: 0.00390625,
    alpha: 0.05,
    underpowered: false,
    conclusive: true,
    practicalPassed: true,
  };
  const contract = { evaluated: true, passed: true, count: 1, violated: 0 };
  const skill = (activationFired, treatmentTokens) => ({
    skill: 'skill',
    baseline: { n: 5, timeMs: 1000, tokens: 100 },
    treatment: { n: 5, timeMs: treatmentTokens * 10, tokens: treatmentTokens },
    activationExpected: 5,
    activationFired,
    preference: crediblePreference,
    activationContract: contract,
  });
  const rows = aggregate([
    {
      plugin: 'plugin',
      model: 'model',
      judgeModel: 'judge',
      commit: { id: 'old-commit' },
      date: 1,
      skills: [skill(5, 50)],
    },
    {
      plugin: 'plugin',
      model: 'model',
      judgeModel: 'judge',
      commit: { id: 'new-commit' },
      date: 2,
      skills: [skill(0, 200)],
    },
  ]);

  assert.equal(rows.length, 1);
  assert.equal(rows[0].commit.id, 'new-commit');
  assert.equal(rows[0].activation, 0);
  assert.equal(rows[0].treatment.tokens, 200);
  assert.equal(rows[0].treatment.timeMs, 2000);
  assert.equal(rows[0].runCount, 2);
  assert.equal(rows[0].earlierRunCount, 1);
  assert.equal(rows[0].history.activation, 1);
  assert.equal(rows[0].history.treatment.tokens, 50);
  const description = valueSentence(rows[0]);
  assert.equal(description.status, 'insufficient');
  assert.doesNotMatch(description.text, /Worth installing|fewer tokens|faster/);
});

test('rollups preserve regression and preference-only leaf guidance', (t) => {
  const previousGlobals = {
    document: globalThis.document,
    window: globalThis.window,
  };
  const hadGlobal = {
    document: Object.hasOwn(globalThis, 'document'),
    window: Object.hasOwn(globalThis, 'window'),
  };
  const modulePath = require.resolve('./skill-value.js');
  globalThis.document = {
    createElement: () => ({
      set textContent(value) { this.innerHTML = String(value); },
      innerHTML: '',
    }),
  };
  globalThis.window = {};
  delete require.cache[modulePath];
  const { singleModelRollup, countRollup } = require(modulePath);

  t.after(() => {
    for (const name of Object.keys(previousGlobals)) {
      if (hadGlobal[name]) globalThis[name] = previousGlobals[name];
      else delete globalThis[name];
    }
    delete require.cache[modulePath];
  });

  const preference = {
    count: 8,
    wins: 7,
    ties: 1,
    losses: 0,
    direction: 'better',
    pValue: 0.0078125,
    alpha: 0.05,
    underpowered: false,
    conclusive: true,
    minCredibleStimuli: 5,
    practicalPassed: true,
  };
  const row = (model, preferenceEvidence, baseline, treatment) => ({
    model,
    preference: preferenceEvidence,
    activationContract: { evaluated: true, passed: true, count: 8, violated: 0 },
    activation: 1,
    baseline,
    treatment,
  });
  const worth = row(
    'worth-model',
    preference,
    { n: 8, tokens: 100, timeMs: 1000 },
    { n: 8, tokens: 80, timeMs: 900 },
  );
  const regression = row(
    'regression-model',
    { ...preference, wins: 0, losses: 7, direction: 'worse' },
    { n: 8, tokens: 100, timeMs: 1000 },
    { n: 8, tokens: 80, timeMs: 900 },
  );
  const preferenceOnly = row(
    'unknown-cost-model',
    preference,
    { n: 8, tokens: 0, timeMs: 0 },
    { n: 8, tokens: 0, timeMs: 0 },
  );
  const underSampled = row(
    'under-sampled-model',
    preference,
    { n: 4, tokens: 100, timeMs: 1000 },
    { n: 4, tokens: 80, timeMs: 900 },
  );

  assert.match(singleModelRollup(regression), /not recommended/);
  assert.doesNotMatch(singleModelRollup(regression), /not yet/);
  assert.match(singleModelRollup(preferenceOnly), /looks helpful; cost unavailable/);
  assert.match(singleModelRollup(underSampled), /n\/a tokens · n\/a time/);
  assert.doesNotMatch(singleModelRollup(underSampled), /−20% tokens|−10% time/);

  const summary = countRollup([worth, regression, preferenceOnly]);
  assert.match(summary, /1 worth installing/);
  assert.match(summary, /1 not recommended/);
  assert.match(summary, /1 look helpful; cost unavailable/);
  assert.doesNotMatch(summary, /do not clear|not yet/);
});
