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

        const timeData = efficiencyEntries.map(e => {
          const b = e.benches.find(b => b.name === `${test} - Skilled Time`);
          return b ? b.value : null;
        });

        const tokenData = efficiencyEntries.map(e => {
          const b = e.benches.find(b => b.name === `${test} - Skilled Tokens In`);
          return b ? b.value / 1000 : null;
        });

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
                pointRadius: 4,
                tension: 0.3,
                fill: false,
                yAxisID: 'y'
              },
              {
                label: 'Tokens In (k)',
                data: tokenData,
                borderColor: '#a371f7',
                borderWidth: 2,
                pointRadius: 4,
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
            plugins: { legend: { labels: { color: '#8b949e', font: { size: 11 } } } },
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

    const dataA = entries.map(e => {
      const b = e.benches.find(b => b.name === nameA);
      return b ? b.value : null;
    });

    const dataB = entries.map(e => {
      const b = e.benches.find(b => b.name === nameB);
      return b ? b.value : null;
    });

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
            pointRadius: 4,
            pointHoverRadius: 6,
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
          legend: { labels: { color: '#8b949e', font: { size: 11 } } },
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
  }

  // Load first component immediately
  await loadComponent(components[0]);
})();
