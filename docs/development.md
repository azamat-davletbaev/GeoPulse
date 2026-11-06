# Локальный запуск GeoPulse

Документ описывает запуск с чистой машины: что установить, как поднять стек, как загрузить данные и как прогнать тесты.

**Файлы-источники**

| Файл | Что взято |
| --- | --- |
| `GeoPulse.slnx` | состав solution |
| `src/*/*.csproj`, `tests/GeoPulse.Tests/GeoPulse.Tests.csproj` | target framework и версии пакетов |
| `docker-compose.yml` | локальный стек, порты, учётные данные |
| `docker-compose.prod.yml`, `.env.example` | прод-стек и переменные `.env` |
| `.config/dotnet-tools.json` | версия `dotnet-ef` |
| `.github/workflows/ci.yml` | версии .NET и Node, команды сборки/тестов |
| `src/*/appsettings*.json` | строки подключения и пути к данным по умолчанию |
| `tests/smoke/webapi-smoke.ps1`, `tests/frontend/run-webapp-check.ps1` | смок-тест API и проверка фронтенда |

## 1. Что нужно установить

| Инструмент | Версия | Зачем | Где зафиксирована |
| --- | --- | --- | --- |
| .NET SDK | `10.0.x` (все проекты — `net10.0`) | сборка, тесты, запуск сервисов вне контейнеров | `ci.yml` (`dotnet-version: '10.0.x'`), `TargetFramework` в каждом csproj |
| Docker Engine / Docker Desktop + `docker compose` | актуальная; в CI — предустановленный Docker на `ubuntu-latest` | инфраструктура (Postgres, Kafka, ClickHouse) и Testcontainers | `docker-compose*.yml`, `ci.yml`, `tests/GeoPulse.Tests/*.csproj` |
| Node.js | `20` | проверка фронтенда (`tests/frontend/webapp-check.js`); локально можно не ставить — скрипт гоняет Node в контейнере | `ci.yml` (`node-version: '20'`), `tests/frontend/run-webapp-check.ps1` (`node:20-alpine`) |
| `dotnet-ef` | `10.0.14` | миграции EF Core вручную | `.config/dotnet-tools.json` |

Версии NuGet-пакетов (из csproj): `Microsoft.EntityFrameworkCore.Relational` 10.0.14, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `Confluent.Kafka` 2.16.0, `ClickHouse.Client` 7.14.0, `ClosedXML` 0.105.1, `Swashbuckle.AspNetCore` 10.3.1, `ExcelDataReader` + `ExcelDataReader.DataSet` 3.9.0, `Microsoft.Extensions.Hosting` 10.0.14; тесты — `xunit` 2.9.3, `xunit.runner.visualstudio` 3.0.2, `Microsoft.NET.Test.Sdk` 17.12.0, `Microsoft.AspNetCore.TestHost` 10.0.14, `Testcontainers` + `.PostgreSql` + `.Kafka` + `.ClickHouse` 4.16.0.

`Makefile` в репозитории нет — все команды запускаются напрямую через `dotnet` и `docker compose`.

## 2. Структура solution

Solution-файл — `GeoPulse.slnx` (XML-формат, не `.sln`). Проекты:

| Проект | Тип | Назначение |
| --- | --- | --- |
| `src/GeoPulse.Core` | библиотека | доменные модели (`Models/`), события (`Events/`), преобразование координат (`Geometry/CoordinateTransform`), интерфейсы репозиториев и `IWellIdResolver`. Зависимостей от инфраструктуры нет. |
| `src/GeoPulse.Infrastructure` | библиотека | EF Core: `CoreDbContext`, сущности, репозитории, миграции, `AddGeoPulseInfrastructure`; резолв `npdWellBoreCode → well_id` (`WellIds/WellIdCache`). |
| `src/GeoPulse.Messaging` | библиотека | общий разбор сообщений (`EventClassifier`) и публикация в DLQ (`DlqPublisher`) для консьюмеров. |
| `src/GeoPulse.ClickHouse` | библиотека | DDL ClickHouse (`ClickHouseSchema`) и его идемпотентное создание. |
| `src/GeoPulse.Ingestion.Service` | консольное приложение | парсит Excel Volve и текстовые траектории, публикует события в Kafka. В БД не пишет. |
| `src/GeoPulse.Consumer` | консольное приложение (`BackgroundService`) | Kafka → PostgreSQL, применяет миграции на старте, ошибки в DLQ. |
| `src/GeoPulse.CHIngest` | консольное приложение (`BackgroundService`) | Kafka → ClickHouse (`gp.production_daily`, `gp.wells`). |
| `src/GeoPulse.Aggregator` | консольное приложение (`BackgroundService`) | ClickHouse `production_daily` → `monthly_production` по таймеру. |
| `src/GeoPulse.WebAPI` | ASP.NET Core Web API | REST API + Swagger + раздача статики `web/`. |
| `tests/GeoPulse.Tests` | xUnit | 103 теста: unit (`Unit/`), контрактные (`Contract/`), интеграционные на Testcontainers (`Integration/`). |

