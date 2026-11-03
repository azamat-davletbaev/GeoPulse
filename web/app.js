'use strict';

// Все данные, включая координаты, приходят только из API — фронт их не содержит.

let map;
let chart;
// Суммарная добыча нефти по месторождению (/api/Analytics/summary) — нужна для доли
// скважины в попапе. Загружается один раз при старте; null — данные ещё не пришли.
let fieldTotalOil = null;

async function fetchJson(path) {
  const response = await fetch(path, { headers: { Accept: 'application/json' } });
  if (!response.ok) {
    throw new Error(`HTTP ${response.status} (${path})`);
  }
  return response.json();
}

function escapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function formatNumber(value) {
  return Number(value ?? 0).toLocaleString('ru-RU', { maximumFractionDigits: 0 });
}

// Пользовательские подписи скважины всегда начинаются со слова «Скважина»:
// и в попапе, и в заголовке панели с графиком.
function wellCaption(well) {
  return `Скважина ${well.name}`;
}

function monthLabel(monthly) {
  return `${monthly.year}-${String(monthly.month).padStart(2, '0')}`;
}

// Сравнимый ключ месяца: не полагаемся на порядок в ответе API.
function monthOrdinal(monthly) {
  return monthly.year * 12 + monthly.month;
}

// Сводные показатели скважины считаются на фронте из уже загруженных monthly —
// так же, как суммарная нефть. Отдельных эндпоинтов для них нет.
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

    // «Месяцы с добычей» — только те, где добыча нефти ненулевая; нулевые хвосты
    // (например, у нагнетательной 15/9-F-4) в период работы и обводнённость не попадают.
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

// Ссылка на персональный отчёт. wellId — числовой идентификатор из API, экранирование
// не требуется, но формат проверяем: при нечисловом id строка в попап не выводится.
function reportUrl(well) {
  const wellId = Number(well.wellId);
  return Number.isInteger(wellId) && wellId > 0 ? `/api/Reports/wells/${wellId}.xlsx` : null;
}

// monthly === null — базовая версия попапа (данные ещё не загружены),
// массив — полная версия со всеми расчётными строками.
function popupHtml(well, monthly) {
  const stats = Array.isArray(monthly) ? wellStats(monthly) : null;

  let html =
    `<strong>${escapeHtml(wellCaption(well))}</strong><br>` +
    `Месторождение: ${escapeHtml(well.field)}<br>`;

  // Забой (TVD) и длина ствола (MD последней точки) приходят из /api/wells
  // (поля tdTvd / tdMd), если траектория загружена в БД.
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
}

