# Эксплуатация GeoPulse (runbook)

Документ для работы на сервере: шпаргалка команд, деплой, откат, диагностика, бэкапы.

**Файлы-источники**

| Файл | Что взято |
| --- | --- |
| `deploy.sh` | все шаги деплоя, health-check, механизм отката |
| `docker-compose.prod.yml` | имена контейнеров, переменные, healthcheck'и |
| `Caddyfile` | домены входа и проксирование |
| `.github/workflows/deploy.yml` | автоматический деплой по SSH, теги образов |
| `.env.example` | обязательные переменные |
| `scripts/bootstrap-server.sh` | подготовка сервера, UFW, `.env`, первый деплой |
| `scripts/backup-pg.sh` | дамп и ротация |
| `DEPLOY.md` | ручные шаги, восстановление из дампа |
| `src/GeoPulse.WebAPI/Controllers/HealthController.cs` | контракт `/health` |

## 1. Шпаргалка команд на сервере

Каталог проекта — `/opt/geopulse` (переопределяется `DEPLOY_DIR`). Пользователь деплоя — `deploy` (`sudo -u deploy ...`). Compose-файл прода — `docker-compose.prod.yml`.

```bash
cd /opt/geopulse
COMPOSE="docker compose -f docker-compose.prod.yml"
```

**Статус контейнеров**

```bash
$COMPOSE ps
# статус здоровья каждого контейнера одной строкой
for c in geopulse-postgres geopulse-kafka geopulse-clickhouse geopulse-consumer \
         geopulse-chingest geopulse-aggregator geopulse-webapi geopulse-caddy; do
  printf '%-24s %s\n' "$c" \
    "$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' $c 2>/dev/null || echo missing)"
done
```

Ожидаемые имена: `geopulse-postgres`, `geopulse-kafka`, `geopulse-clickhouse`, `geopulse-consumer`, `geopulse-chingest`, `geopulse-aggregator`, `geopulse-webapi`, `geopulse-caddy`. Контейнер `geopulse-ingestion` одноразовый — в постоянном составе его быть не должно.

**Логи конкретного сервиса**

```bash
$COMPOSE logs -f --tail 100 webapi
$COMPOSE logs --tail 200 consumer
$COMPOSE logs --tail 200 chingest
$COMPOSE logs --tail 200 aggregator
$COMPOSE logs --tail 200 caddy
```

**Перезапуск сервиса**

```bash
$COMPOSE restart webapi          # мягкий рестарт
$COMPOSE up -d --force-recreate webapi   # пересоздать контейнер на текущем образе
```

**Проверка health**

```bash
# изнутри compose-сети (webapi наружу порт не публикует)
$COMPOSE exec -T caddy wget -q -O- http://webapi:5146/health
# через Caddy по домену (TLS)
curl -fsS https://<DOMAIN>/health
# резервный вход по IP
curl -fsS "http://<FALLBACK_IP>/health"
```

Ответ: `{"status":"ok","database":"up","checkedAtUtc":"..."}` — код 200. Если БД недоступна — код 503 и `"database":"down"`, `"status":"degraded"`. Проверяется **только PostgreSQL** (ClickHouse/Kafka в `/health` не участвуют).

**Kafka**

```bash
$COMPOSE exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:29092 --list
$COMPOSE exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:29092 --describe --topic production-daily
# посмотреть, что попало в DLQ (первые 5 сообщений)
$COMPOSE exec -T kafka /opt/kafka/bin/kafka-console-consumer.sh \
  --bootstrap-server localhost:29092 --topic production-daily-dlq --from-beginning --max-messages 5
# группы потребителей и лаг
$COMPOSE exec -T kafka /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:29092 --describe --all-groups
```

**ClickHouse**

```bash
$COMPOSE exec -T clickhouse clickhouse-client --user "$CLICKHOUSE_USER" --password "$CLICKHOUSE_PASSWORD" \
  --query "select count() from gp.production_daily"
$COMPOSE exec -T clickhouse clickhouse-client --user "$CLICKHOUSE_USER" --password "$CLICKHOUSE_PASSWORD" \
  --query "select count() from gp.monthly_production"
```

**PostgreSQL**

