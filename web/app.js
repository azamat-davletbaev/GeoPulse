'use strict';

// Карта скважин Volve. Всё состояние модуля живёт в локальной области видимости
// (IIFE): глобальных map/chart больше нет. Данные приходят только из API.
(function () {
  const {
    fetchJson,
    formatNumber,
    escapeHtml,
    monthKey,
    PALETTE,
    lineSeries,
    lineChartOption,
    whenFontsReady
  } = window.GeoPulse;

  // ---------- Именованные константы (магические числа вынесены сюда) ----------

  // Вводная анимация: старт с вида Северной Европы, затем плавный перелёт к кусту скважин.
  const INTRO_CENTER = [60.9, 8.6];
  const INTRO_ZOOM = 4;
  const INTRO_TARGET_ZOOM = 13;
  const INTRO_DURATION_MS = 2600;
  const INTRO_SAFETY_MS = 700;
  const TRAJECTORY_CASCADE_DELAY_MS = 150;
  const INTRO_SESSION_KEY = 'geopulse.introSeen';

  // Тайлы Esri World Dark Gray Base (Canvas-сервис ArcGIS, без API-ключа).
  // Подложка тёмная и нейтральная — как прежняя CARTO dark_matter, но без ключа.
  const TILE_URL = 'https://services.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Dark_Gray_Base/MapServer/tile/{z}/{y}/{x}';
  const TILE_MAX_ZOOM = 16;
  const TILE_ATTRIBUTION =
    'Tiles &copy; <a href="https://www.esri.com/">Esri</a> — Source: Esri, HERE, Garmin, ' +
    '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors';

  // Стиль траекторий: мягкое свечение парой слоёв (широкий полупрозрачный + тонкий).
  const TRAJECTORY_COLOR = PALETTE.oil;
  const TRAJECTORY_CORE_WEIGHT = 2;
  const TRAJECTORY_CORE_OPACITY = 0.9;
  const TRAJECTORY_GLOW_WEIGHT = 7;
  const TRAJECTORY_GLOW_OPACITY = 0.18;

  // Маркеры устья и забоя.
  const WELLHEAD_RADIUS = 5;
  const WELLHEAD_FILL_COLOR = PALETTE.oil;
  const WELLHEAD_FILL_OPACITY = 1;
  const WELLHEAD_RING_COLOR = '#ffffff'; // кольцо — только у выделенной скважины
  const WELLHEAD_RING_WEIGHT = 1.5;
  const WELLHEAD_IDLE_WEIGHT = 0;
  const BOTTOMHOLE_RADIUS = 4;
  const BOTTOMHOLE_COLOR = PALETTE.oil;
  const BOTTOMHOLE_WEIGHT = 1.5;
  const BOTTOMHOLE_OPACITY = 0.7;

  // Пан для траекторий — ниже overlay-панели, чтобы маркеры устьев были поверх линий.
  const TRAJECTORY_PANE = 'trajectories';
  const TRAJECTORY_PANE_Z_INDEX = 390;

  const FALLBACK_CENTER = [0, 0];
  const FALLBACK_ZOOM = 2;

  let map;
  let chart;
  let fieldTotalOil = null;
  let selectedMarker = null;
  let introFinished = false;

  function wellCaption(well) {
    return `Скважина ${well.name}`;
  }

  function monthLabel(monthly) {
    return monthKey(monthly.year, monthly.month);
  }

  function monthOrdinal(monthly) {
    return monthly.year * 12 + monthly.month;
  }

  function wellStats(monthly) {
    let oil = 0;
    let water = 0;
    let firstOilMonth = null;
    let lastOilMonth = null;
    let peakMonth = null;

    for (const month of monthly) {
      const monthOil = month.oilVolSum ?? 0;
      const monthWater = month.waterVolSum ?? 0;
      oil += monthOil;
      water += monthWater;

      if (monthOil > 0) {
        if (firstOilMonth === null || monthOrdinal(month) < monthOrdinal(firstOilMonth)) {
          firstOilMonth = month;
        }
        if (lastOilMonth === null || monthOrdinal(month) > monthOrdinal(lastOilMonth)) {
          lastOilMonth = month;
        }
        if (peakMonth === null || monthOil > (peakMonth.oilVolSum ?? 0)) {
          peakMonth = month;
        }
      }
    }

    return { oil, water, firstOilMonth, lastOilMonth, peakMonth };
  }

  function waterCutPct(oil, water) {
    const total = oil + water;
    return total > 0 ? (water / total) * 100 : 0;
  }

  function sharePct(oil, totalOil) {
    return totalOil > 0 ? (oil / totalOil) * 100 : 0;
  }

  function reportUrl(well) {
    const wellId = Number(well.wellId);
    return Number.isInteger(wellId) && wellId > 0 ? `/api/Reports/wells/${wellId}.xlsx` : null;
  }

  function popupHtml(well, monthly) {
    const stats = Array.isArray(monthly) ? wellStats(monthly) : null;

    let html =
      `<strong>${escapeHtml(wellCaption(well))}</strong><br>` +
      `Месторождение: ${escapeHtml(well.field)}<br>`;

    if (well.tdTvd !== null && well.tdTvd !== undefined) {
      html += `Забой: ${formatNumber(well.tdTvd)} м<br>`;
    }

    if (well.tdMd !== null && well.tdMd !== undefined) {
      html += `Длина ствола: ${formatNumber(well.tdMd)} м<br>`;
    }

    html += `Добыча нефти (суммарно): ${stats === null ? '—' : formatNumber(stats.oil) + ' м³'}`;

    if (stats === null) {
      return html;
    }

    const waterCut = stats.lastOilMonth === null
      ? '—'
      : `${formatNumber(waterCutPct(stats.lastOilMonth.oilVolSum ?? 0, stats.lastOilMonth.waterVolSum ?? 0))} %` +
        ` (${escapeHtml(monthLabel(stats.lastOilMonth))})`;

    const peak = stats.peakMonth === null
      ? '—'
      : `${formatNumber(stats.peakMonth.oilVolSum)} м³ (${escapeHtml(monthLabel(stats.peakMonth))})`;

    const period = stats.firstOilMonth === null || stats.lastOilMonth === null
      ? '—'
      : `${escapeHtml(monthLabel(stats.firstOilMonth))} — ${escapeHtml(monthLabel(stats.lastOilMonth))}`;

    const share = fieldTotalOil === null
      ? '—'
      : `${formatNumber(sharePct(stats.oil, fieldTotalOil))} %`;

    html +=
      `<br>Добыча воды (суммарно): ${formatNumber(stats.water)} м³` +
      `<br>Обводнённость: ${waterCut}` +
      `<br>Пик добычи нефти: ${peak}` +
      `<br>Период добычи: ${period}` +
      `<br>Доля в добыче месторождения: ${share}`;

    const url = reportUrl(well);
    if (url !== null) {
      html += `<br><a class="popup-report" href="${url}">Скачать отчёт (Excel)</a>`;
    }

    return html;
  }
  function openPanel() {
    document.getElementById('panel').classList.add('open');
    document.getElementById('panel').setAttribute('aria-hidden', 'false');
  }

  function closePanel() {
    document.getElementById('panel').classList.remove('open');
    document.getElementById('panel').setAttribute('aria-hidden', 'true');
    highlightMarker(null);
  }

  function showPanelSkeleton(visible) {
    const el = document.getElementById('chart-skeleton');
    if (el !== null) {
      el.hidden = !visible;
    }
  }

  // Освобождаем график перед новой отрисовкой: второй init на том же DOM ECharts
  // считает ошибкой и пишет предупреждение в консоль.
  function disposeChart() {
    if (chart) {
      chart.dispose();
      chart = null;
    }
  }

  // Состояние «добыча грузится»: старый график убираем, показываем скелетон.
  function preparePanelLoading(well) {
    document.getElementById('panel-title').textContent = wellCaption(well);
    document.getElementById('panel-error').hidden = true;
    openPanel();
    disposeChart();
    showPanelSkeleton(true);
  }

  function showChart(well, monthly) {
    document.getElementById('panel-title').textContent = `${wellCaption(well)} — ${well.field}`;
    document.getElementById('panel-error').hidden = true;
    openPanel();

    disposeChart();

    const labels = monthly.map(monthLabel);
    const oil = monthly.map((m) => m.oilVolSum ?? 0);
    const water = monthly.map((m) => m.waterVolSum ?? 0);

    return whenFontsReady().then(() => {
      chart = echarts.init(document.getElementById('chart'));
      chart.setOption(lineChartOption(
        labels,
        [
          lineSeries('Нефть, м³', oil, PALETTE.oil, { area: true }),
          lineSeries('Вода, м³', water, PALETTE.water)
        ],
        { yName: 'м³' }
      ));

      showPanelSkeleton(false);
    });
  }

  function showPanelError(well, error) {
    document.getElementById('panel-title').textContent = wellCaption(well);
    document.getElementById('panel-error').textContent =
      'Не удалось загрузить добычу: ' + (error && error.message ? error.message : error);
    document.getElementById('panel-error').hidden = false;
    showPanelSkeleton(false);
    openPanel();
  }

  async function selectWell(well, marker, monthly) {
    highlightMarker(marker);
    if (marker) {
      marker.setPopupContent(popupHtml(well, null));
      marker.openPopup();
    }

    // Данных ещё нет — показываем скелетон ровно на время запроса.
    const hasData = Array.isArray(monthly);
    if (!hasData) {
      preparePanelLoading(well);
    }

    try {
      const data = hasData
        ? monthly
        : await fetchJson(`/api/Analytics/monthly?wellId=${well.wellId}`);
      if (marker) {
        marker.setPopupContent(popupHtml(well, data));
      }
      await showChart(well, data);
    } catch (error) {
      showPanelError(well, error);
    }
  }

  function onMarkerClick(well, marker) {
    return selectWell(well, marker);
  }

  function hasOil(monthly) {
    return Array.isArray(monthly) && monthly.some((month) => (month.oilVolSum ?? 0) > 0);
  }

  async function findProducingWell(located) {
    for (const well of located) {
      try {
        const monthly = await fetchJson(`/api/Analytics/monthly?wellId=${well.wellId}`);
        if (hasOil(monthly)) {
          return { well, monthly };
        }
      } catch (error) {
        console.warn(`Автовыбор: пропущена ${wellCaption(well)}`, error);
      }
    }

    return null;
  }

  async function loadFieldTotalOil() {
    try {
      const summary = await fetchJson('/api/Analytics/summary');
      fieldTotalOil = summary.totalOilVol ?? null;
    } catch (error) {
      console.warn('Не удалось загрузить суммарную добычу месторождения', error);
    }
  }

  async function autoSelectWell(located, markers) {
    await loadFieldTotalOil();

    const found = await findProducingWell(located);
    if (found === null) {
      return;
    }

    await selectWell(found.well, markers.get(found.well.wellId), found.monthly);
  }
  function toFiniteNumber(value) {
    if (value === null || value === undefined) {
      return null;
    }

    if (typeof value === 'string' && value.trim() === '') {
      return null;
    }

    const number = Number(value);
    return Number.isFinite(number) ? number : null;
  }

  function safeLatLng(latitude, longitude) {
    const lat = toFiniteNumber(latitude);
    const lon = toFiniteNumber(longitude);

    if (lat === null || lon === null) {
      return null;
    }

    if (Math.abs(lat) > 90 || Math.abs(lon) > 180) {
      return null;
    }

    return [lat, lon];
  }

  // Замеры приходят из /api/wells/{wellId}/trajectory уже в WGS84.
  async function fetchTrajectoryPoints(well) {
    const points = await fetchJson(`/api/wells/${well.wellId}/trajectory`);
    if (!Array.isArray(points)) {
      return [];
    }

    const valid = [];
    for (const point of points) {
      const latlng = safeLatLng(point.latitude, point.longitude);
      if (latlng === null) {
        continue;
      }

      valid.push({ md: Number(point.md) || 0, latlng });
    }

    return valid.sort((a, b) => a.md - b.md);
  }

  function buildTrajectoryLayer(points) {
    if (points.length === 0) {
      return null;
    }

    const layer = L.layerGroup();

    if (points.length >= 2) {
      const latlngs = points.map((point) => point.latlng);

      // Широкий полупрозрачный слой — мягкое свечение.
      L.polyline(latlngs, {
        pane: TRAJECTORY_PANE,
        color: TRAJECTORY_COLOR,
        weight: TRAJECTORY_GLOW_WEIGHT,
        opacity: TRAJECTORY_GLOW_OPACITY,
        interactive: false
      }).addTo(layer);

      // Тонкая основная линия.
      L.polyline(latlngs, {
        pane: TRAJECTORY_PANE,
        color: TRAJECTORY_COLOR,
        weight: TRAJECTORY_CORE_WEIGHT,
        opacity: TRAJECTORY_CORE_OPACITY,
        interactive: false
      }).addTo(layer);
    }

    // Забой — незалитый кружок в последней точке траектории (по MD).
    L.circleMarker(points[points.length - 1].latlng, {
      pane: TRAJECTORY_PANE,
      radius: BOTTOMHOLE_RADIUS,
      color: BOTTOMHOLE_COLOR,
      weight: BOTTOMHOLE_WEIGHT,
      opacity: BOTTOMHOLE_OPACITY,
      fill: false,
      interactive: false
    }).addTo(layer);

    return layer;
  }

  // Guard на одну скважину: запрос, разбор ответа, построение и добавление слоя
  // завёрнуты в один try/catch, чтобы сбой одной скважины не мешал остальным.
  async function addTrajectoryLayer(well) {
    try {
      const points = await fetchTrajectoryPoints(well);
      const layer = buildTrajectoryLayer(points);
      if (layer === null) {
        return;
      }

      layer.addTo(map);
    } catch (error) {
      console.warn(`Траектория ${wellCaption(well)} не добавлена`, error);
    }
  }

  // Устье — янтарный круг. Тонкое белое кольцо появляется только у выделенной скважины.
  function addWellheadMarker(well) {
    try {
      const latlng = safeLatLng(well.latitude, well.longitude);
      if (latlng === null) {
        console.warn(`Устье ${wellCaption(well)} пропущено: некорректные координаты`, well.latitude, well.longitude);
        return null;
      }

      const marker = L.circleMarker(latlng, {
        radius: WELLHEAD_RADIUS,
        color: 'transparent',
        weight: WELLHEAD_IDLE_WEIGHT,
        fillColor: WELLHEAD_FILL_COLOR,
        fillOpacity: WELLHEAD_FILL_OPACITY
      });
      marker.bindPopup(popupHtml(well, null));
      marker.on('click', () => onMarkerClick(well, marker));
      marker.addTo(map);

      return marker;
    } catch (error) {
      console.warn('Устье не добавлено', well, error);
      return null;
    }
  }
  function highlightMarker(marker) {
    if (selectedMarker && selectedMarker !== marker) {
      selectedMarker.setStyle({ color: 'transparent', weight: WELLHEAD_IDLE_WEIGHT });
    }

    selectedMarker = marker;

    if (marker) {
      marker.setStyle({ color: WELLHEAD_RING_COLOR, weight: WELLHEAD_RING_WEIGHT });
    }
  }

  function computeCluster(located) {
    const bounds = L.latLngBounds([]);
    for (const well of located) {
      const latlng = safeLatLng(well.latitude, well.longitude);
      if (latlng !== null) {
        bounds.extend(latlng);
      }
    }
    return bounds;
  }

  function applyFinalView(located) {
    const bounds = computeCluster(located);
    if (bounds.isValid()) {
      map.setView(bounds.getCenter(), INTRO_TARGET_ZOOM);
    } else {
      map.setView(FALLBACK_CENTER, FALLBACK_ZOOM);
    }
  }

  function flyToCluster(located) {
    const bounds = computeCluster(located);
    if (!bounds.isValid()) {
      map.setView(FALLBACK_CENTER, FALLBACK_ZOOM);
      return;
    }

    try {
      map.flyTo(bounds.getCenter(), INTRO_TARGET_ZOOM, { duration: INTRO_DURATION_MS });
    } catch (error) {
      console.warn('Вводный перелёт не выполнен', error);
      map.setView(bounds.getCenter(), INTRO_TARGET_ZOOM);
    }
  }

  // Каскадная отрисовка траекторий по одной (~150 мс между стартами). Выполняется
  // один раз за сессию — guard introFinished защищает от повторного запуска.
  function drawTrajectoriesCascade(list) {
    if (introFinished) {
      return;
    }
    introFinished = true;

    list.forEach((well, index) => {
      setTimeout(() => {
        addTrajectoryLayer(well);
      }, index * TRAJECTORY_CASCADE_DELAY_MS);
    });
  }

  function showLoadError(error) {
    document.getElementById('panel-title').textContent = 'Ошибка загрузки';
    document.getElementById('panel-error').textContent =
      'Не удалось загрузить скважины: ' + (error && error.message ? error.message : error);
    document.getElementById('panel-error').hidden = false;
    showPanelSkeleton(false);
    openPanel();
  }

  async function init() {
    map = L.map('map');
    map.setView(INTRO_CENTER, INTRO_ZOOM);

    map.createPane(TRAJECTORY_PANE);
    const pane = map.getPane(TRAJECTORY_PANE);
    if (pane) {
      pane.style.zIndex = TRAJECTORY_PANE_Z_INDEX;
    }

    L.tileLayer(TILE_URL, {
      maxZoom: TILE_MAX_ZOOM,
      attribution: TILE_ATTRIBUTION
    }).addTo(map);

    const markers = new Map();
    const located = [];

    let wells = [];
    let loadError = null;
    try {
      wells = await fetchJson('/api/wells');
    } catch (error) {
      loadError = error;
      console.error('Список скважин не загружен', error);
    }

    const list = Array.isArray(wells) ? wells : [];

    // Маркеры устьев добавляем сразу — они образуют «куст», к которому летит карта.
    for (const well of list) {
      const marker = addWellheadMarker(well);
      if (marker !== null) {
        markers.set(well.wellId, marker);
        located.push(well);
      }
    }

    if (loadError !== null || list.length === 0) {
      // Ошибка загрузки — без анимации: сразу финальный вид и сообщение об ошибке.
      applyFinalView(located);
      showLoadError(loadError);
      return;
    }

    if (sessionStorage.getItem(INTRO_SESSION_KEY)) {
      // Повторное открытие в той же сессии — без перелёта.
      applyFinalView(located);
      drawTrajectoriesCascade(list);
    } else {
      sessionStorage.setItem(INTRO_SESSION_KEY, '1');
      flyToCluster(located);

      // После прибытия — каскадная отрисовка траекторий.
      const startCascade = () => drawTrajectoriesCascade(list);
      map.once('moveend', startCascade);
      setTimeout(startCascade, INTRO_DURATION_MS + INTRO_SAFETY_MS);
    }

    try {
      await autoSelectWell(located, markers);
    } catch (error) {
      console.warn('Автовыбор скважины не выполнен', error);
    }
  }

  // Кнопка закрытия панели: без неё страница работает, поэтому отсутствие элемента
  // в разметке не должно валить скрипт до инициализации карты.
  const panelClose = document.getElementById('panel-close');
  if (panelClose !== null) {
    panelClose.addEventListener('click', closePanel);
  }

  // График в панели не пересчитывает размер сам — держим его равным контейнеру.
  window.addEventListener('resize', () => {
    if (chart) {
      chart.resize();
    }
  });

  init().catch((error) => {
    console.error('Инициализация карты не завершена', error);
  });
})();

