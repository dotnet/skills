const test = require('node:test');
const assert = require('node:assert/strict');
const { assess, commitsMatch, formatAge } = require('./dashboard-freshness.js');

test('CommonJS import does not publish a Node global', () => {
  assert.equal(globalThis.EvidenceFreshness, undefined);
});

test('matching full and abbreviated commits are current', () => {
  assert.equal(commitsMatch('d3921f7418de361ad95f842f9178f2c71ef9bbac', 'd3921f7'), true);
  const result = assess(
    { commit: { id: 'd3921f7', timestamp: '2026-08-27T08:23:00Z' } },
    { deployedCommit: { id: 'd3921f7418de361ad95f842f9178f2c71ef9bbac', timestamp: '2026-08-27T08:23:00Z' } },
  );
  assert.equal(result.stale, false);
  assert.equal(result.comparable, true);
});

test('different evidence and deployment commits are stale with commit age', () => {
  const result = assess(
    { commit: { id: '98f848512e9ee4877e399a0ae367bb5e4a193144', timestamp: '2026-08-24T07:00:00Z' } },
    { deployedCommit: { id: 'd3921f7418de361ad95f842f9178f2c71ef9bbac', timestamp: '2026-08-27T08:23:00Z' } },
  );
  assert.equal(result.stale, true);
  assert.equal(result.comparable, true);
  assert.equal(result.older, true);
  assert.equal(formatAge(result.ageMs), '3 days');
});

test('missing deployment metadata does not invent a stale result', () => {
  const result = assess(
    { commit: { id: 'd3921f7418de361ad95f842f9178f2c71ef9bbac', timestamp: '2026-08-27T08:23:00Z' } },
    null,
  );
  assert.equal(result.stale, false);
  assert.equal(result.comparable, false);
  assert.equal(result.ageMs, null);
});

test('different commits without comparable timestamps do not invent age', () => {
  const result = assess(
    { commit: { id: '98f848512e9ee4877e399a0ae367bb5e4a193144' } },
    { deployedCommit: { id: 'd3921f7418de361ad95f842f9178f2c71ef9bbac' } },
  );
  assert.equal(result.stale, true);
  assert.equal(result.older, false);
  assert.equal(result.ageMs, null);
});