```bash
$COMPOSE exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "select count(*) from wells; select count(*) from daily_production;"
$COMPOSE exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c 'select "MigrationId" from "__EFMigrationsHistory" order by 1'
```

Переменные `POSTGRES_USER`, `POSTGRES_DB`, `CLICKHOUSE_USER`, `CLICKHOUSE_PASSWORD` берутся из `/opt/geopulse/.env` — подставьте значения или выполните `set -a; . ./.env; set +a` в текущей сессии.

## 2. Деплой

### 2.1 Как запускается автоматически

1. Пуш в `main` → workflow `.github/workflows/deploy.yml` (имя `CD — build & push to ghcr`).
2. Job `build-and-push` собирает 5 образов (`webapi`, `consumer`, `chingest`, `aggregator`, `ingestion`) и публикует в ghcr.io с двумя тегами: короткий SHA (`${GITHUB_SHA::7}`) и `latest`.
3. Job `deploy` (только для `main`, с `concurrency: production-deploy`) по SSH (`appleboy/ssh-action@v1.2.0`) выполняет на сервере:

```bash
export IMAGE_TAG="<короткий SHA>"
cd /opt/geopulse
bash ./deploy.sh
```

Секреты репозитория: `SSH_HOST`, `SSH_USER`, `SSH_PRIVATE_KEY` (порт в workflow жёстко `22`, таймаут 20 минут, `script_stop: true`).

Параллельно workflow `.github/workflows/ci.yml` (`CI`) на каждый push/PR в `main` собирает solution, гоняет `dotnet test` и проверку фронтенда (`node tests/frontend/webapp-check.js mixed` и `... empty`).

### 2.2 Что делает `deploy.sh` (10 шагов)

| Шаг | Действие |
| --- | --- |
| 1 | Предусловия: каталог `${DEPLOY_DIR:-/opt/geopulse}`, наличие `docker-compose.prod.yml`, `.env`, `.git`, `docker` и `docker compose`, непустой `DOMAIN` в `.env` |
| 2 | Нормализует `IMAGE_TAG` (40-символьный SHA → 7 символов) и записывает в `.env` как `DOCKER_TAG`; предыдущее значение запоминает для отката |
| 3 | `git pull --ff-only origin main`; при локальных правках/конфликте — падение с подсказкой (`git status`, `git reset --hard origin/main`); `.env` не затрагивается |
| 4 | `docker compose pull` — тянет образы нового тега |
| 5 | `up -d postgres kafka clickhouse`, ждёт healthy/running (postgres, clickhouse — до 120 с; Kafka — до 120 с) |
| 6 | Миграции EF Core **до** старта сервисов: если в `src/GeoPulse.Infrastructure/Migrations` миграций больше, чем записей в `__EFMigrationsHistory`, поднимается одноразовый контейнер `geopulse-migrator` на образе `consumer` (он применяет миграции при старте), скрипт ждёт нужного количества записей и удаляет контейнер |
| 7 | Идемпотентно создаёт топики `production-daily` и `production-daily-dlq` (`--if-not-exists`, `--partitions 3`, `--replication-factor 1`) и печатает список топиков |
| 8 | `docker compose up -d --remove-orphans` — поднимает/пересоздаёт сервисы на новом теге |
| 9 | Health-check `GET /health` до 60 с (30 попыток по 2 с): сначала через `caddy wget http://webapi:5146/health`, при неудаче — временный контейнер `curlimages/curl` в сети `geopulse-webapi`. Затем проверяет, что все 8 контейнеров `healthy`/`running` |
| 10 | `docker image prune -f` (только висячие образы; старые теги остаются для отката) и печатает сводку: SHA, коммит, тег, предыдущий тег, домен, статусы контейнеров |

Повторный запуск безопасен: `pull`/`up` идемпотентны, топики создаются с `--if-not-exists`, миграции пропускаются, если уже применены.

Параметры запуска (переменные окружения): `DEPLOY_DIR`, `IMAGE_TAG`, `BRANCH`, `COMPOSE_FILE`, `KAFKA_ATTEMPTS`, `MIGRATION_ATTEMPTS`, `HEALTH_ATTEMPTS`, `MIGRATOR_NAME`, `TOPIC_PARTITIONS`, `PG_CONTAINER`.

