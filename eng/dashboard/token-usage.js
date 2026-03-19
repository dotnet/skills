// Token Usage dashboard view
// Loaded by dashboard.html, exposes window.initTokenUsage()
(function () {
  let initialized = false;

  function escapeHtml(str) {
    if (str == null) return '';
    const div = document.createElement('div');
    div.textContent = String(str);
    return div.innerHTML;
  }

  function fmtK(n) {
    if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M';
    if (n >= 1_000) return (n / 1_000).toFixed(1) + 'k';
    return n.toString();
  }

  function fmtFull(n) {
    return n.toLocaleString();
  }

  function dayKey(ms) {
    return new Date(ms).toISOString().slice(0, 10);
  }

  function dayLabel(key) {
    const d = new Date(key + 'T00:00:00Z');
    return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
  }

  // ── Public entry point ──────────────────────────────────────────────
  window.initTokenUsage = async function () {
    if (initialized) return;
    initialized = true;

    const container = document.getElementById('token-usage-content');
    let data;
    try {
      const res = await fetch('data/token-usage.json');
      if (!res.ok) throw new Error(res.statusText);
      data = await res.json();
    } catch {
      container.innerHTML = '<p style="color:#f85149;text-align:center;padding:2rem;">No token usage data available yet.</p>';
      return;
    }

    const entries = data.entries || [];
    if (entries.length === 0) {
      container.innerHTML = '<p style="color:#8b949e;text-align:center;padding:2rem;">No token usage entries found.</p>';
      return;
    }

    render(container, entries);
  };

  // ── Main renderer ───────────────────────────────────────────────────
  function render(container, entries) {
    // Compute aggregates
    const totals = { tokens: 0, tokensIn: 0, tokensOut: 0, cacheRead: 0, cacheWrite: 0,
                     judgeIn: 0, judgeOut: 0, judgeTotal: 0, judgeCacheRead: 0, judgeCacheWrite: 0 };
    const bySource = { scheduled: { tokens: 0, runs: 0 }, pr: { tokens: 0, runs: 0 } };
    const daySet = new Set();
    const pluginSet = new Set();

    entries.forEach(e => {
      const total = e.totalTokens || 0;
      totals.tokens += total;
      totals.tokensIn += (e.tokensIn || 0);
      totals.tokensOut += (e.tokensOut || 0);
      totals.cacheRead += (e.cacheReadTokens || 0);
      totals.cacheWrite += (e.cacheWriteTokens || 0);
      totals.judgeIn += (e.judgeTokensIn || 0);
      totals.judgeOut += (e.judgeTokensOut || 0);
      totals.judgeTotal += (e.judgeTotalTokens || 0);
      totals.judgeCacheRead += (e.judgeCacheRead || 0);
      totals.judgeCacheWrite += (e.judgeCacheWrite || 0);
      const src = (e.source === 'scheduled' || e.source === 'pr') ? e.source : null;
      if (src) {
        bySource[src].tokens += total + (e.judgeTotalTokens || 0);
        bySource[src].runs += 1;
      }
      if (e.date) daySet.add(dayKey(e.date));
      if (e.plugin) pluginSet.add(e.plugin);
    });

    const days = [...daySet].sort();
    const plugins = [...pluginSet].sort();
    const totalRuns = entries.length;

    container.innerHTML = `
      <div class="summary-cards" id="token-summary"></div>
      <h2 class="section-title">Daily Token Usage</h2>
      <div class="charts-grid" id="daily-charts-grid" style="margin-bottom:24px"></div>
      <h2 class="section-title">Token Usage by Plugin</h2>
      <div class="charts-grid" id="token-plugin-charts"></div>
      <h2 class="section-title">Token Usage Breakdown</h2>
      <div id="token-table-container" style="margin-bottom:32px"></div>
    `;

    renderSummaryCards(totals, bySource, days.length, plugins.length, totalRuns);
    renderDailyChart(entries, days);
    renderPluginCharts(entries, days, plugins);
    renderBreakdownTable(entries, plugins);
  }

  // ── Summary cards ───────────────────────────────────────────────────
  function renderSummaryCards(totals, bySource, dayCount, pluginCount, runCount) {
    const div = document.getElementById('token-summary');
    const grandTotal = totals.tokens + totals.judgeTotal;
    const pctIn = totals.tokens ? (totals.tokensIn / totals.tokens * 100).toFixed(0) : 0;
    const pctOut = totals.tokens ? (totals.tokensOut / totals.tokens * 100).toFixed(0) : 0;
    const pctSched = grandTotal ? (bySource.scheduled.tokens / grandTotal * 100).toFixed(0) : 0;
    const pctPr = grandTotal ? (bySource.pr.tokens / grandTotal * 100).toFixed(0) : 0;
    const cacheHitRate = totals.tokensIn ? (totals.cacheRead / totals.tokensIn * 100).toFixed(0) : 0;
    const pctJudge = grandTotal ? (totals.judgeTotal / grandTotal * 100).toFixed(0) : 0;

    div.innerHTML = `
      <div class="card">
        <div class="card-label">Agent Tokens</div>
        <div class="card-value" style="color:var(--skilled)">${fmtK(totals.tokens)}</div>
        <div class="card-delta">${dayCount} days tracked</div>
      </div>
      <div class="card">
        <div class="card-label">Judge Tokens</div>
        <div class="card-value" style="color:#d2a8ff">${fmtK(totals.judgeTotal)}</div>
        <div class="card-delta">${pctJudge}% of grand total</div>
      </div>
      <div class="card">
        <div class="card-label">Tokens In</div>
        <div class="card-value" style="color:#a371f7">${fmtK(totals.tokensIn)}</div>
        <div class="card-delta">${pctIn}% of agent total</div>
      </div>
      <div class="card">
        <div class="card-label">Tokens Out</div>
        <div class="card-value" style="color:#f0883e">${fmtK(totals.tokensOut)}</div>
        <div class="card-delta">${pctOut}% of agent total</div>
      </div>
      <div class="card">
        <div class="card-label">Cache Read</div>
        <div class="card-value" style="color:#56d364">${fmtK(totals.cacheRead)}</div>
        <div class="card-delta">${cacheHitRate}% of input cached</div>
      </div>
      <div class="card">
        <div class="card-label">Scheduled</div>
        <div class="card-value" style="color:var(--green)">${fmtK(bySource.scheduled.tokens)}</div>
        <div class="card-delta">${pctSched}% · ${bySource.scheduled.runs} skill runs</div>
      </div>
      <div class="card">
        <div class="card-label">PR Runs</div>
        <div class="card-value" style="color:#f0883e">${fmtK(bySource.pr.tokens)}</div>
        <div class="card-delta">${pctPr}% · ${bySource.pr.runs} skill runs</div>
      </div>
      <div class="card">
        <div class="card-label">Plugins</div>
        <div class="card-value">${pluginCount}</div>
        <div class="card-delta">${runCount} total skill runs</div>
      </div>
    `;
  }

  // ── Daily overview charts ───────────────────────────────────────────
  function renderDailyChart(entries, days) {
    const grid = document.getElementById('daily-charts-grid');

    // Data by day
    const schedByDay = {};
    const prByDay = {};
    const inByDay = {};
    const outByDay = {};
    const crByDay = {};
    const cwByDay = {};
    const judgeByDay = {};
    const schedJudgeByDay = {};
    const prJudgeByDay = {};
    days.forEach(d => { schedByDay[d] = 0; prByDay[d] = 0; inByDay[d] = 0; outByDay[d] = 0; crByDay[d] = 0; cwByDay[d] = 0; judgeByDay[d] = 0; schedJudgeByDay[d] = 0; prJudgeByDay[d] = 0; });
    entries.forEach(e => {
      const d = dayKey(e.date);
      const total = e.totalTokens || 0;
      const judge = e.judgeTotalTokens || 0;
      if (e.source === 'pr') { prByDay[d] += total; prJudgeByDay[d] += judge; }
      else { schedByDay[d] += total; schedJudgeByDay[d] += judge; } // default bucket for missing/unknown source
      inByDay[d] += (e.tokensIn || 0);
      outByDay[d] += (e.tokensOut || 0);
      crByDay[d] += (e.cacheReadTokens || 0);
      cwByDay[d] += (e.cacheWriteTokens || 0);
      judgeByDay[d] += judge;
    });

    // Chart 1: Scheduled vs PR
    const div1 = document.createElement('div');
    div1.className = 'chart-container';
    div1.innerHTML = '<h3>Total Tokens Per Day (Scheduled vs PR)</h3><canvas></canvas>';
    grid.appendChild(div1);
    new Chart(div1.querySelector('canvas'), {
      type: 'bar',
      data: {
        labels: days.map(dayLabel),
        datasets: [
          { label: 'Scheduled', data: days.map(d => (schedByDay[d] + schedJudgeByDay[d]) / 1000), backgroundColor: '#3fb95080', borderColor: '#3fb950', borderWidth: 1 },
          { label: 'PR', data: days.map(d => (prByDay[d] + prJudgeByDay[d]) / 1000), backgroundColor: '#f0883e80', borderColor: '#f0883e', borderWidth: 1 }
        ]
      },
      options: chartOpts(true)
    });

    // Chart 2: Token breakdown (In / Out / Cache Read / Cache Write)
    const div2 = document.createElement('div');
    div2.className = 'chart-container';
    div2.innerHTML = '<h3>Token Breakdown Per Day</h3><canvas></canvas>';
    grid.appendChild(div2);
    new Chart(div2.querySelector('canvas'), {
      type: 'bar',
      data: {
        labels: days.map(dayLabel),
        datasets: [
          { label: 'Input (non-cached)', data: days.map(d => Math.max(0, inByDay[d] - crByDay[d]) / 1000), backgroundColor: '#a371f780', borderColor: '#a371f7', borderWidth: 1 },
          { label: 'Cache Read', data: days.map(d => crByDay[d] / 1000), backgroundColor: '#56d36480', borderColor: '#56d364', borderWidth: 1 },
          { label: 'Cache Write', data: days.map(d => cwByDay[d] / 1000), backgroundColor: '#79c0ff80', borderColor: '#79c0ff', borderWidth: 1 },
          { label: 'Output', data: days.map(d => outByDay[d] / 1000), backgroundColor: '#f0883e80', borderColor: '#f0883e', borderWidth: 1 }
        ]
      },
      options: chartOpts(true)
    });

    // Chart 3: Agent vs Judge tokens
    const div3 = document.createElement('div');
    div3.className = 'chart-container';
    div3.innerHTML = '<h3>Agent vs Judge Tokens Per Day</h3><canvas></canvas>';
    grid.appendChild(div3);
    new Chart(div3.querySelector('canvas'), {
      type: 'bar',
      data: {
        labels: days.map(dayLabel),
        datasets: [
          { label: 'Agent', data: days.map(d => (schedByDay[d] + prByDay[d]) / 1000), backgroundColor: '#58a6ff80', borderColor: '#58a6ff', borderWidth: 1 },
          { label: 'Judge', data: days.map(d => judgeByDay[d] / 1000), backgroundColor: '#d2a8ff80', borderColor: '#d2a8ff', borderWidth: 1 }
        ]
      },
      options: chartOpts(true)
    });
  }

  // ── Per-plugin sub-charts ───────────────────────────────────────────
  function renderPluginCharts(entries, days, plugins) {
    const grid = document.getElementById('token-plugin-charts');

    plugins.forEach(plugin => {
      const pe = entries.filter(e => e.plugin === plugin);
      const schedByDay = {};
      const prByDay = {};
      days.forEach(d => { schedByDay[d] = 0; prByDay[d] = 0; });
      pe.forEach(e => {
        const d = dayKey(e.date);
        const total = (e.totalTokens || 0) + (e.judgeTotalTokens || 0);
        if (e.source === 'pr') prByDay[d] += total;
        else schedByDay[d] += total;
      });

      const div = document.createElement('div');
      div.className = 'chart-container';
      div.innerHTML = `<h3>${escapeHtml(plugin)}</h3><canvas></canvas>`;
      grid.appendChild(div);

      new Chart(div.querySelector('canvas'), {
        type: 'bar',
        data: {
          labels: days.map(dayLabel),
          datasets: [
            {
              label: 'Scheduled',
              data: days.map(d => schedByDay[d] / 1000),
              backgroundColor: '#3fb95080',
              borderColor: '#3fb950',
              borderWidth: 1
            },
            {
              label: 'PR',
              data: days.map(d => prByDay[d] / 1000),
              backgroundColor: '#f0883e80',
              borderColor: '#f0883e',
              borderWidth: 1
            }
          ]
        },
        options: chartOpts(true)
      });
    });
  }

  function chartOpts(stacked) {
    return {
      responsive: true,
      plugins: {
        legend: { labels: { color: '#8b949e', font: { size: 11 } } },
        tooltip: {
          callbacks: {
            label: ctx => `${ctx.dataset.label}: ${fmtK(ctx.raw * 1000)} tokens`
          }
        }
      },
      scales: {
        x: { stacked, ticks: { color: '#8b949e' }, grid: { color: '#30363d' } },
        y: {
          stacked,
          ticks: { color: '#8b949e' },
          grid: { color: '#30363d' },
          title: { display: true, text: 'tokens (k)', color: '#8b949e' }
        }
      }
    };
  }

  // ── Collapsible breakdown table ─────────────────────────────────────
  function renderBreakdownTable(entries, plugins) {
    const wrap = document.getElementById('token-table-container');

    // Build tree: source → plugin → skill
    const tree = { scheduled: {}, pr: {} };
    const srcTotals = {
      scheduled: { ti: 0, to: 0, tt: 0, cr: 0, cw: 0, jt: 0, runs: 0 },
      pr:        { ti: 0, to: 0, tt: 0, cr: 0, cw: 0, jt: 0, runs: 0 }
    };

    entries.forEach(e => {
      const s = (e.source === 'pr') ? 'pr' : 'scheduled'; // default bucket for missing/unknown
      const ti = e.tokensIn || 0, to = e.tokensOut || 0, tt = e.totalTokens || 0;
      srcTotals[s].ti += ti;
      srcTotals[s].to += to;
      srcTotals[s].tt += tt;
      srcTotals[s].cr += (e.cacheReadTokens || 0);
      srcTotals[s].cw += (e.cacheWriteTokens || 0);
      srcTotals[s].jt += (e.judgeTotalTokens || 0);
      srcTotals[s].runs += 1;

      if (!tree[s][e.plugin]) tree[s][e.plugin] = {};
      const sk = tree[s][e.plugin];
      if (!sk[e.skill]) sk[e.skill] = { ti: 0, to: 0, tt: 0, cr: 0, cw: 0, jt: 0, runs: 0 };
      sk[e.skill].ti += ti;
      sk[e.skill].to += to;
      sk[e.skill].tt += tt;
      sk[e.skill].cr += (e.cacheReadTokens || 0);
      sk[e.skill].cw += (e.cacheWriteTokens || 0);
      sk[e.skill].jt += (e.judgeTotalTokens || 0);
      sk[e.skill].runs += 1;
    });

    let html = `
      <table class="token-table">
        <thead>
          <tr>
            <th>Source / Plugin / Skill</th>
            <th class="num">Agent Tokens</th>
            <th class="num">Judge Tokens</th>
            <th class="num">Tokens In</th>
            <th class="num">Tokens Out</th>
            <th class="num">Cache Read</th>
            <th class="num">Cache Hit %</th>
            <th class="num">Runs</th>
            <th class="num">Avg / Run</th>
          </tr>
        </thead>
        <tbody>`;

    const sources = [
      ['scheduled', '📅 Scheduled Runs'],
      ['pr', '🔀 PR Runs']
    ];

    sources.forEach(([src, label]) => {
      const st = srcTotals[src];
      if (st.runs === 0) return; // skip empty sections
      const sid = `src-${src}`;

      html += row(0, sid, null, label, st);

      // Plugin rows
      Object.keys(tree[src]).sort().forEach(plugin => {
        const skills = tree[src][plugin];
        const pt = aggregate(skills);
        const pid = `plg-${src}-${plugin}`;

        html += row(1, pid, sid, plugin, pt);

        // Skill rows (leaf)
        Object.keys(skills).sort().forEach(skill => {
          html += row(2, null, pid, skill, skills[skill]);
        });
      });
    });

    html += '</tbody></table>';
    wrap.innerHTML = html;

    // Wire expand/collapse
    wrap.querySelectorAll('.expandable').forEach(tr => {
      tr.addEventListener('click', () => {
        const tid = tr.dataset.toggle;
        const icon = document.getElementById('icon-' + tid);
        if (icon.classList.contains('expanded')) {
          icon.classList.remove('expanded');
          collapseChildren(wrap, tid);
        } else {
          icon.classList.add('expanded');
          wrap.querySelectorAll('.child-of-' + tid).forEach(c => c.style.display = '');
        }
      });
    });
  }

  function row(level, toggleId, parentId, label, d) {
    const cls = [
      `level-${level}`,
      parentId ? `child-of-${parentId}` : '',
      toggleId ? 'expandable' : ''
    ].filter(Boolean).join(' ');
    const style = parentId ? ' style="display:none"' : '';
    const toggle = toggleId ? ` data-toggle="${toggleId}"` : '';
    const icon = toggleId
      ? `<span class="expand-icon" id="icon-${toggleId}">▶</span>`
      : '';
    const avg = d.runs ? Math.round(d.tt / d.runs) : 0;
    const cacheHit = d.ti ? (d.cr / d.ti * 100).toFixed(1) : '0.0';

    return `
      <tr class="${cls}"${style}${toggle}>
        <td>${icon}${escapeHtml(label)}</td>
        <td class="num">${fmtFull(d.tt)}</td>
        <td class="num">${fmtFull(d.jt)}</td>
        <td class="num">${fmtFull(d.ti)}</td>
        <td class="num">${fmtFull(d.to)}</td>
        <td class="num">${fmtFull(d.cr)}</td>
        <td class="num">${cacheHit}%</td>
        <td class="num">${d.runs}</td>
        <td class="num">${fmtK(avg)}</td>
      </tr>`;
  }

  function aggregate(skills) {
    const t = { ti: 0, to: 0, tt: 0, cr: 0, cw: 0, jt: 0, runs: 0 };
    Object.values(skills).forEach(s => {
      t.ti += s.ti; t.to += s.to; t.tt += s.tt; t.cr += s.cr; t.cw += s.cw; t.jt += s.jt; t.runs += s.runs;
    });
    return t;
  }

  function collapseChildren(wrap, parentId) {
    wrap.querySelectorAll('.child-of-' + parentId).forEach(c => {
      c.style.display = 'none';
      if (c.dataset.toggle) {
        const icon = document.getElementById('icon-' + c.dataset.toggle);
        if (icon) icon.classList.remove('expanded');
        collapseChildren(wrap, c.dataset.toggle);
      }
    });
  }

  // Auto-init when the token usage tab is already active (e.g., no-plugins case
  // where dashboard.js runs before this script and can't call initTokenUsage yet)
  const tokenPanel = document.getElementById('panel-__token-usage__');
  if (tokenPanel && tokenPanel.classList.contains('active')) {
    window.initTokenUsage();
  }
})();
