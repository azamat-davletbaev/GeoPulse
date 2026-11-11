# Домен и данные GeoPulse

**Файлы-источники**

| Файл | Что взято |
| --- | --- |
| `src/GeoPulse.Infrastructure/Migrations/*.cs` | DDL PostgreSQL: таблицы, типы, ключи, индексы |
| `src/GeoPulse.Infrastructure/CoreDbContext.cs`, `Entities/*.cs` | модель EF Core, имена колонок (snake_case), связи |
| `src/GeoPulse.Core/Models/*.cs` | доменные модели и их семантика |
| `src/GeoPulse.Core/Events/*.cs` | поля событий, типы, версия схемы |
| `src/GeoPulse.CHIngest/CHIngestService.cs` | DDL ClickHouse: таблицы, движки, `ORDER BY` |
| `src/GeoPulse.Ingestion.Service/IngestionService.cs`, `TrajectoryParser.cs` | какие колонки Excel и поля WITSML читаются |
| `src/GeoPulse.WebAPI/Reports/ReportMetrics.cs`, `web/app.js` | формулы обводнённости и доли |
| `sql/seed_well_locations.sql` | координаты устьев и датум |
| `data/Volve_Production_data/license.txt` | лицензия исходного набора |

## 1. Предметная область

| Понятие | Как представлено в решении |
| --- | --- |
| Месторождение | Поле `field` в `wells` (`NPD_FIELD_NAME` из Excel). В датасете — одно месторождение Volve, поэтому отдельной таблицы месторождений нет. |
| Скважина / ствол | Строка в `wells`. В Volve одна запись = один ствол (`NPD_WELL_BORE_CODE`), поэтому «скважина» и «ствол» в коде не различаются: `Well`, `WellId`, `npdWellBoreCode`. |
| Натуральный ключ | `npdWellBoreCode` — код ствола в Норвежском нефтяном директорате. Уникален (`IX_wells_npd_well_bore_code`), используется в URL API и в ключе сообщений Kafka. |
| Тип скважины | `type` — самое частое значение `WELL_TYPE` (`OP` — добывающая, `WI` — нагнетательная). |
| Суточная добыча | Строка в `daily_production` / `gp.production_daily`: дата + 13 числовых показателей. |
| Помесячная добыча | Агрегат считается на лету из `gp.production_daily` (FINAL): сумма объёмов и `count()` суток за (скважина, год, месяц); предвычисленной таблицы нет (ADR-016). |
| Устье | Координаты WGS84 в `well_locations` (широта/долгота) — для маркера на карте. |
| Траектория | Замеры в `well_trajectories` (MD, TVD, зенитный угол, азимут, Easting/Northing в ED50/UTM31N) — ломаная на карте. |
| Забой | `well_trajectory_meta.td_md` / `td_tvd` — из заголовка выгрузки (`Bottom Hole MD/TVD`). |
| Обводнённость (WaterCut) | Расчётная величина, в БД не хранится. |
| Доля в добыче месторождения | Расчётная величина, в БД не хранится. |

### 1.1 Обводнённость (WaterCut)

Формула реализована дважды и совпадает: `ReportMetrics.WaterCutPct` (сервер, отчёты XLSX/CSV) и `waterCutPct` в `web/app.js` (панель скважины):

```csharp
// src/GeoPulse.WebAPI/Reports/ReportMetrics.cs
private static double WaterCutPct(double oil, double water)
{
    var total = oil + water;
    return total <= 0 ? 0 : water / total * 100;
}
```

```javascript
// web/app.js
function waterCutPct(oil, water) {
  const total = oil + water;
  return total > 0 ? (water / total) * 100 : 0;
}
```

То есть **WaterCut = вода / (нефть + вода) × 100**, при `нефть + вода ≤ 0` результат — `0`. Газ и закачка в формулу не входят.

На карте (`web/app.js`) обводнённость показывается **за последний месяц, в котором была добыча нефти** (`stats.lastOilMonth`), с подписью месяца. В отчётах — за каждый календарный месяц и по месторождению в целом.

### 1.2 Доля в добыче месторождения

```csharp
// src/GeoPulse.WebAPI/Reports/ReportMetrics.cs
private static double SharePct(double oil, double totalOil)
{
    return totalOil <= 0 ? 0 : oil / totalOil * 100;
}
```

