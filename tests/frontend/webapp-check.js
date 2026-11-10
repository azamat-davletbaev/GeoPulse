'use strict';

// Проверка фронтенда web/app.js без браузера: Leaflet, DOM, fetch и sessionStorage
// заменены минимальными заглушками, повторяющими существенное поведение Leaflet
// (L.latLng бросает "Invalid LatLng object" на NaN/undefined).
// Перед app.js загружается web/common.js — он поставляет window.GeoPulse.
//
// Сценарии (аргумент командной строки):
//   mixed — валидные и битые данные вперемешку: карта жива, слои строятся по валидным,
//           каждая сломанная скважина изолирована своим guard'ом;
//   empty — валидных координат нет вовсе: карта остаётся на нейтральном виде;
//   select — выбор скважины кликом по устью, линии и забою: панель со сводкой и
//           списком, бирюзовая подсветка, повторный клик снимает выбор, попапа нет;
//   autoselect — после прилёта панель открывается сама на скважине с максимальной
//           суммарной нефтью (график, сводка, бирюзовая подсветка).
//
// Запуск: node tests/frontend/webapp-check.js [mixed|empty|select|autoselect]

const fs = require('fs');
const path = require('path');
const vm = require('vm');

const scenario = process.argv[2] || process.env.SCENARIO || 'mixed';
const repoRoot = path.resolve(__dirname, '..', '..');
const commonJsPath = path.join(repoRoot, 'web', 'common.js');
const appJsPath = path.join(repoRoot, 'web', 'app.js');

const warnings = [];
const sandboxConsole = {
  warn: (...args) => warnings.push(args.map(String).join(' ')),
  error: (...args) => warnings.push('ERROR: ' + args.map(String).join(' ')),
  log: (...args) => process.stdout.write(args.map(String).join(' ') + '\n')
};

class LatLng {
  constructor(lat, lng) {
    if (!Number.isFinite(lat) || !Number.isFinite(lng)) {
      throw new Error('Invalid LatLng object: (' + lat + ', ' + lng + ')');
    }
    this.lat = lat;
    this.lng = lng;
  }
}

function latLngBounds(initial) {
  const bounds = {
    sw: null,
    ne: null,
    extend(value) {
      const ll = Array.isArray(value) ? new LatLng(Number(value[0]), Number(value[1])) : value;
      if (!ll) {
        return bounds;
      }
      if (bounds.sw === null) {
        bounds.sw = { lat: ll.lat, lng: ll.lng };
        bounds.ne = { lat: ll.lat, lng: ll.lng };
        return bounds;
      }
      bounds.sw.lat = Math.min(bounds.sw.lat, ll.lat);
      bounds.sw.lng = Math.min(bounds.sw.lng, ll.lng);
      bounds.ne.lat = Math.max(bounds.ne.lat, ll.lat);
      bounds.ne.lng = Math.max(bounds.ne.lng, ll.lng);
      return bounds;
    },
    isValid() {
      return bounds.sw !== null && bounds.ne !== null;
    },
    getCenter() {
      return [(bounds.sw.lat + bounds.ne.lat) / 2, (bounds.sw.lng + bounds.ne.lng) / 2];
    }
  };

  if (Array.isArray(initial)) {
    for (const item of initial) {
      bounds.extend(item);
    }
  }

  return bounds;
}

const mapCalls = { setView: [], flyTo: [], added: [] };
let currentZoom = 4;
const map = {
  setView(center, zoom) {
    mapCalls.setView.push({ center: new LatLng(Number(center[0]), Number(center[1])), zoom });
    currentZoom = zoom;
    return map;
  },
  flyTo(center, zoom, options) {
    mapCalls.flyTo.push({ center: new LatLng(Number(center[0]), Number(center[1])), zoom, options });
    // Полёт в заглушке долетает сразу: endZoom = targetZoom.
    currentZoom = zoom;
    return map;
  },
  once(event, cb) {
    // Имитируем завершение перелёта сразу, чтобы каскад траекторий стартовал.
    if (event === 'moveend') {
      cb();
    }
    return map;
  },
  on() {
    return map;
  },
  getZoom() {
    return currentZoom;
  },
  removeLayer() {
    return map;
  },
  createPane() {
    return {};
  },
  getPane() {
    return { style: {} };
  },
  addLayer(layer) {
    mapCalls.added.push(layer);
    return map;
  }
};