Прочие каталоги: `web/` — статический фронтенд (Leaflet + ECharts, локальные vendor-файлы), `data/` — датасет Volve (xlsx + `WellWellbore`), `sql/` — сид координат устьев, `scripts/` — подготовка сервера и бэкап, `tests/smoke/`, `tests/frontend/` — скрипты проверок, `docs/` — документация.

## 3. Переменные окружения (`.env`)

`docker-compose.yml` (локальный стек) **не читает `.env`** — учётные данные и порты прописаны в нём напрямую (`geopulse`/`geopulse`, порты 5432 / 8123 / 9000 / 9092 / 8080 / 5146). Файл `.env` нужен только для прод-стека (`docker-compose.prod.yml`) и деплоя.

Переменные из `.env.example`:

| Переменная | Значение в примере | Смысл |
| --- | --- | --- |
| `DOMAIN` | `geopulse-demo.duckdns.org` | домен без `https://` и без завершающего `/`; на него Caddy выпускает сертификат |
| `FALLBACK_IP` | `45.12.4.88` | публичный IP сервера для резервного входа по HTTP; если пусто — блок `http://{$FALLBACK_IP}` становится catch-all на порту 80 |
| `OWNER` | `azamat-davletbaev` | владелец образов в ghcr.io |
| `DOCKER_TAG` | `latest` | тег образов (`latest` или короткий SHA); `deploy.sh` обновляет его при релизе, вручную меняется для отката |
| `POSTGRES_DB` | `geopulse_core` | имя БД PostgreSQL |
| `POSTGRES_USER` | `geopulse` | пользователь PostgreSQL |
| `POSTGRES_PASSWORD` | (заменить) | пароль PostgreSQL; в прод-стеке обязателен (`${POSTGRES_PASSWORD:?...}`) |
| `CLICKHOUSE_DB` | `gp` | база ClickHouse |
| `CLICKHOUSE_USER` | `geopulse` | пользователь ClickHouse |
| `CLICKHOUSE_PASSWORD` | (заменить) | пароль ClickHouse; в прод-стеке обязателен |

`scripts/bootstrap-server.sh` дополнительно принимает `DEPLOY_PUBKEY`, `GHCR_USER`, `GHCR_TOKEN`, `GITHUB_REPO`, `REPO_SSH_URL`, `DEPLOY_KEY_FILE`, `APP_DIR`, `DEPLOY_USER`, `SSH_PORT`, `BRANCH` — только для подготовки сервера.

## 4. Пошаговый запуск с нуля

Все команды — из корня репозитория.

### Шаг 1. Клонировать репозиторий

```bash
git clone https://github.com/azamat-davletbaev/GeoPulse.git
cd GeoPulse
```

Проверить, что данные на месте: `data/Volve_Production_data/Volve production data.xlsx` и каталог `data/Volve_Well_technical_data/WellWellbore`. Без них ingestion залогирует «Excel-файл не найден» и завершится без ошибки.

### Шаг 2. Поднять инфраструктуру и сервисы

```bash
docker compose up -d --build
```

Что произойдёт: скачаются `postgres:17-alpine`, `clickhouse/clickhouse-server:24.8`, `apache/kafka:4.0.0`, `provectuslabs/kafka-ui:latest`; соберутся образы `consumer` и `webapi`; `consumer` дождётся healthy-состояния Postgres и на старте применит EF Core миграции.

Проверка:

```bash
docker compose ps
curl http://localhost:5146/health
# {"status":"ok","database":"up","checkedAtUtc":"..."}
```

Ожидаемые контейнеры: `geopulse-postgres`, `geopulse-clickhouse`, `geopulse-kafka`, `geopulse-kafka-ui`, `geopulse-consumer`, `geopulse-webapi`. **`chingest`, `aggregator`, `ingestion` и `caddy` в локальный compose не входят** — см. шаги 4–6.

### Шаг 3. Миграции (вручную, если нужно)

Автоматически: `Consumer` вызывает `db.Database.MigrateAsync()` при старте. Вручную:

```bash
dotnet tool restore
dotnet ef database update --project src/GeoPulse.Infrastructure
```

В проекте `GeoPulse.Infrastructure` есть `CoreDbContextFactory` (design-time фабрика), поэтому команда не требует отдельного startup-проекта. Строка подключения берётся из `ConnectionStrings__CoreDb`, иначе — `Host=localhost;Port=5432;Database=geopulse_core;Username=geopulse;Password=geopulse`.

### Шаг 4. Первичный импорт данных

Консьюмеры (`consumer`, `chingest`) можно запускать до импорта: топик `production-daily` создаётся при их старте (ADR-015), ошибка чтения из ещё не созданного топика больше не останавливает сервис.

Ingestion в compose нет, поэтому запускается с хоста (Kafka опубликована на `localhost:9092`):

```bash
dotnet run --project src/GeoPulse.Ingestion.Service
```

Проверено на текущем датасете — в логе:

```
Прочитано строк: 15634, из них с пустым ключом пропущено: 0
Публикация завершена: прочитано строк 15634, пропущено (пустой ключ) 0,
опубликовано well.registered 7, well.trajectory 7, production.daily 15634
```

Повторный запуск безопасен: `Consumer` отсекает дубли по `ON CONFLICT`, ClickHouse — через `ReplacingMergeTree`.

### Шаг 5. Заполнить ClickHouse (для аналитики и отчётов)

`CHIngest` и `Aggregator` в локальный compose не входят. Запустить в двух отдельных терминалах:

```bash
dotnet run --project src/GeoPulse.CHIngest
dotnet run --project src/GeoPulse.Aggregator
```

`CHIngest` возьмёт из `appsettings.json` строки `ClickHouse:ConnectionString` (`Host=localhost;Port=8123;Database=gp;Username=geopulse;Password=geopulse`) и `ConnectionStrings:CoreDb` — по ним он резолвит `npdWellBoreCode → well_id` из PostgreSQL, поэтому запускать его нужно **после** того, как `Consumer` записал скважины. `Aggregator` пересчитывает `gp.monthly_production` сразу на старте и далее каждые `RecalcIntervalSeconds` (60 с).

Проверка объёмов:

```bash
# PostgreSQL
docker compose exec -T postgres psql -U geopulse -d geopulse_core -c "select count(*) from daily_production"
# ClickHouse
docker compose exec -T clickhouse clickhouse-client --user geopulse --password geopulse --query "select count() from gp.monthly_production"
```

### Шаг 6. Загрузить координаты устьев (нужны для карты)

```bash
# bash / macOS / Linux / Git Bash
docker compose exec -T postgres psql -U geopulse -d geopulse_core < sql/seed_well_locations.sql
```

```powershell
# Windows PowerShell (< не поддерживается как редирект ввода)
Get-Content sql\seed_well_locations.sql -Raw | docker compose exec -T postgres psql -U geopulse -d geopulse_core
```

Скрипт идемпотентен (`ON CONFLICT (well_id) DO UPDATE`) — его можно запускать повторно.

### Шаг 7. Открыть приложение

| Адрес | Что там |
| --- | --- |
| <http://localhost:5146/> | карта скважин (`web/index.html`) |
| <http://localhost:5146/reports.html> | отчёты по месторождению |
| <http://localhost:5146/forecast.html> | страница прогноза (заготовка под ML-эндпоинты) |
| <http://localhost:5146/about.html> | «О проекте» |
| <http://localhost:5146/swagger> | Swagger UI |
| <http://localhost:8080> | Kafka UI (`kafka-ui`): топики `production-daily`, `production-daily-dlq` |

## 5. Как запустить тесты

```bash
# 1. Сборка всего solution
dotnet build GeoPulse.slnx -c Release

# 1a. Проверка стиля (то же, что в CI)
dotnet format GeoPulse.slnx --verify-no-changes --no-restore

# 2. Все тесты (нужен запущенный Docker: интеграционные поднимают контейнеры через Testcontainers)
dotnet test tests/GeoPulse.Tests/GeoPulse.Tests.csproj -c Release
```

В наборе 103 теста, разложенных по трём группам:

| Группа | Файлы | Тестов | Нужен Docker |
| --- | --- | --- | --- |
| `Unit` | `CoordinateTransformTests`, `EventClassificationTests`, `EventJsonSerializationTests`, `IngestionParsingTests`, `ReportDataLoaderTests`, `ReportMetricsTests`, `ReportWritersTests`, `TrajectoryParserTests` | 75 | нет |
| `Contract` | `AnalyticsContractValidationTests`, `ReportsContractTests`, `TrajectoryContractTests`, `WellsContractTests` | 13 | нет (in-memory `TestServer` + `FakeRepositories`) |
| `Integration` | `AggregatorServiceTests`, `AnalyticsContractDataTests`, `CHIngestIdempotencyTests`, `ConsumerIdempotencyTests`, `WellIdCacheTests` | 15 | да (`Testcontainers.PostgreSql` / `.Kafka` / `.ClickHouse`); помечены `[Trait("Category","Integration")]` |

Отдельные группы:

```bash
dotnet test tests/GeoPulse.Tests/GeoPulse.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~GeoPulse.Tests.Unit"
dotnet test tests/GeoPulse.Tests/GeoPulse.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~GeoPulse.Tests.Contract"
dotnet test tests/GeoPulse.Tests/GeoPulse.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~GeoPulse.Tests.Integration"
```

Проверка фронтенда (не xUnit-тест; стенд подменяет Leaflet, DOM и `fetch`):

```powershell
powershell -ExecutionPolicy Bypass -File tests/frontend/run-webapp-check.ps1
```

Скрипт прогоняет сценарии `mixed`, `empty`, `select`, `autoselect` в контейнере `node:20-alpine` (Node на хосте не нужен). В CI запускаются только `mixed` и `empty` (job `frontend-check`). Эквивалент одного сценария локально:

```bash
node tests/frontend/webapp-check.js mixed
```

Смок-тест REST API (требует поднятого стека с данными, включая ClickHouse):

```powershell
powershell -ExecutionPolicy Bypass -File tests/smoke/webapi-smoke.ps1 -BaseUrl http://localhost:5146
```

Он проверяет `/health`, список скважин, карточку, страницу суточной добычи (пагинация, фильтры, сортировка), выборку за дату, траекторию, коды 400/404, Swagger и обе выгрузки отчётов.

## 6. Полезные команды

```bash
# Логи сервисов
docker compose logs -f webapi
docker compose logs -f consumer

# Перезапуск одного сервиса
docker compose restart consumer

# PostgreSQL
docker compose exec -T postgres psql -U geopulse -d geopulse_core -c "\dt"
docker compose exec -T postgres psql -U geopulse -d geopulse_core -c "select count(*) from daily_production"

# ClickHouse
docker compose exec -T clickhouse clickhouse-client --user geopulse --password geopulse --query "show tables from gp"
docker compose exec -T clickhouse clickhouse-client --user geopulse --password geopulse --query "select count() from gp.monthly_production"

# Kafka: список топиков и содержимое DLQ
docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:29092 --list
docker compose exec -T kafka /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server localhost:29092 --topic production-daily-dlq --from-beginning --max-messages 5

# Полная остановка (данные в томах geopulse-pgdata / geopulse-chdata сохраняются)
docker compose down

# Остановка с удалением данных
docker compose down -v
```

Во всех примерах выше стоит флаг `-T`: без него `docker compose exec` пытается выделить TTY и в неинтерактивном скрипте ведёт себя нестабильно.

## 7. Открытые вопросы

1. **`docker compose up -d --build` собирает только `consumer` и `webapi`.** `chingest`, `aggregator` и `ingestion` в `docker-compose.yml` не описаны, поэтому «полный стек одной командой» локально недостижим — нужно три отдельных `dotnet run`. Добавлять ли их в локальный compose — [УТОЧНИТЬ].
2. **Порты локального стека** (5432, 8123, 9000, 9092, 8080, 5146) публикуются наружу. Если на машине занят любой из них, `docker compose up` упадёт — какой порт менять безопаснее (зависит от того, что ещё запущено) — [УТОЧНИТЬ].
3. **`dotnet ef database update`** опирается на design-time фабрику `CoreDbContextFactory` в `GeoPulse.Infrastructure`. Проверялась ли команда на чистой машине (и нужен ли `--startup-project` для конкретной версии CLI) — [УТОЧНИТЬ].
4. **Проверка фронтенда локально на Windows** требует запущенного Docker (образ `node:20-alpine`); вариант «Node на хосте» в документации не описан в скриптах — [УТОЧНИТЬ], нужен ли.
5. **Интеграционные тесты (8 шт.)** в этом окружении не прогонялись: они поднимают Postgres/Kafka/ClickHouse через Testcontainers и требуют времени на скачивание образов. Команда выше соответствует `ci.yml` — [УТОЧНИТЬ] результаты на чистой машине.
