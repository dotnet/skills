const test = require('node:test');
const assert = require('node:assert/strict');
const { forVerdict, NO_CHANGE_LABELS } = require('./dashboard-verdict.js');

test('CommonJS import does not publish a Node global', () => {
  assert.equal(globalThis.VerdictDisplay, undefined);
});

test('renders every cause-specific no-clear-winner label', () => {
  const expected = {
    all_ties: 'No preference',
    mixed: 'Mixed evidence',
    positive_tie_limited: 'Improvement signal, tie-limited',
    positive_unproven: 'Improvement signal, unproven',
    negative_tie_limited: 'Baseline signal, tie-limited',
    negative_unproven: 'Baseline signal, unproven',
    positive_sparse: 'Improvement too sparse',
    negative_sparse: 'Baseline signal too sparse',
  };

  assert.deepEqual(NO_CHANGE_LABELS, expected);
  for (const [noChangeDiagnosis, label] of Object.entries(expected)) {
    assert.deepEqual(
      forVerdict({ state: 'VALID_NO_CHANGE', noChangeDiagnosis }),
      { label, cls: 'neutral' },
    );
  }
});

test('retained legacy evidence without a diagnosis uses the generic label', () => {
  assert.deepEqual(
    forVerdict({
      state: 'VALID_NO_CHANGE',
      stateReason: { code: 'no_credible_preference_change' },
      gateEvidence: { wins: 4, ties: 0, losses: 1, discordant: 5 },
    }),
    { label: 'Not proven improved', cls: 'neutral' },
  );
});
