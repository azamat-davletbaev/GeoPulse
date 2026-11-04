#!/usr/bin/env bash
# ============================================================================
# GeoPulse — деплой очередного релиза на сервер (один запуск = один релиз).
#
# Запускается:
#   * из GitHub Actions (job `deploy` в .github/workflows/deploy.yml):
#         IMAGE_TAG=<короткий SHA> bash /opt/geopulse/deploy.sh
#   * вручную на сервере (например первый деплой):
#         sudo -u deploy IMAGE_TAG=latest bash /opt/geopulse/deploy.sh
#
# Порядок шагов:
#   1. предусловия (docker, compose-файл, .env);
#   2. обновление DOCKER_TAG в .env на переданный IMAGE_TAG;
#   3. git pull --ff-only origin main (конфликт = падение с подсказкой);
#   4. docker compose pull новых образов;
#   5. подъём инфраструктуры (postgres/kafka/clickhouse) и ожидание готовности;
#   6. применение EF Core миграций PostgreSQL (база geopulse_core) ДО старта
#      обновлённых сервисов;
#   7. ожидание Kafka + идемпотентное создание топиков;
#   8. docker compose up -d --remove-orphans;
#   9. health-check GET /health через обратный прокси до 60 с, проверка статусов;
#  10. успех: docker image prune -f (только висячие образы) + сводка;
#      провал: старые образы НЕ удаляются, печатается команда откатa, exit 1.
#
# Повторный запуск безопасен: pull/up идемпотентны, топики создаются с
# --if-not-exists, миграции EF пропускаются, если уже применены.
#
# Переменные окружения:
#   DEPLOY_DIR     каталог проекта (по умолчанию /opt/geopulse);
#   IMAGE_TAG      тег образов: полный/короткий SHA или latest (по умолчанию
#                  «не менять» — берётся текущий DOCKER_TAG из .env);
#   BRANCH         ветка (по умолчанию main);
#   COMPOSE_FILE   имя compose-файла (по умолчанию docker-compose.prod.yml).
# ============================================================================
set -euo pipefail

DEPLOY_DIR="${DEPLOY_DIR:-/opt/geopulse}"
BRANCH="${BRANCH:-main}"
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.prod.yml}"
IMAGE_TAG="${IMAGE_TAG:-}"
KAFKA_ATTEMPTS="${KAFKA_ATTEMPTS:-60}"         # 60 × 2 с = 2 мин на старт Kafka
MIGRATION_ATTEMPTS="${MIGRATION_ATTEMPTS:-60}" # 60 × 2 с = 2 мин на миграции
HEALTH_ATTEMPTS="${HEALTH_ATTEMPTS:-30}"       # 30 × 2 с = 60 с на health-check
MIGRATOR_NAME="${MIGRATOR_NAME:-geopulse-migrator}"
TOPIC_PARTITIONS="${TOPIC_PARTITIONS:-3}"
PG_CONTAINER="${PG_CONTAINER:-geopulse-postgres}"
CORE_CONTAINERS=(
  geopulse-postgres geopulse-kafka geopulse-clickhouse
  geopulse-consumer geopulse-chingest geopulse-aggregator
  geopulse-webapi geopulse-caddy
)

log()  { printf '\n\033[1;34m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m[ВНИМАНИЕ] %s\033[0m\n' "$*"; }
die()  { printf '\033[1;31m[ОШИБКА] %s\033[0m\n' "$*" >&2; exit 1; }

# Значение переменной из .env без source (в .env могут быть пробелы и #).
env_get() {
  local key="$1"
  grep -E "^${key}=" .env 2>/dev/null | tail -n1 | cut -d= -f2- || true
}

compose() { docker compose -f "$COMPOSE_FILE" "$@"; }

PREV_TAG=""