const built = { polylines: [], circleMarkers: [], groups: [] };
// Попап с карты убран целиком (сводка живёт в правой панели) — счётчик должен
// остаться нулевым: стенд ловит возврат bindPopup на слой.
const popupBinds = { count: 0 };

function makePath(kind, latlngs, options) {
  const handlers = new Map();
  const layer = {
    kind,
    latlngs,
    options,
    tooltip: null,
    styles: [],
    bindPopup: () => {
      popupBinds.count += 1;
      return layer;
    },
    bindTooltip(content, tooltipOptions) {
      layer.tooltip = { content, options: tooltipOptions };
      return layer;
    },
    openTooltip: () => layer,
    closeTooltip: () => layer,
    on(event, callback) {
      handlers.set(event, callback);
      return layer;
    },
    // Стенд «нажимает» на слой так же, как Leaflet: вызывает обработчик click.
    fire(event) {
      const callback = handlers.get(event);
      if (typeof callback === 'function') {
        callback({ type: event, target: layer });
      }
      return layer;
    },
    setPopupContent: () => layer,
    openPopup: () => layer,
    setStyle(style) {
      layer.styles.push(style);
      return layer;
    },
    addTo(target) {
      if (target === map) {
        mapCalls.added.push(layer);
      } else {
        target.children.push(layer);
      }
      return layer;
    }
  };

  if (kind === 'polyline') {
    for (const pair of latlngs) {
      new LatLng(Number(pair[0]), Number(pair[1]));
    }
    built.polylines.push(layer);
  } else {
    new LatLng(Number(latlngs[0]), Number(latlngs[1]));
    built.circleMarkers.push(layer);
  }

  return layer;
}

const L = {
  map: () => map,
  // Базовый слой: 'load' считаем уже случившимся, иначе вводный перелёт
  // стартовал бы только по fallback-таймауту (1.5 с) — дольше окна проверки.
  tileLayer() {
    const layer = {
      once(event, cb) {
        if (event === 'load') {
          cb();
        }
        return layer;
      },
      addTo: () => layer
    };
    return layer;
  },
  point: (x, y) => ({ x, y }),
  Icon: { Default: {} },
  layerGroup() {
    const group = {
      children: [],
      addTo(target) {
        built.groups.push(group);
        target.addLayer(group);
        return group;
      }
    };
    return group;
  },
  polyline: (latlngs, options) => makePath('polyline', latlngs, options),
  circleMarker: (latlng, options) => makePath('circleMarker', latlng, options),
  latLngBounds
};
// Данные стенда: в сценарии mixed битые и валидные скважины идут вперемешку.
// Код НПД в стенде совпадает с wellId; API адресует траектории и отчёты кодом НПД.
const wells = [
  { wellId: 1, npdWellBoreCode: 1, name: 'ok-full', field: 'Volve', latitude: 58.4412, longitude: 1.8541, tdMd: 3200, tdTvd: 2500 },
  { wellId: 2, npdWellBoreCode: 2, name: 'bad-coords', field: 'Volve', latitude: 'oops', longitude: null, tdMd: null, tdTvd: null },
  { wellId: 3, npdWellBoreCode: 3, name: 'http-500', field: 'Volve', latitude: 58.4413, longitude: 1.8542, tdMd: null, tdTvd: null },
  { wellId: 4, npdWellBoreCode: 4, name: 'single-point', field: 'Volve', latitude: 58.4414, longitude: 1.8543, tdMd: 100, tdTvd: 90 }
];

const trajectories = {
  1: [
    { md: 3000, latitude: 58.46, longitude: 1.89 },
    { md: 0, latitude: 58.4412, longitude: 1.8541 },
    { md: 1500, latitude: 58.45, longitude: 1.87 }
  ],
  3: 'HTTP500',
  4: [
    { md: 0, latitude: 58.4414, longitude: 1.8543 },
    { md: 100, latitude: null, longitude: null }
  ]
};

// Помесячные данные для сценария autoselect: у скважины 1 (ok-full) максимальная
// суммарная нефть, у скважины 4 — небольшая, у скважины 3 добычи нет вовсе.
// Остальные сценарии получают пустой список, чтобы автовыбор не мешал их проверкам.
const monthlyStub = {
  1: [
    { year: 2008, month: 1, oilVolSum: 150000, waterVolSum: 1000 },
    { year: 2008, month: 2, oilVolSum: 90000, waterVolSum: 2000 }
  ],
  3: [],
  4: [{ year: 2009, month: 5, oilVolSum: 25, waterVolSum: 0 }]
};

