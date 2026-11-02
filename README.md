# GeoPulse

Веб-система мониторинга добычи нефти: импорт суточных показателей скважин через событийный пайплайн (Apache Kafka), хранение в PostgreSQL и выдача через REST API. Демонстрационный кейс построен на открытых данных месторождения **Volve** (норвежский шельф, Северное море).

**Стек:** .NET 10, ASP.NET Core Web API, PostgreSQL, Apache Kafka, EF Core, Docker Compose.

## Данные проекта

Проект использует открытый набор данных Volve, опубликованный компанией Equinor (Норвегия) под **Equinor Open Data Licence**.

Данные **не хранятся в репозитории**: папка `data/` добавлена в `.gitignore`. Их нужно скачать самостоятельно с официальной страницы:

<https://www.equinor.com/energy/volve-data-sharing>

Полный набор — около 40 000 файлов общим объёмом более 4 ТБ; качать целиком не нужно. Достаточно архива **Production data**. Файл положить по пути:

```
data/Volve_Production_data/Volve production data.xlsx
```

Этот путь указан в `src/GeoPulse.Ingestion.Service/appsettings.json` (`Volve:ProductionWorkbook`). Остальные папки набора для текущих этапов не используются.

## Архитектура

Пайплайн данных:

```
Excel ──▶ Ingestion.Service ──▶ Kafka (production-daily) ──▶ GeoPulse.Consumer ──▶ PostgreSQL
              парсер+издатель        лента событий              читатель+писатель
```

| Проект | Назначение |
| --- | --- |
| `src/GeoPulse.Core` | Доменные модели (`Well`, `DailyProduction`) и модели событий (`Events/`). Зависимостей от инфраструктуры нет. |
| `src/GeoPulse.Infrastructure` | EF Core: `CoreDbContext`, сущности, конфигурация, миграции, репозитории. Таблицы `wells` и `daily_production` (уникальный индекс `well_id + date`). |
| `src/GeoPulse.Ingestion.Service` | Консольный парсер: Excel Volve → Kafka. В PostgreSQL не пишет. Публикует сначала события `well.registered` (по одному на скважину), затем `production.daily` на каждую строку добычи. Ключ сообщений — строка `npdWellBoreCode` (сохраняет порядок событий в пределах скважины). |
| `src/GeoPulse.Consumer` | Консольный BackgroundService: слушает топик `production-daily` (группа `geopulse-consumer`), применяет EF-миграции на старте, пишет события в PostgreSQL идемпотентно (`INSERT ... ON CONFLICT DO NOTHING`). Некорректные события (битый JSON, неизвестный `eventType`, неподдерживаемая `schemaVersion`, неизвестная скважина) отправляет в топик `production-daily-dlq` с оригинальным телом и заголовком `reason`. |
| `src/GeoPulse.WebAPI` | REST API + Swagger, порт `5146`. |

### Схема событий (schemaVersion: 1)

Издатель — `Ingestion.Service`, потребители — `GeoPulse.Consumer` (и будущие, например ClickHouse-инжест).

- `well.registered` — регистрация/апсерт скважины: `data: { npdWellBoreCode, name, field, type }`.
- `production.daily` — суточная добыча: `data: { npdWellBoreCode, date, onStreamHrs, avgDownholePressure, avgDownholeTemperature, avgDpTubing, avgAnnulusPressure, avgChokeSize, avgWhp, avgWht, dpChokeSize, oilVol, gasVol, waterVol, waterInjectionVol }`.

В событиях нет внутренних id из БД — только натуральный ключ `npdWellBoreCode`; консьюмер резолвит его в `well_id`. Внутренний `wellId` в событиях появляться не должен.

Топики: `production-daily` (3 партиции), `production-daily-dlq` (1 партиция).

## Инфраструктура (docker compose)

Сервисы (имена контейнеров с префиксом `geopulse-`): `postgres` (localhost:5432, БД `geopulse_core`), `kafka` (внешне localhost:9092, внутри compose-сети `kafka:29092`; бинари CLI — в `/opt/kafka/bin/`, не в PATH), `kafka-ui` (<http://localhost:8080>), `webapi` (порт 5146), `clickhouse` (описан, на текущем этапе не используется).

Строки подключения: `ConnectionStrings:CoreDb` — Consumer; `ConnectionStrings:GeoPulse` — WebAPI.

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

3. Запустить консьюмер (он применяет миграции и начинает слушать топик):

   ```bash
   dotnet run --project src/GeoPulse.Consumer
   ```

4. Запустить импорт (публикует события в Kafka):

   ```bash
   dotnet run --project src/GeoPulse.Ingestion.Service
   ```

5. Проверка: <http://localhost:5146/swagger> (`GET /api/Wells` — список скважин; `GET /api/Production/wells` — пока заглушка) и kafka-ui: <http://localhost:8080>.

Порядок важен: сначала Consumer, затем Ingestion. Повторный запуск Ingestion безопасен: события уже в базе, консьюмер покажет «вставлено 0, пропущено N». Данные между Kafka и PostgreSQL — at-least-once, идемпотентность записи компенсирует возможные дубликаты.

## Смок-тест полного цикла

После полного прогона: в `daily_production` — 15 634 записи без дублей (`COUNT(DISTINCT (well_id, date))` = 15 634), в `wells` — 7 скважин, отставание группы консьюмера (lag) — 0 по всем партициям, DLQ пуста.

## Лицензия данных

Данные Volve используются в соответствии с **Equinor Open Data Licence**. Текст лицензии — в наборе данных: `data/Volve_Production_data/license.txt`. На код репозитория лицензия данных не распространяется.