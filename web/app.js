'use strict';

// Все данные, включая координаты, приходят только из API — фронт их не содержит.

let map;
let chart;

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

function popupHtml(well, totalOil) {
  const oil = totalOil === null || totalOil === undefined
    ? '—'
    : formatNumber(totalOil) + ' м³';
  return (
    `<strong>${escapeHtml(wellCaption(well))}</strong><br>` +
    `Месторождение: ${escapeHtml(well.field)}<br>` +
    `Добыча нефти (суммарно): ${oil}`
  );
}

function monthLabel(monthly) {
  return `${monthly.year}-${String(monthly.month).padStart(2, '0')}`;
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

async function onMarkerClick(well, marker) {
  marker.setPopupContent(popupHtml(well, null));
  marker.openPopup();

  try {
    const monthly = await fetchJson(`/api/Analytics/monthly?wellId=${well.wellId}`);
    const totalOil = monthly.reduce((sum, m) => sum + (m.oilVolSum ?? 0), 0);
    marker.setPopupContent(popupHtml(well, totalOil));
    showChart(well, monthly);
  } catch (error) {
    showPanelError(well, error);
  }
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

async function init() {
  map = L.map('map');

  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
  }).addTo(map);

  const wells = await fetchJson('/api/wells');
  const located = wells.filter((w) => w.latitude !== null && w.longitude !== null);

  for (const well of located) {
    const marker = L.marker([well.latitude, well.longitude]);
    marker.bindPopup(popupHtml(well, null));
    marker.on('click', () => onMarkerClick(well, marker));
    marker.addTo(map);
  }

  if (located.length > 0) {
    // Фиксированный первичный вид: центр — средние координаты скважин, зум — константа.
    map.setView(averageCenter(located), INITIAL_ZOOM);
  } else {
    map.setView([0, 0], 2);
  }
}

document.getElementById('panel-close').addEventListener('click', closePanel);

init().catch((error) => {
  console.error(error);
});