`totalOil` — `sum(oil_vol)` по всему `gp.production_daily` (запрос `GetSummaryAsync`, чтение с `FINAL`; предвычисленной таблицы агрегатов нет — ADR-016). В панели скважины на карте (`web/app.js`) знаменатель берётся из `GET /api/Analytics/summary` (`loadFieldTotalOil`), числитель — сумма `oilVolSum` по скважине.

### 1.3 Пик добычи и период работы

Считаются только на клиенте (`web/app.js`, `wellStats`), по месяцам, где `oilVolSum > 0`:

* **пик** — месяц с максимальным `oilVolSum`;
* **период** — от первого до последнего такого месяца.

## 2. Схема PostgreSQL

База — `geopulse_core`, владелец `geopulse`. Все имена колонок в snake_case (правило `CoreDbContext.ApplySnakeCaseNames`). Миграции применяет `Consumer` при старте; вручную — `dotnet ef database update`.

### 2.1 `wells` (миграция `20261101075757_Initial`)

| Колонка | Тип | Ограничения |
| --- | --- | --- |
| `id` | `integer` | PK, identity (`NpgsqlValueGenerationStrategy.IdentityByDefaultColumn`) |
| `npd_well_bore_code` | `integer` | NOT NULL, уникальный индекс `IX_wells_npd_well_bore_code` |
| `name` | `character varying(100)` | NOT NULL |
| `field` | `character varying(100)` | NOT NULL |
| `type` | `character varying(20)` | NOT NULL |

### 2.2 `daily_production` (миграция `20261101075757_Initial`)

| Колонка | Тип | Ограничения |
| --- | --- | --- |
| `id` | `uuid` | PK |
| `well_id` | `integer` | NOT NULL, FK → `wells.id`, `fk_daily_production_wells`, `ON DELETE CASCADE` |
| `date` | `date` | NOT NULL |
| `on_stream_hrs` | `double precision` | NULL |
| `avg_downhole_pressure` | `double precision` | NULL |
| `avg_downhole_temperature` | `double precision` | NULL |
| `avg_dp_tubing` | `double precision` | NULL |
| `avg_annulus_pressure` | `double precision` | NULL |
| `avg_choke_size` | `double precision` | NULL |
| `avg_whp` | `double precision` | NULL |
| `avg_wht` | `double precision` | NULL |
| `dp_choke_size` | `double precision` | NULL |
| `oil_vol` | `double precision` | NULL |
| `gas_vol` | `double precision` | NULL |
| `water_vol` | `double precision` | NULL |
| `water_injection_vol` | `double precision` | NULL |

Уникальный индекс `IX_daily_production_well_id_date` по (`well_id`, `date`) — на него опирается идемпотентный `ON CONFLICT (well_id, date) DO NOTHING`.

### 2.3 `well_locations` (миграция `20261102182938_AddWellLocations`)

| Колонка | Тип | Ограничения |
| --- | --- | --- |
| `well_id` | `integer` | PK и FK → `wells.id`, `fk_well_locations_wells`, `ON DELETE CASCADE` (связь 1:1) |
| `latitude` | `double precision` | NOT NULL (WGS84, градусы) |
| `longitude` | `double precision` | NOT NULL (WGS84, градусы) |
| `source` | `text` | NOT NULL — файл-источник и преобразование |

### 2.4 `well_trajectories` (миграция `20261103070827_AddWellTrajectories`)

| Колонка | Тип | Ограничения |
| --- | --- | --- |
| `well_id` | `integer` | составной PK, FK → `wells.id`, `fk_well_trajectories_wells`, `ON DELETE CASCADE` |
| `md` | `double precision` | составной PK (MD, м) |
| `tvd` | `double precision` | NOT NULL |
| `inclination` | `double precision` | NOT NULL (градусы) |
| `azimuth` | `double precision` | NOT NULL (градусы) |
| `easting` | `double precision` | NOT NULL (ED50 / UTM 31N, м) |
| `northing` | `double precision` | NOT NULL (ED50 / UTM 31N, м) |

PK (`well_id`, `md`) — на неё опирается `ON CONFLICT (well_id, md) DO NOTHING`.

### 2.5 `well_trajectory_meta` (миграция `20261103070827_AddWellTrajectories`)

