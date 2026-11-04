# Функциональность GeoPulse

Документ описывает то, что реально реализовано: страницы веб-клиента, эндпоинты WebAPI и формулы расчёта метрик.

**Файлы-источники**

| Файл | Что взято |
| --- | --- |
| `web/index.html`, `web/app.js`, `web/style.css` | карта: слои, выбор скважины, панель, автовыбор |
| `web/reports.html`, `web/reports.js` | страница отчётов: сводка, график, ссылки на выгрузки |
| `web/forecast.html`, `web/forecast.js` | страница прогноза и ожидаемые пути ML-эндпоинтов |
| `web/about.html`, `web/nav.js`, `web/common.js` | страница «О проекте», навигация, общие хелперы и палитра |
| `src/GeoPulse.WebAPI/Controllers/*.cs` | маршруты, параметры, коды ответов |
| `src/GeoPulse.WebAPI/Models/*.cs` | состав JSON-ответов |
| `src/GeoPulse.WebAPI/ReportService.cs` | структура XLSX/CSV и формулы обводнённости и доли |
| `src/GeoPulse.WebAPI/ClickHouseAnalyticsService.cs` | SQL, на котором считаются агрегаты |
| `tests/smoke/webapi-smoke.ps1` | фактические проверки контракта (коды 200/400/404) |

## 1. Что умеет веб-интерфейс

Фронтенд — статические файлы в `web/`, их раздаёт сам WebAPI (`UseStaticFiles`), внешних CDN нет: Leaflet, ECharts и шрифт Inter лежат в `web/vendor/`. Навигация (`web/nav.js`) общая для четырёх страниц и подсвечивает активный пункт по `location.pathname`.

### 1.1 Страница «Карта» (`/`, `index.html` + `app.js`)

| Возможность | Детали реализации |
| --- | --- |
| Карта месторождения | Leaflet, подложка Esri World Light Gray Base (тайлы `services.arcgisonline.com`, до зума 16). Слой подписей мест подключается только при зуме ≥ 11. |
| Вводный полёт | Старт в точке `[60.9, 8.6]`, зум 4 → перелёт к кусту скважин (зум 14) за 2600 мс. Один раз за сессию (ключ `geopulse.introSeen`). Данные карты скрыты до конца полёта (класс `data-pending`, fade-in 400 мс). |
| Устья скважин | Маркеры по `latitude`/`longitude` из `GET /api/wells`; подписи колонкой (шаг 24 px) при зуме ≥ 13, потому что устья Volve лежат в пятне ~10 м. |
| Траектории стволов | Линия по точкам `GET /api/wells/{wellId}/trajectory` (WGS84 приходит с сервера): янтарная линия с обводкой. Слои добавляются каскадом (не чаще одного за 150 мс), чтобы не блокировать карту. |
| Забои | Кружок в последней точке траектории с подписью «забой, NNNN м» (MD последнего замера), при зуме ≥ 13. |
| Выбор скважины | Клик по устью, линии или забою; при пересечении приоритет — устье → линия → забой (задан `z-index` слоёв). Повторный клик по той же скважине снимает выбор. Клик по пустому месту карты панель не закрывает. |
| Автовыбор | После загрузки выбирается самая добывающая скважина (максимум суммарной нефти среди скважин с ненулевой добычей; при равенстве — первая по алфавиту). Один раз за загрузку страницы. |
| Мобильная панель (< 768px) | Панель скважины — нижний лист 55dvh (`style.css`). При открытии куст центрируется в видимой части карты: `map.stop()` + `invalidateSize()` + `fitBounds(clusterBounds(), { paddingBottomRight: [20, offsetHeight панели + 20], maxZoom: 14 })`; при закрытии возвращается вид, сохранённый перед подгоном. Десктоп не затронут: вся логика под `matchMedia('(max-width: 768px)')`. |
| График скважины | ECharts: помесячная добыча нефти (янтарная, с градиентной заливкой) и воды (голубая) из `GET /api/Analytics/monthly?wellId=`. Подписи оси X — по годам (не более 10 меток), полное `YYYY-MM` — только в тултипе. |
| Сводка скважины | Месторождение; «Забой» (`tdTvd`) и «Длина ствола» (`tdMd`) из `GET /api/wells`; суммарная нефть; суммарная вода; обводнённость последнего месяца с добычей нефти; пик добычи нефти; период добычи; доля в добыче месторождения (знаменатель — `GET /api/Analytics/summary`). |
| Кнопка «Скачать отчёт (Excel)» | Ссылка на `GET /api/Reports/wells/{wellId}.xlsx`. |
| Список всех скважин | В правой панели, с суммарной добычей нефти; пункт выбирает скважину тем же механизмом, что клик по карте. |
| Состояния загрузки | Скелетоны на время запросов, панель ошибки при сбое `GET /api/wells`. |
| Точность чисел | Везде ровно 2 знака после запятой (`toLocaleString('ru-RU')`, `common.js`). |