### 2.3 Как запустить вручную

```bash
cd /opt/geopulse
sudo -u deploy IMAGE_TAG=latest bash deploy.sh     # или IMAGE_TAG=<короткий SHA>
```

### 2.4 Как откатиться

Старые образы **не удаляются** (шаг 10 чистит только висячие), поэтому откат — это смена тега в `.env` и повторный `up`. Именно эту последовательность печатает `deploy.sh` при любом ненулевом коде возврата (`rollback_hint`):

```bash
cd /opt/geopulse
sed -i 's/^DOCKER_TAG=.*/DOCKER_TAG=<предыдущий короткий SHA>/' .env
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d --remove-orphans
curl -fsS https://<DOMAIN>/health   # или: docker compose -f docker-compose.prod.yml exec -T caddy wget -qO- http://webapi:5146/health
```

Предыдущий тег печатается в сводке деплоя (`пред. тег: <sha> (для отката)`) и в подсказке при провале. Миграции при откате **не откатываются** — схема БД остаётся на новой версии. Если релиз содержал несовместимую миграцию, откат кода недостаточен: см. раздел 4 (бэкап).

Альтернатива без правки `.env` — зафиксировать тег на один запуск: `IMAGE_TAG=<sha> bash deploy.sh` (скрипт всё равно перепишет `DOCKER_TAG`).

## 3. Диагностика типовых проблем

### 3.1 Сервис не поднялся после деплоя

Признак: `deploy.sh` завершился с кодом 1 и напечатал блок «ОТКАТ». Скрипт останавливается на health-check (шаг 9) или на проверке статусов контейнеров.

```bash
cd /opt/geopulse
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs --tail 100 webapi
docker inspect -f '{{.State.Status}} / {{if .State.Health}}{{.State.Health.Status}}{{else}}no-healthcheck{{end}}' geopulse-webapi
```

Что смотреть по порядку:

| Симптом | Вероятная причина | Действие |
| --- | --- | --- |
| `geopulse-webapi` в `unhealthy`/`restarting`, в логах ошибки подключения к `postgres` | Postgres не поднялся или неверный `POSTGRES_PASSWORD` в `.env` | `logs postgres`, сверить `.env` и `docker-compose.prod.yml` |
| `webapi` падает с ошибкой «Не задана ConnectionStrings:ClickHouse» | не передана переменная `CLICKHOUSE_PASSWORD` | проверить `.env` (compose подставляет `${CLICKHOUSE_PASSWORD}`) |
| `consumer` падает на миграциях | шаг 6 не смог применить миграции либо БД недоступна | `logs consumer`, затем `select "MigrationId" from "__EFMigrationsHistory"` |
| контейнеры запущены, но `/health` отдаёт 503 | PostgreSQL недоступен приложению | `docker compose exec -T postgres pg_isready -U <POSTGRES_USER> -d <POSTGRES_DB>` |
| шаг 3 упал на `git pull --ff-only` | локальные правки в `/opt/geopulse` | `git -C /opt/geopulse status`; если правки не нужны — `git -C /opt/geopulse reset --hard origin/main` |

После устранения причины повторить деплой; если нужно быстро вернуть прошлую версию — раздел 2.4.

### 3.2 Нет данных на сайте

Порядок проверки — от источника к экрану.

```bash
# 1. Данные в PostgreSQL (точечные страницы /api/wells, /api/wells/{code}/production)
docker compose -f docker-compose.prod.yml exec -T postgres \
  psql -U <POSTGRES_USER> -d <POSTGRES_DB> -c "select count(*) from daily_production; select count(*) from wells;"

# 2. Данные в ClickHouse (графики и отчёты)
docker compose -f docker-compose.prod.yml exec -T clickhouse clickhouse-client \
  --user <CLICKHOUSE_USER> --password <CLICKHOUSE_PASSWORD> -q "select count() from gp.monthly_production"

# 3. API
curl -fsS https://<DOMAIN>/api/Analytics/summary
```

