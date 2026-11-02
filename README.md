# GeoPulse

Веб-система мониторинга добычи нефти: импорт суточных показателей скважин через событийный пайплайн (Apache Kafka), хранение в PostgreSQL и ClickHouse, выдача точечных данных и аналитики через REST API. Демонстрационный кейс построен на открытых данных месторождения **Volve** (норвежский шельф, Северное море).

**Стек:** .NET 10, ASP.NET Core Web API, PostgreSQL, ClickHouse, Apache Kafka, EF Core, Docker Compose.

## Данные проекта

Проект использует открытый набор данных Volve, опубликованный компанией Equinor (Норвегия) под **Equinor Open Data Licence**.

Данные **не хранятся в репозитории**: папка `data/` добавлена в `.gitignore`. Их нужно скачать самостоятельно с официальной страницы:

<https://www.equinor.com/energy/volve-data-sharing>

Полный набор — около 40 000 файлов общим объёмом более 4 ТБ; качать целиком не нужно. Достаточно архива **Production data**. Файл положить по пути:

```
data/Volve_Production_data/Volve production data.xlsx
```

Этот путь указан в `src/GeoPulse.Ingestion.Service/appsettings.json` (`Volve:ProductionWorkbook`). Остальные папки набора для текущих этапов не используются (WITSML-данные из `Volve_Well_technical_data` понадобятся на этапе 7).

## Архитектура

Пайплайн данных:

```
Excel ──▶ Ingestion.Service ──▶ Kafka (production-daily) ──┬─▶ GeoPulse.Consumer ──▶ PostgreSQL (точечные данные)
              парсер+издатель        лента событий          ├─▶ GeoPulse.CHIngest ──▶ ClickHouse gp (сырые события)
                                                            └─▶ production-daily-dlq (некорректные события)

ClickHouse gp.production_daily ──▶ GeoPulse.Aggregator (таймер 60 с) ──▶ ClickHouse gp.monthly_production
ClickHouse gp.monthly_production ──▶ GeoPulse.WebAPI (/api/Analytics/*)
```

| Проект | Назначение |
| --- | --- |
| `src/GeoPulse.Core` | Доменные модели (`Well`, `DailyProduction`) и модели событий (`Events/`). Зависимостей от инфраструктуры нет. |
| `src/GeoPulse.Infrastructure` | EF Core: `CoreDbContext`, сущности, конфигурация, миграции, репозитории. Таблицы `wells`, `daily_production` в схеме `public` (уникальный индекс `well_id + date`). Схема `analytics` (`analytics.monthly_production`) заморожена: это снимок помесячных агрегатов для сверок, код её не читает и не пишет. |
| `src/GeoPulse.Ingestion.Service` | Консольный парсер: Excel Volve → Kafka. В PostgreSQL не пишет. Публикует сначала события `well.registered` (по одному на скважину), затем `production.daily` на каждую строку добычи. Ключ сообщений — строка `npdWellBoreCode` (сохраняет порядок событий в пределах скважины). |
| `src/GeoPulse.Consumer` | Консольный BackgroundService: слушает топик `production-daily` (группа `geopulse-consumer`), применяет EF-миграции на старте, пишет события в PostgreSQL идемпотентно (`INSERT ... ON CONFLICT DO NOTHING`). Некорректные события (битый JSON, неизвестный `eventType`, неподдерживаемая `schemaVersion`, неизвестная скважина) отправляет в топик `production-daily-dlq` с оригинальным телом и заголовком `reason`. |
| `src/GeoPulse.CHIngest` | Консольный BackgroundService — второй читатель топика `production-daily` (группа `geopulse-chingest`). Пишет сырые события в ClickHouse (база `gp`, таблицы `wells` и `production_daily`, движок `ReplacingMergeTree`, чтение через `FINAL`): дубли топика дедуплицируются средствами движка, без дедупликации в памяти, запись — батчами по `Kafka:BatchSize`. Справочник `npdWellBoreCode → well_id` кэшируется на старте из PostgreSQL (`ConnectionStrings:CoreDb`). Некорректные события (битый JSON, неизвестный `eventType`, неподдерживаемая `schemaVersion`, неизвестная скважина) отправляет в `production-daily-dlq` с заголовком `reason`, как и Consumer (причины: у CHIngest неизвестная скважина — `unknown.well`, у Consumer — `well.not.found`). |
| `src/GeoPulse.WebAPI` | REST API + Swagger, порт `5146`. Точечные данные из PostgreSQL: список скважин, карточка скважины, суточная добыча с фильтром по датам, сортировкой и пагинацией, health-проверка. Аналитика — из ClickHouse через `ClickHouseAnalyticsService`: помесячные агрегаты одной скважины и суммарная добыча по всем скважинам одним SQL-запросом к `gp.monthly_production`. |
| `src/GeoPulse.Aggregator` | Консольный BackgroundService, Kafka не использует. На старте и далее по таймеру `RecalcIntervalSeconds` (по умолчанию 60 с) полностью пересобирает `gp.monthly_production` из `gp.production_daily FINAL`: `TRUNCATE` + `INSERT ... SELECT` с группировкой по `well_id`, году и месяцу. Состояния в памяти нет, поэтому повторные пересборки результат не меняют. Окно между `TRUNCATE` и вставкой — см. «Известные ограничения». |