### 1.2 Страница «Отчёты» (`/reports.html`, `reports.js`)

| Возможность | Детали реализации |
| --- | --- |
| Сводка по месторождению | Таблица из `GET /api/Analytics/summary` + `GET /api/wells`: суммарная нефть, вода, газ, закачка воды, суммарные часы работы, число скважин. |
| График по месторождению | Клиент агрегирует помесячные данные всех скважин: `Promise.all` по `GET /api/Analytics/monthly?wellId=<id>`, затем сумма по месяцам; две линии — нефть (с заливкой) и вода. |
| Выгрузка Excel | Кнопка «Скачать Excel» → `GET /api/Reports/monthly.xlsx`. |
| Выгрузка CSV | Кнопка «Скачать CSV» → `GET /api/Reports/monthly.csv`. |
| Обработка ошибок | Блок с текстом и кнопкой «Повторить»; устаревшие ответы игнорируются (`loadSeq`). |

### 1.3 Страница «Прогноз» (`/forecast.html`, `forecast.js`)

| Возможность | Детали реализации |
| --- | --- |
| Переключатель области | «По месторождению» / «По скважине»; во втором режиме появляется `<select>` со списком скважин. |
| Факт | Помесячная добыча нефти и воды (левая ось) и газа (правая ось) — те же `GET /api/wells` и `GET /api/Analytics/monthly`. |
| Прогноз | Запрашиваются `GET /api/Forecast/field` или `GET /api/Forecast/wells/{wellId}`. **Этих эндпоинтов в WebAPI нет** (контроллера `Forecast` в `src/GeoPulse.WebAPI/Controllers` не существует), поэтому страница всегда показывает блок «Прогноз недоступен» с кнопкой «Повторить». Это заявленное поведение: `forecast.js` ловит ошибку и считает её ожидаемой. |
| «План внедрения прогноза» | Статическая карточка под графиком (`forecast.html`, класс `card plan`): XGBoost с лаговыми признаками, офлайн-обучение в Python (train 2008–2015 + holdout, MAE/MAPE), ONNX в рантайме WebAPI через `Microsoft.ML.OnnxRuntime`, эндпоинт `/api/forecast`; на графике факт дополняется пунктирным прогнозом на полгода. Текст не зависит от API, `forecast.js` этот блок не трогает. |

### 1.4 Страница «О проекте» (`/about.html`)

Статический текст: контакты разработчика (телефон, Telegram, GitHub), описание проекта, источник данных Volve и лицензия, схема архитектуры (ASCII), стек, краткий рассказ о процессе разработки.

### 1.5 Чего в интерфейсе нет

* **Нет выбора периода и фильтров.** Ни на одной странице нет полей «с даты / по дату», фильтра по скважине или сортировки: показывается вся история. Параметры `from`, `to`, `sort`, `page`, `pageSize` есть только в API (`/api/wells/{code}/production`).
* **Нет выгрузки суточной добычи.** CSV/XLSX содержат только помесячные агрегаты.
* **Нет аутентификации и авторизации.** Все эндпоинты и статика открыты.
* **Нет пагинации в UI** — страницы суточной добычи клиентом не используются.
* **Нет перфораций и пластов** — в выгружаемых частях Volve их нет, поэтому нет ни таблиц, ни эндпоинтов, ни слоёв карты.

## 2. API-эндпоинты

