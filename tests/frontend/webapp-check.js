'use strict';

// Проверка фронтенда web/app.js без браузера: Leaflet, DOM, fetch и sessionStorage
// заменены минимальными заглушками, повторяющими существенное поведение Leaflet
// (L.latLng бросает "Invalid LatLng object" на NaN/undefined).
// Перед app.js загружается web/common.js — он поставляет window.GeoPulse.
//
// Сценарии (аргумент командной строки):
//   mixed — валидные и битые данные вперемешку: карта жива, слои строятся по валидным,
//           каждая сломанная скважина изолирована своим guard'ом;
//   empty — валидных координат нет вовсе: карта остаётся на нейтральном виде.
//
// Запуск: node tests/frontend/webapp-check.js [mixed|empty]

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
const map = {
  setView(center, zoom) {
    mapCalls.setView.push({ center: new LatLng(Number(center[0]), Number(center[1])), zoom });
    return map;
  },
  flyTo(center, zoom, options) {
    mapCalls.flyTo.push({ center: new LatLng(Number(center[0]), Number(center[1])), zoom, options });
    return map;
  },
  once(event, cb) {
    // Имитируем завершение перелёта сразу, чтобы каскад траекторий стартовал.
    if (event === 'moveend') {
      cb();
    }
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

function makePath(kind, latlngs, options) {
  const layer = {
    kind,
    latlngs,
    options,
    bindPopup: () => layer,
    on: () => layer,
    setPopupContent: () => layer,
    openPopup: () => layer,
    setStyle: () => layer,
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
  tileLayer: () => ({ addTo: () => ({}) }),
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
const wells = [
  { wellId: 1, name: 'ok-full', field: 'Volve', latitude: 58.4412, longitude: 1.8541, tdMd: 3200, tdTvd: 2500 },
  { wellId: 2, name: 'bad-coords', field: 'Volve', latitude: 'oops', longitude: null, tdMd: null, tdTvd: null },
  { wellId: 3, name: 'http-500', field: 'Volve', latitude: 58.4413, longitude: 1.8542, tdMd: null, tdTvd: null },
  { wellId: 4, name: 'single-point', field: 'Volve', latitude: 58.4414, longitude: 1.8543, tdMd: 100, tdTvd: 90 }
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

if (scenario === 'empty') {
  wells.length = 0;
  wells.push(
    { wellId: 9, name: 'no-coords', field: 'Volve' },
    { wellId: 10, name: 'null-coords', field: 'Volve', latitude: null, longitude: null }
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
    const payload = trajectories[Number(match[1])];
    return payload === 'HTTP500' ? response({}, false) : response(payload ?? []);
  }

  if (url.startsWith('/api/Analytics/monthly')) {
    return response([]);
  }

  if (url === '/api/Analytics/summary') {
    return response({ totalOilVol: 0 });
  }

  return response({}, false);
};

globalThis.L = L;
globalThis.console = sandboxConsole;
globalThis.window = globalThis;
globalThis.sessionStorage = { getItem: () => null, setItem: () => {} };
globalThis.echarts = { init: () => ({ setOption() {}, dispose() {} }) };
globalThis.document = {
  getElementById: (id) => (id === 'panel-close'
    ? null
    : {
        classList: { add() {}, remove() {}, toggle() {} },
        setAttribute() {},
        hidden: true,
        textContent: '',
        style: {},
        addEventListener() {},
        innerHTML: ''
      })
};

// Сначала общий модуль, затем карта.
vm.runInThisContext(fs.readFileSync(commonJsPath, 'utf8'), { filename: 'common.js' });
vm.runInThisContext(fs.readFileSync(appJsPath, 'utf8'), { filename: 'app.js' });

setTimeout(() => {
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

  // 5. Перелёт наводится на куст скважин с зумом ~12–13.
  if (expected.flyTo === 1) {
    const fly = mapCalls.flyTo[0];
    if (fly.zoom !== 13) {
      problems.push('ожидался перелёт с зумом 13, получено ' + fly.zoom);
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