# Подсказка по откaту печатается на ЛЮБОМ ненулевом коде возврата.
rollback_hint() {
  cat >&2 <<EOF

-----------------------------------------------------------------------------
ОТКАТ (старые образы НЕ удалены, они уже лежат локально):
  cd ${DEPLOY_DIR}
  sed -i 's/^DOCKER_TAG=.*/DOCKER_TAG=${PREV_TAG:-latest}/' .env
  docker compose -f ${COMPOSE_FILE} pull
  docker compose -f ${COMPOSE_FILE} up -d --remove-orphans
  curl -fsS https://<DOMAIN>/health   # или docker compose exec -T caddy wget -qO- http://webapi:5146/health
-----------------------------------------------------------------------------
EOF
}

on_exit() {
  local rc=$?
  if [ "$rc" -ne 0 ]; then
    warn "Деплой завершился с ошибкой (код ${rc})."
    rollback_hint
  fi
}
trap on_exit EXIT

# ----------------------------------------------------------------------------
# 1. Предусловия.
# ----------------------------------------------------------------------------
log "1/10 Предусловия"
cd "$DEPLOY_DIR" || die "каталог ${DEPLOY_DIR} не найден"
[ -f "$COMPOSE_FILE" ] || die "не найден ${DEPLOY_DIR}/${COMPOSE_FILE}"
[ -f .env ] || die "не найден ${DEPLOY_DIR}/.env — заполните секреты (см. .env.example)"
[ -d .git ] || die "${DEPLOY_DIR} — не git-репозиторий (нужен клон GeoPulse)"
command -v docker >/dev/null 2>&1 || die "docker не установлен (запустите scripts/bootstrap-server.sh)"
docker compose version >/dev/null 2>&1 || die "нет docker compose plugin"

DOMAIN_VALUE="$(env_get DOMAIN)"
[ -n "$DOMAIN_VALUE" ] || die "в .env не задан DOMAIN (Caddy не сможет выпустить сертификат)"
log "Каталог: ${DEPLOY_DIR}; домен: ${DOMAIN_VALUE}"

# ----------------------------------------------------------------------------
# 2. Тег образов. CI присылает полный SHA, а в ghcr образы публикуются с
#    коротким (7 символов) — нормализуем. Значение пишется в .env (DOCKER_TAG),
#    который читает docker-compose.prod.yml.
# ----------------------------------------------------------------------------
log "2/10 Тег образов"
PREV_TAG="$(env_get DOCKER_TAG)"
PREV_TAG="${PREV_TAG:-latest}"

if [ -n "$IMAGE_TAG" ]; then
  NEW_TAG="$IMAGE_TAG"
  # Полный 40-символьный SHA -> короткий тег, как в .github/workflows/deploy.yml.
  if [ "${#NEW_TAG}" -eq 40 ]; then
    NEW_TAG="${NEW_TAG:0:7}"
  fi
  if grep -q '^DOCKER_TAG=' .env; then
    sed -i "s/^DOCKER_TAG=.*/DOCKER_TAG=${NEW_TAG}/" .env
  else
    printf '\n# Тег образов ghcr.io (обновляется deploy.sh).\nDOCKER_TAG=%s\n' "$NEW_TAG" >>.env
  fi
  log "DOCKER_TAG: ${PREV_TAG} -> ${NEW_TAG}"
else
  NEW_TAG="$PREV_TAG"
  warn "IMAGE_TAG не передан — остаётся текущий DOCKER_TAG=${NEW_TAG}"
fi

# ----------------------------------------------------------------------------
# Вспомогательные функции.
# ----------------------------------------------------------------------------

# Ожидание статуса healthy у контейнера с healthcheck (postgres/kafka/clickhouse).
wait_healthy() {
  local name="$1" attempts="$2" i status
  for i in $(seq 1 "$attempts"); do
    status="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$name" 2>/dev/null || true)"
    case "$status" in
      healthy|running) return 0 ;;
      unhealthy) die "контейнер ${name} перешёл в unhealthy (см. docker logs ${name})" ;;
    esac
    sleep 2
  done
  docker logs --tail 30 "$name" >&2 || true
  die "контейнер ${name} не стал healthy за $((attempts * 2)) с"
}