function showChart(well, monthly) {
  document.getElementById('panel-title').textContent = `${wellCaption(well)} — ${well.field}`;
  document.getElementById('panel-error').hidden = true;
  openPanel();

  if (chart) {
    chart.destroy();
    chart = null;
  }

  const labels = monthly.map(monthLabel);
  const oil = monthly.map((m) => m.oilVolSum ?? 0);
  const water = monthly.map((m) => m.waterVolSum ?? 0);

  const ctx = document.getElementById('chart').getContext('2d');
  chart = new Chart(ctx, {
    type: 'line',
    data: {
      labels,
      datasets: [
        {
          label: 'Нефть, м³',
          data: oil,
          borderColor: '#8b4513',
          backgroundColor: 'rgba(139, 69, 19, 0.10)',
          fill: true,
          tension: 0.25,
          pointRadius: 0
        },
        {
          label: 'Вода, м³',
          data: water,
          borderColor: '#3b82c4',
          backgroundColor: 'rgba(59, 130, 196, 0.10)',
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
      plugins: {
        legend: { position: 'bottom' }
      },
      scales: {
        x: {
          title: { display: true, text: 'Месяц' },
          ticks: { maxTicksLimit: 12 }
        },
        y: {
          title: { display: true, text: 'м³' },
          beginAtZero: true
        }
      }
    }
  });
}

function showPanelError(well, error) {
  document.getElementById('panel-title').textContent = wellCaption(well);
  document.getElementById('panel-error').textContent =
    'Не удалось загрузить добычу: ' + (error && error.message ? error.message : error);
  document.getElementById('panel-error').hidden = false;
  openPanel();
}

// Открывает попап маркера и панель с графиком. Если помесячные данные уже загружены
// (автовыбор при старте), они передаются аргументом — повторного запроса не будет.
async function selectWell(well, marker, monthly) {
  // Маркер мог не создаться (битые координаты) — панель всё равно открывается:
  // график и попап не зависят от того, нарисовано ли устье на карте.
  if (marker) {
    marker.setPopupContent(popupHtml(well, null));
    marker.openPopup();
  }

  try {
    const data = monthly ?? (await fetchJson(`/api/Analytics/monthly?wellId=${well.wellId}`));
    if (marker) {
      marker.setPopupContent(popupHtml(well, data));
    }
    showChart(well, data);
  } catch (error) {
    showPanelError(well, error);
  }
}

function onMarkerClick(well, marker) {
  return selectWell(well, marker);
}

// Скважина считается добывающей, если набор не пуст и есть хоть один месяц с нефтью.
// Нагнетательные (15/9-F-4, 15/9-F-5) отдают нули по нефти и воде.
function hasOil(monthly) {
  return Array.isArray(monthly) && monthly.some((month) => (month.oilVolSum ?? 0) > 0);
}

// Первая скважина в порядке ответа /api/wells с координатами и ненулевой добычей.
// Ошибка сети по конкретной скважине не ломает автовыбор: она пропускается,
// а карта и остальные маркеры остаются рабочими (как и при ошибке клика).
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

// Суммарная нефть месторождения нужна для доли в попапе. Берём её из существующего
// /api/Analytics/summary — новых эндпоинтов не добавляем.
async function loadFieldTotalOil() {
  try {
    const summary = await fetchJson('/api/Analytics/summary');
    fieldTotalOil = summary.totalOilVol ?? null;
  } catch (error) {
    console.warn('Не удалось загрузить суммарную добычу месторождения', error);
  }
}

// Автовыбор при открытии страницы: панель первой добывающей скважины открыта сразу.
async function autoSelectWell(located, markers) {
  await loadFieldTotalOil();

  const found = await findProducingWell(located);
  if (found === null) {
    return;
  }

  await selectWell(found.well, markers.get(found.well.wellId), found.monthly);
}

// Первичный вид карты строится fitBounds по границам всех валидных точек — устьев
// и траекторий, — а не по среднему центру: траектории Volve уходят на километры от
// устьев, и фиксированный зум показывал только кластер устьев ~9 × 7 м, то есть
// валидные линии оказывались за кадром.
// MAX_INITIAL_ZOOM ограничивает fitBounds сверху: без ограничителя вырожденные
// границы (все точки в одном месте) уводят карту на максимальный зум тайлов.
// На широте ~58.44° зум 16 — это ~1.25 м/пиксель: ровно тот вид кластера устьев,
// что был раньше, когда траекторий в базе не было.
const MAX_INITIAL_ZOOM = 16;
const VIEW_PADDING = [28, 28];
const FALLBACK_CENTER = [0, 0];
const FALLBACK_ZOOM = 2;

// Приведение координаты к числу. Number(null) и Number('') дают 0, поэтому «пустые»
// значения отсекаются ДО приведения: иначе отсутствующая координата превратилась бы
// в точку (0, 0) — Null Island — и слой выглядел бы валидным.
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

// Единственная точка проверки координат. L.latLng(NaN, …) бросает
// "Invalid LatLng object" и уносит с собой весь слой (и весь цикл маркеров),
// поэтому в Leaflet попадают только конечные числа в диапазонах WGS84.
// null — координата непригодна, точку/скважину не рисуем.
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

// Первичный вид по накопленным границам. Пустые границы (ни одной валидной точки) —
// нейтральный вид, карта остаётся рабочей. Ошибка fitBounds тоже не критична.
function applyInitialView(viewBounds) {
  if (!viewBounds.isValid()) {
    map.setView(FALLBACK_CENTER, FALLBACK_ZOOM);
    return;
  }

  try {
    map.fitBounds(viewBounds, { padding: VIEW_PADDING, maxZoom: MAX_INITIAL_ZOOM });
  } catch (error) {
    console.warn('Первичный вид по границам не применён', error);
    map.setView(viewBounds.getCenter(), MAX_INITIAL_ZOOM);
  }
}

// Замеры приходят из /api/Wells/{wellId}/trajectory уже в WGS84.
// Возвращает только валидные точки { md, latlng }, отсортированные по MD:
// контракт порядок гарантирует, но линию и забой строим независимо от порядка ответа.
async function fetchTrajectoryPoints(well) {
  const points = await fetchJson(`/api/Wells/${well.wellId}/trajectory`);
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

// Слой одной скважины: тёмно-серая линия траектории и незалитый чёрный кружок забоя
// (последняя точка по MD). Линия строится только при двух и более точках: одна точка —
// это не геометрия, для неё достаточно маркера забоя. null — рисовать нечего.
function buildTrajectoryLayer(points) {
  if (points.length === 0) {
    return null;
  }

  const layer = L.layerGroup();

  // Траектория — тёмно-серая линия, без яркой заливки: не должна перекрикивать маркеры.
  if (points.length >= 2) {
    L.polyline(
      points.map((point) => point.latlng),
      { color: '#4a4a4a', weight: 2, opacity: 0.9 }
    ).addTo(layer);
  }

  // Забой — незалитый чёрный кружок в последней точке траектории (по MD).
  L.circleMarker(points[points.length - 1].latlng, {
    radius: 4,
    color: '#000000',
    weight: 2,
    opacity: 1,
    fill: false,
    interactive: false
  }).addTo(layer);

  return layer;
}

// Guard на одну скважину: запрос, разбор ответа, построение слоя и его добавление
// завёрнуты в один try/catch. Падение одной скважины логируется и не мешает
// остальным траекториям, маркерам устьев и самой карте.
async function addTrajectoryLayer(well, viewBounds) {
  try {
    const points = await fetchTrajectoryPoints(well);
    const layer = buildTrajectoryLayer(points);
    if (layer === null) {
      return;
    }

    // Границы первичного вида учитывают и траекторию — иначе линии уходят за кадр.
    for (const point of points) {
      viewBounds.extend(point.latlng);
    }

    layer.addTo(map);
  } catch (error) {
    console.warn(`Траектория ${wellCaption(well)} не добавлена`, error);
  }
}

// Устье — залитый чёрный кружок с тонкой белой обводкой (вместо стандартного пина Leaflet).
// Guard тоже по одной скважине: битая координата или ошибка Leaflet не ломают остальные
// маркеры и не обрывают инициализацию карты.
function addWellheadMarker(well, viewBounds) {
  try {
    const latlng = safeLatLng(well.latitude, well.longitude);
    if (latlng === null) {
      console.warn(`Устье ${wellCaption(well)} пропущено: некорректные координаты`, well.latitude, well.longitude);
      return null;
    }

    const marker = L.circleMarker(latlng, {
      radius: 5,
      color: '#ffffff',
      weight: 1.5,
      fillColor: '#000000',
      fillOpacity: 1
    });
    marker.bindPopup(popupHtml(well, null));
    marker.on('click', () => onMarkerClick(well, marker));
    marker.addTo(map);
    viewBounds.extend(latlng);

    return marker;
  } catch (error) {
    console.warn('Устье не добавлено', well, error);
    return null;
  }
}

async function init() {
  map = L.map('map');

  // Вид выставляется сразу после создания карты: карта «loaded» ещё до загрузки данных,
  // поэтому даже полный отказ API оставляет рабочие тайлы и панорамирование.
  map.setView(FALLBACK_CENTER, FALLBACK_ZOOM);

  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
  }).addTo(map);

  // Границы первичного вида накапливаются по устьям и траекториям по мере добавления слоёв.
  const viewBounds = L.latLngBounds([]);
  const markers = new Map();
  const located = [];

  let wells = [];
  try {
    wells = await fetchJson('/api/wells');
  } catch (error) {
    // Пустой список — карта остаётся на нейтральном виде, без маркеров и траекторий.
    console.error('Список скважин не загружен', error);
  }

  const list = Array.isArray(wells) ? wells : [];

  // Сначала траектории (линии + забои), затем устья поверх них.
  // allSettled: даже неожиданное исключение внутри одной скважины не отменяет остальные.
  const settled = await Promise.allSettled(list.map((well) => addTrajectoryLayer(well, viewBounds)));
  for (const result of settled) {
    if (result.status === 'rejected') {
      console.warn('Слой траектории пропущен', result.reason);
    }
  }

  for (const well of list) {
    const marker = addWellheadMarker(well, viewBounds);
    if (marker !== null) {
      markers.set(well.wellId, marker);
      located.push(well);
    }
  }

  applyInitialView(viewBounds);

  // Автовыбор скважины на карту не влияет: ошибки сети и панели/графика гасятся внутри,
  // но и здесь оставляем guard, чтобы сбой панели не считался сбоем инициализации карты.
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

// Последний рубеж: init уже изолирует каждый слой и каждую скважину, но если упадёт
// что-то за пределами guard'ов, карта останется с нейтральным видом и рабочими тайлами.
init().catch((error) => {
  console.error('Инициализация карты не завершена', error);
});