Базовый адрес локально — `http://localhost:5146`, в проде — `https://<DOMAIN>` (через Caddy). Все эндпоинты возвращают `application/json`, кроме выгрузок отчётов. Ошибки — в формате ProblemDetails (RFC 7807): для необработанных исключений — `app.UseExceptionHandler()`, для валидации — `ValidationProblemDetails`.

| Метод | Путь | Параметры | Ответ | Ошибки |
| --- | --- | --- | --- | --- |
| GET | `/health` | — | `HealthResponse`: `status` (`ok`/`degraded`), `database` (`up`/`down`), `checkedAtUtc` | 503 при недоступной БД |
| GET | `/api/wells` | — | `WellDto[]`: `wellId`, `npdWellBoreCode`, `name`, `field`, `type`, `latitude`, `longitude`, `tdMd`, `tdTvd` | — |
| GET | `/api/wells/{wellId:int}/trajectory` | `wellId` — внутренний id (`wells.id`) | `TrajectoryPointDto[]`: `md`, `tvd`, `inclination`, `azimuth`, `latitude`, `longitude` (WGS84) | 404, если скважины нет |
| GET | `/api/wells/{npdWellBoreCode:int}` | `npdWellBoreCode` — код НПД | `WellDetailsDto`: `npdWellBoreCode`, `name`, `field`, `type`, `firstProductionDate`, `lastProductionDate`, `productionDays` | 404 |
| GET | `/api/wells/{npdWellBoreCode:int}/production` | `from`, `to` (`yyyy-MM-dd`), `page` (≥ 1, по умолчанию 1), `pageSize` (1..500, по умолчанию 100), `sort` (`desc` по умолчанию / `asc`) | `PagedResponse<ProductionRecordDto>`: `items`, `totalCount`, `page`, `pageSize`, `totalPages`, `hasNextPage`, `hasPreviousPage` | 400 (невалидные параметры), 404 (скважина не найдена) |
| GET | `/api/wells/{npdWellBoreCode:int}/production/{date}` | `date` (`yyyy-MM-dd`) | `ProductionRecordDto` | 400 (дата не парсится), 404 (нет записи) |
| GET | `/api/Analytics/monthly` | `wellId` **или** `wellCode` (ровно один) | `MonthlyProductionDto[]`: `year`, `month`, `oilVolSum`, `gasVolSum`, `waterVolSum`, `waterInjectionVolSum`, `onStreamHrsSum`, `daysCount` | 400 (не передан ни один параметр), 404 (скважина не найдена) |
| GET | `/api/Analytics/summary` | — | `ProductionSummaryDto`: `totalOilVol`, `totalGasVol`, `totalWaterVol`, `totalWaterInjectionVol`, `totalOnStreamHrs`, `totalDays` | — |
| GET | `/api/Reports/monthly.xlsx` | — | файл `monthly_YYYY-MM-DD.xlsx`, листы «Сводка», «Помесячно», «По скважинам» | — |
| GET | `/api/Reports/monthly.csv` | — | файл `monthly_YYYY-MM-DD.csv`, `text/csv`, UTF-8 с BOM | — |
| GET | `/api/Reports/wells/{wellId:int}.xlsx` | `wellId` — внутренний id | файл `well_{wellId}_YYYY-MM-DD.xlsx`, листы «Скважина», «Помесячно» | 404 |
| GET | `/swagger`, `/swagger/v1/swagger.json` | — | Swagger UI и контракт OpenAPI (включён во всех окружениях) | — |

Дополнительно WebAPI раздаёт статику из `web/`: `/` (index), `/reports.html`, `/forecast.html`, `/about.html`, `/style.css`, `/app.js` и т. д.

### 2.1 Примеры ответов (сняты с работающего стенда)

`GET /api/wells` — один элемент из 7:

```json
{"wellId":3,"npdWellBoreCode":5351,"name":"15/9-F-14","field":"VOLVE","type":"OP",
 "latitude":58.44098776,"longitude":1.88593993,"tdMd":3750,"tdTvd":3158.65}
```

`GET /api/Analytics/summary`:

```json
{"totalOilVol":10037080.61,"totalGasVol":1475370435.94,"totalWaterVol":15318578.35,
 "totalWaterInjectionVol":30330133.531097535,"totalOnStreamHrs":306889.34079000005,
 "totalDays":15634}
```

