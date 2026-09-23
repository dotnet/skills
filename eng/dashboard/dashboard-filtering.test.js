const test = require('node:test');
const assert = require('node:assert/strict');

class FakeElement {
  constructor(tagName, document) {
    this.tagName = tagName.toUpperCase();
    this.document = document;
    this.children = [];
    this.dataset = {};
    this.style = {};
    this.listeners = new Map();
    this.className = '';
    this.checked = false;
    this._id = '';
    this._innerHTML = '';
  }

  set id(value) {
    this._id = value;
    if (value) this.document.elements.set(value, this);
  }

  get id() {
    return this._id;
  }

  set textContent(value) {
    this._innerHTML = String(value)
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;');
  }

  get textContent() {
    return this._innerHTML;
  }

  set innerHTML(value) {
    this._innerHTML = String(value);
    this.children = [];

    for (const match of this._innerHTML.matchAll(/id="([^"]+)"/g)) {
      const child = new FakeElement('div', this.document);
      child.id = match[1];
      this.appendChild(child);
    }

    if (this._innerHTML.includes('<canvas')) {
      this.appendChild(new FakeElement('canvas', this.document));
    }
  }

  get innerHTML() {
    return this._innerHTML;
  }

  appendChild(child) {
    child.parentElement = this;
    this.children.push(child);
    return child;
  }

  insertBefore(child, reference) {
    child.parentElement = this;
    const index = this.children.indexOf(reference);
    if (index < 0) this.children.push(child);
    else this.children.splice(index, 0, child);
    return child;
  }

  addEventListener(name, callback) {
    this.listeners.set(name, callback);
  }

  dispatch(name) {
    this.listeners.get(name)?.();
  }

  setAttribute() {}

  querySelector(selector) {
    return this.querySelectorAll(selector)[0] || null;
  }

  querySelectorAll(selector) {
    const matches = [];
    const visit = element => {
      for (const child of element.children) {
        if (selector.startsWith('.')) {
          const className = selector.slice(1);
          if (child.className.split(/\s+/).includes(className)) matches.push(child);
        } else if (selector.startsWith('#')) {
          if (child.id === selector.slice(1)) matches.push(child);
        } else if (child.tagName.toLowerCase() === selector.toLowerCase()) {
          matches.push(child);
        }
        visit(child);
      }
    };
    visit(this);
    return matches;
  }

  classList = {
    toggle: (className, enabled) => {
      const classes = new Set(this.className.split(/\s+/).filter(Boolean));
      if (enabled) classes.add(className);
      else classes.delete(className);
      this.className = Array.from(classes).join(' ');
    },
  };
}

class FakeChart {
  static instances = [];

  static defaults = {
    plugins: {
      legend: {
        labels: {
          generateLabels: chart => chart.data.datasets.map((dataset, datasetIndex) => ({
            datasetIndex,
            hidden: !!dataset.hidden,
            strokeStyle: dataset.borderColor,
          })),
        },
      },
    },
  };

  constructor(canvas, config) {
    this.canvas = canvas;
    this.data = config.data;
    this.options = config.options;
    this.updateCalls = [];
    this.destroyCalls = 0;
    this.metas = this.data.datasets.map(() => ({ hidden: null }));
    FakeChart.instances.push(this);
  }

  getDatasetMeta(index) {
    return this.metas[index];
  }

  setDatasetVisibility(index, visible) {
    this.metas[index].hidden = !visible;
  }

  update(mode) {
    this.updateCalls.push(mode);
  }

  destroy() {
    this.destroyCalls++;
  }
}

function qualityEntry(model, date, value, testNames = ['Example']) {
  return {
    model,
    date,
    commit: { message: `${model} quality` },
    benches: testNames.flatMap(testName => [
      { name: `${testName} - Skilled Quality`, value },
      { name: `${testName} - Vanilla Quality`, value: value - 1 },
    ]),
  };
}

function efficiencyEntry(model, date, value, testNames = ['Example']) {
  return {
    model,
    date,
    commit: { message: `${model} efficiency` },
    benches: testNames.flatMap(testName => [
      { name: `${testName} - Skilled Time`, value },
      { name: `${testName} - Skilled Tokens In`, value: value * 1000 },
      { name: `${testName} - Vanilla Time`, value: value + 1 },
      { name: `${testName} - Vanilla Tokens In`, value: (value + 1) * 1000 },
    ]),
  };
}

