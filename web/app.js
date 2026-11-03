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
          borderColor: '#2f6fed',
          backgroundColor: 'rgba(47, 111, 237, 0.10)',
          fill: true,
          tension: 0.25,
          pointRadius: 0
        },
        {
          label: 'Вода, м³',
          data: water,
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
  marker.setPopupContent(popupHtml(well, null));
  marker.openPopup();

  try {
    const data = monthly ?? (await fetchJson(`/api/Analytics/monthly?wellId=${well.wellId}`));
    marker.setPopupContent(popupHtml(well, data));
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

// Первичный вид карты задаётся фиксированным зумом, а не fitBounds:
// все 7 скважин Volve стоят в прямоугольнике ~9 × 7 м (замер по /api/wells),
// поэтому fitBounds вырождался в «случайный» максимальный зум.
// На широте ~58.44° зум 16 даёт ~1.25 м/пиксель: кластер занимает ~7 × 6 px,
// то есть весь набор устьев гарантированно в кадре на любом экране, а расстояние
// между маркерами вдвое больше, чем на зуме 15, — их проще различить и выбрать.
const INITIAL_ZOOM = 16;

// Центр первичного вида — среднее арифметическое координат скважин из /api/wells.
function averageCenter(wells) {
  const lat = wells.reduce((sum, well) => sum + well.latitude, 0) / wells.length;
  const lon = wells.reduce((sum, well) => sum + well.longitude, 0) / wells.length;
  return [lat, lon];
}

// Слой линий траекторий: замеры приходят из /api/Wells/{wellId}/trajectory уже в WGS84.
async function loadTrajectory(well) {
  try {
    const points = await fetchJson(`/api/Wells/${well.wellId}/trajectory`);
    if (!Array.isArray(points) || points.length === 0) {
      return null;
    }

    return L.polyline(
      points.map((point) => [point.latitude, point.longitude]),
      { color: '#2f6fed', weight: 2, opacity: 0.8 }
    );
  } catch (error) {
    console.warn(`Траектория ${wellCaption(well)} не загружена`, error);
    return null;
  }
}

async function init() {
  map = L.map('map');

  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
  }).addTo(map);

  const wells = await fetchJson('/api/wells');
  const located = wells.filter((w) => w.latitude !== null && w.longitude !== null);
  const markers = new Map();

  for (const well of located) {
    const marker = L.marker([well.latitude, well.longitude]);
    marker.bindPopup(popupHtml(well, null));
    marker.on('click', () => onMarkerClick(well, marker));
    marker.addTo(map);
    markers.set(well.wellId, marker);
  }

  // Слой траекторий: линии замеров (WGS84) поверх маркеров устьев.
  await Promise.all(located.map(async (well) => {
    const line = await loadTrajectory(well);
    if (line !== null) {
      line.addTo(map);
    }
  }));

  if (located.length > 0) {
    // Фиксированный первичный вид: центр — средние координаты скважин, зум — константа.
    map.setView(averageCenter(located), INITIAL_ZOOM);
  } else {
    map.setView([0, 0], 2);
  }

  await autoSelectWell(located, markers);
}

document.getElementById('panel-close').addEventListener('click', closePanel);

init().catch((error) => {
  console.error(error);
});
