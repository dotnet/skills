(async function () {
  // Fetch component manifest
  let components;
  try {
    const response = await fetch('data/components.json');
    if (!response.ok) throw new Error(response.statusText);
    components = await response.json();
  } catch {
    document.body.innerHTML = '<h1>No benchmark data available yet.</h1>';
    return;
  }

  if (!Array.isArray(components) || components.length === 0) {
    document.body.innerHTML = '<h1>No component data found.</h1>';
    return;
  }

  components.sort();

  const tabBar = document.getElementById('tab-bar');
  const tabContentContainer = document.getElementById('tab-content');
  const loadedComponents = new Map(); // track loaded component data

  // Build tabs and placeholder panels
  components.forEach((component, idx) => {
    const tab = document.createElement('div');
    tab.className = 'tab' + (idx === 0 ? ' active' : '');
    tab.textContent = component;
    tab.dataset.component = component;
    tab.addEventListener('click', () => switchTab(component));
    tabBar.appendChild(tab);

    const panel = document.createElement('div');
    panel.className = 'tab-content' + (idx === 0 ? ' active' : '');
    panel.id = `panel-${component}`;
    panel.innerHTML = '<p style="color:#8b949e;text-align:center;padding:2rem;">Loading...</p>';
    tabContentContainer.appendChild(panel);
  });

  async function switchTab(component) {
    tabBar.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t.dataset.component === component));
    tabContentContainer.querySelectorAll('.tab-content').forEach(p => p.classList.toggle('active', p.id === `panel-${component}`));
    if (!loadedComponents.has(component)) {
      await loadComponent(component);
    }
  }

  async function loadComponent(component) {
    const panel = document.getElementById(`panel-${component}`);
    try {
      const response = await fetch(`data/${component}.json`);
      if (!response.ok) throw new Error(response.statusText);
      const data = await response.json();
      loadedComponents.set(component, data);
      renderComponent(component, data, panel);
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

  function renderComponent(component, data, panel) {
    if (!data || !data.entries) {
      panel.innerHTML = '<p style="color:#8b949e;text-align:center;padding:2rem;">No data available.</p>';
      return;
    }

    const qualityEntries = data.entries['Quality'] || [];
    const efficiencyEntries = data.entries['Efficiency'] || [];

    panel.innerHTML = `
      <div class="summary-cards" id="summary-${component}"></div>
      <h2 class="section-title">Quality Over Time</h2>
      <div class="charts-grid" id="quality-${component}"></div>
      <h2 class="section-title">Efficiency Over Time</h2>
      <div class="charts-grid" id="efficiency-${component}"></div>
    `;

    // Summary cards — compute averages across the last 50 entries
    const summaryDiv = document.getElementById(`summary-${component}`);
    const SUMMARY_WINDOW = 50;
    if (qualityEntries.length > 0) {
      // Use only the most recent entries for summary cards
      const recentEntries = qualityEntries.slice(-SUMMARY_WINDOW);
      let skilledTotal = 0, skilledCount = 0, vanillaTotal = 0, vanillaCount = 0;
      recentEntries.forEach(entry => {
        entry.benches.forEach(b => {
          if (b.name.endsWith('- Skilled Quality')) { skilledTotal += b.value; skilledCount++; }
          if (b.name.endsWith('- Vanilla Quality')) { vanillaTotal += b.value; vanillaCount++; }
        });
      });
      const skilledAvg = skilledCount > 0 ? skilledTotal / skilledCount : null;
      const vanillaAvg = vanillaCount > 0 ? vanillaTotal / vanillaCount : null;
      const latestModel = qualityEntries[qualityEntries.length - 1].model;
      const windowLabel = qualityEntries.length > SUMMARY_WINDOW
        ? `last ${SUMMARY_WINDOW} of ${qualityEntries.length} runs`
        : `${qualityEntries.length} runs`;
      if (skilledAvg !== null && vanillaAvg !== null) {
        const delta = (skilledAvg - vanillaAvg).toFixed(2);
        const deltaClass = delta > 0 ? 'positive' : delta < 0 ? 'negative' : 'neutral';
        const deltaSign = delta > 0 ? '+' : '';
        summaryDiv.innerHTML = `
          <div class="card">
            <div class="card-label">Skilled Avg</div>
            <div class="card-value" style="color: var(--skilled)">${skilledAvg.toFixed(2)}</div>
            <div class="card-delta">${windowLabel}</div>
          </div>
          <div class="card">
            <div class="card-label">Vanilla Avg</div>
            <div class="card-value" style="color: var(--vanilla)">${vanillaAvg.toFixed(2)}</div>
            <div class="card-delta">${windowLabel}</div>
          </div>
          <div class="card">
            <div class="card-label">Delta</div>
            <div class="card-value ${deltaClass}">${deltaSign}${delta}</div>
            <div class="card-delta ${deltaClass}">${delta > 0 ? 'Skills improve quality' : delta < 0 ? 'Skills degrade quality' : 'No difference'}</div>
          </div>
          <div class="card">
            <div class="card-label">Data Points</div>
            <div class="card-value">${qualityEntries.length}</div>
            <div class="card-delta">total evaluation runs</div>
          </div>
          <div class="card">
            <div class="card-label">Model</div>
            <div class="card-value" style="font-size: 18px">${latestModel || 'N/A'}</div>
            <div class="card-delta">latest run</div>
          </div>
        `;
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
    const qualityChartsDiv = document.getElementById(`quality-${component}`);
    if (qualityEntries.length > 0) {
      // Discover tests from all entries (not just latest, which may have partial data)
      const tests = new Set();
      qualityEntries.forEach(entry => {
        entry.benches.forEach(b => {
          const match = b.name.match(/^(.+) - (Skilled|Vanilla) Quality$/);
          if (match) tests.add(match[1]);
        });
      });

      tests.forEach(test => {
        createPairedChart(
          qualityChartsDiv, test, qualityEntries,
          `${test} - Skilled Quality`, `${test} - Vanilla Quality`,
          'Skilled', 'Vanilla', '#58a6ff', '#8b949e'
        );
      });
    }

    // Efficiency charts
    const efficiencyChartsDiv = document.getElementById(`efficiency-${component}`);
    if (efficiencyEntries.length > 0) {
      // Discover tests from all entries (not just latest, which may have partial data)
      const effTests = new Set();
      efficiencyEntries.forEach(entry => {
        entry.benches.forEach(b => {
          const match = b.name.match(/^(.+) - Skilled Time$/);
          if (match) effTests.add(match[1]);
        });
      });

      effTests.forEach(test => {
        const div = document.createElement('div');
        div.className = 'chart-container';
        div.innerHTML = `<h3>${test}</h3><canvas></canvas>`;
        efficiencyChartsDiv.appendChild(div);
        const canvas = div.querySelector('canvas');

        const labels = efficiencyEntries.map(e => {
          const d = new Date(e.date);
          return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
        });

        // Precompute per-entry data in a single pass over e.benches
        const timeName = `${test} - Skilled Time`;
        const tokenName = `${test} - Skilled Tokens In`;
        const legendFlags = { notActivated: false, timedOut: false, overfittingModerate: false, overfittingHigh: false, multiIssue: false };

        const perEntryData = efficiencyEntries.map(e => {
          let timeBench = undefined;
          let tokenBench = undefined;
          for (const b of e.benches) {
            if (!timeBench && b.name === timeName) timeBench = b;
            else if (!tokenBench && b.name === tokenName) tokenBench = b;
            if (timeBench && tokenBench) break;
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
          };
        });

        const timeData = perEntryData.map(d => d.timeValue);
        const tokenData = perEntryData.map(d => d.tokenValue);

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

        new Chart(canvas, {
          type: 'line',
          data: {
            labels,
            datasets: [
              {
                label: 'Time (s)',
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
                label: 'Tokens In (k)',
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

  // Helper: create a paired line chart
  function createPairedChart(container, title, entries, nameA, nameB, labelA, labelB, colorA, colorB) {
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

  // Load first component immediately
  await loadComponent(components[0]);
})();