async function renderDashboard(t, pluginData, expectedChartCount) {
  const previousGlobals = {
    Chart: globalThis.Chart,
    document: globalThis.document,
    fetch: globalThis.fetch,
    window: globalThis.window,
  };
  const hadGlobal = Object.fromEntries(
    Object.keys(previousGlobals).map(name => [name, Object.hasOwn(globalThis, name)])
  );
  const modulePath = require.resolve('./dashboard.js');
  t.after(() => {
    for (const name of Object.keys(previousGlobals)) {
      if (hadGlobal[name]) globalThis[name] = previousGlobals[name];
      else delete globalThis[name];
    }
    FakeChart.instances = [];
    delete require.cache[modulePath];
  });

  const document = {
    elements: new Map(),
    createElement(tagName) {
      return new FakeElement(tagName, document);
    },
    getElementById(id) {
      return this.elements.get(id) || null;
    },
  };
  const tabBar = document.createElement('div');
  tabBar.id = 'tab-bar';
  const tabContent = document.createElement('div');
  tabContent.id = 'tab-content';

  const animationFrames = [];
  globalThis.document = document;
  globalThis.window = {
    requestAnimationFrame(callback) {
      animationFrames.push(callback);
    },
  };
  globalThis.Chart = FakeChart;

  globalThis.fetch = async url => {
    if (url === 'data/dashboard-meta.json') return { ok: false };
    if (url === 'data/components.json') return { ok: true, json: async () => ['sample'] };
    if (url === 'data/sample.json') return { ok: true, json: async () => pluginData };
    throw new Error(`Unexpected URL: ${url}`);
  };

  require(modulePath);
  for (let attempt = 0; attempt < 20 && FakeChart.instances.length < expectedChartCount; attempt++) {
    await new Promise(resolve => setImmediate(resolve));
  }

  return { animationFrames, document };
}

test('model filtering updates existing charts without rebuilding them', async (t) => {
  const testNames = Array.from({ length: 220 }, (_, index) => `Example ${index + 1}`);
  const pluginData = {
    entries: {
      Quality: [
        qualityEntry('model-a', '2026-09-20T00:00:00Z', 8, testNames),
        qualityEntry('model-b', '2026-09-21T00:00:00Z', 7, testNames),
      ],
      Efficiency: [
        efficiencyEntry('model-a', '2026-09-20T00:00:00Z', 10, testNames),
        efficiencyEntry('model-b', '2026-09-21T00:00:00Z', 12, testNames),
      ],
    },
  };
  const { animationFrames, document } = await renderDashboard(t, pluginData, 440);

  assert.equal(FakeChart.instances.length, 440, 'initial render matches the deployed dotnet-test chart count');
  const filterBar = document.getElementById('model-filter-sample');
  const checkboxes = filterBar.querySelectorAll('input');
  assert.equal(checkboxes.length, 2);

  checkboxes[0].checked = false;
  checkboxes[0].dispatch('change');
  assert.equal(animationFrames.length, 1, 'filter refresh is scheduled for the next frame');
  animationFrames.shift()();

  assert.equal(FakeChart.instances.length, 440, 'filtering reuses all original chart instances');
  assert.ok(FakeChart.instances.every(chart => chart.destroyCalls === 0));
  assert.ok(FakeChart.instances.every(chart => chart.updateCalls.length === 1));
  assert.ok(FakeChart.instances.every(chart => chart.updateCalls[0] === 'none'));
  assert.ok(FakeChart.instances.every(chart => chart.data.labels.length === 1));

  const qualityChart = FakeChart.instances.find(chart =>
    chart.data.datasets.some(dataset => dataset.label.includes('model-a'))
  );
  assert.ok(qualityChart);
  assert.ok(qualityChart.data.datasets.filter(dataset => dataset.label.includes('model-a')).every(dataset => dataset.hidden));
  assert.ok(qualityChart.data.datasets.filter(dataset => dataset.label.includes('model-b')).every(dataset => !dataset.hidden));

  checkboxes[0].checked = true;
  checkboxes[0].dispatch('change');
  checkboxes[0].checked = false;
  checkboxes[0].dispatch('change');
  assert.equal(animationFrames.length, 1, 'rapid toggles are coalesced into one refresh');
  animationFrames.shift()();

  assert.equal(FakeChart.instances.length, 440);
  assert.ok(FakeChart.instances.every(chart => chart.updateCalls.length === 2));
});

