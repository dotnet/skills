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
  assert.match(header, /Preference W\/T\/L/);
  assert.match(header, /Reliability pass rate \(base→skill\)/);
  assert.match(header, /Preference \+ cost guidance/);
  assert.match(wrap.innerHTML, /colspan="7"/);
  assert.match(container.innerHTML, /one-vote-per-eligible-stimulus/);
  assert.match(container.innerHTML, /pass rate are reliability diagnostics only/);
  assert.match(wrap.innerHTML, /8W \/ 0T \/ 0L/);
  assert.match(wrap.innerHTML, /p=0\.004/);
  assert.match(wrap.innerHTML, /diagnostic only/);
  assert.match(wrap.innerHTML, /Worth installing/);
  assert.match(wrap.innerHTML, /paired comparison credibly favors the skill/);
  assert.match(wrap.innerHTML, /20% fewer tokens and 20% faster/);
  const valueCell = wrap.innerHTML.match(/<td class="sv-value positive">(.*?)<\/td>/s);
  assert.ok(valueCell, 'rendered model row contains a value cell');
  assert.doesNotMatch(valueCell[1], /pass rate|grader|reliability/i);
});

test('value assessment uses preference evidence and treats pass telemetry as irrelevant', (t) => {
  const previousWindow = globalThis.window;
  const hadWindow = Object.hasOwn(globalThis, 'window');
  const modulePath = require.resolve('./skill-value.js');
  globalThis.window = {};
  delete require.cache[modulePath];
  const { valueAssessment } = require(modulePath);

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
    passTotal: 100,
    baseFail: 0,
    treatFail: 100,
    hasPass: true,
    baseline: { tokens: 100, timeMs: 1000 },
    treatment: { tokens, timeMs },
  });

  const worth = valueAssessment(row(80, 900));
  assert.equal(worth.status, 'worth');

  const telemetryOnly = valueAssessment({ ...row(80, 900), preference: null });
  assert.equal(telemetryOnly.status, 'insufficient');

  const expensive = valueAssessment(row(120, 1100));
  assert.equal(expensive.status, 'tradeoff');

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

  const skill = (preference) => ({
    skill: 'skill',
    baseline: { n: 5, timeMs: 1000, tokens: 100 },
    treatment: { n: 5, timeMs: 900, tokens: 90 },
    activationExpected: 5,
    activationFired: 5,
    preference,
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
    { plugin: 'plugin', model: 'model', judgeModel: 'judge', date: 2, skills: [skill(null)] },
  ]);

  assert.equal(rows.length, 1);
  assert.equal(rows[0].preference, null);
  assert.equal(valueAssessment(rows[0]).status, 'insufficient');
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

  assert.match(singleModelRollup(regression), /not recommended/);
  assert.doesNotMatch(singleModelRollup(regression), /not yet/);
  assert.match(singleModelRollup(preferenceOnly), /preference win; cost unavailable/);

  const summary = countRollup([worth, regression, preferenceOnly]);
  assert.match(summary, /1 worth installing/);
  assert.match(summary, /1 not recommended/);
  assert.match(summary, /1 preference win\(s\); cost unavailable/);
  assert.doesNotMatch(summary, /do not clear|not yet/);
});
