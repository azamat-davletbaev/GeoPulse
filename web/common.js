'use strict';

// Общий модуль веб-клиента GeoPulse.
// Подключается первым на всех страницах, которые работают с API и графиками.
// Содержит единые хелперы и палитру, чтобы не дублировать их между app.js,
// reports.js и forecast.js. Экспортируется в window.GeoPulse (без сборщиков/module).
(function () {
  // Палитра дизайн-системы. Значения совпадают с CSS-переменными в style.css.
  // Фон и панели осветлены на одну ступень под светлую подложку карты,
  // акценты и цвета графиков остаются прежними.
  const PALETTE = {
    bg: '#0e1220',
    panel: '#181f2b',
    border: '#232c3b',
    oil: '#e8a13c',
    water: '#5ec8e5',
    gas: '#7fd1a8',
    injection: '#7d93a8',
    text: '#e8ecf2',
    muted: '#8b95a5'
  };

  // Шрифт для подписей ECharts: canvas не читает CSS-переменные, поэтому дублируем стек.
  const CHART_FONT = '"Inter", system-ui, -apple-system, "Segoe UI", Roboto, sans-serif';

  // Предел подписей по оси X. Полное «YYYY-MM» шире 50 px и на узкой панели скважины
  // (~370 px) слипается, поэтому подписываем годы и держим не больше 10 меток.
  const X_AXIS_MAX_TICKS = 10;

  // ECharts меряет подписи канвасом в момент рендера. Если Inter ещё не загрузился,
  // метрики считаются по fallback-шрифту, и текст рисуется с обрезкой/смещением.
  // Поэтому перед init/setOption ждём готовности шрифтов (локальные файлы грузятся быстро).
  function whenFontsReady() {
    if (typeof document !== 'undefined' && document.fonts && document.fonts.ready) {
      return document.fonts.ready;
    }
    return Promise.resolve();
  }

  async function fetchJson(path) {
    const response = await fetch(path, { headers: { Accept: 'application/json' } });
    if (!response.ok) {
      throw new Error(`HTTP ${response.status} (${path})`);
    }
    return response.json();
  }

  // Единая точность на всех страницах — ровно 2 знака после запятой.
  function formatNumber(value) {
    return Number(value ?? 0).toLocaleString('ru-RU', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2
    });
  }

  function escapeHtml(value) {
    return String(value ?? '')
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#39;');
  }

  // Сравнимый ключ месяца «YYYY-MM» — одинаковый на всех страницах.
  function monthKey(year, month) {
    return `${year}-${String(month).padStart(2, '0')}`;
  }

  // hex-цвет (#rgb или #rrggbb) в rgba с заданной прозрачностью (для градиентов ECharts).
  function hexToRgba(hex, alpha) {
    const clean = String(hex).replace('#', '');
    const full = clean.length === 3
      ? clean.split('').map((c) => c + c).join('')
      : clean;
    if (full.length !== 6) {
      return `rgba(0, 0, 0, ${alpha})`;
    }
    const num = parseInt(full, 16);
    if (Number.isNaN(num)) {
      return `rgba(0, 0, 0, ${alpha})`;
    }
    const r = (num >> 16) & 255;
    const g = (num >> 8) & 255;
    const b = num & 255;
    return `rgba(${r}, ${g}, ${b}, ${alpha})`;
  }

  // Вертикальный линейный градиент под линией основного показателя:
  // сверху полупрозрачный цвет, вниз — полностью прозрачный.
  function areaGradient(hex) {
    return {
      type: 'linear',
      x: 0,
      y: 0,
      x2: 0,
      y2: 1,
      colorStops: [
        { offset: 0, color: hexToRgba(hex, 0.28) },
        { offset: 1, color: hexToRgba(hex, 0) }
      ]
    };
  }

  // Серия ECharts: сплошная линия; опционально пунктир (прогноз) или градиентная
  // заливка под линией основного показателя (факт, нефть).
  function lineSeries(name, data, color, opts) {
    const o = opts || {};
    const series = {
      name,
      type: 'line',
      data,
      smooth: false,
      showSymbol: false,
      lineStyle: {
        color,
        width: 2,
        type: o.dashed ? 'dashed' : 'solid',
        opacity: o.opacity === undefined ? 1 : o.opacity
      },
      itemStyle: { color },
      emphasis: { focus: 'series' }
    };
    if (o.area) {
      series.areaStyle = { color: areaGradient(color) };
    }
    if (o.yAxisIndex !== undefined) {
      series.yAxisIndex = o.yAxisIndex;
    }
    return series;
  }

  // Подписи оси X для ECharts: деления по годам («2008», «2009», …).
  // Метка ставится на первый месяц года из данных, число меток ограничено
  // X_AXIS_MAX_TICKS (лишние годы прореживаются шагом). Полный «YYYY-MM»
  // остаётся только в тултипе — механизм maxTicksLimit из Chart.js в ECharts не работает.
  function xAxisTicks(labels) {
    const firstIndexOfYear = new Map();
    labels.forEach((label, index) => {
      const year = String(label).slice(0, 4);
      if (!firstIndexOfYear.has(year)) {
        firstIndexOfYear.set(year, index);
      }
    });

    const years = Array.from(firstIndexOfYear.keys());
    const step = Math.max(1, Math.ceil(years.length / X_AXIS_MAX_TICKS));
    const tickIndexes = new Set();
    years.forEach((year, order) => {
      if (order % step === 0) {
        tickIndexes.add(firstIndexOfYear.get(year));
      }
    });

    return {
      // interval в ECharts — предикат по индексу метки: показываем только выбранные годы.
      interval: (index) => tickIndexes.has(index),
      formatter: (value) => String(value).slice(0, 4)
    };
  }

  // Базовый набор настроек графика добычи: тёмные тултипы, почти невидимая сетка
  // (#232c3b), подписи оси X — по годам (см. xAxisTicks), с защитой от наложения
  // на узких графиках (hideOverlap скрывает лишние метки вместо слипания).
  function lineChartOption(labels, series, opts) {
    const o = opts || {};
    const ticks = xAxisTicks(labels);
    return {
      backgroundColor: 'transparent',
      animationDuration: 400,
      textStyle: { fontFamily: CHART_FONT, color: PALETTE.text },
      grid: o.grid || { left: 56, right: 20, top: 24, bottom: o.legend === false ? 28 : 52 },
      tooltip: {
        trigger: 'axis',
        backgroundColor: PALETTE.panel,
        borderColor: PALETTE.border,
        textStyle: { color: PALETTE.text, fontSize: 12 },
        axisPointer: { lineStyle: { color: PALETTE.muted } }
      },
      legend: o.legend === false
        ? { show: false }
        : {
            bottom: 0,
            textStyle: { color: PALETTE.muted },
            icon: 'roundRect',
            itemWidth: 14,
            itemHeight: 10
          },
      xAxis: {
        type: 'category',
        boundaryGap: false,
        data: labels,
        axisLine: { lineStyle: { color: PALETTE.border } },
        axisLabel: {
          color: PALETTE.muted,
          interval: ticks.interval,
          formatter: ticks.formatter,
          hideOverlap: true
        },
        axisTick: { show: false },
        splitLine: { show: false }
      },
      yAxis: o.yAxes || {
        type: 'value',
        name: o.yName || '',
        nameTextStyle: { color: PALETTE.muted },
        axisLabel: { color: PALETTE.muted },
        splitLine: { lineStyle: { color: PALETTE.border } }
      },
      series
    };
  }

  window.GeoPulse = {
    PALETTE,
    CHART_FONT,
    whenFontsReady,
    fetchJson,
    formatNumber,
    escapeHtml,
    monthKey,
    hexToRgba,
    areaGradient,
    lineSeries,
    xAxisTicks,
    lineChartOption
  };
})();
