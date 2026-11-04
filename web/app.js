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

  // Данные на карте скрыты до конца вводного полёта: с высоты космоса семь устьев
  // сливаются в оранжевое пятно. Класс на контейнере карты гасит панели слоёв
  // (opacity 0), а его снятие даёт плавный fade-in ~400 мс — переход в style.css.
  const MAP_DATA_PENDING_CLASS = 'data-pending';

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

  // Подписи забоев: конечная точка траектории, значение MD берётся из данных замера.
  const BOTTOMHOLE_LABEL_MIN_ZOOM = 13;
  const BOTTOMHOLE_LABEL_OFFSET_X = 7;

  // Стиль траекторий: тонкая обводка вместо прежнего свечения — glow на светлой карте
  // выглядит грязью. Сама линия остаётся янтарной.
  const TRAJECTORY_COLOR = PALETTE.oil;
  const TRAJECTORY_CORE_WEIGHT = 2;
  const TRAJECTORY_CORE_OPACITY = 0.95;
  const TRAJECTORY_EDGE_COLOR = 'rgba(120, 73, 12, 0.35)';
  const TRAJECTORY_EDGE_WEIGHT = 3.5;

  // Подсветка выбранной скважины: бирюза читается и на янтарной линии, и на светлых
  // тайлах. Толщина линии +1 px, у устья — бирюзовая заливка в белой обводке.
  // Это цвет выбора, а не палитра графиков: «Нефть» везде остаётся янтарной.
  const SELECTION_COLOR = '#2dd4bf';
  const SELECTION_EDGE_COLOR = 'rgba(45, 212, 191, 0.30)';
  const SELECTION_LINE_WEIGHT_BONUS = 1;
  const SELECTION_FILL_OPACITY = 1;

  // Маркеры устья и забоя. С зума WELLHEAD_COMPACT_MIN_ZOOM устья уменьшаются:
  // в плотном кусте крупные кружки сливаются в одно пятно.
  const WELLHEAD_RADIUS = 5;
  const WELLHEAD_RADIUS_COMPACT = 3.5;
  const WELLHEAD_COMPACT_MIN_ZOOM = 14;
  const WELLHEAD_FILL_COLOR = PALETTE.oil;
  const WELLHEAD_FILL_OPACITY = 0.85;
  const WELLHEAD_STROKE_COLOR = '#ffffff';
  const WELLHEAD_IDLE_WEIGHT = 1.6;
  const WELLHEAD_IDLE_WEIGHT_COMPACT = 1.2;
  const WELLHEAD_ACTIVE_WEIGHT = 2.4;
  const BOTTOMHOLE_RADIUS = 4;
  const BOTTOMHOLE_COLOR = PALETTE.oil;
  const BOTTOMHOLE_WEIGHT = 1.5;
  const BOTTOMHOLE_OPACITY = 0.7;

  // Панели слоёв задают и порядок отрисовки, и приоритет клика при пересечении
  // объектов: устье (overlay-панель Leaflet, 400) > линия (390) > забой (380).
  const TRAJECTORY_PANE = 'trajectories';
  const TRAJECTORY_PANE_Z_INDEX = 390;
  const BOTTOMHOLE_PANE = 'bottomholes';
  const BOTTOMHOLE_PANE_Z_INDEX = 380;

  const FALLBACK_CENTER = [0, 0];
  const FALLBACK_ZOOM = 2;

  let map;
  let chart;
  let fieldTotalOil = null;
  let selectedWell = null;
  let selectedMonthly = null;
  let introFinished = false;
  // Слои по скважинам: устье, линия траектории и забой — нужны для подсветки выбора.
  const wellLayers = new Map();
  // Скважины по wellId: список в панели не хранит ссылки на объекты DOM.
  const wellsById = new Map();
  // Помесячные данные по скважинам: сводка, список и график не ходят в API повторно.
  const monthlyByWell = new Map();
  const monthlyRequests = new Map();
  // Скважины в порядке API — в том же порядке строится список в панели.
  let orderedWells = [];
  // Токен выбора: ответ устаревшего запроса не должен перерисовать панель.
  let selectionToken = 0;
  // Данные на карте ещё скрыты (идёт вводный полёт) — клики по невидимым слоям игнорируем.
  let dataPending = true;
  // Автовыбор скважины с максимальной суммарной нефтью — один раз за загрузку страницы.
  let autoSelectDone = false;
  // Промис фоновой предзагрузки помесячных данных: автовыбор ждёт его, если полёт
  // закончился раньше, — панель не должна открываться пустой.
  let preloadPromise = Promise.resolve();

  function el(id) {
    return document.getElementById(id);
  }

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

  // ------------------------------------------------------------------
  // Сводка скважины (правая панель)
  // ------------------------------------------------------------------
  // Поля сводки те же, что были в попапе Leaflet. Сам попап убран целиком —
  // вместе с позиционированием, анимацией и конфликтом autoPan с перелётом.
  function summaryRow(label, value) {
    return `<div class="summary-row"><dt>${escapeHtml(label)}</dt><dd>${value}</dd></div>`;
  }

  function summaryHtml(well, monthly) {
    const stats = Array.isArray(monthly) ? wellStats(monthly) : null;

    let rows = summaryRow('Месторождение', escapeHtml(well.field));

    if (well.tdTvd !== null && well.tdTvd !== undefined) {
      rows += summaryRow('Забой', `${formatNumber(well.tdTvd)} м`);
    }

    if (well.tdMd !== null && well.tdMd !== undefined) {
      rows += summaryRow('Длина ствола', `${formatNumber(well.tdMd)} м`);
    }

    rows += summaryRow(
      'Добыча нефти (суммарно)',
      stats === null ? '—' : `${formatNumber(stats.oil)} м³`
    );

    if (stats !== null) {
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

      rows +=
        summaryRow('Добыча воды (суммарно)', `${formatNumber(stats.water)} м³`) +
        summaryRow('Обводнённость', waterCut) +
        summaryRow('Пик добычи нефти', peak) +
        summaryRow('Период добычи', period) +
        summaryRow('Доля в добыче месторождения', share);
    }

    const url = reportUrl(well);
    const report = url === null
      ? ''
      : `<a class="button summary-report" href="${url}">Скачать отчёт (Excel)</a>`;

    return `<dl class="summary-list">${rows}</dl>${report}`;
  }

  // Подпись забоя: MD последнего замера траектории. Значение берётся из данных,
  // ничего не досчитывается; без MD подпись не выдумывает глубину.
  function bottomholeLabel(md) {
    const value = Number(md);
    const text = Number.isFinite(value) && value > 0
      ? value.toLocaleString('ru-RU', { maximumFractionDigits: 0 })
      : '—';
    return `забой, ${text} м`;
  }
  function openPanel() {
    el('panel').classList.add('open');
    el('panel').setAttribute('aria-hidden', 'false');
  }

  // Панель не закрывается кликом по карте: единственный способ — крестик,
  // и он же снимает выбор скважины (см. clearSelection).
  function closePanel() {
    el('panel').classList.remove('open');
    el('panel').setAttribute('aria-hidden', 'true');
  }

  function showPanelSkeleton(visible) {
    const skeleton = el('chart-skeleton');
    if (skeleton !== null) {
      skeleton.hidden = !visible;
    }
  }

  function showSummary(well, monthly) {
    const summary = el('well-summary');
    if (summary === null) {
      return;
    }

    summary.innerHTML = summaryHtml(well, monthly);
    summary.hidden = false;
  }

  function clearSummary() {
    const summary = el('well-summary');
    if (summary === null) {
      return;
    }

    summary.innerHTML = '';
    summary.hidden = true;
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
    el('panel-title').textContent = wellCaption(well);
    el('panel-error').hidden = true;
    openPanel();
    disposeChart();
    showPanelSkeleton(true);
  }

  function showChart(well, monthly) {
    el('panel-title').textContent = `${wellCaption(well)} — ${well.field}`;
    el('panel-error').hidden = true;
    openPanel();

    disposeChart();

    const token = selectionToken;
    const labels = monthly.map(monthLabel);
    const oil = monthly.map((m) => m.oilVolSum ?? 0);
    const water = monthly.map((m) => m.waterVolSum ?? 0);

    return whenFontsReady().then(() => {
      // Пока ждали шрифты, могли выбрать другую скважину — иначе два init на одном DOM.
      if (token !== selectionToken) {
        return;
      }

      disposeChart();
      chart = echarts.init(el('chart'));
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
    el('panel-title').textContent = wellCaption(well);
    el('panel-error').textContent =
      'Не удалось загрузить добычу: ' + (error && error.message ? error.message : error);
    el('panel-error').hidden = false;
    showPanelSkeleton(false);
    openPanel();
  }

  // ------------------------------------------------------------------
  // Выбор скважины: подсветка на карте, сводка и график в панели
  // ------------------------------------------------------------------

  // Устье: выбранное — бирюзовая заливка в белой обводке. С зума 14 маркеры
  // уменьшаются: в плотном кусте крупные кружки сливаются в одно пятно.
  function styleWellhead(marker, selected) {
    if (!marker) {
      return;
    }

    const compact = map.getZoom() >= WELLHEAD_COMPACT_MIN_ZOOM;
    const radius = compact ? WELLHEAD_RADIUS_COMPACT : WELLHEAD_RADIUS;

    if (selected) {
      marker.setStyle({
        radius,
        color: WELLHEAD_STROKE_COLOR,
        weight: WELLHEAD_ACTIVE_WEIGHT,
        fillColor: SELECTION_COLOR,
        fillOpacity: SELECTION_FILL_OPACITY
      });
      return;
    }

    marker.setStyle({
      radius,
      color: WELLHEAD_STROKE_COLOR,
      weight: compact ? WELLHEAD_IDLE_WEIGHT_COMPACT : WELLHEAD_IDLE_WEIGHT,
      fillColor: WELLHEAD_FILL_COLOR,
      fillOpacity: WELLHEAD_FILL_OPACITY
    });
  }

  // Траектория выбранной скважины: бирюзовая линия на 1 px толще, забой — бирюзовый.
  function styleTrajectory(entry, selected) {
    const trajectory = entry === undefined || entry === null ? null : entry.trajectory;
    if (!trajectory) {
      return;
    }

    if (trajectory.core) {
      trajectory.core.setStyle(selected
        ? {
            color: SELECTION_COLOR,
            weight: TRAJECTORY_CORE_WEIGHT + SELECTION_LINE_WEIGHT_BONUS,
            opacity: 1
          }
        : {
            color: TRAJECTORY_COLOR,
            weight: TRAJECTORY_CORE_WEIGHT,
            opacity: TRAJECTORY_CORE_OPACITY
          });
    }

    if (trajectory.edge) {
      trajectory.edge.setStyle({ color: selected ? SELECTION_EDGE_COLOR : TRAJECTORY_EDGE_COLOR });
    }

    if (trajectory.bottom) {
      trajectory.bottom.setStyle(selected
        ? { color: SELECTION_COLOR, opacity: 1 }
        : { color: BOTTOMHOLE_COLOR, opacity: BOTTOMHOLE_OPACITY });
    }
  }

  function highlightWell(wellId, selected) {
    const entry = wellLayers.get(wellId);
    if (!entry) {
      return;
    }

    styleWellhead(entry.marker, selected);
    styleTrajectory(entry, selected);
  }

  // Единая точка входа для кликов по устью, линии, забою и по строке списка.
  // Повторный клик по той же скважине снимает выбор.
  function toggleWellSelection(well) {
    if (dataPending) {
      // Данные ещё скрыты вводным перелётом — невидимые слои выбирать нельзя.
      return;
    }

    if (selectedWell !== null && selectedWell.wellId === well.wellId) {
      clearSelection();
      return;
    }

    selectWell(well);
  }

  // Крестик в шапке панели: снимает выбор и прячет панель до следующего выбора.
  function clearSelection() {
    if (selectedWell !== null) {
      highlightWell(selectedWell.wellId, false);
    }

    selectedWell = null;
    selectedMonthly = null;
    selectionToken += 1;

    disposeChart();
    showPanelSkeleton(false);
    clearSummary();
    el('panel-error').hidden = true;
    syncWellListSelection();
    closePanel();
  }

  async function selectWell(well) {
    if (selectedWell !== null) {
      highlightWell(selectedWell.wellId, false);
    }

    selectedWell = well;
    selectedMonthly = null;
    // Подсветка не ждёт сети: выбор на карте виден сразу.
    highlightWell(well.wellId, true);
    syncWellListSelection();

    const token = ++selectionToken;

    if (isMonthlyLoaded(well.wellId)) {
      el('panel-title').textContent = `${wellCaption(well)} — ${well.field}`;
      el('panel-error').hidden = true;
      openPanel();
    } else {
      preparePanelLoading(well);
    }

    try {
      const monthly = await loadMonthly(well);
      if (token !== selectionToken) {
        return;
      }

      selectedMonthly = monthly;
      showSummary(well, monthly);
      renderWellList();
      await showChart(well, monthly);
    } catch (error) {
      if (token !== selectionToken) {
        return;
      }

      // Сводка полезна и без помесячных данных: месторождение, забой, длина ствола.
      showSummary(well, null);
      renderWellList();
      showPanelError(well, error);
    }
  }

  async function loadFieldTotalOil() {
    try {
      const summary = await fetchJson('/api/Analytics/summary');
      fieldTotalOil = summary.totalOilVol ?? null;
    } catch (error) {
      console.warn('Не удалось загрузить суммарную добычу месторождения', error);
    }
  }

  function isMonthlyLoaded(wellId) {
    return Array.isArray(monthlyByWell.get(wellId));
  }

  // Один запрос на скважину: предзагрузка списка и выбор делят его.
  function loadMonthly(well) {
    if (isMonthlyLoaded(well.wellId)) {
      return Promise.resolve(monthlyByWell.get(well.wellId));
    }

    const pending = monthlyRequests.get(well.wellId);
    if (pending !== undefined) {
      return pending;
    }

    const request = fetchJson(`/api/Analytics/monthly?wellId=${well.wellId}`)
      .then((monthly) => {
        monthlyByWell.set(well.wellId, monthly);
        monthlyRequests.delete(well.wellId);
        return monthly;
      })
      .catch((error) => {
        monthlyRequests.delete(well.wellId);
        throw error;
      });

    monthlyRequests.set(well.wellId, request);
    return request;
  }

  function wellOilText(wellId) {
    const monthly = monthlyByWell.get(wellId);
    if (!Array.isArray(monthly)) {
      return '—';
    }

    return `${formatNumber(wellStats(monthly).oil)} м³`;
  }

  // Список всех скважин в панели. Устья куста лежат в пятне ~10 м и на доступных
  // зумах физически не разлепляются — список даёт гарантированный доступ к каждой.
  function renderWellList() {
    const list = el('well-list');
    if (list === null) {
      return;
    }

    list.innerHTML = orderedWells.map((well) => {
      const wellId = Number(well.wellId);
      if (!Number.isInteger(wellId)) {
        return '';
      }

      return `<li><button type="button" class="well-row" data-well-id="${wellId}" aria-pressed="false">` +
        `<span class="well-row-name">${escapeHtml(well.name)}</span>` +
        `<span class="well-row-value">${wellOilText(wellId)}</span>` +
        `</button></li>`;
    }).join('');

    syncWellListSelection();
  }

  // Выбранная строка подсвечивается: состояние обновляем без перерисовки списка.
  function syncWellListSelection() {
    const list = el('well-list');
    if (list === null || typeof list.querySelectorAll !== 'function') {
      return;
    }

    const activeId = selectedWell === null ? null : String(selectedWell.wellId);
    const rows = list.querySelectorAll('[data-well-id]');
    for (const row of rows) {
      const isActive = row.getAttribute('data-well-id') === activeId;
      row.setAttribute('aria-pressed', isActive ? 'true' : 'false');
    }
  }

  function bindWellList() {
    const list = el('well-list');
    if (list === null || typeof list.addEventListener !== 'function') {
      return;
    }

    list.addEventListener('click', (event) => {
      const target = event.target;
      const row = target !== null && target !== undefined && typeof target.closest === 'function'
        ? target.closest('[data-well-id]')
        : null;
      if (row === null) {
        return;
      }

      const well = wellsById.get(Number(row.getAttribute('data-well-id')));
      if (well !== undefined) {
        toggleWellSelection(well);
      }
    });
  }

  // Фоновая предзагрузка: показатель в списке и доля месторождения считаются сразу.
  // Выбор скважины ждёт тот же промис, поэтому дублей запросов нет.
  async function preloadMonthly(located) {
    await loadFieldTotalOil();

    // Суммарная добыча месторождения пришла позже выбора — обновляем долю в сводке.
    if (selectedWell !== null && Array.isArray(selectedMonthly)) {
      showSummary(selectedWell, selectedMonthly);
    }

    await Promise.all(located.map((well) =>
      loadMonthly(well).catch((error) => {
        console.warn(`Помесячные данные ${wellCaption(well)} не загружены`, error);
      })));

    renderWellList();
  }

  // Скважина с максимальной суммарной нефтью среди добывающих: нулевую добычу
  // пропускаем (в списке есть, например, нагнетательная 15/9-F-4 с 0 м³),
  // при равенстве берём первую по алфавиту.
  function pickTopProducingWell(located) {
    let best = null;
    let bestOil = 0;

    for (const well of located) {
      const monthly = monthlyByWell.get(well.wellId);
      if (!Array.isArray(monthly)) {
        continue;
      }

      const oil = wellStats(monthly).oil;
      if (!(oil > 0)) {
        continue;
      }

      const better = best === null ||
        oil > bestOil ||
        (oil === bestOil && String(well.name) < String(best.name));

      if (better) {
        best = well;
        bestOil = oil;
      }
    }

    return best;
  }

  // Автовыбор после прилёта (или сразу после финального вида без полёта). Помесячные
  // данные к этому моменту обычно уже загружены фоном; если нет — ждём их, чтобы
  // панель не открылась пустой.
  function requestAutoSelect(located) {
    if (autoSelectDone) {
      return;
    }

    autoSelectDone = true;
    preloadPromise
      .then(() => {
        // Пользователь успел выбрать скважину сам — его выбор не перебиваем.
        if (selectedWell !== null) {
          return undefined;
        }

        const best = pickTopProducingWell(located);
        if (best === null) {
          return undefined;
        }

        return selectWell(best);
      })
      .catch((error) => {
        console.warn('Автовыбор скважины не выполнен', error);
      });
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

  function buildTrajectoryLayer(well, points) {
    if (points.length === 0) {
      return null;
    }

    const layer = L.layerGroup();
    const trajectory = { core: null, edge: null, bottom: null };
    // Все три объекта скважины выбирают её: устье, линия и забой.
    const select = () => toggleWellSelection(well);

    if (points.length >= 2) {
      const latlngs = points.map((point) => point.latlng);

      // Тонкая тёмно-янтарная обводка: держит линию читаемой на светлых тайлах,
      // но, в отличие от прежнего glow, не расплывается грязным пятном.
      trajectory.edge = L.polyline(latlngs, {
        pane: TRAJECTORY_PANE,
        color: TRAJECTORY_EDGE_COLOR,
        weight: TRAJECTORY_EDGE_WEIGHT,
        opacity: 1,
        interactive: false
      }).addTo(layer);

      // Тонкая основная линия — единственный интерактивный элемент линии:
      // клик по широкой обводке выбирал бы скважину случайно.
      trajectory.core = L.polyline(latlngs, {
        pane: TRAJECTORY_PANE,
        color: TRAJECTORY_COLOR,
        weight: TRAJECTORY_CORE_WEIGHT,
        opacity: TRAJECTORY_CORE_OPACITY
      }).addTo(layer);
      trajectory.core.on('click', select);
    }

    // Забой — незалитый кружок в последней точке траектории (по MD) с подписью MD.
    // Панель забоев ниже панели линий: при пересечении клик достаётся линии.
    const last = points[points.length - 1];
    trajectory.bottom = L.circleMarker(last.latlng, {
      pane: BOTTOMHOLE_PANE,
      radius: BOTTOMHOLE_RADIUS,
      color: BOTTOMHOLE_COLOR,
      weight: BOTTOMHOLE_WEIGHT,
      opacity: BOTTOMHOLE_OPACITY,
      fill: false
    }).addTo(layer);
    trajectory.bottom.bindTooltip(bottomholeLabel(last.md), {
      className: 'bottomhole-label',
      direction: 'right',
      permanent: true,
      interactive: false,
      opacity: 1,
      offset: L.point(BOTTOMHOLE_LABEL_OFFSET_X, 0)
    });
    trajectory.bottom.on('click', select);

    const entry = wellLayers.get(well.wellId) || { well, marker: null };
    entry.trajectory = trajectory;
    wellLayers.set(well.wellId, entry);

    // Скважину могли выбрать до прихода траектории — подсветка применяется сразу.
    if (selectedWell !== null && selectedWell.wellId === well.wellId) {
      styleTrajectory(entry, true);
    }

    return layer;
  }

  // Guard на одну скважину: запрос и построение слоя завёрнуты в try/catch,
  // чтобы сбой одной скважины не мешал остальным.
  async function loadTrajectoryLayer(well) {
    try {
      const points = await fetchTrajectoryPoints(well);
      return buildTrajectoryLayer(well, points);
    } catch (error) {
      console.warn(`Траектория ${wellCaption(well)} не добавлена`, error);
      return null;
    }
  }

  // Устье — янтарный круг с белой обводкой (соседние точки не сливаются в пятно).
  // Подпись — постоянный tooltip справа; показывается с зума WELL_LABEL_MIN_ZOOM.
  // Клик по устью выбирает скважину — у маркера высший приоритет клика.
  function addWellheadMarker(well, labelOffsetY) {
    try {
      const latlng = safeLatLng(well.latitude, well.longitude);
      if (latlng === null) {
        console.warn(`Устье ${wellCaption(well)} пропущено: некорректные координаты`, well.latitude, well.longitude);
        return null;
      }

      const compact = map.getZoom() >= WELLHEAD_COMPACT_MIN_ZOOM;
      const marker = L.circleMarker(latlng, {
        radius: compact ? WELLHEAD_RADIUS_COMPACT : WELLHEAD_RADIUS,
        color: WELLHEAD_STROKE_COLOR,
        weight: compact ? WELLHEAD_IDLE_WEIGHT_COMPACT : WELLHEAD_IDLE_WEIGHT,
        fillColor: WELLHEAD_FILL_COLOR,
        fillOpacity: WELLHEAD_FILL_OPACITY
      });
      marker.bindTooltip(well.name, {
        className: 'well-label',
        direction: 'right',
        permanent: true,
        interactive: false,
        opacity: 1,
        offset: L.point(WELL_LABEL_OFFSET_X, labelOffsetY)
      });
      marker.on('click', () => toggleWellSelection(well));
      marker.addTo(map);

      return marker;
    } catch (error) {
      console.warn('Устье не добавлено', well, error);
      return null;
    }
  }

  // Зум меняет и подписи, и размер устьев: с 14 маркеры уменьшаются, иначе плотный
  // куст сливается в одно пятно. Подписи устьев — с WELL_LABEL_MIN_ZOOM.
  function syncZoomLayers() {
    const showWellLabels = map.getZoom() >= WELL_LABEL_MIN_ZOOM;

    wellLayers.forEach((entry) => {
      const selected = selectedWell !== null && selectedWell.wellId === entry.well.wellId;
      styleWellhead(entry.marker, selected);

      if (!entry.marker) {
        return;
      }

      if (showWellLabels) {
        entry.marker.openTooltip();
      } else {
        entry.marker.closeTooltip();
      }
    });

    syncBottomholeLabels();
  }

  // Подписи забоев («забой, NNNN м») — с зума BOTTOMHOLE_LABEL_MIN_ZOOM.
  function syncBottomholeLabels() {
    const show = map.getZoom() >= BOTTOMHOLE_LABEL_MIN_ZOOM;

    wellLayers.forEach((entry) => {
      const bottom = entry.trajectory ? entry.trajectory.bottom : null;
      if (!bottom) {
        return;
      }

      if (show) {
        bottom.openTooltip();
      } else {
        bottom.closeTooltip();
      }
    });
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
        // Подписи забоев открываются только у слоёв, уже стоящих на карте.
        syncBottomholeLabels();
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
      try {
        applyFinalView(located);
      } catch (viewError) {
        console.warn('Финальный вид карты не применён', viewError);
      }
      revealMapData();
      drawTrajectoriesCascade(list);
      requestAutoSelect(located);
    };

    try {
      tilesReady.then(() => {
        try {
          flyToCluster(located);

          // onArrived может прийти дважды (moveend и страховочный таймер) — обрабатываем один раз.
          let arrived = false;
          const onArrived = () => {
            if (arrived) {
              return;
            }
            arrived = true;
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
            // Прибыли: только теперь проявляем данные (fade-in ~400 мс из style.css),
            // а затем автоматически выбираем самую добывающую скважину.
            revealMapData();
            drawTrajectoriesCascade(list);
            requestAutoSelect(located);
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

  // Пока на контейнере карты висит класс data-pending, панели слоёв прозрачны
  // (см. style.css): с высоты космоса видна только карта, а не пятно из слитых
  // маркеров. Класс ставится до добавления слоёв.
  function hideMapData() {
    dataPending = true;
    const container = el('map');
    if (container !== null && container.classList) {
      container.classList.add(MAP_DATA_PENDING_CLASS);
    }
  }

  // Снятие класса запускает CSS-переход opacity 400 мс — плавное проявление данных.
  // Вызывается после прилёта, а при prefers-reduced-motion — сразу после финального вида.
  function revealMapData() {
    if (!dataPending) {
      return;
    }

    dataPending = false;
    const container = el('map');
    if (container !== null && container.classList) {
      container.classList.remove(MAP_DATA_PENDING_CLASS);
    }

    // Подписи забоев открываем только у слоёв, которые уже на карте.
    syncBottomholeLabels();
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
    el('panel-title').textContent = 'Ошибка загрузки';
    el('panel-error').textContent =
      'Не удалось загрузить скважины: ' + (error && error.message ? error.message : error);
    el('panel-error').hidden = false;
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
    // Данные скрыты до конца вводного полёта — ставим до добавления первого слоя.
    hideMapData();

    map.createPane(TRAJECTORY_PANE);
    const pane = map.getPane(TRAJECTORY_PANE);
    if (pane) {
      pane.style.zIndex = TRAJECTORY_PANE_Z_INDEX;
    }

    // Панель забоев ниже траекторий: при пересечении клик достаётся линии.
    map.createPane(BOTTOMHOLE_PANE);
    const bottomPane = map.getPane(BOTTOMHOLE_PANE);
    if (bottomPane) {
      bottomPane.style.zIndex = BOTTOMHOLE_PANE_Z_INDEX;
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
    // Подписи идут колонкой вниз от куста: устья Volve лежат в пятне ~10 м (2–4 px
    // на зуме 14), поэтому доступ к каждой скважине даёт список в панели.
    list.forEach((well, index) => {
      const marker = addWellheadMarker(well, index * WELL_LABEL_STEP_Y);
      if (marker === null) {
        return;
      }

      wellLayers.set(well.wellId, { well, marker, trajectory: null });
      wellsById.set(well.wellId, well);
      located.push(well);
    });

    // Список скважин: пункт выбирает скважину тем же механизмом, что клик по карте.
    // Только скважины с валидными координатами: без устья на карте выбирать нечего.
    orderedWells = located;
    renderWellList();
    bindWellList();

    if (loadError !== null || list.length === 0) {
      // Ошибка загрузки — без анимации: сразу финальный вид и сообщение об ошибке.
      applyFinalView(located);
      revealMapData();
      showLoadError(loadError);
      return;
    }

    // Фоновая предзагрузка помесячных данных: её ждут список, сводка и автовыбор.
    // Стартует до ветвления по анимации — иначе автовыбор после прилёта мог бы
    // сработать раньше самих данных и оставить панель пустой.
    preloadPromise = preloadMonthly(located).catch((error) => {
      console.warn('Помесячные данные не загружены', error);
    });

    // Подписи и размер маркеров зависят от зума; обработчик вешаем до первого перелёта.
    map.on('zoomend', syncZoomLayers);
    syncZoomLayers();

    if (prefersReducedMotion() || sessionStorage.getItem(INTRO_SESSION_KEY)) {
      // Повторный визит в сессии или просьба «меньше движения»: сразу финальный вид,
      // данные проявляются тем же fade-in, но без полёта; автовыбор — сразу после этого.
      applyFinalView(located);
      revealMapData();
      drawTrajectoriesCascade(list);
      requestAutoSelect(located);
    } else {
      sessionStorage.setItem(INTRO_SESSION_KEY, '1');
      startIntro(tilesReady, located, list);
    }
  }

  // Кнопка закрытия панели: без неё страница работает, поэтому отсутствие элемента
  // в разметке не должно валить скрипт до инициализации карты. Крестик снимает выбор.
  const panelClose = document.getElementById('panel-close');
  if (panelClose !== null) {
    panelClose.addEventListener('click', clearSelection);
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

