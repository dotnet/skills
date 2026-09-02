// Skill Value dashboard view
// Loaded by dashboard.html, exposes window.initSkillValue().
//
// Answers, per skill: "Add this skill and model X uses ~M% fewer tokens and
// ~Y% less time for typical scenarios; without it ~p% of those scenarios do not
// pass their checks."
// Data source: compact `data/skill-value.json`, derived from the
// `entries.SkillValue` arrays in per-plugin dashboard data by
// eng/dashboard/generate-benchmark-data.ps1. Both arms — baseline (without the
// skill) and treatment (with the skill) — are carried per run. The compact index
// avoids downloading every plugin's large Quality/Efficiency history on startup.
(function () {
  let initialized = false;
  let initialization = null;

  // ── Tunable thresholds (documented in eng/dashboard skill-value design) ──
  // Minimum PAIRED OBSERVATIONS (scenario-runs measured in both arms, summed
  // across the trailing window) before a stat earns a number instead of
  // "insufficient signal". This is a minimum-observations floor for a stable
  // trailing mean, NOT a distinct-stimulus breadth guarantee: e.g. one scenario
  // measured across 5 runs also reaches n=5. The distinct scenario breadth per
  // run and the run count are shown in the drill-down so the two are not
  // conflated. 5 mirrors the harness's MIN_CREDIBLE_STIMULI as a familiar floor.
  const MIN_SAMPLES = 5;
  // The plain-English value sentence is suppressed below this activation fraction:
  // if the skill rarely fires, the treatment arm behaves like baseline and the
  // delta is diluted rather than real.
  const ACTIVATION_MIN = 0.5;
  // Trailing window: most-recent N runs per (skill, executor model, judge model).
  const TRAILING_RUNS = 20;
  // Headline "tokens" = input + output (the adapter's tokenEstimate / totalTokens).
  // Cache read/write are billed/served differently and swing with infra caching,
  // so they are shown only in the drill-down, never in the headline delta.

  function escapeHtml(str) {
    if (str == null) return '';
    const div = document.createElement('div');
    div.textContent = String(str);
    return div.innerHTML;
  }

  function fmtInt(n) { return Math.round(n).toLocaleString(); }
  function fmtK(n) {
    if (n == null) return '–';
    if (Math.abs(n) >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M';
    if (Math.abs(n) >= 1_000) return (n / 1_000).toFixed(1) + 'k';
    return Math.round(n).toString();
  }
  function fmtPct(frac) { return (frac * 100).toFixed(0) + '%'; }
  function fmtSecs(ms) { return (ms / 1000).toFixed(1) + 's'; }

  // n-weighted running accumulator for one arm's metrics across runs.
  function newArm() {
    return { n: 0, timeMs: 0, tokens: 0, tokensIn: 0, tokensOut: 0, cacheRead: 0, cacheWrite: 0 };
  }
  function addArm(acc, arm) {
    const w = arm && arm.n ? arm.n : 0;
    if (w <= 0) return;
    acc.n += w;
    acc.timeMs += (arm.timeMs || 0) * w;
    acc.tokens += (arm.tokens || 0) * w;
    acc.tokensIn += (arm.tokensIn || 0) * w;
    acc.tokensOut += (arm.tokensOut || 0) * w;
    acc.cacheRead += (arm.cacheRead || 0) * w;
    acc.cacheWrite += (arm.cacheWrite || 0) * w;
  }
  function meanArm(acc) {
    if (acc.n <= 0) return null;
    return {
      n: acc.n,
      timeMs: acc.timeMs / acc.n,
      tokens: acc.tokens / acc.n,
      tokensIn: acc.tokensIn / acc.n,
      tokensOut: acc.tokensOut / acc.n,
      cacheRead: acc.cacheRead / acc.n,
      cacheWrite: acc.cacheWrite / acc.n,
    };
  }

  // ── Public entry point ──────────────────────────────────────────────
  window.initSkillValue = async function () {
    if (initialized) return;
    if (initialization) return initialization;

    const container = document.getElementById('skill-value-content');
    initialization = (async () => {
      try {
        const res = await fetch('data/skill-value.json');
        if (!res.ok) throw new Error(res.statusText);
        const data = await res.json();
        const entries = Array.isArray(data.entries) ? data.entries : [];

        if (entries.length === 0) {
          container.innerHTML = '<p style="color:#8b949e;text-align:center;padding:2rem;">No skill-value data available yet. It is produced by scheduled evaluation runs.</p>';
        } else {
          render(container, entries);
        }
        initialized = true;
      } catch {
        container.innerHTML = '<p style="color:#8b949e;text-align:center;padding:2rem;">Unable to load skill-value data. Select the Skill Value tab to retry.</p>';
      } finally {
        initialization = null;
      }
    })();

    return initialization;
  };

  // Aggregate entries into per-(skill, model, judgeModel) rows over the trailing
  // window. Never blends different model strings — model version is part of the key.
  function aggregate(entries) {
    // Group runs by key.
    const groups = new Map();
    for (const e of entries) {
      const model = e.model || 'unknown';
      const judge = e.judgeModel || 'unknown';
      for (const s of (e.skills || [])) {
        // Key includes plugin: two plugins may share a skill name, and the view is
        // grouped Plugin -> Skill -> Model, so their histories must never blend.
        const key = `${e.plugin}\u0000${s.skill}\u0000${model}\u0000${judge}`;
        if (!groups.has(key)) groups.set(key, { skill: s.skill, plugin: e.plugin, model, judge, runs: [] });
        groups.get(key).runs.push({ date: e.date || 0, s });
      }
    }

    const rows = [];
    for (const g of groups.values()) {
      // Trailing window: most-recent runs only.
      const runs = g.runs.sort((a, b) => b.date - a.date).slice(0, TRAILING_RUNS);
      const base = newArm();
      const treat = newArm();
      let actExpected = 0, actFired = 0;
      let passTotal = 0, baseFail = 0, treatFail = 0;
      let hasPass = false;
      let timedOutRuns = 0, baseAvail = 0, treatAvail = 0;
      for (const { s } of runs) {
        addArm(base, s.baseline);
        addArm(treat, s.treatment);
        actExpected += s.activationExpected || 0;
        actFired += s.activationFired || 0;
        passTotal += s.passTotal || 0;
        baseFail += s.baselineFail || 0;
        treatFail += s.treatmentFail || 0;
        if (s.hasPassData) hasPass = true;
        if (s.timedOut) timedOutRuns += 1;
        baseAvail += s.baseAvailable || 0;
        treatAvail += s.treatAvailable || 0;
      }
      rows.push({
        skill: g.skill, plugin: g.plugin, model: g.model, judge: g.judge,
        runCount: runs.length,
        baseline: meanArm(base), treatment: meanArm(treat),
        activation: actExpected > 0 ? actFired / actExpected : null,
        activationExpected: actExpected, activationFired: actFired,
        passTotal, baseFail, treatFail, hasPass,
        timedOutRuns, baseAvail, treatAvail,
      });
    }
    rows.sort((a, b) => a.skill.localeCompare(b.skill) || a.model.localeCompare(b.model) || a.judge.localeCompare(b.judge));
    return rows;
  }

  // Relative reduction of treatment vs baseline (positive = treatment is smaller).
  function reduction(baseVal, treatVal) {
    if (baseVal == null || treatVal == null || baseVal === 0) return null;
    return (baseVal - treatVal) / baseVal;
  }

  // Render a reduction as a signed percent: '−N%' = N% less (good), '+N%' = N%
  // more, '0%' = no measurable change. Zero — exact or rounded — never gets a
  // sign, so "no change" is not misread as a +0% regression.
  function signedPct(r, nullText) {
    if (r == null) return nullText;
    const pct = Math.round(r * 100);
    if (pct === 0) return '0%';
    return `${pct > 0 ? '−' : '+'}${Math.abs(pct)}%`;
  }

  function deltaCell(base, treat, unitFmt, diluted) {
    if (base == null || treat == null) return '<td class="num">–</td>';
    const r = reduction(base, treat);
    const abs = treat - base;
    const cls = r == null ? 'neutral' : (r > 0 ? 'positive' : (r < 0 ? 'negative' : 'neutral'));
    const sign = abs > 0 ? '+' : '';
    // Null reduction (baseline metric is 0) shows an explicit "n/a", matching the
    // rollup cells, so the percent is never a blank/ambiguous label.
    const pctTxt = signedPct(r, 'n/a');
    // When the skill barely fires, the treatment arm is mostly baseline, so this
    // delta understates the on-activation effect. Mark it so it is not misread.
    const dil = diluted ? ' sv-diluted' : '';
    const mark = diluted ? '<span class="sv-dilute-mark" title="Skill fired in a minority of runs — delta is diluted toward baseline">≈</span>' : '';
    return `<td class="num${dil}">${mark}<span class="${cls}">${pctTxt}</span>` +
      `<span class="sv-sub">${escapeHtml(unitFmt(base))} → ${escapeHtml(unitFmt(treat))} (${sign}${escapeHtml(unitFmt(abs))})</span></td>`;
  }

  function gated(n) { return n != null && n >= MIN_SAMPLES; }

  function valueSentence(row) {
    const pairedN = Math.min(row.baseline ? row.baseline.n : 0, row.treatment ? row.treatment.n : 0);
    if (!gated(pairedN)) return { text: `Insufficient signal (n=${pairedN} paired, need ≥${MIN_SAMPLES})`, cls: 'sv-insufficient' };
    // Activation contamination guard. If the skill is not confirmed to fire in
    // most expected scenarios, the treatment arm behaves like baseline and any
    // delta is diluted — so suppress the confident value claim. A null activation
    // means NO scenario declared expect_activation, so we cannot confirm the skill
    // fired at all; treat that as unverified rather than asserting value.
    if (row.activation == null) {
      return { text: 'Insufficient signal — no expected-activation scenarios, so the skill firing is unverified', cls: 'sv-insufficient' };
    }
    if (row.activation < ACTIVATION_MIN) {
      return { text: `Insufficient signal — skill fired in only ${fmtPct(row.activation)} of expected scenarios (delta is diluted)`, cls: 'sv-insufficient' };
    }
    const tokR = reduction(row.baseline.tokens, row.treatment.tokens);
    const timeR = reduction(row.baseline.timeMs, row.treatment.timeMs);
    // A null reduction means the baseline value was zero/absent — do not coerce it
    // into a "~0% fewer" claim. Fall back to reporting the metric as unavailable.
    if (tokR == null || timeR == null) {
      return { text: `Add <b>${escapeHtml(row.skill)}</b>: token/time delta unavailable for <b>${escapeHtml(row.model)}</b> (a baseline metric was zero or missing).`, cls: 'sv-insufficient' };
    }
    const tokWord = tokR >= 0 ? 'fewer' : 'more';
    const timeWord = timeR >= 0 ? 'less' : 'more';
    let s = `Add <b>${escapeHtml(row.skill)}</b> and <b>${escapeHtml(row.model)}</b> uses ~${Math.abs(tokR * 100).toFixed(0)}% ${tokWord} tokens and ~${Math.abs(timeR * 100).toFixed(0)}% ${timeWord} time for typical scenarios`;
    if (row.hasPass && gated(row.passTotal)) {
      const baseFailFrac = row.passTotal > 0 ? row.baseFail / row.passTotal : 0;
      s += `; without it ~${fmtPct(baseFailFrac)} of its counted trials do not pass their checks.`;
    } else if (row.hasPass) {
      s += ` (not-passed rate still gathering data: n=${row.passTotal}, need ≥${MIN_SAMPLES}).`;
    } else {
      s += '. (No pass/fail data for a not-passed estimate.)';
    }
    return { text: s, cls: 'sv-value' };
  }

  function failureCell(row) {
    if (!row.hasPass || row.passTotal === 0) return '<td class="num" title="No counted baseline/treatment pass data is available">N/A</td>';
    if (!gated(row.passTotal)) return `<td class="num"><span class="sv-insufficient">n=${row.passTotal}</span></td>`;
    const b = row.baseFail / row.passTotal;
    const t = row.treatFail / row.passTotal;
    const cls = t < b ? 'positive' : (t > b ? 'negative' : 'neutral');
    return `<td class="num"><span class="${cls}">${fmtPct(b)} → ${fmtPct(t)}</span>` +
      `<span class="sv-sub">n=${row.passTotal}</span></td>`;
  }

  function activationCell(row) {
    if (row.activation == null) return '<td class="num">–</td>';
    const cls = row.activation >= ACTIVATION_MIN ? 'positive' : 'negative';
    return `<td class="num"><span class="${cls}">${fmtPct(row.activation)}</span>` +
      `<span class="sv-sub">${row.activationFired}/${row.activationExpected}</span></td>`;
  }

  function metricCell(base, treat, fmt) {
    // Delta cell already shows both arms; this is the paired-n column.
    const n = Math.min(base ? base.n : 0, treat ? treat.n : 0);
    const cls = gated(n) ? 'neutral' : 'sv-insufficient';
    return `<td class="num"><span class="${cls}">n=${n}</span></td>`;
  }

  function drilldown(row) {
    const arm = (a, label) => {
      if (!a) return `<div class="sv-arm"><span class="sv-arm-label">${label}</span> <span class="sv-sub">no metrics</span></div>`;
      return `<div class="sv-arm"><span class="sv-arm-label">${label}</span> ` +
        `time ${fmtSecs(a.timeMs)} · tokens ${fmtK(a.tokens)} ` +
        `(in ${fmtK(a.tokensIn)} / out ${fmtK(a.tokensOut)}) · ` +
        `cache read ${fmtK(a.cacheRead)} / write ${fmtK(a.cacheWrite)} · n=${a.n}</div>`;
    };
    const pairedN = Math.min(row.baseline ? row.baseline.n : 0, row.treatment ? row.treatment.n : 0);
    // Surface how many scenarios each arm measured on its own vs. the paired set,
    // and any timed-out runs — both indicate selective missingness that could bias
    // an unpaired reading (e.g. treatment timeouts dropping the slowest cases).
    let notes = `<div class="sv-sub" style="margin-top:6px;">Paired observations n=${pairedN}` +
      ` · measured alone: baseline ${row.baseAvail}, treatment ${row.treatAvail}`;
    if (row.timedOutRuns > 0) notes += ` · ⚠ ${row.timedOutRuns} run(s) had a timed-out scenario`;
    notes += '</div>';
    return `<div class="sv-drill">` +
      `<div class="sv-sub" style="margin-bottom:6px;">${escapeHtml(row.plugin)} · executor <b>${escapeHtml(row.model)}</b> · judge <b>${escapeHtml(row.judge)}</b> · ${row.runCount} run(s) in window</div>` +
      arm(row.baseline, 'Without skill') + arm(row.treatment, 'With skill') +
      notes +
      `</div>`;
  }

  // A (skill, executor, judge) leaf reached a confident value claim.
  function isConfirmed(row) { return valueSentence(row).cls === 'sv-value'; }

  function pctSigned(r) { return signedPct(r, 'n/a'); }

  // Rollup for a group that resolves to a SINGLE model leaf (e.g. a skill with one
  // model, or any group once the viewer filters to one executor/judge). With one
  // model there is nothing to blend, so show that model's ACTUAL deltas instead of
  // a bare count — this is the header working under a single-model filter.
  function singleModelRollup(row) {
    const tokR = reduction(row.baseline ? row.baseline.tokens : null, row.treatment ? row.treatment.tokens : null);
    const timeR = reduction(row.baseline ? row.baseline.timeMs : null, row.treatment ? row.treatment.timeMs : null);
    const diluted = row.activation != null && row.activation < ACTIVATION_MIN;
    const mark = diluted ? '<span class="sv-dilute-mark" title="Skill fired in a minority of runs — delta is diluted toward baseline">≈</span>' : '';
    const tag = isConfirmed(row)
      ? '<span class="positive">value confirmed</span>'
      : '<span class="sv-insufficient">gathering data</span>';
    return `${escapeHtml(row.model)}: ${mark}${pctSigned(tokR)} tokens · ${pctSigned(timeR)} time · ${tag}`;
  }

  // Rollup for a multi-model group. Avoids the ambiguous "0/m" (which reads like a
  // failure): it states how many models show a measured value AND how many are
  // still gathering data, so an all-insufficient group reads as "awaiting data",
  // never as "the skill failed".
  function countRollup(rows) {
    const conf = rows.filter(isConfirmed).length;
    const insuff = rows.length - conf;
    let s = `${conf} of ${rows.length} model/judge result(s) show measured value`;
    if (insuff > 0) s += ` · ${insuff} still gathering data (need ≥${MIN_SAMPLES} paired obs)`;
    return s;
  }

  function render(container, entries) {
    const allRows = aggregate(entries);
    const models = [...new Set(allRows.map(r => r.model))].sort();
    const judges = [...new Set(allRows.map(r => r.judge))].sort();

    container.innerHTML =
      `<div class="sv-controls">` +
      `<label>Executor model <select id="sv-model"><option value="">All</option>${models.map(m => `<option>${escapeHtml(m)}</option>`).join('')}</select></label>` +
      `<label>Judge model <select id="sv-judge"><option value="">All</option>${judges.map(m => `<option>${escapeHtml(m)}</option>`).join('')}</select></label>` +
      `<button type="button" id="sv-expand" class="sv-btn">Expand all</button>` +
      `<button type="button" id="sv-collapse" class="sv-btn">Collapse all</button>` +
      `<span class="sv-sub">Grouped Plugin → Skill → Model. Trailing average over the last ${TRAILING_RUNS} runs per (skill, executor, judge). Gate ≥${MIN_SAMPLES} paired observations. Tokens = input+output. ≈ marks a diluted (low-activation) delta. Deltas are never blended across models.</span>` +
      `</div>` +
      `<div id="sv-table-wrap"></div>`;

    const modelSel = container.querySelector('#sv-model');
    const judgeSel = container.querySelector('#sv-judge');
    const wrap = container.querySelector('#sv-table-wrap');

    // A Plugin/Skill group-header row. Metric columns are intentionally blank:
    // token/time deltas are RELATIVE to each model's own baseline, so they cannot
    // be averaged across different executor models without misleading — the same
    // "never blend model versions" rule the leaf rows follow. The header instead
    // carries a non-numeric rollup (child counts + how many model rows reached a
    // confirmed value).
    function groupRow(level, toggleId, parentId, label, rollup) {
      const childCls = parentId ? ` child-of-${parentId}` : '';
      const style = parentId ? ' style="display:none"' : '';
      return `<tr class="level-${level}${childCls} expandable" data-toggle="${toggleId}"${style}>` +
        `<td><span class="expand-icon" id="icon-${toggleId}">▶</span>${label}` +
        (rollup ? ` <span class="sv-sub">${rollup}</span>` : '') +
        `</td><td></td><td></td><td></td><td></td><td></td><td></td><td></td></tr>`;
    }

    function collapseChildren(parentId) {
      wrap.querySelectorAll('.child-of-' + parentId).forEach(c => {
        c.style.display = 'none';
        if (c.dataset.toggle) {
          const icon = wrap.querySelector('#icon-' + c.dataset.toggle);
          if (icon) icon.classList.remove('expanded');
          collapseChildren(c.dataset.toggle);
        }
      });
    }

    function openRow(toggleId) {
      const icon = wrap.querySelector('#icon-' + toggleId);
      if (icon) icon.classList.add('expanded');
      wrap.querySelectorAll('.child-of-' + toggleId).forEach(c => { c.style.display = ''; });
    }

    function setAll(open) {
      // Reveal/hide plugin, skill and model rows. Model drill-down rows (sv-detail)
      // stay closed on "Expand all" — opening every drill-down at once buries the
      // table; the viewer opens a specific model to see its arms.
      wrap.querySelectorAll('.expandable').forEach(tr => {
        const icon = wrap.querySelector('#icon-' + tr.dataset.toggle);
        // Model-leaf (level-2) icons keep their collapsed ▶ because their detail row
        // is intentionally left closed.
        if (icon) icon.classList.toggle('expanded', open && !tr.classList.contains('level-2'));
      });
      wrap.querySelectorAll('[class*="child-of-"]').forEach(c => {
        if (open && c.classList.contains('sv-detail')) { c.style.display = 'none'; return; }
        c.style.display = open ? '' : 'none';
      });
    }

    function wireTree() {
      wrap.querySelectorAll('.expandable').forEach(tr => {
        tr.addEventListener('click', () => {
          const tid = tr.dataset.toggle;
          const icon = wrap.querySelector('#icon-' + tid);
          if (icon && icon.classList.contains('expanded')) {
            icon.classList.remove('expanded');
            collapseChildren(tid);
          } else {
            openRow(tid);
          }
        });
      });
      // Default: reveal plugins and skills (levels 0-1); keep per-model detail closed.
      wrap.querySelectorAll('tr.level-0.expandable, tr.level-1.expandable').forEach(tr => openRow(tr.dataset.toggle));
    }

    function draw() {
      const fm = modelSel.value, fj = judgeSel.value;
      const rows = allRows.filter(r => (!fm || r.model === fm) && (!fj || r.judge === fj));
      if (rows.length === 0) { wrap.innerHTML = '<p style="color:#8b949e;padding:1rem;">No skills match the selected filters.</p>'; return; }

      // Group into Plugin → Skill → model rows.
      const byPlugin = new Map();
      for (const r of rows) {
        if (!byPlugin.has(r.plugin)) byPlugin.set(r.plugin, new Map());
        const sk = byPlugin.get(r.plugin);
        if (!sk.has(r.skill)) sk.set(r.skill, []);
        sk.get(r.skill).push(r);
      }

      let html = `<table class="token-table sv-table"><thead><tr>` +
        `<th>Plugin / Skill / Model</th><th class="num">Activation</th><th class="num">Tokens Δ</th><th class="num">n</th>` +
        `<th class="num">Time Δ</th><th class="num">n</th><th class="num" title="Share of counted trials whose pass check was false, baseline → treatment. Aggregate pass telemetry — may include judge-scored graders, not a purely objective gate.">Not-passed (base→treat)</th><th>Value</th></tr></thead><tbody>`;

      let uid = 0;
      for (const plugin of [...byPlugin.keys()].sort()) {
        const skills = byPlugin.get(plugin);
        const pid = `svp${uid++}`;
        const pRows = [];
        for (const arr of skills.values()) { for (const r of arr) pRows.push(r); }
        // One model in the whole plugin (e.g. filtered to a single executor+judge):
        // show its real delta; otherwise a skill count + measured/gathering breakdown.
        const pRollup = pRows.length === 1
          ? singleModelRollup(pRows[0])
          : `${skills.size} skill(s) · ${countRollup(pRows)}`;
        html += groupRow(0, pid, null, escapeHtml(plugin), pRollup);

        for (const skill of [...skills.keys()].sort()) {
          const modelRows = skills.get(skill).slice()
            .sort((a, b) => a.model.localeCompare(b.model) || a.judge.localeCompare(b.judge));
          const sid = `svs${uid++}`;
          const sRollup = modelRows.length === 1
            ? singleModelRollup(modelRows[0])
            : countRollup(modelRows);
          html += groupRow(1, sid, pid, escapeHtml(skill), sRollup);

          for (const row of modelRows) {
            const mid = `svm${uid++}`;
            const v = valueSentence(row);
            const diluted = row.activation != null && row.activation < ACTIVATION_MIN;
            const label = `${escapeHtml(row.model)} <span class="sv-sub">judge ${escapeHtml(row.judge)}</span>`;
            html += `<tr class="level-2 child-of-${sid} expandable" data-toggle="${mid}" style="display:none">` +
              `<td><span class="expand-icon" id="icon-${mid}">▶</span>${label}</td>` +
              activationCell(row) +
              deltaCell(row.baseline ? row.baseline.tokens : null, row.treatment ? row.treatment.tokens : null, fmtK, diluted) +
              metricCell(row.baseline, row.treatment, fmtK) +
              deltaCell(row.baseline ? row.baseline.timeMs : null, row.treatment ? row.treatment.timeMs : null, fmtSecs, diluted) +
              metricCell(row.baseline, row.treatment, fmtSecs) +
              failureCell(row) +
              `<td class="${v.cls}">${v.text}</td></tr>` +
              `<tr class="sv-detail child-of-${mid}" style="display:none"><td colspan="8">${drilldown(row)}</td></tr>`;
          }
        }
      }
      html += '</tbody></table>';
      wrap.innerHTML = html;
      wireTree();
    }

    container.querySelector('#sv-expand').addEventListener('click', () => setAll(true));
    container.querySelector('#sv-collapse').addEventListener('click', () => setAll(false));
    modelSel.addEventListener('change', draw);
    judgeSel.addEventListener('change', draw);
    draw();
  }
})();
