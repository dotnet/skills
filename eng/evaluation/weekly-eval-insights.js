#!/usr/bin/env node

const fs = require('node:fs');
const path = require('node:path');

const DAY_MS = 24 * 60 * 60 * 1000;

function parseArgs(args) {
  const values = {};
  for (let index = 0; index < args.length; index += 2) {
    const key = args[index];
    const value = args[index + 1];
    if (!key?.startsWith('--') || value === undefined) {
      throw new Error(`Invalid argument near ${key ?? '<end>'}`);
    }
    values[key.slice(2)] = value;
  }
  return values;
}

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

function mean(values) {
  if (values.length === 0) return null;
  return values.reduce((sum, value) => sum + value, 0) / values.length;
}

function ratio(numerator, denominator) {
  return denominator > 0 ? numerator / denominator : null;
}

function round(value, digits = 4) {
  if (value === null || !Number.isFinite(value)) return null;
  return Number(value.toFixed(digits));
}

function flattenWorkflowRuns(input) {
  const pages = Array.isArray(input) ? input : [input];
  return pages.flatMap(page => Array.isArray(page?.workflow_runs) ? page.workflow_runs : []);
}

function isDifferentiated(skill) {
  const preference = skill.preference;
  if (!preference || preference.conclusive !== true || preference.underpowered === true) {
    return false;
  }
  const alpha = Number.isFinite(preference.alpha) ? preference.alpha : 0.05;
  return preference.direction === 'better'
    && preference.practicalPassed === true
    && Number.isFinite(preference.pValue)
    && preference.pValue <= alpha
    && skill.activationContract?.passed !== false;
}

function isPreferenceLoss(skill) {
  const preference = skill.preference;
  if (!preference || preference.conclusive !== true || preference.underpowered === true) {
    return false;
  }
  const alpha = Number.isFinite(preference.alpha) ? preference.alpha : 0.05;
  return preference.direction === 'worse'
    && preference.practicalPassed === true
    && Number.isFinite(preference.pValue)
    && preference.pValue <= alpha
    && skill.activationContract?.passed !== false;
}

function classify(skill) {
  if (skill.activationContract?.passed === false) return 'activation-failed';
  if (!skill.preference || skill.preference.conclusive !== true) return 'invalid';
  if (skill.preference.underpowered === true) return 'underpowered';
  if (isDifferentiated(skill)) return 'differentiated';
  if (isPreferenceLoss(skill)) return 'preference-loss';
  return 'no-change';
}

function weightedMetric(records, arm, field) {
  let total = 0;
  let count = 0;
  for (const record of records) {
    const metrics = record.skill[arm];
    const n = Number(metrics?.n);
    const value = Number(metrics?.[field]);
    if (n > 0 && Number.isFinite(value)) {
      total += value * n;
      count += n;
    }
  }
  return {
    n: count,
    value: count > 0 ? total / count : null,
  };
}

function deduplicateMetricRecords(records) {
  const unique = new Map();
  for (const record of records) {
    const commit = record.commit?.id ?? record.commit?.url ?? 'unknown-commit';
    const key = [
      record.plugin,
      record.skill.skill,
      record.model,
      commit,
      record.date,
    ].join('\u0000');
    if (!unique.has(key)) unique.set(key, record);
  }
  return [...unique.values()];
}