| Колонка | Тип | Ограничения |
| --- | --- | --- |
| `well_id` | `integer` | PK и FK → `wells.id`, `fk_well_trajectory_meta_wells`, `ON DELETE CASCADE` (1:1) |
| `td_md` | `double precision` | NULL (Bottom Hole MD, м) |
| `td_tvd` | `double precision` | NULL (Bottom Hole TVD, м) |

Апсертится (`ON CONFLICT (well_id) DO UPDATE`), поэтому переимпорт обновляет глубины забоя.

## 3. Схема ClickHouse

База — `gp`. Обе таблицы создаются самим сервисом: `ClickHouseSchema` (`src/GeoPulse.ClickHouse/ClickHouseSchema.cs`) содержит DDL базы и таблиц, а `ClickHouseSchema.EnsureAsync` выполняет `CREATE DATABASE IF NOT EXISTS gp` и `CREATE TABLE IF NOT EXISTS` при старте `CHIngest`, поэтому отдельного DDL-файла в репозитории нет.

### 3.1 `gp.production_daily`

```
ENGINE = ReplacingMergeTree ORDER BY (npd_well_bore_code, date)
```

| Колонка | Тип |
| --- | --- |
| `well_id` | `UInt32` |
| `npd_well_bore_code` | `UInt32` |
| `date` | `Date` |
| `oil_vol`, `gas_vol`, `water_vol`, `water_injection_vol` | `Float64` |
| `on_stream_hrs`, `avg_downhole_pressure`, `avg_downhole_temperature`, `avg_dp_tubing`, `avg_annulus_pressure`, `avg_choke_size`, `avg_whp`, `avg_wht`, `dp_choke_size` | `Float64` |

Ключ сортировки — (`npd_well_bore_code`, `date`), хотя `well_id` тоже хранится: дедупликация `ReplacingMergeTree` идёт по ключу сортировки, а не по `well_id`. Чтение обязано идти с `FINAL` — так его делает `ClickHouseAnalyticsService`: помесячные суммы считаются на лету из `gp.production_daily FINAL` (ADR-016).

### 3.2 `gp.wells`

```
ENGINE = ReplacingMergeTree ORDER BY npd_well_bore_code
```

| Колонка | Тип |
| --- | --- |
| `npd_well_bore_code` | `UInt32` |
| `name`, `field`, `type` | `String` |
| `well_id` | `UInt32` |

Заполняется из событий `well.registered` (резолв `well_id` берётся из PostgreSQL). В запросах API эта таблица не участвует — справочник скважин всегда читается из PostgreSQL.

## 4. Топики Kafka

| Топик | Назначение | Партиции | Ключ сообщения |
| --- | --- | --- | --- |
| `production-daily` | все события пайплайна: `well.registered`, `well.trajectory`, `production.daily` | 3 в проде (`KAFKA_NUM_PARTITIONS: 3`, `TOPIC_PARTITIONS=3` в `deploy.sh`), 1 в `docker-compose.yml` | строка `npdWellBoreCode` |
| `production-daily-dlq` | сообщения, которые не удалось обработать | те же правила (автосоздание) | тот же ключ, что у исходного сообщения |

Стратегия ключа: ключ = код ствола, поэтому все события одной скважины идут в одну партицию и обрабатываются в порядке публикации (`well.registered` → `well.trajectory` → `production.daily`). Подробнее — `docs/architecture.md`, раздел 4.

Имена топиков задаются конфигурацией: `Kafka:Topic` / `Kafka:DlqTopic` в `appsettings.json` (`Consumer`, `CHIngest`, `Ingestion.Service`) и `Kafka__Topic` / `Kafka__DlqTopic` в `docker-compose.prod.yml`. Настройки брокера — `KAFKA_DEFAULT_REPLICATION_FACTOR=1`, `KAFKA_AUTO_CREATE_TOPICS_ENABLE=true`.

## 5. Источник данных: датасет Volve

Лицензия — **Equinor Open Data Licence** (CC BY-NC-SA 4.0), текст лежит в `data/Volve_Production_data/license.txt`. Источник: <https://www.equinor.com/energy/volve-data-sharing>.

### 5.1 Что лежит в репозитории

