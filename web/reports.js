'use strict';

// Данные для отчётов приходят только из API. Обводнённость (water cut) здесь
// не рассчитывается — она считается один раз на бэкенде в ReportService.

async function fetchJson(path) {
  const response = await fetch(path, { headers: { Accept: 'application/json' } });
  if (!response.ok) {
    throw new Error(`HTTP ${response.status} (${path})`);
  }
  return response.json();
}

function formatNumber(value) {
  return Number(value ?? 0).toLocaleString('ru-RU', { maximumFractionDigits: 2 });
}

function monthKey(year, month) {
  return `${year}-${String(month).padStart(2, '0')}`;
}

function renderSummary(summary, wellCount) {
  const rows = [
    ['Суммарная нефть, м³', formatNumber(summary.totalOilVol)],
    ['Суммарная вода, м³', formatNumber(summary.totalWaterVol)],
    ['Суммарный газ, м³', formatNumber(summary.totalGasVol)],
    ['Суммарная закачка воды, м³', formatNumber(summary.totalWaterInjectionVol)],
    ['Суммарные часы работы', formatNumber(summary.totalOnStreamHrs)],
    ['Число скважин', wellCount],
  ];

  const tbody = document.querySelector('#summary tbody');
  tbody.innerHTML = rows
    .map(([label, value]) => `<tr><th>${label}</th><td>${value}</td></tr>`)
    .join('');
}

// Агрегируем помесячную добычу всех скважин по месяцам (источник — /api/Analytics/monthly).
function aggregateMonthly(perWell) {
  const map = new Map();
  for (const monthly of perWell) {
    for (const row of monthly) {
      const key = monthKey(row.year, row.month);
      const acc = map.get(key) || { year: row.year, month: row.month, oil: 0, water: 0 };
      acc.oil += row.oilVolSum ?? 0;
      acc.water += row.waterVolSum ?? 0;
      map.set(key, acc);
    }
  }

  return Array.from(map.values()).sort((a, b) => a.year - b.year || a.month - b.month);
}

let chart;

function renderChart(monthly) {
  const ctx = document.getElementById('monthlyChart').getContext('2d');
  chart = new Chart(ctx, {
    type: 'line',
    data: {
      labels: monthly.map((m) => monthKey(m.year, m.month)),
      datasets: [
        {
          label: 'Нефть, м³',
          data: monthly.map((m) => m.oil),
          borderColor: '#2f6fed',
          backgroundColor: 'rgba(47, 111, 237, 0.10)',
          fill: true,
          tension: 0.25,
          pointRadius: 0
        },
        {
          label: 'Вода, м³',
          data: monthly.map((m) => m.water),
          borderColor: '#16a085',
          backgroundColor: 'rgba(22, 160, 133, 0.10)',
          fill: true,
          tension: 0.25,
          pointRadius: 0
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: { mode: 'index', intersect: false },
      plugins: { legend: { position: 'bottom' } },
      scales: {
        x: { title: { display: true, text: 'Месяц' }, ticks: { maxTicksLimit: 12 } },
        y: { title: { display: true, text: 'м³' }, beginAtZero: true }
      }
    }
  });
}

function showError(error) {
  const el = document.getElementById('error');
  el.textContent = 'Не удалось загрузить данные: ' + (error && error.message ? error.message : error);
  el.hidden = false;
}

async function init() {
  try {
    const [wells, summary] = await Promise.all([
      fetchJson('/api/wells'),
      fetchJson('/api/Analytics/summary')
    ]);

    renderSummary(summary, wells.length);

    const perWell = await Promise.all(
      wells.map((well) => fetchJson(`/api/Analytics/monthly?wellId=${well.wellId}`))
    );
    renderChart(aggregateMonthly(perWell));
  } catch (error) {
    showError(error);
  }
}

init();