function aggregateSkill(records) {
  const metricRecords = deduplicateMetricRecords(records);
  const expectedResults = records.length;
  const conclusiveResults = records.filter(record => record.skill.preference?.conclusive === true).length;
  const differentiatedResults = records.filter(record => isDifferentiated(record.skill)).length;
  const preferenceLossResults = records.filter(record => isPreferenceLoss(record.skill)).length;
  const invalidResults = expectedResults - conclusiveResults;
  const underpoweredResults = records.filter(record => record.skill.preference?.underpowered === true).length;
  const timedOutResults = records.filter(record => record.skill.timedOut === true).length;
  const activationFailures = records.filter(record => record.skill.activationContract?.passed === false).length;
  const passTotal = metricRecords.reduce((sum, record) => sum + (Number(record.skill.passTotal) || 0), 0);
  const treatmentFailures = metricRecords.reduce(
    (sum, record) => sum + (Number(record.skill.treatmentFail) || 0),
    0,
  );
  const baselineFailures = metricRecords.reduce(
    (sum, record) => sum + (Number(record.skill.baselineFail) || 0),
    0,
  );
  const treatmentTime = weightedMetric(metricRecords, 'treatment', 'timeMs');
  const baselineTime = weightedMetric(metricRecords, 'baseline', 'timeMs');
  const treatmentTokens = weightedMetric(metricRecords, 'treatment', 'tokens');
  const baselineTokens = weightedMetric(metricRecords, 'baseline', 'tokens');
  const statuses = [...new Set(records.map(record => classify(record.skill)))].sort();
  const models = [...new Set(records.map(record => record.model).filter(Boolean))].sort();
  const judgeModels = [...new Set(records.map(record => record.judgeModel).filter(Boolean))].sort();
  const flakyDimensions = [];
  const stableDimensionStatuses = [];
  for (const [dimension, dimensionRecords] of groupBy(
    records,
    record => `${record.model ?? 'unknown'}\u0000${record.judgeModel ?? 'unknown'}`,
  )) {
    const dimensionStatuses = [...new Set(
      dimensionRecords.map(record => classify(record.skill)),
    )].sort();
    if (dimensionStatuses.length > 1) {
      const [model, judgeModel] = dimension.split('\u0000');
      flakyDimensions.push({ model, judgeModel, statuses: dimensionStatuses });
    } else if (dimensionStatuses.length === 1) {
      stableDimensionStatuses.push(dimensionStatuses[0]);
    }
  }

  return {
    expectedResults,
    conclusiveResults,
    invalidResults,
    underpoweredResults,
    differentiatedResults,
    preferenceLossResults,
    reliabilityRate: round(ratio(conclusiveResults, expectedResults)),
    differentiationRate: round(ratio(differentiatedResults, conclusiveResults)),
    treatmentPassRate: round(ratio(passTotal - treatmentFailures, passTotal)),
    baselinePassRate: round(ratio(passTotal - baselineFailures, passTotal)),
    timedOutResults,
    activationFailures,
    metricObservationCount: metricRecords.length,
    averageTreatmentDurationMs: round(treatmentTime.value, 1),
    averageBaselineDurationMs: round(baselineTime.value, 1),
    averageTreatmentTokens: round(treatmentTokens.value, 1),
    averageBaselineTokens: round(baselineTokens.value, 1),
    pairedScenarioCount: treatmentTime.n,
    models,
    judgeModels,
    statuses,
    flaky: flakyDimensions.length > 0,
    flakyDimensions,
    modelSensitive:
      flakyDimensions.length === 0
      && new Set(stableDimensionStatuses).size > 1,
    consistentlyPassing:
      expectedResults >= 2
      && conclusiveResults === expectedResults
      && passTotal > 0
      && treatmentFailures === 0,
    consistentlyDifferentiating:
      expectedResults >= 2 && differentiatedResults === expectedResults && conclusiveResults === expectedResults,
  };
}

function collectRecords(entries, startMs, endMs) {
  const records = [];
  for (const entry of entries) {
    if (!Number.isFinite(entry?.date) || entry.date < startMs || entry.date >= endMs) continue;
    for (const skill of entry.skills ?? []) {
      records.push({
        plugin: entry.plugin,
        model: entry.model,
        judgeModel: entry.judgeModel,
        date: entry.date,
        commit: entry.commit,
        skill,
      });
    }
  }
  return records;
}

function groupBy(records, keySelector) {
  const groups = new Map();
  for (const record of records) {
    const key = keySelector(record);
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(record);
  }
  return groups;
}

