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
  // Финальный зум 14: на 13 устья сливаются в одно пятно.
  const INTRO_CENTER = [60.9, 8.6];
  const INTRO_ZOOM = 4;
  const INTRO_TARGET_ZOOM = 14;
  const INTRO_DURATION_MS = 2600;
  const INTRO_SAFETY_MS = 700;
  // Полёт стартует после первого load тайлов; дольше этого таймаута не ждём.
  const INTRO_TILE_WAIT_MS = 1500;
  const TRAJECTORY_CASCADE_DELAY_MS = 150;
  const INTRO_SESSION_KEY = 'geopulse.introSeen';

  // Тайлы Esri World Light Gray Base (Canvas-сервис ArcGIS, без API-ключа):
  // светлая подложка внутри тёмного UI — на ней янтарные траектории читаются лучше,
  // чем на прежней тёмной карте. Порядок уровней — {z}/{y}/{x}, как у сервиса.
  const TILE_URL = 'https://services.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Light_Gray_Base/MapServer/tile/{z}/{y}/{x}';
  const TILE_MAX_ZOOM = 16;
  const TILE_ATTRIBUTION =
    'Tiles &copy; <a href="https://www.esri.com/">Esri</a> — Source: Esri, HERE, Garmin, ' +
    '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors';

  // Второй слой — подписи мест (Reference). Показывается только на зуме ≥ 11,
  // чтобы старт полёта над океаном был чистым (подписи малы и всё равно нечитаемы).
  const LABEL_TILE_URL = 'https://services.arcgisonline.com/ArcGIS/rest/services/Canvas/World_Light_Gray_Reference/MapServer/tile/{z}/{y}/{x}';
  const LABEL_TILE_OPACITY = 0.9;
  const LABEL_MIN_ZOOM = 11;
  const LABEL_PANE = 'labels';
  // Выше тайлов (200) и ниже траекторий (390): подписи мест не перекрывают скважины.
  const LABEL_PANE_Z_INDEX = 250;

  // Постоянные подписи скважин у устьев.
  const WELL_LABEL_MIN_ZOOM = 13;
  const WELL_LABEL_OFFSET_X = 8;
  // Устья Volve лежат в пятне ~10 м: на рабочих зумах это 2–4 px, поэтому подписи
  // идут колонкой с шагом 24 px — плашка подписи ~19 px, запас покрывает разброс точек.
  const WELL_LABEL_STEP_Y = 24;

  // Стиль траекторий: тонкая обводка вместо прежнего свечения — glow на светлой карте
  // выглядит грязью. Сама линия остаётся янтарной.
  const TRAJECTORY_COLOR = PALETTE.oil;
  const TRAJECTORY_CORE_WEIGHT = 2;
  const TRAJECTORY_CORE_OPACITY = 0.95;
  const TRAJECTORY_EDGE_COLOR = 'rgba(120, 73, 12, 0.35)';
  const TRAJECTORY_EDGE_WEIGHT = 3.5;

  // Маркеры устья и забоя.
  const WELLHEAD_RADIUS = 5;
  const WELLHEAD_FILL_COLOR = PALETTE.oil;
  const WELLHEAD_FILL_OPACITY = 0.85;
  const WELLHEAD_STROKE_COLOR = '#ffffff';
  const WELLHEAD_IDLE_WEIGHT = 1.6;
  const WELLHEAD_ACTIVE_WEIGHT = 2.4;
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
  let selectedWell = null;
  let selectedMonthly = null;
  let introFinished = false;
  // Пока идёт перелёт, попапы не открываем: Leaflet.autoPan прерывает flyTo (дрожание карты).
  let introInFlight = false;

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

  // Содержимое попапа заворачивается во внутреннюю обёртку .popup-body: анимировать
  // можно её (opacity + лёгкий сдвиг), но не позиционируемый самим Leaflet контейнер.
  function wrapPopup(inner) {
    return `<div class="popup-body">${inner}</div>`;
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
      return wrapPopup(html);
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

    return wrapPopup(html);
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
    selectedWell = well;
    selectedMonthly = null;
    if (marker) {
      marker.setPopupContent(popupHtml(well, null));
      // Во время вводного перелёта попап не открываем: autoPan Leaflet'а прерывает
      // flyTo, из-за чего карта «застревает» и дрожит. Откроем его после прибытия.
      if (!introInFlight) {
        marker.openPopup();
      }
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
      selectedMonthly = data;
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

      // Тонкая тёмно-янтарная обводка: держит линию читаемой на светлых тайлах,
      // но, в отличие от прежнего glow, не расплывается грязным пятном.
      L.polyline(latlngs, {
        pane: TRAJECTORY_PANE,
        color: TRAJECTORY_EDGE_COLOR,
        weight: TRAJECTORY_EDGE_WEIGHT,
        opacity: 1,
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

  // Guard на одну скважину: запрос и построение слоя завёрнуты в try/catch,
  // чтобы сбой одной скважины не мешал остальным.
  async function loadTrajectoryLayer(well) {
    try {
      const points = await fetchTrajectoryPoints(well);
      return buildTrajectoryLayer(points);
    } catch (error) {
      console.warn(`Траектория ${wellCaption(well)} не добавлена`, error);
      return null;
    }
  }

  // Устье — янтарный круг с белой обводкой (соседние точки не сливаются в пятно).
  // Подпись — постоянный tooltip справа; показывается с зума WELL_LABEL_MIN_ZOOM.
  function addWellheadMarker(well, labelOffsetY) {
    try {
      const latlng = safeLatLng(well.latitude, well.longitude);
      if (latlng === null) {
        console.warn(`Устье ${wellCaption(well)} пропущено: некорректные координаты`, well.latitude, well.longitude);
        return null;
      }

      const marker = L.circleMarker(latlng, {
        radius: WELLHEAD_RADIUS,
        color: WELLHEAD_STROKE_COLOR,
        weight: WELLHEAD_IDLE_WEIGHT,
        fillColor: WELLHEAD_FILL_COLOR,
        fillOpacity: WELLHEAD_FILL_OPACITY
      });
      marker.bindPopup(popupHtml(well, null));
      marker.bindTooltip(well.name, {
        className: 'well-label',
        direction: 'right',
        permanent: true,
        interactive: false,
        opacity: 1,
        offset: L.point(WELL_LABEL_OFFSET_X, labelOffsetY)
      });
      marker.on('click', () => onMarkerClick(well, marker));
      marker.addTo(map);

      return marker;
    } catch (error) {
      console.warn('Устье не добавлено', well, error);
      return null;
    }
  }

  // Подписи устьев: постоянные tooltip'ы открыты только на зуме ≥ WELL_LABEL_MIN_ZOOM.
  function syncWellLabels(markers) {
    const show = map.getZoom() >= WELL_LABEL_MIN_ZOOM;
    markers.forEach((marker) => {
      if (show) {
        marker.openTooltip();
      } else {
        marker.closeTooltip();
      }
    });
  }

  function highlightMarker(marker) {
    if (selectedMarker && selectedMarker !== marker) {
      selectedMarker.setStyle({ weight: WELLHEAD_IDLE_WEIGHT });
    }

    selectedMarker = marker;

    if (marker) {
      marker.setStyle({ weight: WELLHEAD_ACTIVE_WEIGHT });
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
      // ВАЖНО: у Leaflet flyTo опция duration — в секундах, не в миллисекундах.
      // Передача 2600 «как мс» растягивает полёт на 43 минуты: карта кажется застывшей.
      map.flyTo(bounds.getCenter(), INTRO_TARGET_ZOOM, {
        duration: INTRO_DURATION_MS / 1000
      });
    } catch (error) {
      console.warn('Вводный перелёт не выполнен', error);
      map.setView(bounds.getCenter(), INTRO_TARGET_ZOOM);
    }
  }

  // Каскадная отрисовка траекторий: данные всех скважин грузятся параллельно, а слои
  // добавляются по одному за кадр через requestAnimationFrame — без reflow-шторма
  // (каждый addTo заставляет Leaflet перерисовать SVG-слой).
  // Выполняется один раз за сессию — guard introFinished защищает от повторного запуска.
  function drawTrajectoriesCascade(list) {
    if (introFinished) {
      return;
    }
    introFinished = true;

    const pending = list.map((well, index) =>
      loadTrajectoryLayer(well).then((layer) => ({ index, layer })));

    Promise.all(pending)
      .then((results) => {
        const layers = results
          .sort((a, b) => a.index - b.index)
          .map((item) => item.layer)
          .filter((layer) => layer !== null);
        addLayersInFrames(layers);
      })
      .catch((error) => {
        console.warn('Траектории не построены', error);
      });
  }

  function now() {
    return typeof performance !== 'undefined' && typeof performance.now === 'function'
      ? performance.now()
      : Date.now();
  }

  function nextFrame(callback) {
    if (typeof requestAnimationFrame === 'function') {
      requestAnimationFrame(callback);
      return;
    }
    setTimeout(() => callback(now()), 16);
  }

  // Слои добавляются не чаще одного за кадр (интервал TRAJECTORY_CASCADE_DELAY_MS),
  // поэтому Leaflet перерисовывает SVG один раз за кадр, а не на каждый вызов.
  function addLayersInFrames(layers) {
    const started = now();
    let index = 0;

    const step = () => {
      while (index < layers.length && now() - started >= index * TRAJECTORY_CASCADE_DELAY_MS) {
        try {
          layers[index].addTo(map);
        } catch (error) {
          console.warn('Слой траектории не добавлен', error);
        }
        index += 1;
      }

      if (index < layers.length) {
        nextFrame(step);
      }
    };

    nextFrame(step);
  }

  // Готовность базового слоя: первый load тайлов или fallback-таймаут. Промис только
  // резолвится — ошибки тайлов не должны ломать вводную анимацию.
  function whenTilesReady(layer, timeoutMs) {
    return new Promise((resolve) => {
      let settled = false;
      const done = () => {
        if (settled) {
          return;
        }
        settled = true;
        resolve();
      };

      if (layer && typeof layer.once === 'function') {
        layer.once('load', done);
      }
      setTimeout(done, timeoutMs);
    });
  }

  // Пользователь просил меньше движения — перелёт пропускаем целиком.
  function prefersReducedMotion() {
    if (typeof window.matchMedia !== 'function') {
      return false;
    }
    try {
      return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    } catch (error) {
      return false;
    }
  }

  // Вводная анимация. Гарантии: полёт стартует только после готовности тайлов,
  // любое исключение в цепочке сразу показывает финальный вид, а не вид мира.
  function startIntro(tilesReady, located, list) {
    const fallbackToFinal = (message, error) => {
      if (message) {
        console.warn(message, error);
      }
      introInFlight = false;
      try {
        applyFinalView(located);
      } catch (viewError) {
        console.warn('Финальный вид карты не применён', viewError);
      }
      drawTrajectoriesCascade(list);
    };

    try {
      tilesReady.then(() => {
        introInFlight = true;
        try {
          flyToCluster(located);

          // onArrived может прийти дважды (moveend и страховочный таймер) — обрабатываем один раз.
          let arrived = false;
          const onArrived = () => {
            if (arrived) {
              return;
            }
            arrived = true;
            introInFlight = false;
            // Гарантия: если перелёт по любой причине не доехал до финального зума
            // (прерван, троттлинг кадров, сбой анимации), сразу ставим финальный вид.
            // Когда валидных устьев нет, нейтральный вид уже выставлен — не дублируем.
            try {
              if (located.length > 0 && Math.abs(map.getZoom() - INTRO_TARGET_ZOOM) > 0.01) {
                applyFinalView(located);
              }
            } catch (error) {
              console.warn('Финальный вид не применён', error);
            }
            openSelectedPopup();
            drawTrajectoriesCascade(list);
          };

          map.once('moveend', onArrived);
          setTimeout(onArrived, INTRO_DURATION_MS + INTRO_SAFETY_MS);
        } catch (error) {
          fallbackToFinal('Вводный перелёт не выполнен', error);
        }
      }).catch((error) => {
        fallbackToFinal('Вводная анимация пропущена', error);
      });
    } catch (error) {
      fallbackToFinal('Вводная анимация не запущена', error);
    }
  }

  // Попап выделенной скважины открывается после прибытия: во время перелёта autoPan
  // прерывает flyTo (карта «застревает» и дрожит). Содержимое не сбрасываем —
  // берём уже загруженные месячные данные, иначе в попапе снова окажется «—».
  function openSelectedPopup() {
    if (selectedWell === null || selectedMarker === null) {
      return;
    }
    try {
      selectedMarker.setPopupContent(popupHtml(selectedWell, selectedMonthly));
      selectedMarker.openPopup();
    } catch (error) {
      console.warn('Попап скважины не открыт', error);
    }
  }

  // Слой подписей мест: добавляется на карту только на зуме ≥ LABEL_MIN_ZOOM.
  function createLabelLayer(target) {
    let added = false;

    const layer = L.tileLayer(LABEL_TILE_URL, {
      pane: LABEL_PANE,
      maxZoom: TILE_MAX_ZOOM,
      opacity: LABEL_TILE_OPACITY
    });

    const sync = () => {
      const need = target.getZoom() >= LABEL_MIN_ZOOM;
      if (need === added) {
        return;
      }
      if (need) {
        layer.addTo(target);
      } else {
        target.removeLayer(layer);
      }
      added = need;
    };

    target.on('zoomend', sync);
    sync();

    return layer;
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
    // Иконки Leaflet лежат рядом с vendor-файлами; без явного пути L.Icon.Default
    // ищет их относительно страницы и получает 404 (marker-icon.png и др.).
    if (L.Icon && L.Icon.Default) {
      L.Icon.Default.imagePath = '/vendor/leaflet/images/';
    }

    map = L.map('map');
    map.setView(INTRO_CENTER, INTRO_ZOOM);

    map.createPane(TRAJECTORY_PANE);
    const pane = map.getPane(TRAJECTORY_PANE);
    if (pane) {
      pane.style.zIndex = TRAJECTORY_PANE_Z_INDEX;
    }

    map.createPane(LABEL_PANE);
    const labelPane = map.getPane(LABEL_PANE);
    if (labelPane) {
      labelPane.style.zIndex = LABEL_PANE_Z_INDEX;
    }

    const baseLayer = L.tileLayer(TILE_URL, {
      maxZoom: TILE_MAX_ZOOM,
      attribution: TILE_ATTRIBUTION
    }).addTo(map);

    // Слушатель ставится сразу после addTo: иначе событие load можно пропустить,
    // пока ждём /api/wells, и полёт начнётся на пустой (ещё не отрисованной) карте.
    const tilesReady = whenTilesReady(baseLayer, INTRO_TILE_WAIT_MS);
    createLabelLayer(map);

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
    // Подписи идут колонкой вниз от куста: устья Volve лежат в пятне ~10 м (2–4 px на
    // зуме 14), а попап раскрывается вверх — так подписи не перекрываются ни им, ни собой.
    list.forEach((well, index) => {
      const marker = addWellheadMarker(well, index * WELL_LABEL_STEP_Y);
      if (marker !== null) {
        markers.set(well.wellId, marker);
        located.push(well);
      }
    });

    if (loadError !== null || list.length === 0) {
      // Ошибка загрузки — без анимации: сразу финальный вид и сообщение об ошибке.
      applyFinalView(located);
      showLoadError(loadError);
      return;
    }

    // Подписи устьев включаются по зуму; обработчик вешаем до первого перелёта.
    map.on('zoomend', () => syncWellLabels(markers));
    syncWellLabels(markers);

    if (prefersReducedMotion() || sessionStorage.getItem(INTRO_SESSION_KEY)) {
      // Повторный визит в сессии или просьба «меньше движения» — сразу финальный вид.
      applyFinalView(located);
      drawTrajectoriesCascade(list);
    } else {
      sessionStorage.setItem(INTRO_SESSION_KEY, '1');
      startIntro(tilesReady, located, list);
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

