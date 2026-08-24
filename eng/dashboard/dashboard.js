(async function () {
  // HTML escape helper for defense-in-depth against XSS
  function escapeHtml(str) {
    if (str == null) return '';
    const div = document.createElement('div');
    div.textContent = String(str);
    return div.innerHTML;
  }

  // AGENTVIZ session replay configuration
  // Session data lives in the standalone dotnet/skills-data repo to keep this repo small.
  const sessionManifestUrl = 'https://raw.githubusercontent.com/dotnet/skills-data/dashboard-session-data/data/manifest.json';
  const replayBaseUrl = 'replay/index.html';

  // Fetch plugin manifest
  let plugins;
  try {
    const response = await fetch('data/components.json');
    if (!response.ok) throw new Error(response.statusText);
    plugins = await response.json();
  } catch {
    // No evaluation data – still allow Token Usage tab to work
    plugins = [];
  }

  if (!Array.isArray(plugins)) {
    plugins = [];
  }

  plugins.sort();

  const tabBar = document.getElementById('tab-bar');
  const tabContentContainer = document.getElementById('tab-content');
  const loadedPlugins = new Map(); // track loaded plugin data

  // Build tabs and placeholder panels
  plugins.forEach((plugin, idx) => {
    const tab = document.createElement('div');
    tab.className = 'tab' + (idx === 0 ? ' active' : '');
    tab.textContent = plugin;
    tab.dataset.plugin = plugin;
    tab.addEventListener('click', () => switchTab(plugin));
    tabBar.appendChild(tab);

    const panel = document.createElement('div');
    panel.className = 'tab-content' + (idx === 0 ? ' active' : '');
    panel.id = `panel-${plugin}`;
    panel.innerHTML = '<p style="color:#8b949e;text-align:center;padding:2rem;">Loading...</p>';
    tabContentContainer.appendChild(panel);
  });

  // Add Token Usage tab at the end
  const tokenTabId = '__token-usage__';
  const noPlugins = plugins.length === 0;
  const tokenTab = document.createElement('div');
  tokenTab.className = 'tab' + (noPlugins ? ' active' : '');
  tokenTab.textContent = '🔢 Token Usage';
  tokenTab.dataset.plugin = tokenTabId;
  tokenTab.addEventListener('click', () => switchTab(tokenTabId));
  tabBar.appendChild(tokenTab);

  const tokenPanel = document.createElement('div');
  tokenPanel.className = 'tab-content' + (noPlugins ? ' active' : '');
  tokenPanel.id = `panel-${tokenTabId}`;
  tokenPanel.innerHTML = '<div id="token-usage-content"><p style="color:#8b949e;text-align:center;padding:2rem;">Loading…</p></div>';
  tabContentContainer.appendChild(tokenPanel);

  async function switchTab(plugin) {
    tabBar.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t.dataset.plugin === plugin));
    tabContentContainer.querySelectorAll('.tab-content').forEach(p => p.classList.toggle('active', p.id === `panel-${plugin}`));
    if (plugin === tokenTabId) {
      if (window.initTokenUsage) window.initTokenUsage();
      return;
    }
    if (!loadedPlugins.has(plugin)) {
      await loadPlugin(plugin);
    }
  }

  // Token usage auto-init is handled by token-usage.js itself (it checks
  // whether the tab is already active after it loads).

  async function loadPlugin(plugin) {
    const panel = document.getElementById(`panel-${plugin}`);
    try {
      const response = await fetch(`data/${plugin}.json`);
      if (!response.ok) throw new Error(response.statusText);
      const data = await response.json();
      loadedPlugins.set(plugin, data);
      renderPlugin(plugin, data, panel);
    } catch {
      panel.innerHTML = '<p style="color:#f85149;text-align:center;padding:2rem;">Failed to load data.</p>';
    }
  }

  // --- Shared constants and helpers for issue markers ---
  const ISSUE_COLORS = {
    notActivated: '#d29922',
    timedOut: '#f85149',
    overfittingModerate: '#d29922',
    overfittingHigh: '#f85149',
    multiIssue: '#f85149',
  };

  // Cross-family runs interleave several executor models in one plugin file.
  // Colour each model distinctly so a trend line never blends two families,
  // and the variant (Isolated / Plugin / Vanilla) is carried by the dash style.
  const MODEL_PALETTE = ['#58a6ff', '#3fb950', '#d29922', '#a371f7', '#ff7b72', '#79c0ff', '#f778ba', '#56d364'];
  const MAX_INLINE_LEGEND_SERIES = 6;
  function orderedModels(entries) {
    const seen = [];
    for (const e of entries) {
      const m = (e && e.model) ? e.model : 'unknown';
      if (!seen.includes(m)) seen.push(m);
    }
    return seen;
  }
  function buildModelColorMap(models) {
    const map = {};
    models.forEach((m, i) => { map[m] = MODEL_PALETTE[i % MODEL_PALETTE.length]; });
    return map;
  }
  function modelColorFor(model, models) {
    const i = models.indexOf(model);
    return MODEL_PALETTE[(i < 0 ? 0 : i) % MODEL_PALETTE.length];
  }
  // Canonical model->colour map for the plugin currently being rendered, built
  // from the FULL history so the summary-table dot and every trend line agree on
  // a model's colour even though the summary window (last N) can see a different
  // subset/order of models than the charts. Falls back to per-set order when a
  // chart is drawn before this is populated.
  let activeModelColors = {};
  function colourForModel(model, fallbackModels) {
    const m = (model || 'unknown');
    if (Object.prototype.hasOwnProperty.call(activeModelColors, m)) return activeModelColors[m];
    return modelColorFor(m, fallbackModels || [m]);
  }

  function getPointAppearance(flags, defaultColor) {
    const count = (flags.timedOut ? 1 : 0) + (flags.notActivated ? 1 : 0) + (flags.overfitting ? 1 : 0);
    if (count > 1) return { color: ISSUE_COLORS.multiIssue, style: 'circle', radius: 4, borderWidth: 2 };
    if (flags.timedOut) return { color: ISSUE_COLORS.timedOut, style: 'rectRot', radius: 6, borderWidth: 2 };
    if (flags.notActivated) return { color: ISSUE_COLORS.notActivated, style: 'triangle', radius: 6, borderWidth: 2 };
    if (flags.overfitting === 'high') return { color: ISSUE_COLORS.overfittingHigh, style: 'star', radius: 7, borderWidth: 2 };
    if (flags.overfitting) return { color: ISSUE_COLORS.overfittingModerate, style: 'star', radius: 6, borderWidth: 2 };
    return { color: defaultColor, style: 'circle', radius: 4, borderWidth: 2 };
  }

  function buildIssueTooltipLines(entry, benchFilter) {
    if (!entry || !entry.benches) return [];
    const benches = benchFilter ? entry.benches.filter(benchFilter) : entry.benches;
    const lines = [];
    if (benches.some(b => b.notActivated)) lines.push('⚠️ SKILL NOT ACTIVATED');
    if (benches.some(b => b.timedOut)) lines.push('⏰ EXECUTION TIMED OUT');
    const ofBench = benches.find(b => b.overfitting);
    if (ofBench) {
      const sev = ofBench.overfitting;
      const score = ofBench.overfittingScore;
      const icon = sev === 'high' ? '🔴' : '🟡';
      lines.push(`${icon} ${sev.toUpperCase()} EVAL OVERFITTING (score: ${score != null ? score.toFixed(2) : 'N/A'})`);
    }
    if (lines.length > 1) {
      return ['⛔ MULTIPLE ISSUES:', ...lines.map(l => '  ' + l)];
    }
    return lines;
  }

  // Custom generateLabels that always shows a circle in the series color,
  // regardless of per-point error markers (triangles, diamonds, stars).
  function legendLabelsWithCircle(chart) {
    return Chart.defaults.plugins.legend.labels.generateLabels(chart).map(function(l) {
      const ds = chart.data.datasets[l.datasetIndex];
      const seriesColor = ds && ds.borderColor ? ds.borderColor : l.strokeStyle;
      return Object.assign({}, l, { pointStyle: 'circle', fillStyle: seriesColor, strokeStyle: seriesColor });
    });
  }

  function appendLegendNotes(div, flags) {
    if (flags.notActivated) {
      const note = document.createElement('div');
      note.className = 'not-activated-legend';
      note.innerHTML = `⚠️ <span style="color:${ISSUE_COLORS.notActivated}">▲</span> = Skill was not activated`;
      div.appendChild(note);
    }
    if (flags.timedOut) {
      const note = document.createElement('div');
      note.className = 'not-activated-legend';
      note.innerHTML = `⏰ <span style="color:${ISSUE_COLORS.timedOut}">◆</span> = Execution timed out`;
      div.appendChild(note);
    }
    if (flags.overfittingHigh) {
      const note = document.createElement('div');
      note.className = 'not-activated-legend';
      note.innerHTML = `🔴 <span style="color:${ISSUE_COLORS.overfittingHigh}">★</span> = High eval overfitting`;
      div.appendChild(note);
    }
    if (flags.overfittingModerate) {
      const note = document.createElement('div');
      note.className = 'not-activated-legend';
      note.innerHTML = `🟡 <span style="color:${ISSUE_COLORS.overfittingModerate}">★</span> = Moderate eval overfitting`;
      div.appendChild(note);
    }
    if (flags.multiIssue) {
      const note = document.createElement('div');
      note.className = 'not-activated-legend';
      note.innerHTML = `⛔ <span style="color:${ISSUE_COLORS.multiIssue}">●</span> = Multiple issues (see tooltip)`;
      div.appendChild(note);
    }
  }

  function renderPlugin(plugin, data, panel) {
    if (!data || !data.entries) {
      panel.innerHTML = '<p style="color:#8b949e;text-align:center;padding:2rem;">No data available.</p>';
      return;
    }

    const allQualityEntries = data.entries['Quality'] || [];
    const allEfficiencyEntries = data.entries['Efficiency'] || [];

    // One canonical model->colour map for this plugin, from the FULL history, so
    // the summary table and all charts colour each model identically and a model
    // keeps its colour even while other models are filtered out of the view.
    // Captured in a plugin-scoped const because the module-level activeModelColors
    // is shared across plugin tabs: draw() restores it from this local on every
    // (re)render, so a lazy filter toggle after switching tabs can't pick up
    // another plugin's colour map.
    const allModels = orderedModels(allQualityEntries);
    const pluginModelColors = buildModelColorMap(allModels);
    activeModelColors = pluginModelColors;

    // Model filter state: every model is enabled by default. The filter bar (built
    // below) lets the viewer focus on a subset; toggling re-renders via draw().
    const activeModels = new Set(allModels);
    const liveCharts = [];

    const replayHref = `${replayBaseUrl}?manifest=${encodeURIComponent(sessionManifestUrl)}&tag=${encodeURIComponent(plugin)}`;

    panel.innerHTML = `
      <div style="display:flex;align-items:center;gap:16px;margin-bottom:8px;">
        <a href="${escapeHtml(replayHref)}" target="_blank" rel="noopener"
           style="color:#58a6ff;font-size:13px;text-decoration:none;">&#9654; Sessions Visualisation</a>
      </div>
      <div id="model-filter-${plugin}" style="display:flex;flex-wrap:wrap;align-items:center;gap:12px;margin-bottom:16px;"></div>
      <div class="summary-cards" id="summary-${plugin}"></div>
      <h2 class="section-title">Quality Over Time</h2>
      <div class="charts-grid" id="quality-${plugin}"></div>
      <h2 class="section-title">Efficiency Over Time</h2>
      <div class="charts-grid" id="efficiency-${plugin}"></div>
    `;

    function draw() {
      // Restore this plugin's canonical colour map. activeModelColors is a shared
      // module global that another plugin tab may have overwritten since this
      // plugin last rendered.
      activeModelColors = pluginModelColors;

      // Restrict history to the models the viewer has enabled.
      const qualityEntries = allQualityEntries.filter(e => activeModels.has((e && e.model) ? e.model : 'unknown'));
      const efficiencyEntries = allEfficiencyEntries.filter(e => activeModels.has((e && e.model) ? e.model : 'unknown'));

      // Tear down the previous render so charts don't leak and canvases aren't reused.
      liveCharts.forEach(c => { try { c.destroy(); } catch { /* already detached */ } });
      liveCharts.length = 0;
      const _summary = document.getElementById(`summary-${plugin}`);
      const _quality = document.getElementById(`quality-${plugin}`);
      const _efficiency = document.getElementById(`efficiency-${plugin}`);
      if (_summary) _summary.innerHTML = '';
      if (_quality) _quality.innerHTML = '';
      if (_efficiency) _efficiency.innerHTML = '';

    // Summary cards — compute averages across the last 50 entries
    const summaryDiv = document.getElementById(`summary-${plugin}`);
    const SUMMARY_WINDOW = 50;
    if (qualityEntries.length > 0) {
      // Use only the most recent entries for summary cards
      const recentEntries = qualityEntries.slice(-SUMMARY_WINDOW);
      const windowLabel = qualityEntries.length > SUMMARY_WINDOW
        ? `last ${SUMMARY_WINDOW} of ${qualityEntries.length} runs`
        : `${qualityEntries.length} runs`;

      // Per-model breakdown: cross-family runs mix executor models into one
      // file, so a single blended average would hide per-model differences.
      // Group the recent window by model and show one row per model.
      const summaryModels = orderedModels(recentEntries);
      const stats = {}; // model -> running sums
      let anyPluginSummary = false;
      recentEntries.forEach(entry => {
        const m = (entry && entry.model) ? entry.model : 'unknown';
        const st = stats[m] || (stats[m] = { sSum: 0, sN: 0, pSum: 0, pN: 0, vSum: 0, vN: 0, runs: 0 });
        st.runs++;
        entry.benches.forEach(b => {
          if (b.name.endsWith('- Skilled Quality')) { st.sSum += b.value; st.sN++; }
          else if (b.name.endsWith('- Plugin Quality')) { st.pSum += b.value; st.pN++; anyPluginSummary = true; }
          else if (b.name.endsWith('- Vanilla Quality')) { st.vSum += b.value; st.vN++; }
        });
      });
      const fmtAvg = (sum, n) => (n > 0 ? (sum / n).toFixed(2) : '&mdash;');
      const fmtDelta = (aSum, aN, bSum, bN) => {
        if (!(aN > 0 && bN > 0)) return '&mdash;';
        const d = aSum / aN - bSum / bN;
        const cls = d > 0 ? 'positive' : d < 0 ? 'negative' : 'neutral';
        return `<span class="${cls}">${d > 0 ? '+' : ''}${d.toFixed(2)}</span>`;
      };
      const modelRows = summaryModels.map(m => {
        const st = stats[m];
        const dot = `<span style="display:inline-block;width:9px;height:9px;border-radius:50%;background:${colourForModel(m, summaryModels)};margin-right:6px;"></span>`;
        return `<tr>
          <td style="text-align:left;white-space:nowrap">${dot}${escapeHtml(m) || 'N/A'}</td>
          <td>${st.runs}</td>
          <td style="color:var(--skilled)">${fmtAvg(st.sSum, st.sN)}</td>
          ${anyPluginSummary ? `<td style="color:#3fb950">${fmtAvg(st.pSum, st.pN)}</td>` : ''}
          <td style="color:var(--vanilla)">${fmtAvg(st.vSum, st.vN)}</td>
          <td>${fmtDelta(st.sSum, st.sN, st.vSum, st.vN)}</td>
          ${anyPluginSummary ? `<td>${fmtDelta(st.pSum, st.pN, st.vSum, st.vN)}</td>` : ''}
        </tr>`;
      }).join('');
      summaryDiv.innerHTML = `
        <div class="card" style="grid-column:1/-1;flex:1 1 100%;text-align:left">
          <div class="card-label">Quality by model &mdash; ${windowLabel} &middot; ${qualityEntries.length} total runs</div>
          <table class="model-summary" style="width:100%;border-collapse:collapse;margin-top:10px;font-size:13px;text-align:center;">
            <thead><tr style="color:#8b949e">
              <th style="text-align:left">Model</th><th>Runs</th><th>Skilled</th>${anyPluginSummary ? '<th>Plugin</th>' : ''}<th>Vanilla</th><th>&Delta; Isolated</th>${anyPluginSummary ? '<th>&Delta; Plugin</th>' : ''}
            </tr></thead>
            <tbody>${modelRows}</tbody>
          </table>
        </div>`;

      // Count not-activated entries
      let notActivatedCount = 0;
      recentEntries.forEach(entry => {
        if (entry.benches.some(b => b.notActivated)) notActivatedCount++;
      });
      if (notActivatedCount > 0) {
        summaryDiv.innerHTML += `
          <div class="card">
            <div class="card-label">Not Activated</div>
            <div class="card-value" style="color: var(--warning)">${notActivatedCount}</div>
            <div class="card-delta">runs where skill was not loaded</div>
          </div>
        `;
      }

      // Count timed-out entries
      let timedOutCount = 0;
      recentEntries.forEach(entry => {
        if (entry.benches.some(b => b.timedOut)) timedOutCount++;
      });
      if (timedOutCount > 0) {
        summaryDiv.innerHTML += `
          <div class="card">
            <div class="card-label">Timed Out</div>
            <div class="card-value" style="color: var(--timeout)">${timedOutCount}</div>
            <div class="card-delta">runs where execution timed out</div>
          </div>
        `;
      }

      // Count overfitting entries by severity
      let overfittingHighCount = 0;
      let overfittingModerateCount = 0;
      recentEntries.forEach(entry => {
        const ofBench = entry.benches.find(b => b.overfitting);
        if (ofBench) {
          if (ofBench.overfitting === 'high') overfittingHighCount++;
          else overfittingModerateCount++;
        }
      });
      const overfittingTotal = overfittingHighCount + overfittingModerateCount;
      if (overfittingTotal > 0) {
        const cardColor = overfittingHighCount > 0 ? ISSUE_COLORS.overfittingHigh : ISSUE_COLORS.overfittingModerate;
        const breakdown = [];
        if (overfittingHighCount > 0) breakdown.push(`${overfittingHighCount} high`);
        if (overfittingModerateCount > 0) breakdown.push(`${overfittingModerateCount} moderate`);
        summaryDiv.innerHTML += `
          <div class="card">
            <div class="card-label">Overfitting</div>
            <div class="card-value" style="color: ${cardColor}">${overfittingTotal}</div>
            <div class="card-delta">${breakdown.join(', ')} overfitting</div>
          </div>
        `;
      }
    }

    // Quality charts
    const qualityChartsDiv = document.getElementById(`quality-${plugin}`);
    if (qualityEntries.length > 0) {
      // Discover tests from all entries (not just latest, which may have partial data)
      const tests = new Set();
      let hasAnyPlugin = false;
      qualityEntries.forEach(entry => {
        entry.benches.forEach(b => {
          const match = b.name.match(/^(.+) - (Skilled|Plugin|Vanilla) Quality$/);
          if (match) {
            tests.add(match[1]);
            if (match[2] === 'Plugin') hasAnyPlugin = true;
          }
        });
      });

      tests.forEach(test => {
        if (hasAnyPlugin) {
          liveCharts.push(createTripleChart(
            qualityChartsDiv, test, qualityEntries,
            `${test} - Skilled Quality`, `${test} - Plugin Quality`, `${test} - Vanilla Quality`,
            'Isolated', 'Plugin', 'Vanilla',
            '#58a6ff', '#3fb950', '#8b949e'
          ));
        } else {
          liveCharts.push(createPairedChart(
            qualityChartsDiv, test, qualityEntries,
            `${test} - Skilled Quality`, `${test} - Vanilla Quality`,
            'Skilled', 'Vanilla', '#58a6ff', '#8b949e'
          ));
        }
      });
    }

    // Efficiency charts
    const efficiencyChartsDiv = document.getElementById(`efficiency-${plugin}`);
    if (efficiencyEntries.length > 0) {
      // Discover tests from all entries (not just latest, which may have partial data)
      const effTests = new Set();
      let hasAnyPluginEff = false;
      let hasAnyVanillaEff = false;
      efficiencyEntries.forEach(entry => {
        entry.benches.forEach(b => {
          const matchSkilled = b.name.match(/^(.+) - Skilled Time$/);
          if (matchSkilled) effTests.add(matchSkilled[1]);
          const matchPlugin = b.name.match(/^(.+) - Plugin Time$/);
          if (matchPlugin) { effTests.add(matchPlugin[1]); hasAnyPluginEff = true; }
          const matchVanilla = b.name.match(/^(.+) - Vanilla Time$/);
          if (matchVanilla) { effTests.add(matchVanilla[1]); hasAnyVanillaEff = true; }
        });
      });

      effTests.forEach(test => {
        const div = document.createElement('div');
        div.className = 'chart-container';
        div.innerHTML = `<h3>${escapeHtml(test)}</h3><canvas></canvas>`;
        efficiencyChartsDiv.appendChild(div);
        const canvas = div.querySelector('canvas');

        const labels = efficiencyEntries.map(e => {
          const d = new Date(e.date);
          return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
        });

        // Precompute per-entry data in a single pass over e.benches
        const timeName = `${test} - Skilled Time`;
        const tokenName = `${test} - Skilled Tokens In`;
        const plugTimeName = `${test} - Plugin Time`;
        const plugTokenName = `${test} - Plugin Tokens In`;
        const vanTimeName = `${test} - Vanilla Time`;
        const vanTokenName = `${test} - Vanilla Tokens In`;
        const legendFlags = { notActivated: false, timedOut: false, overfittingModerate: false, overfittingHigh: false, multiIssue: false };

        const perEntryData = efficiencyEntries.map(e => {
          let timeBench = undefined;
          let tokenBench = undefined;
          let plugTimeBench = undefined;
          let plugTokenBench = undefined;
          let vanTimeBench = undefined;
          let vanTokenBench = undefined;
          for (const b of e.benches) {
            if (!timeBench && b.name === timeName) timeBench = b;
            else if (!tokenBench && b.name === tokenName) tokenBench = b;
            else if (!plugTimeBench && b.name === plugTimeName) plugTimeBench = b;
            else if (!plugTokenBench && b.name === plugTokenName) plugTokenBench = b;
            else if (!vanTimeBench && b.name === vanTimeName) vanTimeBench = b;
            else if (!vanTokenBench && b.name === vanTokenName) vanTokenBench = b;
          }
          const timeNA = !!(timeBench && timeBench.notActivated);
          const tokenNA = !!(tokenBench && tokenBench.notActivated);
          const timeTO = !!(timeBench && timeBench.timedOut);
          const tokenTO = !!(tokenBench && tokenBench.timedOut);
          const timeOF = timeBench && timeBench.overfitting ? timeBench.overfitting : null;
          const tokenOF = tokenBench && tokenBench.overfitting ? tokenBench.overfitting : null;
          if (timeNA || tokenNA) legendFlags.notActivated = true;
          if (timeTO || tokenTO) legendFlags.timedOut = true;
          if (timeOF || tokenOF) {
            if (timeOF === 'high' || tokenOF === 'high') legendFlags.overfittingHigh = true;
            else legendFlags.overfittingModerate = true;
          }
          const timeIssues = (timeNA ? 1 : 0) + (timeTO ? 1 : 0) + (timeOF ? 1 : 0);
          const tokenIssues = (tokenNA ? 1 : 0) + (tokenTO ? 1 : 0) + (tokenOF ? 1 : 0);
          if (timeIssues > 1 || tokenIssues > 1) legendFlags.multiIssue = true;
          return {
            timeValue: timeBench ? timeBench.value : null,
            timeNotActivated: timeNA,
            timeTimedOut: timeTO,
            timeOverfitting: timeOF,
            tokenValue: tokenBench ? tokenBench.value / 1000 : null,
            tokenNotActivated: tokenNA,
            tokenTimedOut: tokenTO,
            tokenOverfitting: tokenOF,
            plugTimeValue: plugTimeBench ? plugTimeBench.value : null,
            plugTokenValue: plugTokenBench ? plugTokenBench.value / 1000 : null,
            vanTimeValue: vanTimeBench ? vanTimeBench.value : null,
            vanTokenValue: vanTokenBench ? vanTokenBench.value / 1000 : null,
          };
        });

        const timeData = perEntryData.map(d => d.timeValue);
        const tokenData = perEntryData.map(d => d.tokenValue);
        const plugTimeData = perEntryData.map(d => d.plugTimeValue);
        const plugTokenData = perEntryData.map(d => d.plugTokenValue);
        const vanTimeData = perEntryData.map(d => d.vanTimeValue);
        const vanTokenData = perEntryData.map(d => d.vanTokenValue);

        // Per-point styling using shared helper
        const timeAp = perEntryData.map(d => getPointAppearance({ timedOut: d.timeTimedOut, notActivated: d.timeNotActivated, overfitting: d.timeOverfitting }, '#58a6ff'));
        const timePointBg = timeAp.map(a => a.color);
        const timePointStyle = timeAp.map(a => a.style);
        const timePointRadius = timeAp.map(a => a.radius);
        const timePointBorderWidth = timeAp.map(a => a.borderWidth);
        const tokenAp = perEntryData.map(d => getPointAppearance({ timedOut: d.tokenTimedOut, notActivated: d.tokenNotActivated, overfitting: d.tokenOverfitting }, '#58a6ff'));
        const tokenPointBg = tokenAp.map(a => a.color);
        const tokenPointStyle = tokenAp.map(a => a.style);
        const tokenPointRadius = tokenAp.map(a => a.radius);
        const tokenPointBorderWidth = tokenAp.map(a => a.borderWidth);

        const datasets = [
          {
            label: 'Isolated Time (s)',
            data: timeData,
            borderColor: '#58a6ff',
            borderWidth: 2,
            pointBackgroundColor: timePointBg,
            pointBorderColor: timePointBg,
            pointRadius: timePointRadius,
            pointBorderWidth: timePointBorderWidth,
            pointStyle: timePointStyle,
            tension: 0.3,
            fill: false,
            yAxisID: 'y'
          },
          {
            label: 'Isolated Tokens (k)',
            data: tokenData,
            borderColor: '#58a6ff',
            borderWidth: 2,
            pointBackgroundColor: tokenPointBg,
            pointBorderColor: tokenPointBg,
            pointRadius: tokenPointRadius,
            pointBorderWidth: tokenPointBorderWidth,
            pointStyle: tokenPointStyle,
            tension: 0.3,
            borderDash: [5, 5],
            fill: false,
            yAxisID: 'y1'
          }
        ];

        // Add plugin efficiency datasets if any plugin data exists
        if (hasAnyPluginEff) {
          datasets.push({
            label: 'Plugin Time (s)',
            data: plugTimeData,
            borderColor: '#3fb950',
            borderWidth: 2,
            pointRadius: 4,
            pointHoverRadius: 6,
            tension: 0.3,
            fill: false,
            yAxisID: 'y'
          });
          datasets.push({
            label: 'Plugin Tokens (k)',
            data: plugTokenData,
            borderColor: '#3fb950',
            borderWidth: 2,
            pointRadius: 4,
            pointHoverRadius: 6,
            tension: 0.3,
            borderDash: [5, 5],
            fill: false,
            yAxisID: 'y1'
          });
        }

        // Add vanilla efficiency datasets if any vanilla data exists
        if (hasAnyVanillaEff) {
          datasets.push({
            label: 'Vanilla Time (s)',
            data: vanTimeData,
            borderColor: '#8b949e',
            borderWidth: 2,
            // Hollow diamond markers keep vanilla visible when it overlaps the
            // isolated/plugin lines. Vanilla is pushed last, so it draws on top.
            pointStyle: 'rectRot',
            pointBackgroundColor: 'transparent',
            pointBorderColor: '#8b949e',
            pointBorderWidth: 1.5,
            pointRadius: 5,
            pointHoverRadius: 7,
            tension: 0.3,
            borderDash: [8, 6],
            fill: false,
            yAxisID: 'y'
          });
          datasets.push({
            label: 'Vanilla Tokens (k)',
            data: vanTokenData,
            borderColor: '#8b949e',
            borderWidth: 2,
            pointStyle: 'rectRot',
            pointBackgroundColor: 'transparent',
            pointBorderColor: '#8b949e',
            pointBorderWidth: 1.5,
            pointRadius: 5,
            pointHoverRadius: 7,
            tension: 0.3,
            borderDash: [8, 6],
            fill: false,
            yAxisID: 'y1'
          });
        }

        const effChart = new Chart(canvas, {
          type: 'line',
          data: {
            labels,
            datasets
          },
          options: {
            responsive: true,
            interaction: { mode: 'index', intersect: false },
            plugins: {
              legend: { labels: { color: '#8b949e', font: { size: 11 }, usePointStyle: true, generateLabels: legendLabelsWithCircle } },
              tooltip: {
                callbacks: {
                  afterTitle: (items) => {
                    const idx = items[0].dataIndex;
                    const entry = efficiencyEntries[idx];
                    const parts = [];
                    if (entry && entry.model) parts.push(`Model: ${entry.model}`);
                    if (entry && entry.commit) {
                      const msg = entry.commit.message.split('\n')[0];
                      parts.push(msg.length > 60 ? msg.substring(0, 60) + '...' : msg);
                    }
                    parts.push(...buildIssueTooltipLines(entry, b => b.name === timeName || b.name === tokenName));
                    return parts.join('\n');
                  }
                }
              }
            },
            scales: {
              x: { ticks: { color: '#8b949e' }, grid: { color: '#30363d' } },
              y: {
                type: 'linear',
                position: 'left',
                ticks: { color: '#8b949e' },
                grid: { color: '#30363d' },
                title: { display: true, text: 'seconds', color: '#8b949e' }
              },
              y1: {
                type: 'linear',
                position: 'right',
                ticks: { color: '#8b949e' },
                grid: { drawOnChartArea: false },
                title: { display: true, text: 'tokens (k)', color: '#8b949e' }
              }
            }
          }
        });

        appendLegendNotes(div, legendFlags);
        liveCharts.push(effChart);
      });
    }
    } // end draw()

    // Per-model filter bar: all models enabled by default. Toggling a model
    // re-renders the summary table and every chart for just the selected models.
    // Colours stay canonical (bound to full history), so hiding a model never
    // recolours the others. Only shown when there is more than one model.
    const filterBar = document.getElementById(`model-filter-${plugin}`);
    if (filterBar && allModels.length > 1) {
      const lbl = document.createElement('span');
      lbl.textContent = 'Models:';
      lbl.style.cssText = 'color:#8b949e;font-size:12px;text-transform:uppercase;letter-spacing:0.5px;';
      filterBar.appendChild(lbl);
      allModels.forEach(m => {
        const item = document.createElement('label');
        item.style.cssText = 'display:inline-flex;align-items:center;gap:6px;font-size:13px;color:#e6edf3;cursor:pointer;user-select:none;';
        const cb = document.createElement('input');
        cb.type = 'checkbox';
        cb.checked = true;
        cb.addEventListener('change', () => {
          // Keep at least one model active so the view is never empty.
          if (!cb.checked && activeModels.size === 1 && activeModels.has(m)) {
            cb.checked = true;
            return;
          }
          if (cb.checked) activeModels.add(m); else activeModels.delete(m);
          draw();
        });
        const dot = document.createElement('span');
        dot.style.cssText = `width:10px;height:10px;border-radius:50%;display:inline-block;background:${colourForModel(m, allModels)};`;
        const nm = document.createElement('span');
        nm.textContent = m;
        item.appendChild(cb);
        item.appendChild(dot);
        item.appendChild(nm);
        filterBar.appendChild(item);
      });
    }

    draw();
  }

  // Quality trend charts, segmented by executor model.
  //
  // Cross-family evaluation interleaves several models in one plugin's history,
  // so a single line per variant would connect points from different families
  // and blend them. Instead, draw one line PER MODEL (colour = model) and encode
  // the variant (Isolated / Plugin / Vanilla) with the dash style. Each per-model
  // dataset carries values only at its own runs (null elsewhere, spanGaps:false),
  // so a line never bridges two models.
  function renderModelSegmentedChart(container, title, entries, variants) {
    const div = document.createElement('div');
    div.className = 'chart-container';
    div.innerHTML = `<h3>${escapeHtml(title)}</h3><canvas></canvas>`;
    container.appendChild(div);
    const canvas = div.querySelector('canvas');

    const labels = entries.map(e => {
      const d = new Date(e.date);
      return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
    });
    const models = orderedModels(entries);
    const colorMap = {};
    models.forEach(m => { colorMap[m] = colourForModel(m, models); });
    const modelOf = entries.map(e => (e && e.model) ? e.model : 'unknown');
    const allNames = variants.map(v => v.name);

    const legendFlags = { notActivated: false, timedOut: false, overfittingModerate: false, overfittingHigh: false, multiIssue: false };
    const datasets = [];

    variants.forEach(v => {
      // Bench for this variant at each entry (or null when absent).
      const per = entries.map(e => e.benches.find(x => x.name === v.name) || null);

      // Issue legend flags come from the skilled-side variants only (matches the
      // previous behaviour where Vanilla did not raise issue markers).
      if (!v.vanilla) {
        per.forEach(b => {
          if (!b) return;
          const na = !!b.notActivated, to = !!b.timedOut, of = b.overfitting || null;
          if (na) legendFlags.notActivated = true;
          if (to) legendFlags.timedOut = true;
          if (of) { if (of === 'high') legendFlags.overfittingHigh = true; else legendFlags.overfittingModerate = true; }
          if ((na ? 1 : 0) + (to ? 1 : 0) + (of ? 1 : 0) > 1) legendFlags.multiIssue = true;
        });
      }

      // Full-length per-point appearance; base colour is the point's model colour
      // so non-issue markers match their model line. Issue markers still override.
      const appearance = per.map((b, i) => {
        const base = colorMap[modelOf[i]];
        if (v.vanilla) return { color: base, bg: 'transparent', style: 'rectRot', radius: 5, borderWidth: 1.5 };
        const ap = getPointAppearance({ timedOut: b && b.timedOut, notActivated: b && b.notActivated, overfitting: b && b.overfitting }, base);
        return { color: ap.color, bg: ap.color, style: ap.style, radius: ap.radius, borderWidth: ap.borderWidth };
      });
      const pointBg = appearance.map(a => a.bg);
      const pointBorder = appearance.map(a => a.color);
      const pointStyle = appearance.map(a => a.style);
      const pointRadius = appearance.map(a => a.radius);
      const pointBorderWidth = appearance.map(a => a.borderWidth);

      // One dataset per model: value present only at that model's indices.
      models.forEach(m => {
        const data = per.map((b, i) => (modelOf[i] === m && b) ? b.value : null);
        if (data.every(x => x === null)) return;
        datasets.push({
          label: `${m} \u00B7 ${v.label}`,
          data,
          borderColor: colorMap[m],
          backgroundColor: colorMap[m] + '20',
          borderWidth: 2,
          borderDash: (v.dash && v.dash.length) ? v.dash : [],
          pointBackgroundColor: pointBg,
          pointBorderColor: pointBorder,
          pointStyle: pointStyle,
          pointRadius: pointRadius,
          pointBorderWidth: pointBorderWidth,
          pointHoverRadius: 8,
          tension: 0.3,
          spanGaps: false,
          fill: false,
        });
      });
    });

    const chart = new Chart(canvas, {
      type: 'line',
      data: { labels, datasets },
      options: {
        responsive: true,
        interaction: { mode: 'index', intersect: false },
        plugins: {
          // The model filter above the charts already provides the colour key.
          // Hide the repeated model-by-variant legend before it crowds out the plot.
          legend: {
            display: datasets.length <= MAX_INLINE_LEGEND_SERIES,
            labels: { color: '#8b949e', font: { size: 11 }, usePointStyle: true, generateLabels: legendLabelsWithCircle }
          },
          tooltip: {
            callbacks: {
              afterTitle: (items) => {
                const idx = items[0].dataIndex;
                const entry = entries[idx];
                const parts = [];
                if (entry && entry.model) parts.push(`Model: ${entry.model}`);
                if (entry && entry.commit) {
                  const msg = entry.commit.message.split('\n')[0];
                  parts.push(msg.length > 60 ? msg.substring(0, 60) + '...' : msg);
                }
                parts.push(...buildIssueTooltipLines(entry, b => allNames.includes(b.name)));
                return parts.join('\n');
              }
            }
          }
        },
        scales: {
          x: { ticks: { color: '#8b949e' }, grid: { color: '#30363d' } },
          y: { ticks: { color: '#8b949e' }, grid: { color: '#30363d' }, suggestedMin: 0, suggestedMax: 10 }
        }
      }
    });

    const dashName = d => (!d || !d.length) ? 'solid' : (d[0] >= 6 ? 'dashed' : 'dotted');
    const cap = document.createElement('div');
    cap.className = 'not-activated-legend';
    const colourKey = datasets.length > MAX_INLINE_LEGEND_SERIES
      ? 'Line colour = model (see Models filter above) \u00B7 '
      : 'Line colour = model \u00B7 ';
    cap.innerHTML = colourKey + variants.map(v => `${dashName(v.dash)} = ${escapeHtml(v.label)}`).join(', ');
    div.appendChild(cap);

    appendLegendNotes(div, legendFlags);
    return chart;
  }

  // Triple chart (Skilled / Plugin / Vanilla), now one line per model.
  function createTripleChart(container, title, entries, nameA, nameB, nameC, labelA, labelB, labelC, colorA, colorB, colorC) {
    return renderModelSegmentedChart(container, title, entries, [
      { name: nameA, label: labelA, dash: [], vanilla: false },
      { name: nameB, label: labelB, dash: [8, 6], vanilla: false },
      { name: nameC, label: labelC, dash: [2, 3], vanilla: true },
    ]);
  }

  // Paired chart (Skilled / Vanilla), now one line per model.
  function createPairedChart(container, title, entries, nameA, nameB, labelA, labelB, colorA, colorB) {
    return renderModelSegmentedChart(container, title, entries, [
      { name: nameA, label: labelA, dash: [], vanilla: false },
      { name: nameB, label: labelB, dash: [2, 3], vanilla: true },
    ]);
  }

  // Load first plugin immediately (skip if no evaluation plugins)
  if (plugins.length > 0) {
    await loadPlugin(plugins[0]);
  }
})();