function aggregateWindow(entries, startMs, endMs) {
  const records = collectRecords(entries, startMs, endMs);
  const overall = aggregateSkill(records);
  const plugins = [];

  for (const [plugin, pluginRecords] of [...groupBy(records, record => record.plugin).entries()].sort()) {
    const skills = [];
    for (const [skill, skillRecords] of [...groupBy(pluginRecords, record => record.skill.skill).entries()].sort()) {
      skills.push({
        skill,
        ...aggregateSkill(skillRecords),
      });
    }
    plugins.push({
      plugin,
      ...aggregateSkill(pluginRecords),
      skills,
    });
  }

  const allSkills = plugins.flatMap(plugin =>
    plugin.skills.map(skill => ({ plugin: plugin.plugin, ...skill })));
  const descending = (field) => (left, right) =>
    (right[field] ?? Number.NEGATIVE_INFINITY) - (left[field] ?? Number.NEGATIVE_INFINITY);

  return {
    start: new Date(startMs).toISOString(),
    end: new Date(endMs).toISOString(),
    expectedResults: records.length,
    conclusiveResults: records.filter(record => record.skill.preference?.conclusive === true).length,
    invalidResults: records.filter(record => record.skill.preference?.conclusive !== true).length,
    underpoweredResults: records.filter(record => record.skill.preference?.underpowered === true).length,
    differentiatedResults: records.filter(record => isDifferentiated(record.skill)).length,
    preferenceLossResults: records.filter(record => isPreferenceLoss(record.skill)).length,
    reliabilityRate: round(ratio(
      records.filter(record => record.skill.preference?.conclusive === true).length,
      records.length,
    )),
    differentiationRate: round(ratio(
      records.filter(record => isDifferentiated(record.skill)).length,
      records.filter(record => record.skill.preference?.conclusive === true).length,
    )),
    models: [...new Set(records.map(record => record.model).filter(Boolean))].sort(),
    judgeModels: [...new Set(records.map(record => record.judgeModel).filter(Boolean))].sort(),
    treatmentPassRate: overall.treatmentPassRate,
    baselinePassRate: overall.baselinePassRate,
    averageTreatmentDurationMs: overall.averageTreatmentDurationMs,
    averageBaselineDurationMs: overall.averageBaselineDurationMs,
    averageTreatmentTokens: overall.averageTreatmentTokens,
    averageBaselineTokens: overall.averageBaselineTokens,
    metricObservationCount: overall.metricObservationCount,
    pairedScenarioCount: overall.pairedScenarioCount,
    plugins,
    signals: {
      flaky: allSkills.filter(skill => skill.flaky)
        .sort((left, right) => left.plugin.localeCompare(right.plugin) || left.skill.localeCompare(right.skill)),
      consistentlyPassing: allSkills.filter(skill => skill.consistentlyPassing)
        .sort((left, right) => left.plugin.localeCompare(right.plugin) || left.skill.localeCompare(right.skill)),
      consistentlyDifferentiating: allSkills.filter(skill => skill.consistentlyDifferentiating)
        .sort((left, right) => left.plugin.localeCompare(right.plugin) || left.skill.localeCompare(right.skill)),
      preferenceLosses: allSkills.filter(skill => skill.preferenceLossResults > 0)
        .sort((left, right) =>
          right.preferenceLossResults - left.preferenceLossResults
          || left.plugin.localeCompare(right.plugin)
          || left.skill.localeCompare(right.skill)),
      modelSensitive: allSkills.filter(skill => skill.modelSensitive)
        .sort((left, right) => left.plugin.localeCompare(right.plugin) || left.skill.localeCompare(right.skill)),
      slowest: allSkills.filter(skill => skill.averageTreatmentDurationMs !== null)
        .sort(descending('averageTreatmentDurationMs'))
        .slice(0, 10),
      highestTokenUse: allSkills.filter(skill => skill.averageTreatmentTokens !== null)
        .sort(descending('averageTreatmentTokens'))
        .slice(0, 10),
      leastReliable: allSkills
        .filter(skill => skill.reliabilityRate !== null && skill.reliabilityRate < 1)
        .sort((left, right) => left.reliabilityRate - right.reliabilityRate)
        .slice(0, 10),
    },
  };
}

