(async function () {
  // HTML escape helper for defense-in-depth against XSS
  function escapeHtml(str) {
    if (str == null) return '';
    const div = document.createElement('div');
    div.textContent = String(str);
    return div.innerHTML;
  }

  // AGENTVIZ session replay configuration
  const sessionManifestUrl = 'https://raw.githubusercontent.com/dotnet/skills/dashboard-session-data/data/manifest.json';
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

    const qualityEntries = data.entries['Quality'] || [];
    const efficiencyEntries = data.entries['Efficiency'] || [];

    const replayHref = `${replayBaseUrl}?manifest=${encodeURIComponent(sessionManifestUrl)}&tag=${encodeURIComponent(plugin)}`;

    panel.innerHTML = `
      <div style="display:flex;align-items:center;gap:16px;margin-bottom:8px;">
        <a href="${escapeHtml(replayHref)}" target="_blank" rel="noopener"
           style="color:#58a6ff;font-size:13px;text-decoration:none;">&#9654; Sessions Visualisation</a>
      </div>
      <div class="summary-cards" id="summary-${plugin}"></div>
      <h2 class="section-title">Quality Over Time</h2>
      <div class="charts-grid" id="quality-${plugin}"></div>
      <h2 class="section-title">Efficiency Over Time</h2>
      <div class="charts-grid" id="efficiency-${plugin}"></div>
    `;

    // Summary cards — compute averages across the last 50 entries
    const summaryDiv = document.getElementById(`summary-${plugin}`);
    const SUMMARY_WINDOW = 50;
    if (qualityEntries.length > 0) {
      // Use only the most recent entries for summary cards
      const recentEntries = qualityEntries.slice(-SUMMARY_WINDOW);
      let skilledTotal = 0, skilledCount = 0, pluginTotal = 0, pluginCount = 0, vanillaTotal = 0, vanillaCount = 0;
      recentEntries.forEach(entry => {
        entry.benches.forEach(b => {
          if (b.name.endsWith('- Skilled Quality')) { skilledTotal += b.value; skilledCount++; }
          if (b.name.endsWith('- Plugin Quality')) { pluginTotal += b.value; pluginCount++; }
          if (b.name.endsWith('- Vanilla Quality')) { vanillaTotal += b.value; vanillaCount++; }
        });
      });
      const skilledAvg = skilledCount > 0 ? skilledTotal / skilledCount : null;
      const pluginAvg = pluginCount > 0 ? pluginTotal / pluginCount : null;
      const vanillaAvg = vanillaCount > 0 ? vanillaTotal / vanillaCount : null;
      const latestModel = qualityEntries[qualityEntries.length - 1].model;
      const windowLabel = qualityEntries.length > SUMMARY_WINDOW
        ? `last ${SUMMARY_WINDOW} of ${qualityEntries.length} runs`
        : `${qualityEntries.length} runs`;
      if (skilledAvg !== null && vanillaAvg !== null) {
        const delta = (skilledAvg - vanillaAvg).toFixed(2);
        const deltaClass = delta > 0 ? 'positive' : delta < 0 ? 'negative' : 'neutral';
        const deltaSign = delta > 0 ? '+' : '';
        let cardsHtml = `
          <div class="card">
            <div class="card-label">Skilled (Isolated) Avg</div>
            <div class="card-value" style="color: var(--skilled)">${skilledAvg.toFixed(2)}</div>
            <div class="card-delta">${windowLabel}</div>
          </div>`;
        if (pluginAvg !== null) {
          cardsHtml += `
          <div class="card">
            <div class="card-label">Skilled (Plugin) Avg</div>
            <div class="card-value" style="color: #3fb950">${pluginAvg.toFixed(2)}</div>
            <div class="card-delta">${windowLabel}</div>
          </div>`;
        }
        cardsHtml += `
          <div class="card">
            <div class="card-label">Vanilla Avg</div>
            <div class="card-value" style="color: var(--vanilla)">${vanillaAvg.toFixed(2)}</div>
            <div class="card-delta">${windowLabel}</div>
          </div>
          <div class="card">
            <div class="card-label">Delta (Isolated)</div>
            <div class="card-value ${deltaClass}">${deltaSign}${delta}</div>
            <div class="card-delta ${deltaClass}">${delta > 0 ? 'Skills improve quality' : delta < 0 ? 'Skills degrade quality' : 'No difference'}</div>
          </div>`;
        if (pluginAvg !== null && vanillaAvg !== null) {
          const pluginDelta = (pluginAvg - vanillaAvg).toFixed(2);
          const pluginDeltaClass = pluginDelta > 0 ? 'positive' : pluginDelta < 0 ? 'negative' : 'neutral';
          const pluginDeltaSign = pluginDelta > 0 ? '+' : '';
          cardsHtml += `
          <div class="card">
            <div class="card-label">Delta (Plugin)</div>
            <div class="card-value ${pluginDeltaClass}">${pluginDeltaSign}${pluginDelta}</div>
            <div class="card-delta ${pluginDeltaClass}">${pluginDelta > 0 ? 'Plugin improves quality' : pluginDelta < 0 ? 'Plugin degrades quality' : 'No difference'}</div>
          </div>`;
        }
        cardsHtml += `
          <div class="card">
            <div class="card-label">Data Points</div>
            <div class="card-value">${qualityEntries.length}</div>
            <div class="card-delta">total evaluation runs</div>
          </div>
          <div class="card">
            <div class="card-label">Model</div>
            <div class="card-value" style="font-size: 18px">${escapeHtml(latestModel) || 'N/A'}</div>
            <div class="card-delta">latest run</div>
          </div>
        `;
        summaryDiv.innerHTML = cardsHtml;
      }

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
          createTripleChart(
            qualityChartsDiv, test, qualityEntries,
            `${test} - Skilled Quality`, `${test} - Plugin Quality`, `${test} - Vanilla Quality`,
            'Isolated', 'Plugin', 'Vanilla',
            '#58a6ff', '#3fb950', '#8b949e'
          );
        } else {
          createPairedChart(
            qualityChartsDiv, test, qualityEntries,
            `${test} - Skilled Quality`, `${test} - Vanilla Quality`,
            'Skilled', 'Vanilla', '#58a6ff', '#8b949e'
          );
        }
      });
    }

    // Efficiency charts
    const efficiencyChartsDiv = document.getElementById(`efficiency-${plugin}`);
    if (efficiencyEntries.length > 0) {
      // Discover tests from all entries (not just latest, which may have partial data)
      const effTests = new Set();
      let hasAnyPluginEff = false;
      efficiencyEntries.forEach(entry => {
        entry.benches.forEach(b => {
          const matchSkilled = b.name.match(/^(.+) - Skilled Time$/);
          if (matchSkilled) effTests.add(matchSkilled[1]);
          const matchPlugin = b.name.match(/^(.+) - Plugin Time$/);
          if (matchPlugin) { effTests.add(matchPlugin[1]); hasAnyPluginEff = true; }
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
        const legendFlags = { notActivated: false, timedOut: false, overfittingModerate: false, overfittingHigh: false, multiIssue: false };

        const perEntryData = efficiencyEntries.map(e => {
          let timeBench = undefined;
          let tokenBench = undefined;
          let plugTimeBench = undefined;
          let plugTokenBench = undefined;
          for (const b of e.benches) {
            if (!timeBench && b.name === timeName) timeBench = b;
            else if (!tokenBench && b.name === tokenName) tokenBench = b;
            else if (!plugTimeBench && b.name === plugTimeName) plugTimeBench = b;
            else if (!plugTokenBench && b.name === plugTokenName) plugTokenBench = b;
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
          };
        });

        const timeData = perEntryData.map(d => d.timeValue);
        const tokenData = perEntryData.map(d => d.tokenValue);
        const plugTimeData = perEntryData.map(d => d.plugTimeValue);
        const plugTokenData = perEntryData.map(d => d.plugTokenValue);

        // Per-point styling using shared helper
        const timeAp = perEntryData.map(d => getPointAppearance({ timedOut: d.timeTimedOut, notActivated: d.timeNotActivated, overfitting: d.timeOverfitting }, '#f0883e'));
        const timePointBg = timeAp.map(a => a.color);
        const timePointStyle = timeAp.map(a => a.style);
        const timePointRadius = timeAp.map(a => a.radius);
        const timePointBorderWidth = timeAp.map(a => a.borderWidth);
        const tokenAp = perEntryData.map(d => getPointAppearance({ timedOut: d.tokenTimedOut, notActivated: d.tokenNotActivated, overfitting: d.tokenOverfitting }, '#a371f7'));
        const tokenPointBg = tokenAp.map(a => a.color);
        const tokenPointStyle = tokenAp.map(a => a.style);
        const tokenPointRadius = tokenAp.map(a => a.radius);
        const tokenPointBorderWidth = tokenAp.map(a => a.borderWidth);

        const datasets = [
          {
            label: 'Isolated Time (s)',
            data: timeData,
            borderColor: '#f0883e',
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
            borderColor: '#a371f7',
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
            borderColor: '#56d364',
            borderWidth: 2,
            pointRadius: 4,
            pointHoverRadius: 6,
            tension: 0.3,
            borderDash: [5, 5],
            fill: false,
            yAxisID: 'y1'
          });
        }

        new Chart(canvas, {
          type: 'line',
          data: {
            labels,
            datasets
          },
          options: {
            responsive: true,
            interaction: { mode: 'index', intersect: false },
            plugins: {
              legend: { labels: { color: '#8b949e', font: { size: 11 }, usePointStyle: true } },
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
                ticks: { color: '#f0883e' },
                grid: { color: '#30363d' },
                title: { display: true, text: 'seconds', color: '#f0883e' }
              },
              y1: {
                type: 'linear',
                position: 'right',
                ticks: { color: '#a371f7' },
                grid: { drawOnChartArea: false },
                title: { display: true, text: 'tokens (k)', color: '#a371f7' }
              }
            }
          }
        });

        appendLegendNotes(div, legendFlags);
      });
    }
  }

  // Helper: create a triple line chart with three series (e.g., Skill / Plugin / Vanilla quality)
  function createTripleChart(container, title, entries, nameA, nameB, nameC, labelA, labelB, labelC, colorA, colorB, colorC) {
    const div = document.createElement('div');
    div.className = 'chart-container';
    div.innerHTML = `<h3>${title}</h3><canvas></canvas>`;
    container.appendChild(div);
    const canvas = div.querySelector('canvas');

    const labels = entries.map(e => {
      const d = new Date(e.date);
      return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
    });

    // Precompute per-entry data in a single pass
    const legendFlags = { notActivated: false, timedOut: false, overfittingModerate: false, overfittingHigh: false, multiIssue: false };
    const perEntryData = entries.map(e => {
      let benchA = undefined, benchB = undefined, benchC = undefined;
      for (const b of e.benches) {
        if (!benchA && b.name === nameA) benchA = b;
        else if (!benchB && b.name === nameB) benchB = b;
        else if (!benchC && b.name === nameC) benchC = b;
        if (benchA && benchB && benchC) break;
      }
      const aNotActivated = !!(benchA && benchA.notActivated);
      const aTimedOut = !!(benchA && benchA.timedOut);
      const aOverfitting = benchA && benchA.overfitting ? benchA.overfitting : null;
      const bNotActivated = !!(benchB && benchB.notActivated);
      const bTimedOut = !!(benchB && benchB.timedOut);
      const bOverfitting = benchB && benchB.overfitting ? benchB.overfitting : null;
      if (aNotActivated || bNotActivated) legendFlags.notActivated = true;
      if (aTimedOut || bTimedOut) legendFlags.timedOut = true;
      if (aOverfitting || bOverfitting) {
        if (aOverfitting === 'high' || bOverfitting === 'high') legendFlags.overfittingHigh = true;
        else legendFlags.overfittingModerate = true;
      }
      const aIssues = (aNotActivated ? 1 : 0) + (aTimedOut ? 1 : 0) + (aOverfitting ? 1 : 0);
      const bIssues = (bNotActivated ? 1 : 0) + (bTimedOut ? 1 : 0) + (bOverfitting ? 1 : 0);
      if (aIssues > 1 || bIssues > 1) legendFlags.multiIssue = true;
      return {
        valueA: benchA ? benchA.value : null,
        valueB: benchB ? benchB.value : null,
        valueC: benchC ? benchC.value : null,
        aNotActivated, aTimedOut, aOverfitting,
        bNotActivated, bTimedOut, bOverfitting,
      };
    });

    const dataA = perEntryData.map(d => d.valueA);
    const dataB = perEntryData.map(d => d.valueB);
    const dataC = perEntryData.map(d => d.valueC);

    // Per-point styling for dataset A (Isolated) and B (Plugin)
    const pointApA = perEntryData.map(d => getPointAppearance({ timedOut: d.aTimedOut, notActivated: d.aNotActivated, overfitting: d.aOverfitting }, colorA));
    const pointBgA = pointApA.map(a => a.color);
    const pointBorderA = pointApA.map(a => a.color);
    const pointRadiusA = pointApA.map(a => a.radius);
    const pointStyleA = pointApA.map(a => a.style);
    const pointBorderWidthA = pointApA.map(a => a.borderWidth);
    const pointApB = perEntryData.map(d => getPointAppearance({ timedOut: d.bTimedOut, notActivated: d.bNotActivated, overfitting: d.bOverfitting }, colorB));
    const pointBgB = pointApB.map(a => a.color);
    const pointBorderB = pointApB.map(a => a.color);
    const pointRadiusB = pointApB.map(a => a.radius);
    const pointStyleB = pointApB.map(a => a.style);
    const pointBorderWidthB = pointApB.map(a => a.borderWidth);

    new Chart(canvas, {
      type: 'line',
      data: {
        labels,
        datasets: [
          {
            label: labelA,
            data: dataA,
            borderColor: colorA,
            backgroundColor: colorA + '20',
            borderWidth: 2,
            pointBackgroundColor: pointBgA,
            pointBorderColor: pointBorderA,
            pointRadius: pointRadiusA,
            pointBorderWidth: pointBorderWidthA,
            pointStyle: pointStyleA,
            pointHoverRadius: 8,
            tension: 0.3,
            fill: false
          },
          {
            label: labelB,
            data: dataB,
            borderColor: colorB,
            backgroundColor: colorB + '20',
            borderWidth: 2,
            pointBackgroundColor: pointBgB,
            pointBorderColor: pointBorderB,
            pointRadius: pointRadiusB,
            pointBorderWidth: pointBorderWidthB,
            pointStyle: pointStyleB,
            pointHoverRadius: 8,
            tension: 0.3,
            fill: false
          },
          {
            label: labelC,
            data: dataC,
            borderColor: colorC,
            backgroundColor: colorC + '20',
            borderWidth: 2,
            pointRadius: 4,
            pointHoverRadius: 6,
            tension: 0.3,
            borderDash: [5, 5],
            fill: false
          }
        ]
      },
      options: {
        responsive: true,
        interaction: { mode: 'index', intersect: false },
        plugins: {
          legend: { labels: { color: '#8b949e', font: { size: 11 }, usePointStyle: true } },
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
                parts.push(...buildIssueTooltipLines(entry, b => b.name === nameA || b.name === nameB || b.name === nameC));
                return parts.join('\n');
              }
            }
          }
        },
        scales: {
          x: { ticks: { color: '#8b949e' }, grid: { color: '#30363d' } },
          y: {
            ticks: { color: '#8b949e' },
            grid: { color: '#30363d' },
            suggestedMin: 0,
            suggestedMax: 10
          }
        }
      }
    });

    appendLegendNotes(div, legendFlags);
  }

  // Helper: create a paired line chart
  function createPairedChart(container, title, entries, nameA, nameB, labelA, labelB, colorA, colorB) {
    const div = document.createElement('div');
    div.className = 'chart-container';
    div.innerHTML = `<h3>${escapeHtml(title)}</h3><canvas></canvas>`;
    container.appendChild(div);
    const canvas = div.querySelector('canvas');

    const labels = entries.map(e => {
      const d = new Date(e.date);
      return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
    });

    // Precompute per-entry data in a single pass
    const legendFlags = { notActivated: false, timedOut: false, overfittingModerate: false, overfittingHigh: false, multiIssue: false };
    const perEntryData = entries.map(e => {
      let benchA = undefined;
      let benchB = undefined;
      for (const b of e.benches) {
        if (!benchA && b.name === nameA) benchA = b;
        else if (!benchB && b.name === nameB) benchB = b;
        if (benchA && benchB) break;
      }
      const aNotActivated = !!(benchA && benchA.notActivated);
      const aTimedOut = !!(benchA && benchA.timedOut);
      const aOverfitting = benchA && benchA.overfitting ? benchA.overfitting : null;
      if (aNotActivated) legendFlags.notActivated = true;
      if (aTimedOut) legendFlags.timedOut = true;
      if (aOverfitting) {
        if (aOverfitting === 'high') legendFlags.overfittingHigh = true;
        else legendFlags.overfittingModerate = true;
      }
      const issueCount = (aNotActivated ? 1 : 0) + (aTimedOut ? 1 : 0) + (aOverfitting ? 1 : 0);
      if (issueCount > 1) legendFlags.multiIssue = true;
      return {
        valueA: benchA ? benchA.value : null,
        valueB: benchB ? benchB.value : null,
        aNotActivated,
        aTimedOut,
        aOverfitting,
      };
    });

    const dataA = perEntryData.map(d => d.valueA);
    const dataB = perEntryData.map(d => d.valueB);

    // Build per-point styling for dataset A (Skilled) using shared helper
    const pointApA = perEntryData.map(d => getPointAppearance({ timedOut: d.aTimedOut, notActivated: d.aNotActivated, overfitting: d.aOverfitting }, colorA));
    const pointBgA = pointApA.map(a => a.color);
    const pointBorderA = pointApA.map(a => a.color);
    const pointRadiusA = pointApA.map(a => a.radius);
    const pointStyleA = pointApA.map(a => a.style);
    const pointBorderWidthA = pointApA.map(a => a.borderWidth);

    new Chart(canvas, {
      type: 'line',
      data: {
        labels,
        datasets: [
          {
            label: labelA,
            data: dataA,
            borderColor: colorA,
            backgroundColor: colorA + '20',
            borderWidth: 2,
            pointBackgroundColor: pointBgA,
            pointBorderColor: pointBorderA,
            pointRadius: pointRadiusA,
            pointBorderWidth: pointBorderWidthA,
            pointStyle: pointStyleA,
            pointHoverRadius: 8,
            tension: 0.3,
            fill: false
          },
          {
            label: labelB,
            data: dataB,
            borderColor: colorB,
            backgroundColor: colorB + '20',
            borderWidth: 2,
            pointRadius: 4,
            pointHoverRadius: 6,
            tension: 0.3,
            borderDash: [5, 5],
            fill: false
          }
        ]
      },
      options: {
        responsive: true,
        interaction: { mode: 'index', intersect: false },
        plugins: {
          legend: { labels: { color: '#8b949e', font: { size: 11 }, usePointStyle: true } },
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
                    parts.push(...buildIssueTooltipLines(entry, b => b.name === nameA || b.name === nameB));
                    return parts.join('\n');
                  }
                }
              }
            },
            scales: {
              x: { ticks: { color: '#8b949e' }, grid: { color: '#30363d' } },
              y: {
                ticks: { color: '#8b949e' },
                grid: { color: '#30363d' },
                suggestedMin: 0,
                suggestedMax: 10
              }
            }
          }
        });

    appendLegendNotes(div, legendFlags);
  }

  // Load first plugin immediately (skip if no evaluation plugins)
  if (plugins.length > 0) {
    await loadPlugin(plugins[0]);
  }
})();