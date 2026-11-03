'use strict';

// Проверка фронтенда web/app.js без браузера: Leaflet, DOM и fetch заменены
// минимальными заглушками, повторяющими существенное поведение Leaflet
// (L.latLng бросает "Invalid LatLng object" на NaN/undefined).
//
// Сценарии (аргумент командной строки):
//   mixed — валидные и битые данные вперемешку: карта жива, слои строятся по валидным,
//           каждая сломанная скважина изолирована своим guard'ом;
//   empty — валидных координат нет вовсе: карта остаётся на нейтральном виде.
//
// Запуск: node tests/frontend/webapp-check.js [mixed|empty]
// Проверка не входит в dotnet test: это отдельный прогон (см. run-webapp-check.ps1).

const fs = require('fs');
const path = require('path');
const vm = require('vm');

const scenario = process.argv[2] || process.env.SCENARIO || 'mixed';
const appJsPath = path.resolve(__dirname, '..', '..', 'web', 'app.js');

const warnings = [];
const sandboxConsole = {
  warn: (...args) => warnings.push(args.map(String).join(' ')),
  error: (...args) => warnings.push('ERROR: ' + args.map(String).join(' ')),
  // app.js ничего не печатает через log, поэтому вывод отдаётся стенду.
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


const mapCalls = { setView: [], fitBounds: [], added: [] };
const map = {
  setView(center, zoom) {
    mapCalls.setView.push({ center: new LatLng(Number(center[0]), Number(center[1])), zoom });
    return map;
  },
  fitBounds(bounds, options) {
    if (!bounds.isValid()) {
      throw new Error('fitBounds: invalid bounds');
    }
    mapCalls.fitBounds.push({ bounds, options });
    return map;
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
    // Как в Leaflet: точки проверяются в момент создания слоя.
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

// Замеры нарочно не отсортированы по MD: линия и забой строятся по MD.
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
  ? { setView: 2, fitBounds: 0, polyline: 0, groups: 0, markers: 0 }
  : { setView: 1, fitBounds: 1, polyline: 1, groups: 2, markers: 3 };

function response(data, ok) {
  return { ok: ok !== false, status: ok === false ? 500 : 200, json: async () => data };
}

globalThis.fetch = async (url) => {
  if (url === '/api/wells') {
    return response(wells);
  }

  const match = /^\/api\/Wells\/(\d+)\/trajectory$/.exec(url);
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
globalThis.document = {
  getElementById: (id) => (id === 'panel-close'
    ? null
    : { classList: { add() {}, remove() {} }, setAttribute() {}, hidden: true, textContent: '' })
};

vm.runInThisContext(fs.readFileSync(appJsPath, 'utf8'), { filename: 'app.js' });

setTimeout(() => {
  const problems = [];

  // 1. Карта жива: нейтральный вид выставлен до данных, затем fitBounds по границам.
  if (mapCalls.setView.length !== expected.setView) {
    problems.push('ожидалось setView=' + expected.setView + ', получено ' + mapCalls.setView.length);
  }
  if (mapCalls.fitBounds.length !== expected.fitBounds) {
    problems.push('ожидалось fitBounds=' + expected.fitBounds + ', получено ' + mapCalls.fitBounds.length);
  }

  // 2. Слои: линия строится только по траекториям с двумя и более валидными точками.
  if (built.polylines.length !== expected.polyline) {
    problems.push('ожидалось polyline=' + expected.polyline + ', получено ' + built.polylines.length);
  }
  if (built.groups.length !== expected.groups) {
    problems.push('ожидалось групп траекторий ' + expected.groups + ', получено ' + built.groups.length);
  }

  // 3. Маркеры устьев: только по валидным координатам.
  const wellheadMarkers = mapCalls.added.filter((layer) => layer.kind === 'circleMarker');
  if (wellheadMarkers.length !== expected.markers) {
    problems.push('ожидалось маркеров устьев ' + expected.markers + ', получено ' + wellheadMarkers.length);
  }

  // 4. Первичный вид учитывает траектории, а не только устья, и ограничен по зуму.
  if (expected.fitBounds === 1) {
    const bounds = mapCalls.fitBounds[0].bounds;
    if (!(bounds.sw.lat <= 58.4412 && bounds.ne.lat >= 58.46)) {
      problems.push('границы не включают точки траектории: ' + JSON.stringify(bounds));
    }
    if (mapCalls.fitBounds[0].options.maxZoom !== 16) {
      problems.push('у fitBounds нет ограничителя maxZoom');
    }
  }

  // 5. Сломанная скважина логируется отдельно и не мешает остальным.
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
    ', setView=' + mapCalls.setView.length + ', fitBounds=' + mapCalls.fitBounds.length);

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
  if (mapCalls.fitBounds.length > 0) {
    console.log('   fitBounds=' + JSON.stringify(mapCalls.fitBounds[0].bounds) +
      ', maxZoom=' + mapCalls.fitBounds[0].options.maxZoom);
  }
}, 300);