| Путь | Формат | Используется |
| --- | --- | --- |
| `data/Volve_Production_data/Volve production data.xlsx` | XLSX, листы `Daily Production Data` и `Monthly Production Data` | да — только лист `Daily Production Data` (`IngestionService.DailySheetName`) |
| `data/Volve_Production_data/license.txt` | текст лицензии | нет (юридическая атрибуция) |
| `data/Volve_Well_technical_data/WellWellbore/<скважина>/<ствол>/*_ACTUAL` | плоский текст (survey) | да — траектории, забои |
| `data/Volve_Well_technical_data/EDM.XML/`, `Daily Drilling Report - *`, `StressCheck/`, `Wellcat/`, `Site/` и др. | XML/PDF/HTML/бинарные | нет — исключены в `.gitignore` и `.dockerignore`, парсером не читаются |

В репозиторий и в образ `geopulse-ingestion` попадают только production-xlsx и `WellWellbore` (см. `.gitignore`, `.dockerignore`, `src/GeoPulse.Ingestion.Service/Dockerfile`). Пути заданы в `appsettings.json`:

```json
"Volve": {
  "ProductionWorkbook": "data/Volve_Production_data/Volve production data.xlsx",
  "WellboreRoot": "data/Volve_Well_technical_data/WellWellbore"
}
```

Относительные пути разрешаются через `Path.GetFullPath` от текущей рабочей директории; в контейнере данные скопированы в `/app/data/...`.

### 5.2 Что именно импортирует ingestion-джоба

**Шаг 1 — Excel.** Читается лист `Daily Production Data` (заголовок — первая строка). Используемые колонки (имена — как в файле, `Cell(row, "<имя>")`):

| Колонка Excel | Куда попадает |
| --- | --- |
| `DATEPRD` | `date` |
| `NPD_WELL_BORE_CODE` | `npdWellBoreCode` (ключ; строки с пустым ключом пропускаются) |
| `NPD_WELL_BORE_NAME`, `WELL_BORE_CODE` | `name` (первое непустое) |
| `NPD_FIELD_NAME` | `field` |
| `WELL_TYPE` | `type` (мода по строкам скважины) |
| `ON_STREAM_HRS` | `onStreamHrs` |
| `AVG_DOWNHOLE_PRESSURE` | `avgDownholePressure` |
| `AVG_DOWNHOLE_TEMPERATURE` | `avgDownholeTemperature` |
| `AVG_DP_TUBING` | `avgDpTubing` |
| `AVG_ANNULUS_PRESS` | `avgAnnulusPressure` |
| `AVG_CHOKE_SIZE_P` | `avgChokeSize` |
| `AVG_WHP_P` | `avgWhp` |
| `AVG_WHT_P` | `avgWht` |
| `DP_CHOKE_SIZE` | `dpChokeSize` |
| `BORE_OIL_VOL` | `oilVol` |
| `BORE_GAS_VOL` | `gasVol` |
| `BORE_WAT_VOL` | `waterVol` |
| `BORE_WI_VOL` | `waterInjectionVol` |

Дата разбирается тремя способами: `DateTime` из ячейки, Excel-serial (`DateTime.FromOADate` для чисел `1 ≤ x < 100000`) или строка в `InvariantCulture`. Числа — из ячеек `double`/`decimal`/`int`, иначе строка `InvariantCulture`. Пустые и `DBNull` превращаются в `null`. Лист `Monthly Production Data` **не читается**: помесячные суммы считаются на лету из суточных (ADR-016).

**Шаг 2 — регистрация скважин.** По уникальным `NPD_WELL_BORE_CODE` публикуются события `well.registered` в порядке возрастания кода — до любых других событий, чтобы потребители успели завести строки в `wells`.

**Шаг 3 — траектории.** Для каждого ствола ищется файл `*_ACTUAL` в `WellWellbore/**`, у которого имя **родительского каталога** совпадает с именем ствола из Excel после замены `/` → `_` (`NormalizeWellName`). Из файла берутся:

* заголовок: `Bottom Hole MD:` → `tdMd`, `Bottom Hole TVD:` → `tdTvd` (суффикс `m`/`M` отбрасывается);
* таблица после строки `SURVEY LIST`: первые две строки — имена и единицы колонок, данные начинаются с третьей. Колонки: `MD`, `Inc`, `Azim`, `TVD`, `X-Offset`, `Y-Offset`, `UTM E/W`, `UTM N/S`, `DLS`. В событие попадают MD, TVD, зенитный угол, азимут и `UTM E/W` / `UTM N/S` (ED50 / UTM 31N). Строки, где первый столбец не число, пропускаются.