# Готовность Kafka: тот же путь к kafka-topics.sh, что в healthcheck compose.
# TODO-verify: путь /opt/kafka/bin/kafka-topics.sh и порт 29092 сверены с
# docker-compose.prod.yml (образ apache/kafka:4.0.0, bootstrap kafka:29092).
kafka_ready() {
  compose exec -T kafka /opt/kafka/bin/kafka-topics.sh \
    --bootstrap-server localhost:29092 --list >/dev/null 2>&1
}

wait_kafka() {
  local i
  for i in $(seq 1 "$KAFKA_ATTEMPTS"); do
    if kafka_ready; then
      log "Kafka готова (попытка ${i})"
      return 0
    fi
    sleep 2
  done
  compose logs --tail 30 kafka >&2 || true
  die "Kafka не ответила за $((KAFKA_ATTEMPTS * 2)) с"
}

# Идемпотентное создание топика.
create_topic() {
  local topic="$1"
  compose exec -T kafka /opt/kafka/bin/kafka-topics.sh \
    --bootstrap-server localhost:29092 \
    --create --if-not-exists \
    --topic "$topic" --partitions "$TOPIC_PARTITIONS" --replication-factor 1
}

# Количество миграций EF Core в репозитории (файлы 2*_*.cs без *.Designer.cs).
repo_migrations_count() {
  find src/GeoPulse.Infrastructure/Migrations -maxdepth 1 \
    -name '2*_*.cs' ! -name '*.Designer.cs' 2>/dev/null | wc -l | tr -d ' '
}

# Количество применённых миграций (таблица EF Core по умолчанию).
# TODO-verify: имя таблицы "__EFMigrationsHistory" — дефолт EF Core; в
# CoreDbContext переопределения истории миграций нет (проверено).
db_applied_count() {
  local user db
  user="$(env_get POSTGRES_USER)"; user="${user:-geopulse}"
  db="$(env_get POSTGRES_DB)";     db="${db:-geopulse_core}"
  docker exec "$PG_CONTAINER" psql -U "$user" -d "$db" -tAc \
    'SELECT count(*) FROM "__EFMigrationsHistory";' 2>/dev/null | tr -d '[:space:]'
}

# ----------------------------------------------------------------------------
# Применение миграций ДО старта обновлённых сервисов.
#
# В образе webapi нет .NET SDK и dotnet-ef (базовый слой aspnet:10.0), поэтому
# `dotnet ef database update` в контейнере не выполнить. Миграции в проекте
# накатывает GeoPulse.Consumer при старте (Database.MigrateAsync()), поэтому
# берём одноразовый контейнер ИЗ ОБРАЗА consumer: он применяет миграции и далее
# начинает читать Kafka — мы ждём нужное число миграций в БД и гасим его ДО
# `up -d`, чтобы не появилась вторая группа консьюмеров.
#
# TODO-verify: правильнее добавить в src/GeoPulse.Consumer флаг `--migrate-only`
# (миграции + выход, без Kafka) — ЭТО ОТДЕЛЬНЫЙ ПАТЧ ПОЗЖЕ; до него работает
# описанный приём. Альтернатива — отдельный сервис-мигратор в compose.
# ----------------------------------------------------------------------------
apply_migrations() {
  local expected applied i

  expected="$(repo_migrations_count)"
  applied="$(db_applied_count || true)"; applied="${applied:-0}"

  log "Миграции: в репозитории ${expected}, применено в БД ${applied}"
  if [ "$expected" -eq 0 ]; then
    warn "В src/GeoPulse.Infrastructure/Migrations не найдено миграций — шаг пропущен"
    return 0
  fi
  if [ "$applied" -ge "$expected" ]; then
    log "Все миграции уже применены — шаг пропущен"
    return 0
  fi

  docker rm -f "$MIGRATOR_NAME" >/dev/null 2>&1 || true
  log "Запускаю одноразовый контейнер миграций (образ consumer)"
  compose run -d --rm --name "$MIGRATOR_NAME" --no-deps consumer >/dev/null

  for i in $(seq 1 "$MIGRATION_ATTEMPTS"); do
    applied="$(db_applied_count || true)"; applied="${applied:-0}"
    if [ "$applied" -ge "$expected" ]; then
      docker rm -f "$MIGRATOR_NAME" >/dev/null 2>&1 || true
      log "Миграции применены: ${applied}/${expected}"
      return 0
    fi
    if ! docker inspect -f '{{.State.Running}}' "$MIGRATOR_NAME" 2>/dev/null | grep -q true; then
      warn "Контейнер миграций завершился раньше, чем применились все миграции (${applied}/${expected})"
      docker logs --tail 40 "$MIGRATOR_NAME" >&2 || true
      docker rm -f "$MIGRATOR_NAME" >/dev/null 2>&1 || true
      die "Миграции не применились — обновлённые сервисы НЕ запущены"
    fi
    sleep 2
  done

  docker logs --tail 40 "$MIGRATOR_NAME" >&2 || true
  docker rm -f "$MIGRATOR_NAME" >/dev/null 2>&1 || true
  die "Миграции не применились за $((MIGRATION_ATTEMPTS * 2)) с"
}

