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
    const totals = { tokens: 0, tokensIn: 0, tokensOut: 0, cacheRead: 0, cacheWrite: 0 };
    const bySource = { scheduled: { tokens: 0, runs: 0 }, pr: { tokens: 0, runs: 0 } };
    const daySet = new Set();
    const pluginSet = new Set();

    entries.forEach(e => {
      totals.tokens += e.totalTokens;
      totals.tokensIn += e.tokensIn;
      totals.tokensOut += e.tokensOut;
      totals.cacheRead += (e.cacheReadTokens || 0);
      totals.cacheWrite += (e.cacheWriteTokens || 0);
      bySource[e.source].tokens += e.totalTokens;
      bySource[e.source].runs += 1;
      daySet.add(dayKey(e.date));
      pluginSet.add(e.plugin);
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
    const pctIn = totals.tokens ? (totals.tokensIn / totals.tokens * 100).toFixed(0) : 0;
    const pctOut = totals.tokens ? (totals.tokensOut / totals.tokens * 100).toFixed(0) : 0;
    const pctSched = totals.tokens ? (bySource.scheduled.tokens / totals.tokens * 100).toFixed(0) : 0;
    const pctPr = totals.tokens ? (bySource.pr.tokens / totals.tokens * 100).toFixed(0) : 0;
    const cacheHitRate = totals.tokensIn ? (totals.cacheRead / totals.tokensIn * 100).toFixed(0) : 0;

    div.innerHTML = `
      <div class="card">
        <div class="card-label">Total Tokens</div>
        <div class="card-value" style="color:var(--skilled)">${fmtK(totals.tokens)}</div>
        <div class="card-delta">${dayCount} days tracked</div>
      </div>
      <div class="card">
        <div class="card-label">Tokens In</div>
        <div class="card-value" style="color:#a371f7">${fmtK(totals.tokensIn)}</div>
        <div class="card-delta">${pctIn}% of total</div>
      </div>
      <div class="card">
        <div class="card-label">Tokens Out</div>
        <div class="card-value" style="color:#f0883e">${fmtK(totals.tokensOut)}</div>
        <div class="card-delta">${pctOut}% of total</div>
      </div>
      <div class="card">
        <div class="card-label">Cache Read</div>
        <div class="card-value" style="color:#56d364">${fmtK(totals.cacheRead)}</div>
        <div class="card-delta">${cacheHitRate}% of input cached</div>
      </div>
      <div class="card">
        <div class="card-label">Cache Write</div>
        <div class="card-value" style="color:#79c0ff">${fmtK(totals.cacheWrite)}</div>
        <div class="card-delta">written to cache</div>
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
    days.forEach(d => { schedByDay[d] = 0; prByDay[d] = 0; inByDay[d] = 0; outByDay[d] = 0; crByDay[d] = 0; cwByDay[d] = 0; });
    entries.forEach(e => {
      const d = dayKey(e.date);
      if (e.source === 'scheduled') schedByDay[d] += e.totalTokens;
      else prByDay[d] += e.totalTokens;
      inByDay[d] += e.tokensIn;
      outByDay[d] += e.tokensOut;
      crByDay[d] += (e.cacheReadTokens || 0);
      cwByDay[d] += (e.cacheWriteTokens || 0);
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
          { label: 'Scheduled', data: days.map(d => schedByDay[d] / 1000), backgroundColor: '#3fb95080', borderColor: '#3fb950', borderWidth: 1 },
          { label: 'PR', data: days.map(d => prByDay[d] / 1000), backgroundColor: '#f0883e80', borderColor: '#f0883e', borderWidth: 1 }
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
        if (e.source === 'scheduled') schedByDay[d] += e.totalTokens;
        else prByDay[d] += e.totalTokens;
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
      scheduled: { ti: 0, to: 0, tt: 0, cr: 0, cw: 0, runs: 0 },
      pr:        { ti: 0, to: 0, tt: 0, cr: 0, cw: 0, runs: 0 }
    };

    entries.forEach(e => {
      const s = e.source;
      srcTotals[s].ti += e.tokensIn;
      srcTotals[s].to += e.tokensOut;
      srcTotals[s].tt += e.totalTokens;
      srcTotals[s].cr += (e.cacheReadTokens || 0);
      srcTotals[s].cw += (e.cacheWriteTokens || 0);
      srcTotals[s].runs += 1;

      if (!tree[s][e.plugin]) tree[s][e.plugin] = {};
      const sk = tree[s][e.plugin];
      if (!sk[e.skill]) sk[e.skill] = { ti: 0, to: 0, tt: 0, cr: 0, cw: 0, runs: 0 };
      sk[e.skill].ti += e.tokensIn;
      sk[e.skill].to += e.tokensOut;
      sk[e.skill].tt += e.totalTokens;
      sk[e.skill].cr += (e.cacheReadTokens || 0);
      sk[e.skill].cw += (e.cacheWriteTokens || 0);
      sk[e.skill].runs += 1;
    });

    let html = `
      <table class="token-table">
        <thead>
          <tr>
            <th>Source / Plugin / Skill</th>
            <th class="num">Total Tokens</th>
            <th class="num">Tokens In</th>
            <th class="num">Tokens Out</th>
            <th class="num">Cache Read</th>
            <th class="num">Cache Write</th>
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
        <td class="num">${fmtFull(d.ti)}</td>
        <td class="num">${fmtFull(d.to)}</td>
        <td class="num">${fmtFull(d.cr)}</td>
        <td class="num">${fmtFull(d.cw)}</td>
        <td class="num">${cacheHit}%</td>
        <td class="num">${d.runs}</td>
        <td class="num">${fmtK(avg)}</td>
      </tr>`;
  }

  function aggregate(skills) {
    const t = { ti: 0, to: 0, tt: 0, cr: 0, cw: 0, runs: 0 };
    Object.values(skills).forEach(s => {
      t.ti += s.ti; t.to += s.to; t.tt += s.tt; t.cr += s.cr; t.cw += s.cw; t.runs += s.runs;
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
})();
