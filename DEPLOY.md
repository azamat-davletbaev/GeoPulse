# Деплой GeoPulse на VPS

Развёртывание стека на сервере (Ubuntu 24.04, 1 vCPU / 2 ГБ RAM / 32 ГБ диск).
Сервер **ничего не собирает** — только тянет готовые образы из `ghcr.io`.

Архитектура деплоя:

- Образы собирает GitHub Actions (`.github/workflows/deploy.yml`) и публикует
  в GitHub Container Registry: `ghcr.io/<OWNER>/geopulse-*` (webapi, consumer,
  chingest, aggregator, ingestion). Теги — короткий SHA коммита и `latest`.
- На сервер копируются только файлы деплоя: `docker-compose.prod.yml`,
  `Caddyfile`, `.env` (секреты) и `sql/seed_well_locations.sql` (сид координат устьев).
- Данные Volve **запечены в образ `geopulse-ingestion`** — сервер их отдельно не скачивает.

---

## 0. Один раз: сделать пакеты публичными (GitHub UI)

Сервер пулит образы без логина, поэтому пакеты должны быть публичными.
По умолчанию пакеты, публикуемые в приватный репозиторий, имеют приватную видимость.

Для каждого из пяти пакетов (`geopulse-webapi`, `geopulse-consumer`,
`geopulse-chingest`, `geopulse-aggregator`, `geopulse-ingestion`):

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

На сервере заполните `.env` (домен DuckDNS, пароли):

```bash
cd /opt/geopulse
nano .env
```

Минимально нужны: `DOMAIN=geopulse-demo.duckdns.org`, `POSTGRES_PASSWORD`,
`CLICKHOUSE_PASSWORD`. Остальное имеет значения по умолчанию.

---

## 3. Запуск стека

```bash
cd /opt/geopulse

# 1) Постоянный состав (postgres, kafka, clickhouse, consumer, chingest,
#    aggregator, webapi, caddy). Ingestion НЕ входит в up -d.
docker compose -f docker-compose.prod.yml up -d

# 2) Дождаться готовности Kafka/Postgres/ClickHouse:
docker compose -f docker-compose.prod.yml ps

# 3) Одноразовый импорт данных (публикует события в Kafka):
docker compose -f docker-compose.prod.yml run --rm ingestion

# 4) Дождаться, пока consumer/chingest/aggregator обработают события
#    (aggregator пересобирает gp.monthly_production каждые 60 с):
rows=0
for attempt in $(seq 1 10); do
  rows=$(docker exec geopulse-clickhouse clickhouse-client --query "SELECT count() FROM gp.monthly_production" 2>/dev/null | tr -d '[:space:]')
  rows=${rows:-0}
  if [ "$rows" -gt 0 ]; then
    echo "Готово: в gp.monthly_production ${rows} строк (попытка ${attempt}/10)"
    break
  fi
  echo "Попытка ${attempt}/10: агрегатов пока нет, ждём 10 с..."
  sleep 10
done
if [ "${rows:-0}" -eq 0 ]; then
  echo "ВНИМАНИЕ: агрегатор ещё не собрал данные, проверьте логи chingest/aggregator" >&2
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

Порядок важен: сначала оба читателя (consumer, chingest) и агрегатор должны быть
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

1. Внести изменения, запушить в `main`.
2. GitHub Actions `deploy.yml` соберёт и опубликует новые образы (тег `latest` + SHA).
3. На сервере:

```bash
cd /opt/geopulse
docker compose -f docker-compose.prod.yml pull        # подтянуть новые latest
docker compose -f docker-compose.prod.yml up -d       # пересоздать изменившиеся контейнеры
```

Для отката на конкретный коммит — задать `TAG` в `.env` (короткий SHA) и повторить `pull`/`up -d`.

---

## 6. Память (сервер 2 ГБ RAM)

Лимиты в `docker-compose.prod.yml` — это верхние границы (потолки), а не резервирование:
Postgres `256m`, Kafka heap `-Xmx384m` (контейнер `768m`), ClickHouse `768m`,
каждый .NET-сервис `256m`. При нехватке памяти OOM-killer убьёт контейнер,
`restart: unless-stopped` его поднимет. Если OOM повторяется — уменьшите лимиты
ClickHouse/Kafka или число одновременных .NET-сервисов в пике.

