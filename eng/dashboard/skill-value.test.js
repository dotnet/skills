const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('skill value column headers stay visible during page scroll', () => {
  const html = fs.readFileSync(path.join(__dirname, 'dashboard.html'), 'utf8');
  assert.match(html, /\.sv-table\s*\{\s*overflow:\s*visible;/);
  assert.match(html, /\.sv-table thead th\s*\{\s*position:\s*sticky;\s*top:\s*0;/);
});

function dashboardElement(className = '', dataset = {}) {
  const element = {
    className, dataset, style: { display: '' }, value: '', disabled: false,
    listeners: new Map(),
    addEventListener(event, callback) { this.listeners.set(event, callback); },
    dispatch(event) { this.listeners.get(event)?.(); },
  };
  element.classList = {
    contains: name => element.className.split(' ').includes(name),
    add: name => { if (!element.classList.contains(name)) element.className += ` ${name}`; },
    remove: name => { element.className = element.className.split(' ').filter(part => part !== name).join(' '); },
    toggle: (name, force) => {
      const enabled = force ?? !element.classList.contains(name);
      if (enabled) element.classList.add(name);
      else element.classList.remove(name);
    },
  };
  return element;
}

function dashboardTable() {
  const table = {
    renders: 0, rows: [], icons: new Map(), html: '',
    set innerHTML(html) {
      this.html = html;
      this.renders++;
      this.rows = [];
      this.icons.clear();
      for (const match of html.matchAll(/<tr class="([^"]+)"([^>]*)>([\s\S]*?)<\/tr>/g)) {
        const attributes = match[2];
        const toggle = attributes.match(/data-toggle="([^"]+)"/)?.[1];
        const parent = attributes.match(/data-parent="([^"]+)"/)?.[1];
        const row = dashboardElement(match[1], { toggle, parent });
        row.style.display = attributes.includes('display:none') ? 'none' : '';
        row.html = match[3];
        this.rows.push(row);
        if (toggle) this.icons.set(toggle, dashboardElement('expand-icon'));
      }
    },
    get innerHTML() { return this.html; },
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; },
    querySelectorAll(selector) {
      if (selector.startsWith('#icon-')) {
        const icon = this.icons.get(selector.slice(6));
        return icon ? [icon] : [];
      }
      if (selector === '[class*="child-of-"]') return this.rows.filter(row => row.className.includes('child-of-'));
      if (selector.startsWith('.child-of-')) return this.rows.filter(row => row.classList.contains(selector.slice(1)));
      return this.rows.filter(row => selector.split(',').some(part => {
        const classes = part.trim().split('.');
        return classes.slice(1).every(name => row.classList.contains(name));
      }));
    },
  };
  return table;
}

async function renderSearchDashboard(t, entries) {
  const previous = { document: globalThis.document, window: globalThis.window, fetch: globalThis.fetch };
  const present = Object.fromEntries(Object.keys(previous).map(name => [name, Object.hasOwn(globalThis, name)]));
  const modulePath = require.resolve('./skill-value.js');
  t.after(() => {
    for (const name of Object.keys(previous)) {
      if (present[name]) globalThis[name] = previous[name];
      else delete globalThis[name];
    }
    delete require.cache[modulePath];
  });
  const wrap = dashboardTable();
  const controls = new Map([
    ['#sv-table-wrap', wrap],
    ...['#sv-search', '#sv-search-empty', '#sv-model', '#sv-judge', '#sv-expand', '#sv-collapse']
      .map(id => [id, dashboardElement()]),
  ]);
  const container = {
    innerHTML: '',
    querySelector: selector => controls.get(selector),
  };
  const frames = [];
  globalThis.document = {
    getElementById: () => container,
    createElement: () => ({
      innerHTML: '',
      set textContent(value) {
        this.innerHTML = String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
      },
    }),
  };
  globalThis.window = { requestAnimationFrame: callback => frames.push(callback) };
  globalThis.fetch = async () => ({ ok: true, json: async () => ({ entries }) });
  delete require.cache[modulePath];
  require(modulePath);
  await window.initSkillValue();
  return { wrap, controls, frames };
}

function visibleRows(wrap, level) {
  return wrap.rows.filter(row => row.classList.contains(`level-${level}`) && row.style.display !== 'none');
}

