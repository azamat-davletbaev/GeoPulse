'use strict';

// Отчёты по месторождению Volve. Состояние модуля — в локальной области видимости
// (IIFE): глобального `let chart` больше нет. Данные приходят только из API.
// Скелетон живёт ровно столько, сколько длится запрос: при успехе он скрывается и
// показываются данные, при ошибке — скрывается вместе с ним показывается блок с кнопкой.
(function () {
  const {
    fetchJson,
    formatNumber,
    monthKey,
    PALETTE,
    lineSeries,
    lineChartOption,
    whenFontsReady
  } = window.GeoPulse;

  let chart;
  // Номер актуальной загрузки: ответы устаревших запросов (после «Повторить») игнорируются.
  let loadSeq = 0;

  function setHidden(id, hidden) {
    const el = document.getElementById(id);
    if (el !== null) {
      el.hidden = hidden;
    }
  }

  // Состояние «идёт загрузка»: скелетоны видны, данные и ошибка — нет.
  function showLoading() {
    setHidden('summary', true);
    setHidden('summary-skeleton', false);
    setHidden('monthlyChart-skeleton', false);
    setHidden('error', true);
  }

  function showSummary() {
    setHidden('summary-skeleton', true);
    setHidden('summary', false);
  }

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
    return whenFontsReady().then(() => {
      // При повторной загрузке старый экземпляр освобождаем: двумя init на одном DOM
      // ECharts ругается в консоль.
      if (chart) {
        chart.dispose();
        chart = null;
      }

      chart = echarts.init(document.getElementById('monthlyChart'));
      chart.setOption(lineChartOption(
        labels,
        [
          lineSeries('Нефть, м³', monthly.map((m) => m.oil), PALETTE.oil, { area: true }),
          lineSeries('Вода, м³', monthly.map((m) => m.water), PALETTE.water)
        ],
        { yName: 'м³' }
      ));

      setHidden('monthlyChart-skeleton', true);
    });
  }

  function showError(error) {
    // Скелетоны убираем в любом случае — иначе они висят над пустой областью.
    setHidden('summary-skeleton', true);
    setHidden('monthlyChart-skeleton', true);

    document.getElementById('error-text').textContent =
      'Не удалось загрузить данные: ' + (error && error.message ? error.message : error);
    setHidden('error', false);
  }

  async function init() {
    const seq = ++loadSeq;
    showLoading();

    try {
      const [wells, summary] = await Promise.all([
        fetchJson('/api/wells'),
        fetchJson('/api/Analytics/summary')
      ]);

      // Ответ устарел: пока он шёл, пользователь нажал «Повторить».
      if (seq !== loadSeq) {
        return;
      }

      // Guard: список скважин может прийти не массивом (как в app.js).
      const wellList = Array.isArray(wells) ? wells : [];

      renderSummary(summary || {}, wellList.length);
      showSummary();

      const perWell = await Promise.all(
        wellList.map((well) => fetchJson(`/api/Analytics/monthly?wellId=${well.wellId}`))
      );
      if (seq !== loadSeq) {
        return;
      }

      await renderChart(aggregateMonthly(perWell));
    } catch (error) {
      if (seq !== loadSeq) {
        return;
      }
      showError(error);
    }
  }

  const retry = document.getElementById('retry');
  if (retry !== null) {
    retry.addEventListener('click', init);
  }

  // График не пересчитывает размер сам — держим его равным ширине контейнера.
  window.addEventListener('resize', () => {
    if (chart) {
      chart.resize();
    }
  });

  init();
})();