| Что видно | Причина | Действие |
| --- | --- | --- |
| `wells` и `daily_production` пусты | ingestion ни разу не запускался | `docker compose -f docker-compose.prod.yml run --rm ingestion` (профиль `init`, в постоянный состав не входит) |
| `geopulse-consumer` сразу `Exited`, в логах `ConsumeException: Subscribed topic not available` | в Kafka нет топика `production-daily`: подписка консьюмера топик не создаёт (KIP-361) | топик создаёт сам консьюмер при старте (ADR-015); если образ старый — `docker compose up -d --build consumer`, либо вручную: `kafka-topics.sh --bootstrap-server localhost:29092 --create --topic production-daily --partitions 1 --replication-factor 1 --if-not-exists` |
| PostgreSQL заполнен, ClickHouse пуст | `chingest`/`aggregator` не работали или упали | `logs chingest`, `logs aggregator`, затем перезапустить оба |
| `monthly_production` пуст, `production_daily` заполнен | агрегатор не успел отработать или упал | `logs aggregator`; он пересчитывает на старте и далее каждые 60 с |
| `/api/Analytics/summary` отдаёт нули | агрегат пересчитан по пустым данным | проверить `select count() from gp.production_daily` |
| на карте нет маркеров | не загружен сид координат `well_locations` | `psql -U <POSTGRES_USER> -d <POSTGRES_DB> < sql/seed_well_locations.sql` |
| на карте нет линий траекторий | `well.trajectory` не пришли или таблица пуста | `select count(*) from well_trajectories;` и `logs consumer` |
| данные есть, но часть событий не дошла | сообщения в DLQ | команда чтения `production-daily-dlq` из раздела 1; причина — в заголовке `reason` |

### 3.3 Kafka недоступна

Признак: `consumer`/`chingest` в цикле падают или висят; `deploy.sh` падает на шаге 5 с сообщением «Kafka не ответила за 120 с».

```bash
docker compose -f docker-compose.prod.yml logs --tail 100 kafka
docker compose -f docker-compose.prod.yml exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:29092 --list
docker inspect -f '{{.State.Health.Status}}' geopulse-kafka
docker stats --no-stream
```

Типовые причины: не хватило памяти (лимит `768m`, heap `-Xmx384m`), не совпал `CLUSTER_ID` с данными в томе, повреждён лог после жёсткой остановки.

### 3.4 ClickHouse недоступен

Признак: `/health` отвечает 200 (он проверяет только PostgreSQL), но `/api/Analytics/*` и `/api/Reports/*` отдают 500.

```bash
docker compose -f docker-compose.prod.yml logs --tail 100 clickhouse
docker compose -f docker-compose.prod.yml exec -T clickhouse wget -qO- http://127.0.0.1:8123/ping
docker compose -f docker-compose.prod.yml exec -T clickhouse clickhouse-client \
  --user <CLICKHOUSE_USER> --password <CLICKHOUSE_PASSWORD> -q "show databases"
```

Типовые причины: OOM (лимит `768m`), неверный `CLICKHOUSE_PASSWORD` в `.env` (тогда падают `chingest`, `aggregator`, `webapi`), таблицы ещё не созданы — их создают `chingest`/`aggregator` при старте, отдельных DDL-скриптов в репозитории нет.

### 3.5 Проблема с TLS-сертификатом Caddy

Признак: `https://<DOMAIN>` отдаёт ошибку сертификата либо Caddy в цикле запрашивает сертификат.

```bash
docker compose -f docker-compose.prod.yml logs --tail 200 caddy
curl -vI https://<DOMAIN>/ 2>&1 | head -30
```

Проверить по порядку:

1. `DOMAIN` в `.env` заполнен и **указывает на IP этого сервера** (DuckDNS); в `Caddyfile` он подставляется как `{$DOMAIN}` — без схемы и без завершающего `/`.
2. Порты 80 и 443 открыты: `sudo ufw status verbose` (ожидаются `80/tcp ALLOW` и `443/tcp ALLOW`), а также у облачного провайдера.
3. Caddy может писать сертификаты: тома `caddy_data` (`/data`) и `caddy_config` (`/config`).
4. Пока DNS/сертификат недоступны, работает резервный вход `http://<FALLBACK_IP>/` — отдельный блок `http://{$FALLBACK_IP}` без TLS. Если `FALLBACK_IP` пуст, блок превращается в catch-all на порту 80.
5. Rate limit Let's Encrypt: повторные неудачные попытки упираются в лимит — в логах Caddy будет явное сообщение; подождите или временно используйте вход по IP.