test('search retains full matched hierarchy without rebuilding the table and pins agentic-workflows last', async t => {
  const skill = name => ({
    skill: name,
    baseline: { n: 5, timeMs: 1000, tokens: 100 },
    treatment: { n: 5, timeMs: 900, tokens: 90 },
  });
  const entries = [
    { plugin: 'agentic-workflows', model: 'model-a', judgeModel: 'judge-a', date: 1, skills: [skill('workflow')] },
    { plugin: 'zzz-plugin', model: 'model-a', judgeModel: 'judge-a', date: 1, skills: [skill('needle-two')] },
    { plugin: 'alpha-plugin', model: 'model-a', judgeModel: 'judge-a', date: 1, skills: [skill('needle-one'), skill('other')] },
    { plugin: 'alpha-plugin', model: 'model-b', judgeModel: 'judge-b', date: 1, skills: [skill('needle-one'), skill('other')] },
  ];
  const { wrap, controls, frames } = await renderSearchDashboard(t, entries);
  const search = controls.get('#sv-search');
  const empty = controls.get('#sv-search-empty');
  const pluginNames = () => visibleRows(wrap, 0).map(row => row.html.match(/<\/span>([^<]+)/)?.[1].trim());
  const skillNames = () => visibleRows(wrap, 1).map(row => row.html.match(/<\/span>([^<]+)/)?.[1].trim());
  const submit = value => {
    search.value = value;
    search.dispatch('input');
    assert.equal(frames.length, 1, 'rapid input is coalesced into a single frame');
    frames.shift()();
  };

  assert.deepEqual(pluginNames(), ['alpha-plugin', 'zzz-plugin', 'agentic-workflows']);
  controls.get('#sv-collapse').dispatch('click');
  assert.equal(visibleRows(wrap, 1).length, 0);
  search.value = 'n';
  search.dispatch('input');
  search.value = 'NEEDLE';
  search.dispatch('input');
  assert.equal(frames.length, 1);
  frames.shift()();
  assert.deepEqual(pluginNames(), ['alpha-plugin', 'zzz-plugin']);
  assert.deepEqual(skillNames(), ['needle-one', 'needle-two']);
  assert.equal(visibleRows(wrap, 2).length, 3, 'a matching skill includes every model');
  assert.equal(wrap.renders, 1, 'search changes visibility, not table markup');
  assert.equal(controls.get('#sv-expand').disabled, true);

  submit('ALPHA');
  assert.deepEqual(pluginNames(), ['alpha-plugin']);
  assert.deepEqual(skillNames(), ['needle-one', 'other'], 'a plugin match shows all its skills');
  assert.equal(visibleRows(wrap, 2).length, 4);

  submit('not-present');
  assert.equal(visibleRows(wrap, 0).length, 0);
  assert.equal(empty.style.display, '');
  submit('');
  assert.deepEqual(pluginNames(), ['alpha-plugin', 'zzz-plugin', 'agentic-workflows']);
  assert.equal(visibleRows(wrap, 1).length, 0, 'clearing search restores collapsed rows');
  assert.equal(empty.style.display, 'none');
  assert.equal(controls.get('#sv-expand').disabled, false);
  assert.equal(wrap.renders, 1);

  submit('needle');
  controls.get('#sv-model').value = 'model-b';
  controls.get('#sv-model').dispatch('change');
  assert.deepEqual(pluginNames(), ['alpha-plugin']);
  assert.deepEqual(skillNames(), ['needle-one']);
  assert.equal(visibleRows(wrap, 2).length, 1, 'model filter and search intersect');
  assert.equal(wrap.renders, 2, 'only model filter changes rebuild the table');
  submit('');
  assert.equal(visibleRows(wrap, 1).length, 0, 'clearing search after a model redraw restores collapsed skills');
  assert.equal(visibleRows(wrap, 2).length, 0);

  controls.get('#sv-model').value = '';
  controls.get('#sv-model').dispatch('change');
  controls.get('#sv-collapse').dispatch('click');
  const alpha = wrap.rows.find(row => row.classList.contains('level-0') && row.html.includes('alpha-plugin'));
  alpha.dispatch('click');
  assert.equal(visibleRows(wrap, 1).length, 2);
  submit('needle');
  controls.get('#sv-judge').value = 'judge-b';
  controls.get('#sv-judge').dispatch('change');
  submit('');
  assert.deepEqual(pluginNames(), ['alpha-plugin']);
  assert.equal(visibleRows(wrap, 1).length, 2, 'judge redraw restores the open plugin');
  assert.equal(visibleRows(wrap, 2).length, 0, 'judge redraw restores collapsed skills');

  controls.get('#sv-judge').value = '';
  controls.get('#sv-judge').dispatch('change');
  controls.get('#sv-model').value = 'model-a';
  controls.get('#sv-model').dispatch('change');
  controls.get('#sv-collapse').dispatch('click');
  submit('needle');
  controls.get('#sv-model').value = '';
  controls.get('#sv-model').dispatch('change');
  submit('');
  assert.equal(visibleRows(wrap, 1).length, 0, 'widening executor filter keeps skills collapsed');
  assert.equal(visibleRows(wrap, 2).length, 0, 'new model rows stay hidden beneath collapsed skills');

  controls.get('#sv-judge').value = 'judge-a';
  controls.get('#sv-judge').dispatch('change');
  controls.get('#sv-collapse').dispatch('click');
  submit('needle');
  controls.get('#sv-judge').value = '';
  controls.get('#sv-judge').dispatch('change');
  submit('');
  assert.equal(visibleRows(wrap, 1).length, 0, 'widening judge filter keeps skills collapsed');
  assert.equal(visibleRows(wrap, 2).length, 0, 'new judge rows stay hidden beneath collapsed skills');
});

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
          style: {},
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