if (scenario === 'empty') {
  wells.length = 0;
  wells.push(
    { wellId: 9, npdWellBoreCode: 9, name: 'no-coords', field: 'Volve' },
    { wellId: 10, npdWellBoreCode: 10, name: 'null-coords', field: 'Volve', latitude: null, longitude: null }
  );
  trajectories[9] = [];
  trajectories[10] = [{ md: 0, latitude: null, longitude: null }];
}

const expected = scenario === 'empty'
  ? { setView: 2, flyTo: 0, polyline: 0, groups: 0, markers: 0 }
  : { setView: 1, flyTo: 1, polyline: 2, groups: 2, markers: 3 };

function response(data, ok) {
  return { ok: ok !== false, status: ok === false ? 500 : 200, json: async () => data };
}

globalThis.fetch = async (url) => {
  if (url === '/api/wells') {
    return response(wells);
  }

  const match = /^\/api\/wells\/(\d+)\/trajectory$/.exec(url);
  if (match) {
    // В стенде код НПД совпадает с wellId, поэтому ключи trajectories не меняются.
    const payload = trajectories[Number(match[1])];
    return payload === 'HTTP500' ? response({}, false) : response(payload ?? []);
  }

  if (url.startsWith('/api/Analytics/monthly')) {
    if (scenario !== 'autoselect') {
      return response([]);
    }

    const wellId = Number((/wellId=(\d+)/.exec(url) || [])[1]);
    return response(monthlyStub[wellId] ?? []);
  }

  if (url === '/api/Analytics/summary') {
    return response({ totalOilVol: 0 });
  }

  return response({}, false);
};

globalThis.L = L;
globalThis.console = sandboxConsole;
globalThis.window = globalThis;
// app.js подписывается на resize окна — в Node этого API нет, ставим пустую заглушку.
globalThis.addEventListener = () => {};
globalThis.sessionStorage = { getItem: () => null, setItem: () => {} };
// Счётчик инициализаций графика: автовыбор обязан отрисовать график, а сценарий
// без данных — не должен.
const chartInits = { count: 0 };
globalThis.echarts = {
  init: () => {
    chartInits.count += 1;
    return { setOption() {}, dispose() {} };
  }
};
globalThis.document = (() => {
  // Состояние узлов живёт между обращениями: панель, сводка и список проверяются
  // как настоящий DOM, а не как одноразовые заглушки.
  const nodes = new Map();

  function createNode(id) {
    const node = {
      id,
      attrs: {},
      classes: new Set(),
      hidden: true,
      textContent: '',
      innerHTML: '',
      style: {},
      addEventListener() {},
      querySelectorAll: () => [],
      setAttribute(name, value) {
        node.attrs[name] = String(value);
      },
      getAttribute(name) {
        return Object.prototype.hasOwnProperty.call(node.attrs, name) ? node.attrs[name] : null;
      }
    };

    node.classList = {
      add: (name) => node.classes.add(name),
      remove: (name) => node.classes.delete(name),
      contains: (name) => node.classes.has(name),
      toggle: () => {}
    };

    return node;
  }

  return {
    // Кнопки закрытия в разметке нет в заглушке: app.js должен это переживать.
    getElementById(id) {
      if (id === 'panel-close') {
        return null;
      }
      if (!nodes.has(id)) {
        nodes.set(id, createNode(id));
      }
      return nodes.get(id);
    }
  };
})();

