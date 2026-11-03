'use strict';

// Общий модуль веб-клиента GeoPulse.
// Подключается первым на всех страницах, которые работают с API и графиками.
// Содержит единые хелперы и палитру, чтобы не дублировать их между app.js,
// reports.js и forecast.js. Экспортируется в window.GeoPulse (без сборщиков/module).
(function () {
  // Палитра дизайн-системы. Значения совпадают с CSS-переменными в style.css.
  const PALETTE = {
    bg: '#0b0e14',
    panel: '#141a24',
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

  // Базовый набор настроек графика добычи: тёмные тултипы, почти невидимая сетка
  // (#232c3b), подписи месяцев с ограничением по числу делений (~12 на год).
  function lineChartOption(labels, series, opts) {
    const o = opts || {};
    const tickInterval = Math.max(1, Math.ceil(labels.length / 12));
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
        axisLabel: { color: PALETTE.muted, interval: tickInterval },
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
    fetchJson,
    formatNumber,
    escapeHtml,
    monthKey,
    hexToRgba,
    areaGradient,
    lineSeries,
    lineChartOption
  };
})();