`GET /api/Analytics/monthly?wellId=3` — первый элемент:

```json
{"year":2008,"month":2,"oilVolSum":0,"gasVolSum":0,"waterVolSum":0,
 "waterInjectionVolSum":0,"onStreamHrsSum":0,"daysCount":18}
```

`GET /api/wells/5351`:

```json
{"npdWellBoreCode":5351,"name":"15/9-F-14","field":"VOLVE","type":"OP",
 "firstProductionDate":"2008-02-12","lastProductionDate":"2016-09-17","productionDays":3056}
```

`GET /api/wells/5351/production?pageSize=1`:

```json
{"items":[{"date":"2016-09-17","onStreamHrs":0,"avgDownholePressure":0,
 "avgDownholeTemperature":0,"avgDpTubing":10.87613,"avgAnnulusPressure":10.58302,
 "avgChokeSize":1.1132164583333333,"avgWhp":10.87613,"avgWht":0,"dpChokeSize":8.82793,
 "oilVol":0,"gasVol":0,"waterVol":0,"waterInjectionVol":null}],
 "totalCount":3056,"page":1,"pageSize":1,"totalPages":3056,"hasNextPage":true,"hasPreviousPage":false}
```

`GET /api/wells/3/trajectory` — первый замер:

```json
[{"md":145.9,"tvd":145.9,"inclination":0,"azimuth":0,
  "latitude":58.440987760967666,"longitude":1.8859399236727732}, ...]
```

`GET /api/Reports/monthly.csv` — шапка и первые строки (BOM + запятая, числа `0.00`):

```
Year,Month,OilVol,WaterVol,WaterCutPct
2007,9,0.00,0.00,0.00
2008,2,49091.06,412.61,0.83
2008,3,83361.26,27.42,0.03
```

## 3. Как считаются отчётные метрики

Схема расчёта — три слоя, каждый в одном месте:

```
daily_production (PostgreSQL, суточные строки)
        │  CHIngest (Kafka → ClickHouse)
        ▼
gp.production_daily (ReplacingMergeTree)
        │  Aggregator: TRUNCATE + INSERT ... SELECT ... GROUP BY
        ▼
gp.monthly_production (MergeTree)  →  /api/Analytics/*, /api/Reports/*
```

### 3.1 Помесячный агрегат

`AggregatorService.RebuildInsertSql`, выполняется каждые `RecalcIntervalSeconds` (60 с) и сразу на старте:

```sql
INSERT INTO gp.monthly_production
SELECT well_id, toYear(date) AS year, toMonth(date) AS month,
       sum(oil_vol), sum(gas_vol), sum(water_vol), sum(water_injection_vol),
       sum(on_stream_hrs), count() AS days_count
FROM gp.production_daily FINAL
GROUP BY well_id, year, month
```

Важные следствия:

* `FINAL` схлопывает дубликаты `ReplacingMergeTree` (ключ сортировки `npd_well_bore_code, date`) — поэтому повторная доставка сообщений топика не удваивает суммы.
* `days_count` = `count()` строк, а не количество дней с `on_stream_hrs > 0`. На текущем датасете сумма `days_count` по всем скважинам равна 15 634 — столько же, сколько строк `daily_production`.
* Таблица пересобирается целиком (`TRUNCATE` + `INSERT`), инкрементального состояния нет: любое изменение сырых данных подхватывается на следующем тике.
* При загрузке в ClickHouse `null` из события превращается в `0` (`CHIngestService.Float`: `value?.ToString("R") ?? "0"`), поэтому «нет данных» и «ноль» в ClickHouse неразличимы. В PostgreSQL `null` сохраняется как `NULL` (`ProductionConsumerService.DailyColumns` с `NpgsqlDbType.Double`).

### 3.2 Сводка по месторождению (`/api/Analytics/summary`)

`ClickHouseAnalyticsService.GetSummaryAsync` — один SQL:

```sql
SELECT sum(oil_vol_sum), sum(gas_vol_sum), sum(water_vol_sum),
       sum(water_injection_vol_sum), sum(on_stream_hrs_sum), sum(days_count)
FROM gp.monthly_production
```

То есть сводка — сумма помесячных сумм за всё время, `totalDays` — сумма `days_count`.