// Сначала общий модуль, затем карта.
vm.runInThisContext(fs.readFileSync(commonJsPath, 'utf8'), { filename: 'common.js' });
vm.runInThisContext(fs.readFileSync(appJsPath, 'utf8'), { filename: 'app.js' });

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// Сценарий select: выбор скважины кликом по трём объектам карты (устье, линия,
// забой), сводка и список в правой панели, повторный клик снимает выбор.
async function checkSelection(problems) {
  const panel = document.getElementById('panel');
  const title = document.getElementById('panel-title');
  const summary = document.getElementById('well-summary');
  const list = document.getElementById('well-list');

  const lastStyle = (layer) => layer.styles[layer.styles.length - 1] || {};

  // 1. Листа-попапа Leaflet больше нет: bindPopup не вызывается ни разу.
  if (popupBinds.count !== 0) {
    problems.push('вернулся попап Leaflet: bindPopup вызван ' + popupBinds.count + ' раз');
  }

  // 2. Панель скрыта до первого выбора; список скважин уже построен.
  if (panel.classList.contains('open')) {
    problems.push('панель открыта до выбора скважины');
  }

  const rows = (list.innerHTML.match(/data-well-id=/g) || []).length;
  if (rows !== expected.markers) {
    problems.push('в списке скважин ' + rows + ' строк, ожидалось ' + expected.markers);
  }

  // 3. Клик по устью выбирает скважину: панель, сводка, отчёт, бирюзовая подсветка.
  const wellhead = built.circleMarkers[0];
  wellhead.fire('click');
  await sleep(50);

  if (!panel.classList.contains('open')) {
    problems.push('клик по устью не открыл панель');
  }
  if (title.textContent.indexOf('ok-full') === -1) {
    problems.push('в заголовке панели не та скважина: ' + JSON.stringify(title.textContent));
  }
  if (summary.hidden || summary.innerHTML.indexOf('Добыча нефти (суммарно)') === -1) {
    problems.push('карточка сводки не заполнена');
  }
  if (summary.innerHTML.indexOf('/api/Reports/wells/1.xlsx') === -1) {
    problems.push('в сводке нет ссылки «Скачать отчёт (Excel)»');
  }
  if (lastStyle(wellhead).fillColor !== '#2dd4bf') {
    problems.push('устье выбранной скважины не подсвечено бирюзой');
  }

  const highlighted = built.polylines.filter((line) =>
    line.styles.some((style) => style.color === '#2dd4bf'));
  if (highlighted.length !== 1) {
    problems.push('бирюзой подсвечено ' + highlighted.length + ' линий, ожидалась 1');
  } else if (lastStyle(highlighted[0]).weight !== 3) {
    problems.push('подсветка линии не даёт +1 px: ' + JSON.stringify(lastStyle(highlighted[0])));
  }

  // 4. Повторный клик по той же скважине снимает выбор и прячет панель.
  wellhead.fire('click');
  await sleep(50);
  if (panel.classList.contains('open')) {
    problems.push('повторный клик по скважине не снял выбор');
  }

  // 5. Забой (конечная точка траектории) выбирает ту же скважину.
  const bottom = built.circleMarkers.find((marker) =>
    marker.tooltip !== null && /забой, 3\s000 м/.test(String(marker.tooltip.content)));
  if (!bottom) {
    problems.push('подпись забоя не построена по MD из данных');
  } else {
    bottom.fire('click');
    await sleep(50);
    if (!panel.classList.contains('open') || title.textContent.indexOf('ok-full') === -1) {
      problems.push('клик по забою не выбрал скважину');
    }
  }

  // 6. Выбор другой скважины снимает подсветку прежней.
  built.circleMarkers[2].fire('click');
  await sleep(50);
  if (title.textContent.indexOf('single-point') === -1) {
    problems.push('выбор не переключился на другую скважину');
  }
  if (lastStyle(wellhead).fillColor !== '#e8a13c') {
    problems.push('подсветка прежней скважины не снята');
  }

  // 7. Линия траектории — тоже точка выбора (приоритет ниже устья, но работает).
  built.polylines[1].fire('click');
  await sleep(50);
  if (!panel.classList.contains('open') || title.textContent.indexOf('ok-full') === -1) {
    problems.push('клик по линии не выбрал скважину');
  }
}

// Сценарий autoselect: после прилёта панель открывается сама и показывает скважину
// с максимальной суммарной нефтью (wellId=1); нулевая добыча не выбирается.
async function checkAutoSelect(problems) {
  const panel = document.getElementById('panel');
  const title = document.getElementById('panel-title');
  const summary = document.getElementById('well-summary');

  // Автовыбор ждёт промис предзагрузки помесячных данных — даём ему такт.
  for (let attempt = 0; attempt < 40 && !panel.classList.contains('open'); attempt += 1) {
    await sleep(25);
  }

  if (!panel.classList.contains('open')) {
    problems.push('после прилёта панель не открылась автоматически');
  }
  if (title.textContent.indexOf('ok-full') === -1) {
    problems.push('автовыбор показал не ту скважину: ' + JSON.stringify(title.textContent));
  }
  if (chartInits.count !== 1) {
    problems.push('график автовыбора не отрисован: init=' + chartInits.count);
  }
  if (summary.hidden || summary.innerHTML.indexOf('Добыча нефти (суммарно)') === -1) {
    problems.push('сводка автовыбора не заполнена');
  }

  const wellhead = built.circleMarkers[0];
  const headStyle = wellhead.styles[wellhead.styles.length - 1] || {};
  if (headStyle.fillColor !== '#2dd4bf') {
    problems.push('устье автовыбранной скважины не подсвечено бирюзой');
  }

  const highlighted = built.polylines.filter((line) =>
    line.styles.some((style) => style.color === '#2dd4bf'));
  if (highlighted.length !== 1) {
    problems.push('автовыбор подсветил ' + highlighted.length + ' линий, ожидалась 1');
  }
}