## 4. Бэкапы PostgreSQL

> **Автобэкап в репозитории не настроен.** Есть только скрипт `scripts/backup-pg.sh` и пример строки cron в `DEPLOY.md`. Пока cron не добавлен на сервере, резервные копии **не создаются** — это нужно сделать один раз руками.

Что делает скрипт (проверяется по файлу):

* `docker exec geopulse-postgres pg_dump -U <user> -d <db> -Fc` — custom-формат, сжатый; пароль не нужен, дамп делается внутри контейнера от имени пользователя БД;
* имя файла: `geopulse-YYYYMMDD-HHMMSS.dump` в каталоге бэкапов;
* каталог по умолчанию — `<корень репозитория>/backup`, можно передать первым аргументом;
* ротация: оставляет последние **14** дампов (`ls ... | sort | head -n -14 | xargs -r rm -f`);
* параметры окружения: `POSTGRES_DB` (по умолчанию `geopulse_core`), `POSTGRES_USER` (по умолчанию `geopulse`), `POSTGRES_CONTAINER` (по умолчанию `geopulse-postgres`).

Разовый запуск:

```bash
cd /opt/geopulse
mkdir -p /opt/geopulse/backup
bash scripts/backup-pg.sh /opt/geopulse/backup
```

Ежедневный бэкап в 02:30 — строка из `DEPLOY.md`, устанавливается через `crontab -e` (пользователь `deploy`, каталог `/opt/geopulse/backup` должен быть ему доступен на запись):

```
30 2 * * * cd /opt/geopulse && bash scripts/backup-pg.sh /opt/geopulse/backup >> /var/log/geopulse-backup.log 2>&1
```

Восстановление:

```bash
docker exec -i geopulse-postgres pg_restore -U <POSTGRES_USER> -d <POSTGRES_DB> --clean --if-exists < backup/geopulse-<STAMP>.dump
```

Куда складывать: локально в `/opt/geopulse/backup` (каталог `backup/` в `.gitignore`). Вынос копий за пределы сервера (S3, `rsync` на другую машину) в проекте **не реализован**.

Что **не** покрыто бэкапом: ClickHouse (`gp.*` восстанавливается переигрыванием Kafka и пересборкой агрегатора), тома Kafka, том `caddy_data` (сертификаты Caddy выпустит заново).

## 5. Открытые вопросы

1. **Автобэкап не настроен.** Скрипт `scripts/backup-pg.sh` есть, cron-строки в проекте нет (только в `DEPLOY.md`). Ставить ли её автоматически в `bootstrap-server.sh` — [УТОЧНИТЬ].
2. **Куда складывать бэкапы вне сервера** — регламента нет (S3 / rsync / второй сервер). [УТОЧНИТЬ].
3. **Бэкап ClickHouse.** Отдельного скрипта нет; предполагается восстановление переигрыванием Kafka и пересборкой агрегатов, но проверялся ли такой сценарий — [УТОЧНИТЬ].
4. **Тома Kafka.** В `docker-compose.prod.yml` для Kafka не объявлен именованный том (в отличие от `pgdata`, `chdata`, `caddy_data`, `caddy_config`), поэтому данные брокера живут в анонимном томе контейнера и теряются при пересоздании контейнера. Осознанно ли это (данные переигрываются из ingestion) — [УТОЧНИТЬ].
5. **Порт SSH.** `deploy.yml` жёстко использует `port: 22`, хотя `bootstrap-server.sh` принимает `SSH_PORT` и открывает его в UFW. Если SSH на сервере не на 22, деплой из CI не пройдёт — [УТОЧНИТЬ], нужно ли выносить порт в секрет.
6. **Мониторинг.** Алертов по `/health`, размеру DLQ и свободному месту на диске нет — только проверки внутри `deploy.sh` в момент релиза. Нужен ли внешний uptime-мониторинг — [УТОЧНИТЬ].
7. **Откат миграций.** Механизм отката меняет только тег образа; `Down`-миграции не запускаются. Регламент для несовместимых миграций — [УТОЧНИТЬ].
