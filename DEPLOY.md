# Деплой GeoPulse на VPS

Развёртывание стека на сервере (Ubuntu 24.04, 1 vCPU / 2 ГБ RAM / 32 ГБ диск).
Сервер **ничего не собирает** — только тянет готовые образы из `ghcr.io`.

Архитектура деплоя:

- Образы собирает GitHub Actions (`.github/workflows/pipeline.yml`) и публикует
  в GitHub Container Registry: `ghcr.io/<OWNER>/geopulse-*` (webapi, consumer,
  chingest, ingestion). Теги — короткий SHA коммита и `latest`.
- На сервер копируются только файлы деплоя: `docker-compose.prod.yml`,
  `Caddyfile`, `.env` (секреты) и `sql/seed_well_locations.sql` (сид координат устьев).
- Данные Volve **запечены в образ `geopulse-ingestion`** — сервер их отдельно не скачивает.

---

## 0. Один раз: сделать пакеты публичными (GitHub UI)

Сервер пулит образы без логина, поэтому пакеты должны быть публичными.
По умолчанию пакеты, публикуемые в приватный репозиторий, имеют приватную видимость.

Для каждого из четырёх пакетов (`geopulse-webapi`, `geopulse-consumer`,
`geopulse-chingest`, `geopulse-ingestion`):

1. GitHub → профиль → **Packages** (или репозиторий → вкладка **Packages**).
2. Откройте пакет `geopulse-*`.
3. **Package settings** (справа внизу) → **Danger Zone** → **Change visibility**.
4. Выберите **Public** и подтвердите (нужно ввести имя пакета).

> Повторять не нужно: после смены видимости новые теги того же пакета остаются публичными.
> Примечание: в образе `geopulse-ingestion` запечены данные Volve (лицензия
> CC BY-NC-SA 4.0, некоммерческое использование, с указанием авторства Equinor) —
> публикация образа допустима, но без перепродажи данных и с сохранением атрибуции.

---

## 1. Первичная настройка сервера

Все команды — от пользователя с `sudo`. Первый вход — по SSH-ключу.

> **Автоматизация.** Шаги 1.1–1.4, а также клонирование репозитория в
> `/opt/geopulse`, создание `.env` из `.env.example`, `docker login ghcr.io` и
> настройку UFW выполняет идемпотентный скрипт `scripts/bootstrap-server.sh`:
>
> ```bash
> sudo DEPLOY_PUBKEY="$(cat ~/.ssh/id_ed25519.pub)" \
>      GHCR_TOKEN="<PAT с read:packages, если пакеты приватные>" \
>      bash scripts/bootstrap-server.sh
> ```
>
> Ниже шаги описаны вручную — на случай отладки или нестандартной конфигурации.

### 1.1. Обновление пакетов

```bash
sudo apt update && sudo apt upgrade -y
```

### 1.2. SSH по ключу + отключение входа root по паролю

Если ключа ещё нет — сгенерируйте локально (`ssh-keygen`) и скопируйте на сервер:

```bash
# локально:
ssh-copy-id USER@SERVER_IP
```

На сервере откройте `/etc/ssh/sshd_config` (`sudo nano /etc/ssh/sshd_config`)
и убедитесь в значениях:

```
PermitRootLogin prohibit-password
PasswordAuthentication no
PubkeyAuthentication yes
```

Перезапустите sshd:

```bash
sudo systemctl reload ssh
```

> Порядок важен: сначала убедитесь, что вход по ключу работает, и только потом
> отключайте парольную аутентификацию, иначе можно потерять доступ к серверу.

### 1.3. Файрвол UFW (открыть только 22/80/443)

```bash
sudo ufw allow OpenSSH   # 22
sudo ufw allow 80/tcp    # HTTP  — сертификат Let's Encrypt и резервный вход по IP (IP сервера из FALLBACK_IP)
sudo ufw allow 443/tcp   # HTTPS — основной трафик
sudo ufw --force enable
sudo ufw status verbose
```

Порты Postgres (5432), ClickHouse (8123/9000), Kafka (9092/29092) **наружу не
открываются** — в `docker-compose.prod.yml` они не публикуются.