# webapi не публикует порт наружу (наружу смотрит только Caddy), поэтому
# /health опрашивается изнутри compose-сети.
# TODO-verify: эндпоинт GET /health подтверждён кодом —
# src/GeoPulse.WebAPI/Controllers/HealthController.cs ([Route("health")]);
# ответ 200 + "database":"up" либо 503 + "database":"down".
probe_webapi() {
  local net
  # Основной путь: контейнер caddy (alpine, есть busybox wget) в той же сети.
  if compose exec -T caddy wget -q -O- -T 3 http://webapi:5146/health 2>/dev/null \
       | grep -q '"database"[[:space:]]*:[[:space:]]*"up"'; then
    return 0
  fi
  # Резервный путь: временный контейнер curl в сети webapi (если в caddy нет wget).
  net="$(docker inspect -f '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{end}}' geopulse-webapi 2>/dev/null || true)"
  [ -n "$net" ] || return 1
  docker run --rm --network "$net" curlimages/curl:8.11.1 -fsS --max-time 3 \
    http://webapi:5146/health 2>/dev/null | grep -q '"database"[[:space:]]*:[[:space:]]*"up"'
}

# ----------------------------------------------------------------------------
# 3. Обновление кода. Только fast-forward: при локальных правках падаем.
# ----------------------------------------------------------------------------
log "3/10 git pull --ff-only origin ${BRANCH}"
if ! git pull --ff-only origin "$BRANCH"; then
  die "git pull --ff-only не удался: в ${DEPLOY_DIR} есть локальные изменения или ветка разошлась с origin/${BRANCH} (конфликт).
  Разберитесь вручную: git -C ${DEPLOY_DIR} status
  Если локальные правки не нужны: git -C ${DEPLOY_DIR} reset --hard origin/${BRANCH}
  Файл .env не под версионным контролем и этими командами не затрагивается."
fi
GIT_SHA="$(git rev-parse --short HEAD)"

# ----------------------------------------------------------------------------
# 4. Загрузка новых образов (тег берётся из .env: DOCKER_TAG).
# ----------------------------------------------------------------------------
log "4/10 docker compose pull (DOCKER_TAG=${NEW_TAG})"
compose pull

# ----------------------------------------------------------------------------
# 5. Инфраструктура: поднимаем БД/брокер и ждём готовности.
#    Образы postgres/kafka/clickhouse закреплены по версиям и от DOCKER_TAG
#    не зависят, поэтому поднимаются первыми — миграциям нужен живой Postgres.
# ----------------------------------------------------------------------------
log "5/10 Инфраструктура: postgres, kafka, clickhouse"
compose up -d postgres kafka clickhouse
wait_healthy geopulse-postgres 60
wait_healthy geopulse-clickhouse 60
wait_kafka

# ----------------------------------------------------------------------------
# 6. Миграции EF Core ДО старта обновлённых сервисов.
# ----------------------------------------------------------------------------
log "6/10 Миграции PostgreSQL (база из .env: POSTGRES_DB)"
apply_migrations