setTimeout(async () => {
  const problems = [];

  // 1. Карта жива: нейтральный вид выставлен до данных.
  if (mapCalls.setView.length !== expected.setView) {
    problems.push('ожидалось setView=' + expected.setView + ', получено ' + mapCalls.setView.length);
  }

  // 2. Вводная анимация: перелёт к кусту скважин (или нейтральный вид без валидных точек).
  if (mapCalls.flyTo.length !== expected.flyTo) {
    problems.push('ожидалось flyTo=' + expected.flyTo + ', получено ' + mapCalls.flyTo.length);
  }

  // 3. Слои: траектория = пара слоёв (широкий + тонкий), строится при 2+ валидных точках.
  if (built.polylines.length !== expected.polyline) {
    problems.push('ожидалось polyline=' + expected.polyline + ', получено ' + built.polylines.length);
  }
  if (built.groups.length !== expected.groups) {
    problems.push('ожидалось групп траекторий ' + expected.groups + ', получено ' + built.groups.length);
  }

  // 4. Маркеры устьев: только по валидным координатам.
  const wellheadMarkers = mapCalls.added.filter((layer) => layer.kind === 'circleMarker');
  if (wellheadMarkers.length !== expected.markers) {
    problems.push('ожидалось маркеров устьев ' + expected.markers + ', получено ' + wellheadMarkers.length);
  }

  // 5. Перелёт наводится на куст скважин с зумом 14 (на 13 устья сливаются в пятно).
  if (expected.flyTo === 1) {
    const fly = mapCalls.flyTo[0];
    if (fly.zoom !== 14) {
      problems.push('ожидался перелёт с зумом 14, получено ' + fly.zoom);
    }
    if (!(fly.center.lat >= 58.44 && fly.center.lat <= 58.46 && fly.center.lng >= 1.85 && fly.center.lng <= 1.9)) {
      problems.push('перелёт не наведён на куст скважин: ' + JSON.stringify(fly.center));
    }
  }

  // 6. Сломанная скважина логируется отдельно и не мешает остальным.
  const text = warnings.join('\n');
  if (scenario === 'mixed') {
    if (!/bad-coords[\s\S]*некорректные координаты/.test(text)) {
      problems.push('нет предупреждения о битых координатах устья');
    }
    if (!/http-500[\s\S]*не добавлена/.test(text)) {
      problems.push('нет предупреждения об ошибке траектории (HTTP 500)');
    }
  }

  // 7. Выбор скважины: клик по устью, линии и забою, сводка и список в панели.
  if (scenario === 'select') {
    await checkSelection(problems);
  }

  // 8. Автовыбор самой добывающей скважины после прилёта.
  if (scenario === 'autoselect') {
    await checkAutoSelect(problems);
  }

  // 9. Без данных о добыче автовыбора нет: панель остаётся скрытой, график не создаётся.
  if (scenario === 'empty') {
    if (document.getElementById('panel').classList.contains('open')) {
      problems.push('в сценарии без данных панель открылась автоматически');
    }
    if (chartInits.count !== 0) {
      problems.push('в сценарии без данных отрисован график: ' + chartInits.count);
    }
  }

  if (/ERROR:/.test(text)) {
    problems.push('инициализация карты завершилась ошибкой: ' + text);
  }

  console.log('scenario=' + scenario + ': polyline=' + built.polylines.length +
    ', groups=' + built.groups.length + ', wellheadMarkers=' + wellheadMarkers.length +
    ', setView=' + mapCalls.setView.length + ', flyTo=' + mapCalls.flyTo.length);

  if (problems.length > 0) {
    console.log('FAIL scenario=' + scenario);
    for (const problem of problems) {
      console.log(' - ' + problem);
    }
    console.log('--- консоль страницы ---');
    console.log(text || '(пусто)');
    process.exit(1);
  }

  console.log('OK scenario=' + scenario);
  if (mapCalls.flyTo.length > 0) {
    console.log('   flyTo=' + JSON.stringify(mapCalls.flyTo[0].center) + ', zoom=' + mapCalls.flyTo[0].zoom);
  }
  process.exit(0);
}, 1000);