test('efficiency legend gives high overfitting precedence per entry without losing multi-issue detection', async (t) => {
  const issueEntry = efficiencyEntry('model-a', '2026-09-20T00:00:00Z', 10);
  const timeBench = issueEntry.benches.find(bench => bench.name === 'Example - Skilled Time');
  const tokenBench = issueEntry.benches.find(bench => bench.name === 'Example - Skilled Tokens In');
  timeBench.notActivated = true;
  timeBench.overfitting = 'high';
  tokenBench.overfitting = 'moderate';
  const survivingEntry = efficiencyEntry('model-b', '2026-09-21T00:00:00Z', 12);
  survivingEntry.benches.find(bench => bench.name === 'Example - Skilled Time').overfitting = 'moderate';

  const pluginData = {
    entries: {
      Quality: [
        qualityEntry('model-a', '2026-09-20T00:00:00Z', 8),
        qualityEntry('model-b', '2026-09-21T00:00:00Z', 7),
      ],
      Efficiency: [issueEntry, survivingEntry],
    },
  };
  const { animationFrames, document } = await renderDashboard(t, pluginData, 2);

  assert.equal(FakeChart.instances.length, 2);
  const efficiencyChart = FakeChart.instances.find(chart =>
    chart.data.datasets.some(dataset => dataset.label === 'Isolated Time (s)')
  );
  assert.ok(efficiencyChart);
  const notes = efficiencyChart.canvas.parentElement
    .querySelectorAll('.not-activated-legend')
    .map(note => note.innerHTML);
  assert.ok(notes.some(note => note.includes('High eval overfitting')));
  assert.ok(notes.some(note => note.includes('Moderate eval overfitting')));
  assert.ok(notes.some(note => note.includes('Multiple issues')));

  const checkboxes = document.getElementById('model-filter-sample').querySelectorAll('input');
  checkboxes[0].checked = false;
  checkboxes[0].dispatch('change');
  animationFrames.shift()();

  const filteredNotes = efficiencyChart.canvas.parentElement
    .querySelectorAll('.not-activated-legend')
    .map(note => note.innerHTML);
  assert.ok(filteredNotes.every(note => !note.includes('High eval overfitting')));
  assert.ok(filteredNotes.some(note => note.includes('Moderate eval overfitting')));
  assert.ok(filteredNotes.every(note => !note.includes('Multiple issues')));

  const tooltip = efficiencyChart.options.plugins.tooltip.callbacks.afterTitle([{ dataIndex: 0 }]);
  assert.match(tooltip, /Model: model-b/);
  assert.match(tooltip, /model-b efficiency/);
});

test('model filtering limits legend items and resets visibility metadata for the toggled model', async (t) => {
  const models = Array.from({ length: 10 }, (_, index) => `model-${index + 1}`);
  const pluginData = {
    entries: {
      Quality: models.map((model, index) =>
        qualityEntry(model, `2026-09-${String(index + 1).padStart(2, '0')}T00:00:00Z`, 8)
      ),
      Efficiency: [],
    },
  };
  const { animationFrames, document } = await renderDashboard(t, pluginData, 1);
  const qualityChart = FakeChart.instances[0];
  const checkboxes = document.getElementById('model-filter-sample').querySelectorAll('input');

  assert.equal(qualityChart.options.plugins.legend.display, false);
  qualityChart.setDatasetVisibility(0, false);
  checkboxes.slice(1).forEach(checkbox => {
    checkbox.checked = false;
    checkbox.dispatch('change');
  });
  assert.equal(animationFrames.length, 1);
  animationFrames.shift()();

  assert.equal(qualityChart.options.plugins.legend.display, true);
  assert.equal(
    qualityChart.options.plugins.legend.labels.generateLabels(qualityChart).length,
    2,
    'only the selected model variants appear in the legend'
  );
  assert.equal(
    qualityChart.getDatasetMeta(0).hidden,
    true,
    'filtering unrelated models preserves a user-hidden active series'
  );

  checkboxes[1].checked = true;
  checkboxes[1].dispatch('change');
  animationFrames.shift()();
  checkboxes[0].checked = false;
  checkboxes[0].dispatch('change');
  animationFrames.shift()();
  checkboxes[0].checked = true;
  checkboxes[0].dispatch('change');
  checkboxes[1].checked = false;
  checkboxes[1].dispatch('change');
  animationFrames.shift()();

  assert.equal(
    qualityChart.getDatasetMeta(0).hidden,
    false,
    'toggling a model off and on resets its Chart.js visibility override'
  );
});

test('model filtering hides and restores chart containers without selected measurements', async (t) => {
  const pluginData = {
    entries: {
      Quality: [
        qualityEntry('model-a', '2026-09-20T00:00:00Z', 8, ['Test A']),
        qualityEntry('model-b', '2026-09-21T00:00:00Z', 7, ['Test B']),
      ],
      Efficiency: [
        efficiencyEntry('model-a', '2026-09-20T00:00:00Z', 10, ['Test A']),
        efficiencyEntry('model-b', '2026-09-21T00:00:00Z', 12, ['Test B']),
      ],
    },
  };
  const { animationFrames, document } = await renderDashboard(t, pluginData, 4);
  const checkboxes = document.getElementById('model-filter-sample').querySelectorAll('input');
  const testAContainers = FakeChart.instances
    .filter(chart => chart.canvas.parentElement.innerHTML.includes('<h3>Test A</h3>'))
    .map(chart => chart.canvas.parentElement);
  const testBContainers = FakeChart.instances
    .filter(chart => chart.canvas.parentElement.innerHTML.includes('<h3>Test B</h3>'))
    .map(chart => chart.canvas.parentElement);

  assert.equal(testAContainers.length, 2);
  assert.equal(testBContainers.length, 2);
  checkboxes[0].checked = false;
  checkboxes[0].dispatch('change');
  animationFrames.shift()();

  assert.ok(testAContainers.every(container => container.style.display === 'none'));
  assert.ok(testBContainers.every(container => container.style.display === ''));

  checkboxes[0].checked = true;
  checkboxes[0].dispatch('change');
  animationFrames.shift()();

  assert.ok(testAContainers.every(container => container.style.display === ''));
  assert.ok(testBContainers.every(container => container.style.display === ''));
});
