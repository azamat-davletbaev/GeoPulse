# GeoPulse

Веб-система мониторинга добычи нефти: импорт суточных показателей скважин, хранение в PostgreSQL и выдача через REST API. Демонстрационный кейс построен на открытых данных месторождения **Volve** (норвежский шельф, Северное море).

**Стек:** .NET 10, ASP.NET Core Web API, PostgreSQL, EF Core, Docker Compose, Apache Kafka (в разработке).

## Данные проекта

Проект использует открытый набор данных Volve, опубликованный компанией Equinor (Норвегия) под **Equinor Open Data Licence**.

Данные **не хранятся в репозитории**: папка `data/` добавлена в `.gitignore`. Их нужно скачать самостоятельно с официальной страницы:

<https://www.equinor.com/energy/volve-data-sharing>

Полный набор — около 40 000 файлов общим объёмом в несколько терабайт (более 4 ТБ), качать его целиком не нужно. Для проекта достаточно архива с данными добычи (**Production data**).

После скачивания положить файл `Volve production data.xlsx` по пути (относительно корня репозитория):

```
data/Volve_Production_data/Volve production data.xlsx
```

Именно этот путь указан в `src/GeoPulse.Ingestion.Service/appsettings.json` (`Volve:ProductionWorkbook`), поэтому переименовывать файл и менять структуру папок не требуется.

Остальные папки набора (`Volve_Well_technical_data` и пр.) для текущего этапа не используются — они пригодятся в будущих этапах (траектории стволов, суточные отчёты бурения, данные по колоннам и т. д.).

## Архитектура

| Проект | Назначение |
| --- | --- |
| `src/GeoPulse.Core` | Доменные модели (`Well`, `DailyProduction`) и интерфейсы репозиториев. Зависимостей от инфраструктуры нет. |
| `src/GeoPulse.Infrastructure` | EF Core: `CoreDbContext`, сущности БД, конфигурация подключения, миграции, реализации репозиториев. Таблицы `wells` и `daily_production` (уникальный индекс `well_id + date`). |
| `src/GeoPulse.Ingestion.Service` | Консольный парсер: Excel-файл Volve → PostgreSQL. Применяет миграции, затем пакетно вставляет данные через `INSERT ... ON CONFLICT (well_id, date) DO NOTHING`. |
| `src/GeoPulse.WebAPI` | REST API + Swagger, порт `5146`. |

Строка подключения: `ConnectionStrings:CoreDb` для сервиса импорта, `ConnectionStrings:GeoPulse` для WebAPI; по умолчанию обе указывают на `localhost:5432`, БД `geopulse_core`, пользователь `geopulse`.

## Запуск

Выполнять из корня репозитория.

1. Поднять контейнеры:

   ```bash
   docker compose up -d
   ```

   Поднимаются PostgreSQL и `webapi` (порт `5146`); в compose также описаны ClickHouse, Kafka и Kafka UI — они относятся к будущим этапам и приложением пока не используются.

2. Применить миграции и запустить парсер:

   ```bash
   dotnet run --project src/GeoPulse.Ingestion.Service
   ```

   Сервис сам применяет миграции EF Core, загружает скважины и дневную добычу в PostgreSQL. Повторный запуск безопасен: вставка идемпотентная, уже существующие записи (`well_id` + `date`) пропускаются.

3. Открыть API: <http://localhost:5146/swagger>

   `GET /api/Wells` возвращает список скважин из базы.
   (`GET /api/Production/wells` пока возвращает заглушку с тестовыми данными.)

## Лицензия данных

Данные Volve используются в соответствии с **Equinor Open Data Licence**. Текст лицензии входит в сам набор данных — файл `data/Volve_Production_data/license.txt` (упоминает Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International); там же указаны владельцы данных и требование атрибуции при распространении.

На код репозитория лицензия данных не распространяется: исходный код GeoPulse распространяется по обычной лицензии проекта.