### Схема событий (schemaVersion: 1)

Издатель — `Ingestion.Service`, потребители — `GeoPulse.Consumer` (PostgreSQL) и `GeoPulse.CHIngest` (ClickHouse). Оба проверяют заголовок события (тип и версию схемы) и отправляют некорректные сообщения в DLQ.

- `well.registered` — регистрация/апсерт скважины: `data: { npdWellBoreCode, name, field, type }`.
- `production.daily` — суточная добыча: `data: { npdWellBoreCode, date, onStreamHrs, avgDownholePressure, avgDownholeTemperature, avgDpTubing, avgAnnulusPressure, avgChokeSize, avgWhp, avgWht, dpChokeSize, oilVol, gasVol, waterVol, waterInjectionVol }`.

В событиях нет внутренних id из БД — только натуральный ключ `npdWellBoreCode`; читатели резолвят его в `well_id` (Consumer — при записи в PostgreSQL, CHIngest — через кэш скважин). Внутренний `wellId` в событиях появляться не должен.

Топики: `production-daily` (3 партиции; группы `geopulse-consumer` и `geopulse-chingest`), `production-daily-dlq` (1 партиция).

## Инфраструктура (docker compose)

Сервисы (имена контейнеров с префиксом `geopulse-`): `postgres` (`geopulse-postgres`, localhost:5432, БД `geopulse_core`), `kafka` (`geopulse-kafka`; внешне localhost:9092, внутри compose-сети `kafka:29092`; бинари CLI — в `/opt/kafka/bin/`, не в PATH), `kafka-ui` (`geopulse-kafka-ui`, <http://localhost:8080>), `clickhouse` (`geopulse-clickhouse`, HTTP 8123, база `gp`), `consumer` (`geopulse-consumer`, слушает `production-daily`), `webapi` (`geopulse-webapi`, порт 5146). Healthcheck объявлен у `postgres` и `clickhouse` — по нему `webapi` ждёт готовности баз; у `kafka`, `kafka-ui` и `consumer` healthcheck нет, они просто запускаются.

Строки подключения: `ConnectionStrings:CoreDb` — Consumer и CHIngest (PostgreSQL); `ConnectionStrings:GeoPulse` — WebAPI (PostgreSQL); `ConnectionStrings:ClickHouse` — WebAPI (ClickHouse); `ClickHouse:ConnectionString` — CHIngest и Aggregator (ClickHouse). База ClickHouse во всех строках — `gp`.

## Канонические имена

Значения, которые должны совпадать в compose, `appsettings*.json` и коде.

| Сущность | Значение |
| --- | --- |
| PostgreSQL: база | `geopulse_core` (пользователь/пароль `geopulse`/`geopulse`) |
| PostgreSQL: схема данных | `public` (`wells`, `daily_production`); схемы `data` нет |
| PostgreSQL: схема аналитики | `analytics` (`monthly_production`) — заморожена, справочный снимок |
| ClickHouse: база | `gp` (`wells`, `production_daily`, `monthly_production`); SQL — с полным именем `gp.<таблица>` |
| Kafka: топики | `production-daily` (3 партиции), `production-daily-dlq` (1 партиция) |
| Kafka: группы консьюмеров | `geopulse-consumer` (Consumer), `geopulse-chingest` (CHIngest) |
| Контейнеры | `geopulse-postgres`, `geopulse-clickhouse`, `geopulse-kafka`, `geopulse-kafka-ui`, `geopulse-consumer`, `geopulse-webapi` |
| Порты | PostgreSQL `5432`, ClickHouse HTTP `8123` (нативный `9000`), Kafka `9092` снаружи / `29092` внутри compose, WebAPI `5146`, kafka-ui `8080` |
| Схема событий | `schemaVersion: 1`; `eventType`: `well.registered`, `production.daily` |

## Запуск

Из корня репозитория.

1. Поднять контейнеры:

   ```bash
   docker compose up -d
   ```

2. При первом старте (или если топиков нет) — создать топики:

   ```bash
   docker exec geopulse-kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --create --topic production-daily --partitions 3 --replication-factor 1
   docker exec geopulse-kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --create --topic production-daily-dlq --partitions 1 --replication-factor 1
   ```

3. Запустить консьюмер PostgreSQL (он применяет EF-миграции и начинает слушать топик):

   ```bash
   dotnet run --project src/GeoPulse.Consumer
   ```

4. Запустить CHIngest — второй читатель того же топика (создаёт базу `gp` и таблицы ClickHouse):

   ```bash
   dotnet run --project src/GeoPulse.CHIngest
   ```

5. Запустить импорт (публикует события в Kafka; Excel-файл должен быть уже скачан — см. «Данные проекта»):

   ```bash
   dotnet run --project src/GeoPulse.Ingestion.Service
   ```

6. Запустить агрегатор (пересобирает `gp.monthly_production` на старте и далее каждые 60 с):

   ```bash
   dotnet run --project src/GeoPulse.Aggregator
   ```

7. Проверка: <http://localhost:5146/swagger> (`GET /api/wells` — список скважин, `GET /api/wells/5351/production?pageSize=20` — суточная добыча, `GET /health` — состояние API и БД, `GET /api/Analytics/summary` — суммарная добыча из ClickHouse), kafka-ui: <http://localhost:8080>. Данные в ClickHouse:

   ```bash
   docker exec geopulse-clickhouse clickhouse-client --query "SELECT count() FROM gp.production_daily FINAL"
   docker exec geopulse-clickhouse clickhouse-client --query "SELECT count() FROM gp.monthly_production"
   ```

   Полная проверка API — смок-тест из раздела «Смок-тест API».

Порядок важен: сначала оба читателя (Consumer, CHIngest), затем Ingestion; агрегатор можно запускать в любой момент — он пересчитывает агрегаты по таймеру. Повторный запуск Ingestion безопасен: события уже в базах, консьюмер покажет «вставлено 0, пропущено N», а ClickHouse дедуплицирует дубли средствами `ReplacingMergeTree` (чтение через `FINAL`). Данные между Kafka и хранилищами — at-least-once; идемпотентность записи в PostgreSQL и движок ClickHouse компенсируют возможные дубликаты. WebAPI в compose поднимается автоматически; для локального запуска — `dotnet run --project src/GeoPulse.WebAPI` (порт 5146).

## Смок-тест полного цикла

После полного прогона: в `daily_production` — 15 634 записи без дублей (`COUNT(DISTINCT (well_id, date))` = 15 634), в `wells` — 7 скважин, отставание групп консьюмеров (`geopulse-consumer`, `geopulse-chingest`) — 0 по всем партициям, DLQ пуста. Проверка API на этих же данных — смок-тест из раздела «Смок-тест API».

Контрольные числа этапа 4:

| Проверка | Значение |
| --- | --- |
| `SELECT count() FROM gp.production_daily FINAL` | 15 634 |
| `SELECT count() FROM gp.wells FINAL` | 7 |
| `SELECT count() FROM gp.monthly_production` | 526 |
| `GET /api/Analytics/summary` → `totalOilVol` | ≈ 10 037 080.61 |
| Смок-тест API | 34 проверки из 34 |

## REST API: точечные данные и аналитика

API отдаёт точечные данные из PostgreSQL, а аналитику — из ClickHouse (`gp.monthly_production` через `ClickHouseAnalyticsService`). Адресация ресурсов — по натуральному ключу `npdWellBoreCode`; внутренние id из БД (`wells.id`, `daily_production.id`) наружу не публикуются.

| Метод и путь | Назначение |
| --- | --- |
| `GET /health` | Состояние API и подключения к PostgreSQL: `200` + `status=ok`, либо `503` + `status=degraded`, `database=down` |
| `GET /api/wells` | Список скважин |
| `GET /api/wells/{npdWellBoreCode}` | Карточка скважины и покрытие данными: `firstProductionDate`, `lastProductionDate`, `productionDays` |
| `GET /api/wells/{npdWellBoreCode}/production` | Суточная добыча скважины: диапазон дат, сортировка, пагинация |
| `GET /api/wells/{npdWellBoreCode}/production/{date}` | Одна запись за дату (`yyyy-MM-dd`) |
| `GET /api/Analytics/monthly?wellCode={code}` | Помесячные агрегаты одной скважины (`wellCode` — это `npdWellBoreCode`) из `gp.monthly_production` (ClickHouse) |
| `GET /api/Analytics/summary` | Суммарная добыча по всем скважинам за всё время: один SQL-запрос к `gp.monthly_production` (ClickHouse) |

Параметры выборки суточной добычи:

| Параметр | По умолчанию | Описание |
| --- | --- | --- |
| `from` | — | Начало диапазона дат включительно, `yyyy-MM-dd` |
| `to` | — | Конец диапазона дат включительно, `yyyy-MM-dd` |
| `page` | `1` | Номер страницы, не меньше 1 |
| `pageSize` | `100` | Размер страницы, 1..500 |
| `sort` | `desc` | Порядок по дате: `desc` — сначала новые, `asc` — по возрастанию |

Страница отдаётся конвертом: `items`, `totalCount`, `page`, `pageSize`, `totalPages`, `hasNextPage`, `hasPreviousPage`. Сортировка и `OFFSET/LIMIT` выполняются в SQL, `totalCount` считается отдельным запросом.

Ошибки — в формате ProblemDetails (`application/problem+json`): `400` — несогласованные или недопустимые параметры (`from` больше `to`, `pageSize` вне 1..500, неизвестный `sort`, неверная дата), `404` — нет скважины или записи за дату, `500` — необработанное исключение.

```bash
curl http://localhost:5146/health
curl http://localhost:5146/api/wells
curl http://localhost:5146/api/wells/5351
curl "http://localhost:5146/api/wells/5351/production?from=2016-01-01&to=2016-12-31&pageSize=50&sort=asc"
curl http://localhost:5146/api/wells/5351/production/2016-01-01
curl "http://localhost:5146/api/Analytics/monthly?wellCode=5351"
curl http://localhost:5146/api/Analytics/summary
```

## Смок-тест API

Повторяемая проверка API на заполненной базе: health, список и карточка скважины, пагинация, фильтр по датам, обе сортировки, запись за дату, ответы `400`/`404`, доступность Swagger.

```powershell
powershell -ExecutionPolicy Bypass -File tests/smoke/webapi-smoke.ps1 -BaseUrl http://localhost:5146
```

Скрипт печатает результат каждой проверки и завершается с кодом `1`, если есть проваленные проверки; успешный прогон — 34 проверки и сообщение «Все проверки пройдены: 34». `-ExecutionPolicy Bypass` нужен, если в системе запрещено выполнение локальных скриптов.

## Известные ограничения

- **Окно `TRUNCATE` → `INSERT` при пересборе `gp.monthly_production`.** Агрегатор сначала очищает таблицу, затем вставляет пересчитанные строки, поэтому в этот момент читатель может получить частичный агрегат (пустую или неполную таблицу). На объёме Volve окно занимает доли секунды; решение на будущее — атомарная подмена таблицы (`RENAME`/`EXCHANGE TABLES` на заранее заполненную копию).
- **ClickHouse не самодостаточен: справочник скважин резолвится через PostgreSQL.** CHIngest строит кэш `npdWellBoreCode → well_id` по таблице `wells` в PostgreSQL на старте; при расхождении справочников событие уходит в DLQ как `unknown.well`.
- **Kafka без volume.** У брокера нет persistent volume: после `docker compose down -v` топики и офсеты групп пропадают. Их нужно создавать заново (шаг 2 раздела «Запуск»), а читатели начнут с начала потока.
- **Aggregator и CHIngest запускаются локально** (`dotnet run`), в compose их нет — контейнеризованы только `consumer` и `webapi`. Поэтому в их `appsettings.json` ClickHouse и PostgreSQL указаны как `localhost`. При контейнеризации понадобятся собственные переменные окружения (`ClickHouse__ConnectionString` с `Host=clickhouse`, `ConnectionStrings__CoreDb` с `Host=postgres`) и `depends_on` на готовность `postgres`/`clickhouse`.

## Этапы

| Этап | Статус | Итог |
| --- | --- | --- |
| 1. Каркас и модель данных | закрыт | Решение из 7 проектов в `src/`, доменные модели, схема PostgreSQL через EF Core, миграции. |
| 2. Пайплайн Kafka | закрыт | Ingestion публикует `well.registered` и `production.daily`; Consumer идемпотентно пишет в PostgreSQL; DLQ проверена. |
| 3. REST API | закрыт | Список и карточка скважины, суточная добыча с фильтрами, сортировкой и пагинацией, health, ProblemDetails, Swagger. |
| 4. Аналитика на ClickHouse | закрыт | CHIngest (сырые события в `gp`, `ReplacingMergeTree`, чтение `FINAL`) и Aggregator (пересборка `gp.monthly_production` по таймеру), `/api/Analytics/*` из ClickHouse. Контрольные числа: 15 634 строки добычи, 7 скважин, 526 агрегатов, `totalOilVol` ≈ 10 037 080.61. |
| 5. Веб-клиент с картой | план | Карта месторождения, скважины и их показатели, графики добычи поверх текущего API. |
| 6. Отчёты | план | Отчёты по добыче: период, скважина, сводные таблицы, выгрузка. |
| 7. WITSML-данные | план | Траектории, перфорации и пики из `Volve_Well_technical_data`. |
| 8. Деплой на VPS | план | Развёртывание стека на удалённом сервере. |

## Лицензия данных

Данные Volve используются в соответствии с **Equinor Open Data Licence**. Текст лицензии — в наборе данных: `data/Volve_Production_data/license.txt`. На код репозитория лицензия данных не распространяется.