function aggregateRuns(runs, startMs, endMs) {
  const selected = runs.filter(run => {
    const created = Date.parse(run.created_at);
    return Number.isFinite(created) && created >= startMs && created < endMs;
  });
  const completedDurations = selected
    .map(run => [Date.parse(run.run_started_at ?? run.created_at), Date.parse(run.updated_at)])
    .filter(([start, end]) => Number.isFinite(start) && Number.isFinite(end) && end >= start)
    .map(([start, end]) => end - start);
  const conclusions = {};
  for (const run of selected) {
    const key = run.conclusion ?? run.status ?? 'unknown';
    conclusions[key] = (conclusions[key] ?? 0) + 1;
  }
  return {
    count: selected.length,
    conclusions,
    averageDurationMs: round(mean(completedDurations), 1),
    references: selected
      .filter(run => run.html_url)
      .sort((left, right) => Date.parse(right.created_at) - Date.parse(left.created_at))
      .slice(0, 3)
      .map(run => ({
        id: run.id,
        conclusion: run.conclusion,
        url: run.html_url,
      })),
  };
}

function buildReport({ dashboard, workflowRuns, windowEnd }) {
  const endMs = Date.parse(windowEnd);
  if (!Number.isFinite(endMs)) throw new Error(`Invalid --window-end value: ${windowEnd}`);
  const startMs = endMs - (7 * DAY_MS);
  const previousStartMs = startMs - (7 * DAY_MS);

  return {
    schemaVersion: 1,
    generatedAt: new Date().toISOString(),
    weekKey: isoWeekKey(new Date(endMs - 1)),
    metricContract: {
      observation:
        'One published plugin, skill, executor-model, and judge-model evaluation result in the reporting window.',
      differentiation:
        'A conclusive, sufficiently powered treatment win with p <= alpha, practical significance, and a passing activation contract.',
      preferenceLoss:
        'A conclusive, sufficiently powered reverse preference with p <= alpha, practical significance, and a passing activation contract. This is triage evidence, not an objective completion regression.',
      reliability: 'Conclusive expected evaluation results divided by expected evaluation results.',
      passRate: 'Treatment completion passes divided by trials with explicit paired pass evidence.',
      resourceDeduplication:
        'Resource metrics remove only exact duplicate judge views with the same plugin, skill, executor model, commit, and evaluation timestamp. Distinct runs are preserved.',
      aiCredits:
        'Unavailable: official evaluation CI does not emit authoritative AI Credits. Token telemetry is reported without estimating credits.',
    },
    workflowRuns: aggregateRuns(workflowRuns, startMs, endMs),
    current: aggregateWindow(dashboard.entries ?? [], startMs, endMs),
    previous: aggregateWindow(dashboard.entries ?? [], previousStartMs, startMs),
    aiCredits: {
      available: false,
      value: null,
      reason:
        'Official evaluation CI publishes token usage but not the normalized AI Credits value or a versioned pricing record.',
    },
  };
}

function isoWeek(date) {
  const current = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()));
  const day = current.getUTCDay() || 7;
  current.setUTCDate(current.getUTCDate() + 4 - day);
  const yearStart = new Date(Date.UTC(current.getUTCFullYear(), 0, 1));
  return String(Math.ceil((((current - yearStart) / DAY_MS) + 1) / 7)).padStart(2, '0');
}

function isoWeekKey(date) {
  const current = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()));
  const day = current.getUTCDay() || 7;
  current.setUTCDate(current.getUTCDate() + 4 - day);
  return `${current.getUTCFullYear()}-W${isoWeek(date)}`;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const required = ['dashboard-file', 'runs-file', 'window-end', 'output'];
  for (const key of required) {
    if (!args[key]) throw new Error(`Missing required argument --${key}`);
  }

  const report = buildReport({
    dashboard: readJson(args['dashboard-file']),
    workflowRuns: flattenWorkflowRuns(readJson(args['runs-file'])),
    windowEnd: args['window-end'],
  });
  fs.mkdirSync(path.dirname(args.output), { recursive: true });
  fs.writeFileSync(args.output, `${JSON.stringify(report, null, 2)}\n`);
}

if (require.main === module) {
  main();
}

module.exports = {
  aggregateRuns,
  aggregateSkill,
  aggregateWindow,
  buildReport,
  classify,
  flattenWorkflowRuns,
  isDifferentiated,
  isPreferenceLoss,
  isoWeek,
  isoWeekKey,
};