Публикуется одно событие `well.trajectory` на ствол. Если файл не найден или в нём нет секции `SURVEY LIST` — предупреждение в лог, событие не публикуется.

**Шаг 4 — добыча.** Для каждой разобранной строки Excel публикуется `production.daily` в порядке строк файла. Затем `producer.Flush(TimeSpan.FromSeconds(30))` и итоговая запись в лог: сколько строк прочитано, сколько пропущено из-за пустого ключа, сколько событий каждого типа опубликовано.

### 5.3 Координаты устьев

Координаты устьев парсер **не** извлекает: они лежат готовым списком в `sql/seed_well_locations.sql` (7 скважин: коды 5693, 7405, 5351, 5599, 5769, 7078, 7289). Значения получены из заголовков WITSML-выгрузок (`Surface Latitude/Longitude`, `Surface EW/NS`, датум «European 1950 - Mean», зона 31N) и пересчитаны в WGS84 по EPSG:1613 «ED50 to WGS 84 (24)» — тем же преобразованием, что и `CoordinateTransform.Ed50Utm31ToWgs84`. Скрипт идемпотентен (`ON CONFLICT (well_id) DO UPDATE`).

Траектории, наоборот, конвертируются «на лету»: в БД лежат ED50/UTM31N (`well_trajectories.easting/northing`), а `GET /api/wells/{npdWellBoreCode}/trajectory` отдаёт уже WGS84.

## 6. Единицы измерения

| Величина | Единица | Где зафиксирована |
| --- | --- | --- |
| Нефть, вода, газ, закачка (суточные) | м³/сут | комментарии `DailyProduction`, `DailyProductionEntity` |
| Помесячные суммы | м³ | `MonthlyProduction`, `MonthlyProductionDto`, листы отчётов |
| Время работы | часы | `ON_STREAM_HRS`, `on_stream_hrs_sum` |
| Давления (`downhole`, `tubing`, `annulus`, `WHP`) | бар | комментарии моделей |
| Температуры (`downhole`, `WHT`) | °C | комментарии моделей |
| Штуцер (`choke`) | % | комментарии моделей |
| MD, TVD, Easting, Northing | м | `TrajectoryPointDto`, `WellTrajectory` |
| Зенитный угол, азимут | градусы | `TrajectoryPointDto` |
| Широта/долгота | градусы WGS84 | `WellLocationEntity`, `TrajectoryPointDto` |
| Обводнённость, доля в добыче | % | `ReportMetrics.WaterCutPct`, `SharePct` |

## 7. Открытые вопросы

1. **Количество строк и колонок Excel.** Оценка «15 634 строки данных, 24 колонки» подтверждена прогоном ingestion (в логе «Прочитано строк: 15634»). Парсер обращается к 19 колонкам (остальные — коды месторождения/объекта, `AVG_CHOKE_UOM`, `FLOW_KIND` и подобные — не используются). Точный перечень 24 колонок файла — [УТОЧНИТЬ].
2. **Сопоставление траекторий по имени каталога.** Работает, если имя каталога ствола совпадает с `NPD_WELL_BORE_NAME` после замены `/` → `_`. Для стволов, где имена расходятся, траектория не загрузится (в лог попадёт предупреждение). Полный список таких случаев — [УТОЧНИТЬ].
3. **Тип скважины как мода.** При смешанных `WELL_TYPE` в строках одной скважины берётся самое частое значение; при равенстве порядок не определён (`OrderByDescending` без `ThenBy`). Корректно ли это для всех 7 стволов — [УТОЧНИТЬ].
4. **Координаты устьев** — статический список на 7 скважин. При добавлении скважин сид-скрипт нужно дополнять вручную; автоматического извлечения `Surface Latitude/Longitude` в коде нет — [УТОЧНИТЬ], нужно ли.
5. **Совпадение устья и первой точки траектории.** Устье берётся из `well_locations` (сид), а не из первого замера траектории. Совпадают ли они для всех стволов — [УТОЧНИТЬ].
6. **`days_count` в агрегате** — это `count()` строк `production_daily`, а не число суток с `on_stream_hrs > 0`. Если в датасете есть строки с нулевыми объёмами, «дней с данными» будет больше, чем «дней работы» — [УТОЧНИТЬ], ожидаемое ли это поведение.

Последняя сверка с кодом: 2026-11-10
