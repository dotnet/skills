const test = require('node:test');
const assert = require('node:assert/strict');

test('skill value table shows one shared sample-count column', async () => {
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
          baseline: { n: 6, timeMs: 1000, tokens: 100 },
          treatment: { n: 6, timeMs: 800, tokens: 80 },
          activationExpected: 6,
          activationFired: 6,
          passTotal: 6,
          baselineFail: 2,
          treatmentFail: 1,
          hasPassData: true,
        }],
      }],
    }),
  });

  require('./skill-value.js');
  await window.initSkillValue();

  const header = wrap.innerHTML.match(/<thead><tr>(.*?)<\/tr><\/thead>/s)[1];
  assert.equal((header.match(/<th/g) || []).length, 7);
  assert.equal((header.match(/>n<\/th>/g) || []).length, 1);
  assert.match(wrap.innerHTML, /colspan="7"/);
});
