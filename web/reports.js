'use strict';

// Отчёты по месторождению Volve. Состояние модуля — в локальной области видимости
// (IIFE): глобального `let chart` больше нет. Данные приходят только из API.
(function () {
  const {
    fetchJson,
    formatNumber,
    monthKey,
    PALETTE,
    lineSeries,
    lineChartOption
  } = window.GeoPulse;

  let chart;

  function renderSummary(summary, wellCount) {
    const rows = [
      ['Суммарная нефть, м³', formatNumber(summary.totalOilVol)],
      ['Суммарная вода, м³', formatNumber(summary.totalWaterVol)],
      ['Суммарный газ, м³', formatNumber(summary.totalGasVol)],
      ['Суммарная закачка воды, м³', formatNumber(summary.totalWaterInjectionVol)],
      ['Суммарные часы работы', formatNumber(summary.totalOnStreamHrs)],
      ['Число скважин', wellCount]
    ];

    document.querySelector('#summary tbody').innerHTML = rows
      .map(([label, value]) => `<tr><th>${label}</th><td>${value}</td></tr>`)
      .join('');
  }

  // Агрегируем помесячную добычу всех скважин по месяцам (источник — /api/Analytics/monthly).
  function aggregateMonthly(perWell) {
    const map = new Map();
    for (const monthly of perWell) {
      // Guard как в app.js: элемент ответа может оказаться не массивом.
      if (!Array.isArray(monthly)) {
        continue;
      }
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

  function renderChart(monthly) {
    const labels = monthly.map((m) => monthKey(m.year, m.month));

    chart = echarts.init(document.getElementById('monthlyChart'));
    chart.setOption(lineChartOption(
      labels,
      [
        lineSeries('Нефть, м³', monthly.map((m) => m.oil), PALETTE.oil, { area: true }),
        lineSeries('Вода, м³', monthly.map((m) => m.water), PALETTE.water)
      ],
      { yName: 'м³' }
    ));

    const skeleton = document.getElementById('monthlyChart-skeleton');
    if (skeleton && skeleton.style) {
      skeleton.style.display = 'none';
    }
  }

  function showError(error) {
    const el = document.getElementById('error');
    el.textContent = 'Не удалось загрузить данные: ' + (error && error.message ? error.message : error);
    el.hidden = false;

    const skeleton = document.getElementById('summary-skeleton');
    if (skeleton) {
      skeleton.hidden = true;
    }
  }

  function showSummary() {
    document.getElementById('summary-skeleton').hidden = true;
    document.getElementById('summary').hidden = false;
  }

  async function init() {
    try {
      const [wells, summary] = await Promise.all([
        fetchJson('/api/wells'),
        fetchJson('/api/Analytics/summary')
      ]);

      // Guard: список скважин может прийти не массивом (как в app.js).
      const wellList = Array.isArray(wells) ? wells : [];

      renderSummary(summary, wellList.length);
      showSummary();

      const perWell = await Promise.all(
        wellList.map((well) => fetchJson(`/api/Analytics/monthly?wellId=${well.wellId}`))
      );
      renderChart(aggregateMonthly(perWell));
    } catch (error) {
      showError(error);
    }
  }

  init();
})();