### 1.4. Установка Docker + Compose plugin

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker "$USER"
# перелогиниться, чтобы группа применилась
```

Проверка:

```bash
docker --version && docker compose version
```

---

## 2. Копирование файлов деплоя на сервер

На сервере создайте каталог проекта (далее — `/opt/geopulse`):

```bash
sudo mkdir -p /opt/geopulse && sudo chown "$USER":"$USER" /opt/geopulse
```

Со своей машины скопируйте четыре файла:

```bash
scp docker-compose.prod.yml Caddyfile .env USER@SERVER_IP:/opt/geopulse/
scp sql/seed_well_locations.sql USER@SERVER_IP:/opt/geopulse/
```

> `.env` создаётся из `.env.example` и содержит секреты — в репозиторий он не коммитится.

> Каталог `observability/` нужен только для профиля наблюдаемости (раздел 7).
> Если он используется, скопируйте его целиком:
> `scp -r observability USER@SERVER_IP:/opt/geopulse/`.

На сервере заполните `.env` (домен DuckDNS, пароли):

```bash
cd /opt/geopulse
nano .env
```

Минимально нужны: `DOMAIN=geopulse-demo.duckdns.org`, `POSTGRES_PASSWORD`,
`CLICKHOUSE_PASSWORD`. Остальное имеет значения по умолчанию.
Для профиля наблюдаемости задайте также `GRAFANA_ADMIN_PASSWORD` (без него
действует `admin` — допустимо только потому, что Grafana слушает loopback).

---

## 3. Запуск стека

```bash
cd /opt/geopulse

# 1) Постоянный состав (postgres, kafka, clickhouse, consumer, chingest,
#    webapi, caddy). Ingestion НЕ входит в up -d.
docker compose -f docker-compose.prod.yml up -d

# 2) Дождаться готовности Kafka/Postgres/ClickHouse:
docker compose -f docker-compose.prod.yml ps

# 3) Одноразовый импорт данных (публикует события в Kafka):
docker compose -f docker-compose.prod.yml run --rm ingestion

# 4) Дождаться, пока consumer/chingest обработают события:
rows=0
for attempt in $(seq 1 10); do
  rows=$(docker exec geopulse-clickhouse clickhouse-client --query "SELECT count() FROM gp.production_daily" 2>/dev/null | tr -d '[:space:]')
  rows=${rows:-0}
  if [ "$rows" -gt 0 ]; then
    echo "Готово: в gp.production_daily ${rows} строк (попытка ${attempt}/10)"
    break
  fi
  echo "Попытка ${attempt}/10: суточных строк пока нет, ждём 10 с..."
  sleep 10
done
if [ "${rows:-0}" -eq 0 ]; then
  echo "ВНИМАНИЕ: ClickHouse пуст, проверьте логи chingest/consumer" >&2
fi

# 5) Сид координат устьев (нужен для маркеров на карте):
docker exec -i geopulse-postgres psql -U "${POSTGRES_USER:-geopulse}" -d "${POSTGRES_DB:-geopulse_core}" < seed_well_locations.sql
```

Проверка заполнения:

```bash
# 7 скважин в PostgreSQL
docker exec geopulse-postgres psql -U geopulse -d geopulse_core -c "SELECT count(*) FROM wells;"
# 15 634 строки добычи без дублей
docker exec geopulse-postgres psql -U geopulse -d geopulse_core -c "SELECT count(*) FROM (SELECT DISTINCT (well_id, date) FROM daily_production) t;"
# по домену (HTTPS, сертификат Let's Encrypt)
curl -s https://geopulse-demo.duckdns.org/api/Analytics/summary
# по IP (резервный вход по HTTP, без сертификата)
curl -s "http://$FALLBACK_IP/api/Analytics/summary"
```

Проверка доступа к фронту — отдельно по домену и по IP, у обоих должен быть `200`:

```bash
curl -s -o /dev/null -w 'по домену: %{http_code}\n' https://geopulse-demo.duckdns.org/
curl -s -o /dev/null -w 'по IP:     %{http_code}\n' "http://$FALLBACK_IP/"
```

Порядок важен: сначала оба читателя (consumer, chingest) должны быть
подняты, затем — импорт. Повторный запуск `ingestion` безопасен: запись в
PostgreSQL идемпотентна (`ON CONFLICT DO NOTHING`), ClickHouse дедуплицирует
дубли через `ReplacingMergeTree` (чтение `FINAL`).

> Caddy автоматически выпустит сертификат Let's Encrypt для `DOMAIN`. Для этого
> домен должен указывать на IP сервера (DuckDNS), а порты 80/443 должны быть открыты.
> Параллельно Caddy обслуживает резервный вход по «голому» IP — этот вход
> обслуживается блоком `http://{$FALLBACK_IP}` в `Caddyfile` (без сертификата, по
> порту 80) и требует заполненной переменной `FALLBACK_IP` в `.env` (IP сервера).
> Если `FALLBACK_IP` пуста, блок остаётся валидным, но превращается в catch-all
> на порту 80 (обслуживает любой Host).

