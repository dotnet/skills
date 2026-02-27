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
        const NOT_ACTIVATED_COLOR = '#d29922';
        const TIMED_OUT_COLOR = '#f85149';
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
        let hasAnyNotActivated = false;
        let hasAnyTimedOut = false;

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
          if (timeNA || tokenNA) hasAnyNotActivated = true;
          if (timeTO || tokenTO) hasAnyTimedOut = true;
          return {
            timeValue: timeBench ? timeBench.value : null,
            timeNotActivated: timeNA,
            timeTimedOut: timeTO,
            tokenValue: tokenBench ? tokenBench.value / 1000 : null,
            tokenNotActivated: tokenNA,
            tokenTimedOut: tokenTO,
          };
        });

        const timeData = perEntryData.map(d => d.timeValue);
        const tokenData = perEntryData.map(d => d.tokenValue);

        // Per-point styling: timedOut takes precedence over notActivated
        const timePointBg = perEntryData.map(d =>
          d.timeTimedOut ? TIMED_OUT_COLOR : d.timeNotActivated ? NOT_ACTIVATED_COLOR : '#f0883e'
        );
        const timePointStyle = perEntryData.map(d =>
          d.timeTimedOut ? 'rectRot' : d.timeNotActivated ? 'triangle' : 'circle'
        );
        const timePointRadius = perEntryData.map(d =>
          (d.timeTimedOut || d.timeNotActivated) ? 6 : 4
        );
        const tokenPointBg = perEntryData.map(d =>
          d.tokenTimedOut ? TIMED_OUT_COLOR : d.tokenNotActivated ? NOT_ACTIVATED_COLOR : '#a371f7'
        );
        const tokenPointStyle = perEntryData.map(d =>
          d.tokenTimedOut ? 'rectRot' : d.tokenNotActivated ? 'triangle' : 'circle'
        );
        const tokenPointRadius = perEntryData.map(d =>
          (d.tokenTimedOut || d.tokenNotActivated) ? 6 : 4
        );

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
                    const hasNotActivated = entry && entry.benches &&
                      entry.benches.some(b => b.notActivated);
                    if (hasNotActivated) {
                      parts.push('⚠️ SKILL NOT ACTIVATED');
                    }
                    const hasTimedOut = entry && entry.benches &&
                      entry.benches.some(b => b.timedOut);
                    if (hasTimedOut) {
                      parts.push('⏰ EXECUTION TIMED OUT');
                    }
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

        // Add legend note below chart if it contains not-activated or timed-out points
        if (hasAnyNotActivated) {
          const note = document.createElement('div');
          note.className = 'not-activated-legend';
          note.innerHTML = `⚠️ <span style="color:${NOT_ACTIVATED_COLOR}">▲</span> = Skill was not activated`;
          div.appendChild(note);
        }
        if (hasAnyTimedOut) {
          const note = document.createElement('div');
          note.className = 'not-activated-legend';
          note.innerHTML = `⏰ <span style="color:${TIMED_OUT_COLOR}">◆</span> = Execution timed out`;
          div.appendChild(note);
        }
      });
    }
  }

  // Helper: create a paired line chart
  function createPairedChart(container, title, entries, nameA, nameB, labelA, labelB, colorA, colorB) {
    const NOT_ACTIVATED_COLOR = '#d29922';
    const TIMED_OUT_COLOR = '#f85149';

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
    let hasAnyNotActivated = false;
    let hasAnyTimedOut = false;
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
      if (aNotActivated) hasAnyNotActivated = true;
      if (aTimedOut) hasAnyTimedOut = true;
      return {
        valueA: benchA ? benchA.value : null,
        valueB: benchB ? benchB.value : null,
        aNotActivated,
        aTimedOut,
      };
    });

    const dataA = perEntryData.map(d => d.valueA);
    const dataB = perEntryData.map(d => d.valueB);

    // Build per-point styling for dataset A (Skilled): timedOut takes precedence over notActivated
    const pointBgA = perEntryData.map(d =>
      d.aTimedOut ? TIMED_OUT_COLOR : d.aNotActivated ? NOT_ACTIVATED_COLOR : colorA
    );
    const pointBorderA = perEntryData.map(d =>
      d.aTimedOut ? TIMED_OUT_COLOR : d.aNotActivated ? NOT_ACTIVATED_COLOR : colorA
    );
    const pointRadiusA = perEntryData.map(d =>
      (d.aTimedOut || d.aNotActivated) ? 6 : 4
    );
    const pointStyleA = perEntryData.map(d =>
      d.aTimedOut ? 'rectRot' : d.aNotActivated ? 'triangle' : 'circle'
    );

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
                // Show activation warning when any bench in this entry is not-activated
                const hasNotActivated = entry && entry.benches &&
                  entry.benches.some(b => b.notActivated);
                if (hasNotActivated) {
                  parts.push('⚠️ SKILL NOT ACTIVATED');
                }
                const hasTimedOut = entry && entry.benches &&
                  entry.benches.some(b => b.timedOut);
                if (hasTimedOut) {
                  parts.push('⏰ EXECUTION TIMED OUT');
                }
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

    // Add legend note below chart if it contains not-activated or timed-out points
    if (hasAnyNotActivated) {
      const note = document.createElement('div');
      note.className = 'not-activated-legend';
      note.innerHTML = `⚠️ <span style="color:${NOT_ACTIVATED_COLOR}">▲</span> = Skill was not activated`;
      div.appendChild(note);
    }
    if (hasAnyTimedOut) {
      const note = document.createElement('div');
      note.className = 'not-activated-legend';
      note.innerHTML = `⏰ <span style="color:${TIMED_OUT_COLOR}">◆</span> = Execution timed out`;
      div.appendChild(note);
    }
  }

  // Load first component immediately
  await loadComponent(components[0]);
})();
