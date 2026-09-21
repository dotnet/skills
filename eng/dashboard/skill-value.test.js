const test = require('node:test');
const assert = require('node:assert/strict');

test('skill value table shows confidence-aware install guidance', async (t) => {
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
  assert.equal((header.match(/>N \/ CI<\/th>/g) || []).length, 1);
  assert.match(header, /Pass rate \(base→skill\)/);
  assert.match(header, /Confidence-adjusted value/);
  assert.match(wrap.innerHTML, /colspan="7"/);
  assert.match(container.innerHTML, /paired 95% confidence interval/);
  assert.doesNotMatch(wrap.innerHTML, /paired 95% lift/);
  assert.match(wrap.innerHTML, /\+34 pp to \+61 pp/);
  assert.match(wrap.innerHTML, /Worth installing/);
  assert.match(wrap.innerHTML, /complete more tasks successfully/);
  assert.match(wrap.innerHTML, /20% fewer tokens and 20% faster/);
  const valueCell = wrap.innerHTML.match(/<td class="sv-value positive">(.*?)<\/td>/s);
  assert.ok(valueCell, 'rendered model row contains a value cell');
  assert.doesNotMatch(valueCell[1], /lift|confidence|CI|lower bound/i);
});

test('value assessment separates statistical noise from expensive reliable gains', (t) => {
  const previousWindow = globalThis.window;
  const hadWindow = Object.hasOwn(globalThis, 'window');
  const modulePath = require.resolve('./skill-value.js');
  globalThis.window = {};
  delete require.cache[modulePath];
  const { pairedDifferenceInterval, valueAssessment } = require(modulePath);

  t.after(() => {
    if (hadWindow) globalThis.window = previousWindow;
    else delete globalThis.window;
    delete require.cache[modulePath];
  });

  const strongCounts = {
    passTotal: 100,
    baseFail: 60,
    treatFail: 10,
    bothPass: 40,
    bothFail: 10,
    baselineOnlyPass: 0,
    treatmentOnlyPass: 50,
    hasPass: true,
  };
  const row = (tokens, timeMs, counts = strongCounts) => ({
    ...counts,
    baseline: { tokens: 100, timeMs: 1000 },
    treatment: { tokens, timeMs },
  });

  const interval = pairedDifferenceInterval(strongCounts);
  assert.equal(interval.method, 'paired');
  assert.equal(interval.estimate, 0.5);
  assert.ok(interval.low > 0.34 && interval.low < 0.35);

  const smallPerfectRecord = pairedDifferenceInterval({
    bothPass: 0,
    bothFail: 0,
    baselineOnlyPass: 0,
    treatmentOnlyPass: 5,
  });
  assert.ok(smallPerfectRecord.low < 0);
  assert.ok(smallPerfectRecord.low < smallPerfectRecord.high);

  const worth = valueAssessment(row(120, 1100));
  assert.equal(worth.status, 'worth');
  assert.ok(worth.index > 1);

  const expensive = valueAssessment(row(300, 2500));
  assert.equal(expensive.status, 'tradeoff');
  assert.ok(expensive.index < 1);

  const noisy = valueAssessment(row(100, 1000, {
    passTotal: 100,
    baseFail: 32,
    treatFail: 28,
    bothPass: 60,
    bothFail: 20,
    baselineOnlyPass: 8,
    treatmentOnlyPass: 12,
    hasPass: true,
  }));
  assert.equal(noisy.status, 'unproven');
  assert.ok(noisy.evidence.lift.low < 0);
  assert.ok(noisy.evidence.lift.high > 0);

  const regression = valueAssessment(row(100, 1000, {
    passTotal: 100,
    baseFail: 10,
    treatFail: 60,
    bothPass: 40,
    bothFail: 10,
    baselineOnlyPass: 50,
    treatmentOnlyPass: 0,
    hasPass: true,
  }));
  assert.equal(regression.status, 'regression');
  assert.ok(regression.evidence.lift.high < 0);
});