# ----------------------------------------------------------------------------
# 7. Топики Kafka (идемпотентно: --if-not-exists).
#
# TODO-verify: имена сверены с кодом и конфигурацией —
#   * src/GeoPulse.Core/Events/EventTypes.cs задаёт ТИПЫ событий
#     (well.registered, production.daily, well.trajectory), а не топики;
#   * топик данных один: production-daily (Kafka__Topic в docker-compose.prod.yml,
#     Kafka:Topic в appsettings.json сервисов Consumer/CHIngest/Ingestion);
#   * DLQ: production-daily-dlq (Kafka__DlqTopic / Kafka:DlqTopic).
# Поэтому топиков well-registered/well-trajectory/dlq НЕ существует — создавать
# их не нужно (события well.* идут в production-daily, ошибки — в *-dlq).
# Автосоздание в брокере включено (KAFKA_AUTO_CREATE_TOPICS_ENABLE=true), ручное
# создание нужно только чтобы заранее зафиксировать партиции (3, как NUM_PARTITIONS).
# ----------------------------------------------------------------------------
log "7/10 Топики Kafka"
for topic in production-daily production-daily-dlq; do
  create_topic "$topic"
done
log "Список топиков:"
compose exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:29092 --list

# ----------------------------------------------------------------------------
# 8. Запуск/пересоздание сервисов приложения на новом теге.
# ----------------------------------------------------------------------------
log "8/10 docker compose up -d --remove-orphans"
compose up -d --remove-orphans

# ----------------------------------------------------------------------------
# 9. Health-check: ретраи до 60 секунд (30 × 2 с), затем проверка статусов.
# ----------------------------------------------------------------------------
log "9/10 Health-check GET /health (до $((HEALTH_ATTEMPTS * 2)) с)"
HEALTH_OK=0
for i in $(seq 1 "$HEALTH_ATTEMPTS"); do
  if probe_webapi; then
    HEALTH_OK=1
    log "webapi отвечает /health (попытка ${i})"
    break
  fi
  sleep 2
done

if [ "$HEALTH_OK" -ne 1 ]; then
  warn "Health-check не прошёл за $((HEALTH_ATTEMPTS * 2)) с"
  compose ps || true
  compose logs --tail 60 webapi >&2 || true
  die "Деплой прерван: GET /health не вернул database=up. Образы НЕ удалены (см. подсказку откатa ниже)."
fi

# Все контейнеры должны быть запущены (healthy/running).
BAD=0
for name in "${CORE_CONTAINERS[@]}"; do
  state="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$name" 2>/dev/null || echo missing)"
  case "$state" in
    healthy|running) : ;;
    *) BAD=1; warn "сервис ${name}: ${state}" ;;
  esac
done
if [ "$BAD" -ne 0 ]; then
  compose ps || true
  die "Не все сервисы в состоянии Up/healthy — деплой считается неуспешным."
fi

# ----------------------------------------------------------------------------
# 10. Успех: удаляем только висячие образы (старые теги остаются для отката).
# ----------------------------------------------------------------------------
log "10/10 Очистка висячих образов"
docker image prune -f || warn "docker image prune завершился с ошибкой (не критично)"

# ----------------------------------------------------------------------------
# Итоговая сводка.
# ----------------------------------------------------------------------------
log "ИТОГ"
GIT_SUBJECT="$(git log -1 --format=%s)"
printf '  git SHA:    %s\n' "$GIT_SHA"
printf '  коммит:     %s\n' "$GIT_SUBJECT"
printf '  образы:     %s\n' "$NEW_TAG"
printf '  пред. тег:  %s (для отката)\n' "$PREV_TAG"
printf '  домен:      https://%s\n' "$(env_get DOMAIN)"
printf '  время:      %s\n' "$(date -u +'%Y-%m-%d %H:%M:%S UTC')"
printf '  статусы контейнеров:\n'
for name in "${CORE_CONTAINERS[@]}"; do
  state="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$name" 2>/dev/null || echo missing)"
  printf '    %-22s %s\n' "$name" "$state"
done
echo
compose ps