---

## 4. Бэкап PostgreSQL

Скрипт — `scripts/backup-pg.sh` (pg_dump custom, ротация 14 копий). Скопируйте
его на сервер и настройте cron.

```bash
scp scripts/backup-pg.sh USER@SERVER_IP:/opt/geopulse/
# на сервере:
mkdir -p /opt/geopulse/backup
crontab -e
```

Строка cron (ежедневно в 02:30, лог в файл):

```
30 2 * * * cd /opt/geopulse && bash backup-pg.sh /opt/geopulse/backup >> /var/log/geopulse-backup.log 2>&1
```

Восстановление (при необходимости):

```bash
docker exec -i geopulse-postgres pg_restore -U geopulse -d geopulse_core --clean --if-exists < backup/geopulse-<STAMP>.dump
```

---

## 5. Обновление демо следующим коммитом

Всё автоматизировано: после пуша в `main` единый workflow `pipeline.yml` сначала
прогоняет все тесты (.NET и фронтенд), затем собирает образы
(теги `latest` и короткий SHA) и job `deploy` по SSH выполняет
`bash /opt/geopulse/deploy.sh` с `IMAGE_TAG=<короткий SHA>`.

`deploy.sh` делает по шагам: `git pull --ff-only` → `docker compose pull` →
подъём postgres/kafka/clickhouse и ожидание готовности → миграции EF Core →
создание топиков Kafka → `docker compose up -d --remove-orphans` →
health-check `GET /health` (до 60 с) → `docker image prune -f` и сводка.
При провале health-check скрипт завершается с кодом 1, печатает команду откатa
и **не удаляет старые образы**.

Ручной деплой (то же самое, но руками):

```bash
cd /opt/geopulse
IMAGE_TAG=latest bash deploy.sh        # или IMAGE_TAG=<короткий SHA>
```

Тег образов хранится в `.env` (`DOCKER_TAG`), который читает
`docker-compose.prod.yml`; `deploy.sh` перед `pull` обновляет его на переданный
`IMAGE_TAG`.

Откат на предыдущий коммит:

```bash
cd /opt/geopulse
sed -i 's/^DOCKER_TAG=.*/DOCKER_TAG=<короткий SHA>/' .env
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d
```

---

## 6. Память (сервер 2 ГБ RAM)

Лимиты в `docker-compose.prod.yml` — это верхние границы (потолки), а не резервирование:
Postgres `256m`, Kafka heap `-Xmx384m` (контейнер `768m`), ClickHouse `1280m` (изнутри — `max_server_memory_usage` 1 ГиБ),
каждый .NET-сервис `256m` (`consumer`, `chingest`, `webapi`), Caddy `64m`. При нехватке памяти OOM-killer убьёт контейнер,
`restart: unless-stopped` его поднимет. Если OOM повторяется — уменьшите лимиты
ClickHouse/Kafka или число одновременных .NET-сервисов в пике.
Полная таблица по всем 10 сервисам — раздел 7.1.

Дополнительно ClickHouse ограничен **изнутри** ≈ 600 МиБ
(`max_server_memory_usage` в `observability/clickhouse/max_server_memory_usage.xml`):
при превышении сервер отказывает тяжёлым запросом (`Code: 241`), а не падает от OOM-killer.

Профиль наблюдаемости (раздел 7) добавляет ещё `256m` (Prometheus), `192m` (Grafana)
и `64m` (node-exporter) — итого до `512 МБ` сверх основного стека, поэтому на 2 ГБ
его включают по требованию, а не постоянно.

---

## 7. Мониторинг (профиль `observability`)

Prometheus + Grafana + node-exporter объявлены в `docker-compose.prod.yml` с
profiles: `observability`, поэтому **обычный `docker compose up -d` их не
поднимает**, а `deploy.sh` про них не знает (его `CORE_CONTAINERS` — ровно 7
сервисов основного состава).

```bash
cd /opt/geopulse
# запуск мониторинга (основной стек уже работает)
docker compose -f docker-compose.prod.yml --profile observability up -d
docker compose -f docker-compose.prod.yml --profile observability ps

# остановить и убрать ТОЛЬКО контейнеры профиля (тома promdata/grafana_data остаются)
docker compose -f docker-compose.prod.yml stop prometheus grafana node-exporter
docker compose -f docker-compose.prod.yml rm -f prometheus grafana node-exporter
```

