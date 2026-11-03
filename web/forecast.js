'use strict';

// Страница «Прогноз»: фактическая добыча (сплошные линии) + пунктирный прогноз
// по нефти и воде из ML-эндпоинта. Пока эндпоинт не развёрнут, страница показывает
// факт и аккуратную заглушку «Прогноз недоступен» с кнопкой повтора.
(function () {
  const {
    fetchJson,
    monthKey,
    PALETTE,
    lineSeries,
    lineChartOption,
    whenFontsReady
  } = window.GeoPulse;

  // Ожидаемые пути ML-эндпоинта прогноза. Контракт будет финализирован при его
  // развёртывании — здесь задокументированная точка входа.
  const FORECAST_ENDPOINTS = {
    field: '/api/Forecast/field',
    well: (wellId) => `/api/Forecast/wells/${wellId}`
  };

  // Газ заметно больше нефти/воды по объёму, поэтому выносим его на правую ось.
  const OIL_WATER_AXIS = {
    type: 'value',
    name: 'м³',
    nameTextStyle: { color: PALETTE.muted },
    axisLabel: { color: PALETTE.muted },
    splitLine: { lineStyle: { color: PALETTE.border } }
  };

  const GAS_AXIS = {
    type: 'value',
    name: 'Газ, м³',
    position: 'right',
    nameTextStyle: { color: PALETTE.gas },
    axisLabel: { color: PALETTE.muted },
    splitLine: { show: false }
  };

  // Верхний отступ — место под подписи осей: у правой оси «Газ, м³» имя рисуется
  // над ней, и при маленьком top ECharts обрезает его по ширине.
  const CHART_GRID = { left: 56, right: 60, top: 44, bottom: 52 };

  let chart;
  let wells = [];
  let currentScope = 'field';
  // Номер актуальной загрузки: ответы устаревших запросов игнорируются.
  let loadSeq = 0;
  // Обработчики вешаются однократно, даже если init() перезапускают по «Повторить».
  let controlsWired = false;

  function monthSeries(monthly) {
    return {
      labels: monthly.map((m) => monthKey(m.year, m.month)),
      oil: monthly.map((m) => m.oil ?? 0),
      water: monthly.map((m) => m.water ?? 0),
      gas: monthly.map((m) => m.gas ?? 0)
    };
  }

  function aggregateMonthly(perWell) {
    const map = new Map();
    for (const monthly of perWell) {
      if (!Array.isArray(monthly)) {
        continue;
      }
      for (const row of monthly) {
        const key = monthKey(row.year, row.month);
        const acc = map.get(key) || { year: row.year, month: row.month, oil: 0, water: 0, gas: 0 };
        acc.oil += row.oilVolSum ?? 0;
        acc.water += row.waterVolSum ?? 0;
        acc.gas += row.gasVolSum ?? 0;
        map.set(key, acc);
      }
    }
    return Array.from(map.values()).sort((a, b) => a.year - b.year || a.month - b.month);
  }

  async function fetchFact() {
    if (currentScope === 'field') {
      const perWell = await Promise.all(
        wells.map((well) => fetchJson(`/api/Analytics/monthly?wellId=${well.wellId}`))
      );
      return aggregateMonthly(perWell);
    }

    const wellId = Number(document.getElementById('well-select').value);
    const monthly = await fetchJson(`/api/Analytics/monthly?wellId=${wellId}`);
    return monthly
      .map((row) => ({
        year: row.year,
        month: row.month,
        oil: row.oilVolSum ?? 0,
        water: row.waterVolSum ?? 0,
        gas: row.gasVolSum ?? 0
      }))
      .sort((a, b) => a.year - b.year || a.month - b.month);
  }

  // Приводим ответ ML-эндпоинта к { labels, oil, water } (120 точек по месяцам).
  // null — контракт не совпал; считаем прогноз недоступным.
  function parseForecast(data) {
    const points = Array.isArray(data) ? data : (data && data.points);
    if (!Array.isArray(points) || points.length === 0) {
      return null;
    }

    const labels = [];
    const oil = [];
    const water = [];
    for (const point of points) {
      if (point === null || typeof point !== 'object') {
        continue;
      }
      const year = Number(point.year);
      const month = Number(point.month);
      if (!Number.isInteger(year) || !Number.isInteger(month)) {
        continue;
      }
      labels.push(monthKey(year, month));
      oil.push(Number(point.oil) || 0);
      water.push(Number(point.water) || 0);
    }

    return labels.length > 0 ? { labels, oil, water } : null;
  }

  async function fetchForecast() {
    const url = currentScope === 'field'
      ? FORECAST_ENDPOINTS.field
      : FORECAST_ENDPOINTS.well(Number(document.getElementById('well-select').value));
    const data = await fetchJson(url);
    return parseForecast(data);
  }

  // Прогноз начинается со следующего месяца после последнего факта: до него null.
  function alignForecast(factValues, forecastValues) {
    return new Array(factValues.length).fill(null).concat(forecastValues);
  }

  function renderChart(fact, forecast) {
    const labels = forecast
      ? fact.labels.concat(forecast.labels)
      : fact.labels;

    const series = [
      lineSeries('Нефть, м³ (факт)', fact.oil, PALETTE.oil, { area: true }),
      lineSeries('Вода, м³ (факт)', fact.water, PALETTE.water),
      lineSeries('Газ, м³ (факт)', fact.gas, PALETTE.gas, { yAxisIndex: 1 })
    ];

    if (forecast) {
      series.push(
        lineSeries('Нефть, м³ (прогноз)', alignForecast(fact.oil, forecast.oil), PALETTE.oil, { dashed: true, opacity: 0.6 }),
        lineSeries('Вода, м³ (прогноз)', alignForecast(fact.water, forecast.water), PALETTE.water, { dashed: true, opacity: 0.6 })
      );
    }

    return whenFontsReady().then(() => {
      chart = echarts.init(document.getElementById('forecastChart'));
      chart.setOption(lineChartOption(labels, series, {
        grid: CHART_GRID,
        yAxes: [OIL_WATER_AXIS, GAS_AXIS]
      }));

      showChartSkeleton(false);
    });
  }

  function setPlaceholder(visible) {
    document.getElementById('forecast-placeholder').hidden = !visible;
  }

  // Скелетон виден только пока идёт запрос.
  function showChartSkeleton(visible) {
    const el = document.getElementById('forecastChart-skeleton');
    if (el !== null) {
      el.hidden = !visible;
    }
  }

  // Ошибка загрузки факта: скелетон скрыт, показан текст и кнопка «Повторить».
  function showError(error) {
    showChartSkeleton(false);
    document.getElementById('error-text').textContent =
      'Не удалось загрузить данные: ' + (error && error.message ? error.message : error);
    const el = document.getElementById('forecast-error');
    if (el !== null) {
      el.hidden = false;
    }
  }

  function hideError() {
    const el = document.getElementById('forecast-error');
    if (el !== null) {
      el.hidden = true;
    }
  }

  function setWellSelect(visible) {
    document.getElementById('well-select').hidden = !visible;
  }

  function setScope(scope) {
    currentScope = scope;
    document.getElementById('scope-field').classList.toggle('active', scope === 'field');
    document.getElementById('scope-well').classList.toggle('active', scope === 'well');
    setWellSelect(scope === 'well');
    load();
  }

  async function load() {
    const seq = ++loadSeq;
    hideError();
    setPlaceholder(false);
    showChartSkeleton(true);

    if (chart) {
      chart.dispose();
      chart = null;
    }

    try {
      const fact = monthSeries(await fetchFact());
      // Ответ устарел (переключили область или нажали «Повторить») — не рисуем его.
      if (seq !== loadSeq) {
        return;
      }

      if (fact.labels.length === 0) {
        showError(new Error('API не вернуло данных о добыче'));
        return;
      }

      let forecast = null;
      try {
        forecast = await fetchForecast();
      } catch (forecastError) {
        // Эндпоинт ещё не развёрнут — ожидаемое состояние, не ошибка страницы.
        console.warn('Прогноз недоступен', forecastError);
        forecast = null;
      }

      if (seq !== loadSeq) {
        return;
      }

      await renderChart(fact, forecast);
      setPlaceholder(forecast === null);
    } catch (error) {
      if (seq !== loadSeq) {
        return;
      }
      showError(error);
    }
  }

  function populateWells() {
    const select = document.getElementById('well-select');
    select.innerHTML = wells
      .map((well) => `<option value="${well.wellId}">${well.name} (${well.field})</option>`)
      .join('');
  }

  // Обработчики вешаем один раз: init() может вызываться повторно по «Повторить».
  function wireControls() {
    if (controlsWired) {
      return;
    }
    controlsWired = true;

    document.getElementById('scope-field').addEventListener('click', () => setScope('field'));
    document.getElementById('scope-well').addEventListener('click', () => setScope('well'));
    document.getElementById('well-select').addEventListener('change', load);
    document.getElementById('retry').addEventListener('click', load);
    document.getElementById('error-retry').addEventListener('click', retry);
  }

  // Повтор после ошибки: если список скважин так и не пришёл — начинаем страницу заново.
  async function retry() {
    hideError();
    if (wells.length === 0) {
      await init();
      return;
    }
    await load();
  }

  async function init() {
    hideError();

    try {
      const data = await fetchJson('/api/wells');
      wells = Array.isArray(data) ? data : [];
    } catch (error) {
      wells = [];
      console.error('Список скважин не загружен', error);
    }

    if (wells.length === 0) {
      showError(new Error('Список скважин пуст'));
      return;
    }

    populateWells();
    wireControls();

    await load();
  }

  // График не пересчитывает размер сам — держим его равным ширине контейнера.
  window.addEventListener('resize', () => {
    if (chart) {
      chart.resize();
    }
  });

  init();
})();