### 3.3 Обводнённость и доля

| Метрика | Формула | Где применяется |
| --- | --- | --- |
| WaterCut, % | `вода / (нефть + вода) × 100`, при `нефть + вода ≤ 0` → `0` | XLSX-лист «Помесячно», CSV-колонка `WaterCutPct`, сводка скважины на карте |
| Доля в добыче месторождения, % | `нефть_скважины / totalOilVol × 100`, при `totalOilVol ≤ 0` → `0` | XLSX «По скважинам» и «Скважина», сводка скважины на карте |

На сервере (`ReportService.LoadAsync`) месячные строки отчёта собираются группировкой «плоских» агрегатов по (`Year`, `Month`): `oil = Σ oilVolSum`, `water = Σ waterVolSum` по всем скважинам, затем `WaterCutPct(oil, water)`. Отчёт по скважине (`LoadWellAsync`) группирует те же данные по `wellId`, суммирует и считает `SharePct(wellOil, summary.TotalOilVol)`.

На клиенте (`web/app.js`) обводнённость в панели считается за **последний месяц, в котором была добыча нефти** (`oilVolSum > 0`); пик — месяц с максимальным `oilVolSum`; период — от первого до последнего месяца с добычей нефти.

### 3.4 Структура выгрузок

**`GET /api/Reports/monthly.xlsx`** (ClosedXML, шапка жирная, автоширина, числовой формат `#,##0.00`):

| Лист | Содержимое |
| --- | --- |
| «Сводка» | «Показатель»/«Значение»: Суммарная нефть, м³; Суммарная вода, м³; Период; Число скважин |
| «Помесячно» | Год, Месяц, Нефть (м³), Вода (м³), Обводнённость (%) |
| «По скважинам» | Скважина, Нефть (м³), Вода (м³), Доля (%) |

**`GET /api/Reports/monthly.csv`**: `Year,Month,OilVol,WaterVol,WaterCutPct`, UTF-8 с BOM, разделитель — запятая, числа в `0.00` (InvariantCulture).

**`GET /api/Reports/wells/{wellId}.xlsx`**:

| Лист | Содержимое |
| --- | --- |
| «Скважина» | Скважина; Месторождение; Суммарная нефть, м³; Суммарная вода, м³; Период; Доля от добычи месторождения, % |
| «Помесячно» | те же колонки, что в отчёте по месторождению |

«Период» строится как `YYYY-MM — YYYY-MM` по первому и последнему элементу помесячного набора (`BuildPeriod`), при пустых данных — `—`.

## 4. Открытые вопросы

1. **Страница «Прогноз» без бэкенда.** `web/forecast.js` обращается к `/api/Forecast/field` и `/api/Forecast/wells/{wellId}`, но контроллера нет. Планируется ли ML-сервис и в каком контракте — [УТОЧНИТЬ].
2. **Периоды и фильтры в UI.** API умеет `from`/`to`/`sort`/`page`, но клиент это не использует. Нужны ли фильтры на карте и отчётах — [УТОЧНИТЬ].
3. **`null` → `0` в ClickHouse.** Из-за этого «нет данных» и «ноль» неразличимы в аналитике: например, `days_count` учитывает все строки, а `avg_*` поля отсутствующих значений дают `0`, а не пропуск. Приемлемо ли это для отчётности — [УТОЧНИТЬ].
4. **`/health` не проверяет ClickHouse и Kafka.** Страница может вернуть 200 при недоступной аналитике (тогда `/api/Analytics/*` падает с 500). Нужны ли отдельные пробы — [УТОЧНИТЬ].
5. **Число 526 агрегатов и 15 634 строки** подтверждены на текущем стенде (`count()` по `gp.monthly_production` и `gp.production_daily`/`daily_production`). Совпадут ли они на другой версии датасета Volve — [УТОЧНИТЬ].
6. **Отчёт по скважине адресуется `wellId`, а суточная добыча — `npdWellBoreCode`.** Два разных идентификатора в одном API (`/api/Reports/wells/{wellId}.xlsx` против `/api/wells/{npdWellBoreCode}`). Осознанная ли это асимметрия (совместимость со старым контрактом) — [УТОЧНИТЬ].