> `docker compose ... down` для остановки мониторинга не используйте: `down`
> действует на весь проект и погасил бы основной стек. Останавливайте сервисы
> профиля по именам, как показано выше.

Что опрашивается (`observability/prometheus/prometheus.yml`, интервал 15 с):
`webapi:5146/metrics`, `host.docker.internal:9100` (node-exporter, host-сеть) и сам
Prometheus. Ретеншн — 7 дней (`--storage.tsdb.retention.time=7d`).

Доступ к Grafana — только через SSH-туннель, порт опубликован на loopback сервера:

```bash
# на своей машине:
ssh -L 3000:127.0.0.1:3000 USER@SERVER_IP
# затем открыть http://127.0.0.1:3000 (логин admin, пароль = GRAFANA_ADMIN_PASSWORD из .env)
```

Datasource `Prometheus` создаётся автоматически из
`observability/grafana/provisioning/datasources/prometheus.yml`.
Готовый дашборд по хосту: Dashboards → Import → ID `1860` (Node Exporter Full),
datasource — Prometheus.

`/metrics` наружу не отдаётся: в `Caddyfile` для обоих сайтов добавлен
`respond @metrics 404`, публичный `/health` не изменён.

Проверка:

```bash
# метрики WebAPI изнутри сети (без выхода наружу)
docker compose -f docker-compose.prod.yml exec -T prometheus wget -qO- http://webapi:5146/metrics | head -5
# сам экспортёр и targets
docker compose -f docker-compose.prod.yml exec -T prometheus wget -qO- http://127.0.0.1:9090/api/v1/targets | head -c 400
# host-метрики node-exporter (порт слушает только хост)
curl -fsS http://127.0.0.1:9100/metrics | head -5
# свободная память при включённом профиле
free -m
```

Нормой на 2 ГБ RAM считается: `used` до ~1.5–1.6 ГБ, `available` ≥ 350–450 МБ,
`swap used` — 0 (swap задействуется только в пике; устойчивый рост swap означает,
что лимиты надо уменьшать).

### 7.1. Лимиты сервисов приложения (полный бюджет памяти стенда)

Обязательный `mem_limit` есть у **всех** контейнеров (сервер 2 ГБ RAM + 2 ГБ swap).
Значения — потолки cgroup, а не резервирование: фактическое потребление ниже.

| # | Сервис | Контейнер | `mem_limit` | Профиль | Почему столько |
|---|---|---|---|---|---|
| 1 | `postgres` | `geopulse-postgres` | `256m` | основной | БД небольшая: ~15 тыс. строк добычи + справочники |
| 2 | `kafka` | `geopulse-kafka` | `768m` | основной | heap JVM `-Xmx384m` + оверхед брокера/контроллера |
| 3 | `clickhouse` | `geopulse-clickhouse` | `1280m` | основной | изнутри ограничен 1 ГиБ (`max_server_memory_usage`) |
| 4 | `consumer` | `geopulse-consumer` | `256m` | основной | .NET: запас под GC при разборе потока Kafka |
| 5 | `chingest` | `geopulse-chingest` | `256m` | основной | .NET: всплески аллокаций при пакетной записи в ClickHouse |
| 6 | `webapi` | `geopulse-webapi` | `256m` | основной | .NET: GC/JIT под нагрузкой, отчёты xlsx/csv собираются в памяти |
| 7 | `caddy` | `geopulse-caddy` | `64m` | основной | только TLS-сертификаты, буферы прокси и журнал запросов |
| 8 | `prometheus` | `geopulse-prometheus` | `256m` | `observability` | TSDB на 7 дней ретеншна, ~10 таргетов |
| 9 | `grafana` | `geopulse-grafana` | `192m` | `observability` | UI + provisioning, дашборды без тяжёлых плагинов |
| 10 | `node-exporter` | `geopulse-node-exporter` | `64m` | `observability` | экспортёр почти не потребляет память |

**Итог:** основной стек — `2624m` (~2.56 ГБ) лимитов; с профилем
`observability` — `3136m` (~3.06 ГБ). Сумма потолков выше объёма RAM намеренно:
контейнеры не выбирают лимит целиком, а сумма даёт запас на пики без OOM.

Проверка лимитов на сервере:

```bash
cd /opt/geopulse
# факт потребления по контейнерам (MemUsage / % от лимита)
docker stats --no-stream --format '{{.Name}}\t{{.MemUsage}}\t{{.MemPerc}}'
# потолки из отрендеренного конфига (значения в байтах)
docker compose -f docker-compose.prod.yml config | grep -B1 'mem_limit'
```

Последняя сверка с кодом: 2026-11-12
