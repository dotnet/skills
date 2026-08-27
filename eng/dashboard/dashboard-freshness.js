(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) {
    module.exports = api;
  }
  if (root) {
    root.EvidenceFreshness = api;
  }
})(typeof window !== 'undefined' ? window : null, function () {
  const DAY_MS = 24 * 60 * 60 * 1000;

  function normalizeCommitId(value) {
    return typeof value === 'string' && /^[0-9a-f]{7,40}$/i.test(value)
      ? value.toLowerCase()
      : null;
  }

  function commitsMatch(left, right) {
    const a = normalizeCommitId(left);
    const b = normalizeCommitId(right);
    return Boolean(a && b && (a.startsWith(b) || b.startsWith(a)));
  }

  function parseTimestamp(value) {
    const parsed = Date.parse(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  function assess(entry, dashboardMeta) {
    const evidenceCommit = entry && entry.commit ? entry.commit : {};
    const deployedCommit = dashboardMeta && dashboardMeta.deployedCommit
      ? dashboardMeta.deployedCommit
      : {};
    const evidenceId = normalizeCommitId(evidenceCommit.id);
    const deployedId = normalizeCommitId(deployedCommit.id);
    const evidenceTime = parseTimestamp(evidenceCommit.timestamp);
    const deployedTime = parseTimestamp(deployedCommit.timestamp);
    const ageMs = evidenceTime !== null && deployedTime !== null
      ? Math.max(0, deployedTime - evidenceTime)
      : null;
    const older = evidenceTime !== null && deployedTime !== null && evidenceTime < deployedTime;

    return {
      stale: Boolean(evidenceId && deployedId && !commitsMatch(evidenceId, deployedId)),
      comparable: Boolean(evidenceId && deployedId),
      evidenceId,
      deployedId,
      ageMs,
      older,
    };
  }

  function formatAge(ageMs) {
    if (!Number.isFinite(ageMs) || ageMs < 0) return 'an unknown interval';
    if (ageMs < 60 * 60 * 1000) return 'less than an hour';
    if (ageMs < DAY_MS) {
      const hours = Math.max(1, Math.floor(ageMs / (60 * 60 * 1000)));
      return `${hours} hour${hours === 1 ? '' : 's'}`;
    }
    const days = Math.max(1, Math.floor(ageMs / DAY_MS));
    return `${days} day${days === 1 ? '' : 's'}`;
  }

  return { assess, commitsMatch, formatAge };
});
