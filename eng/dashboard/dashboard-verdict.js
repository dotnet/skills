(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) {
    module.exports = api;
  }
  if (root) {
    root.VerdictDisplay = api;
  }
})(typeof window !== 'undefined' ? window : null, function () {
  const NO_CHANGE_LABELS = Object.freeze({
    all_ties: 'No preference',
    mixed: 'Mixed evidence',
    positive_tie_limited: 'Improvement signal, tie-limited',
    positive_unproven: 'Improvement signal, unproven',
    negative_tie_limited: 'Baseline signal, tie-limited',
    negative_unproven: 'Baseline signal, unproven',
    positive_sparse: 'Improvement too sparse',
    negative_sparse: 'Baseline signal too sparse',
  });

  function forVerdict(verdict) {
    const reasonCode = verdict && verdict.stateReason && verdict.stateReason.code;
    if (verdict && verdict.state === 'VALID_REGRESSION') {
      return { label: 'Objective regression', cls: 'fail' };
    }
    if (verdict && verdict.state === 'INVALID_INCONCLUSIVE') {
      return { label: 'Invalid or underpowered', cls: 'warning' };
    }
    if (reasonCode === 'activation_contract_failed' ||
        (verdict && verdict.activationContract && verdict.activationContract.passed === false)) {
      return { label: 'Activation contract failed', cls: 'fail' };
    }
    if (verdict && verdict.state === 'VALID_PASS') {
      return { label: 'Improved', cls: 'pass' };
    }
    const legacyPreferenceLoss = verdict &&
      (!verdict.state || verdict.state === 'VALID_NO_CHANGE') &&
      (verdict.preferenceRegressed || verdict.regressed);
    if (reasonCode === 'preference_regression_report_only' || legacyPreferenceLoss) {
      return { label: 'Preference loss (report only)', cls: 'warning' };
    }
    if (verdict && verdict.passed) {
      return { label: 'Improved (legacy)', cls: 'pass' };
    }
    if (verdict && verdict.state === 'VALID_NO_CHANGE') {
      const label = NO_CHANGE_LABELS[verdict.noChangeDiagnosis];
      if (label) return { label, cls: 'neutral' };
    }
    return { label: 'Not proven improved', cls: 'neutral' };
  }

  return { forVerdict, NO_CHANGE_LABELS };
});
