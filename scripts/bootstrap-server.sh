#!/usr/bin/env bash
# ============================================================================
# GeoPulse — одноразовая подготовка ЧИСТОГО сервера Ubuntu 24.04 к деплою.
#
# Что делает:
#   0. проверяет root/sudo (при необходимости перезапускает себя через sudo);
#   1. apt-get update + upgrade, ставит базовые пакеты (git, curl, ufw, gnupg);
#   2. ставит Docker Engine + docker-compose-plugin из ОФИЦИАЛЬНОГО репозитория
#      download.docker.com (не из пакета docker-compose в репозитории Ubuntu);
#   3. создаёт пользователя deploy, добавляет его в группу docker;
#   4. настраивает SSH-доступ по ключу для deploy и отключает парольный вход;
#   5. клонирует GeoPulse в /opt/geopulse (есть вариант с deploy-ключом для
#      приватного репозитория);
#   6. создаёт /opt/geopulse/.env из .env.example: пароли генерируются случайно,
#      DOMAIN/FALLBACK_IP остаются ПУСТЫМИ и должны быть заполнены человеком;
#   7. выполняет docker login ghcr.io (если передан PAT с правом read:packages);
#   8. настраивает UFW: deny incoming по умолчанию, разрешены только 22/80/443;
#   9. печатает инструкцию «что делать дальше».
#
# Запуск (пример):
#   sudo DEPLOY_PUBKEY="ssh-ed25519 AAAA... user@laptop" \
#        GHCR_TOKEN="ghp_xxx_с_правом_read:packages" \
#        bash scripts/bootstrap-server.sh
#
# Переменные окружения (все опциональны, кроме желательности DEPLOY_PUBKEY):
#   DEPLOY_PUBKEY    публичный SSH-ключ для пользователя deploy (строка целиком,
#                    как в ~/.ssh/id_ed25519.pub). Обязателен, чтобы отключить
#                    парольный вход и не потерять доступ к серверу.
#   GHCR_USER        логин GitHub для docker login (по умолчанию azamat-davletbaev).
#   GHCR_TOKEN       PAT с правом read:packages. Не нужен, если пакеты ghcr публичные
#                    (см. DEPLOY.md, раздел 0).
#   GITHUB_REPO      HTTPS-URL репозитория (по умолчанию GeoPulse на github.com).
#   REPO_SSH_URL     SSH-URL репозитория (git@github.com:owner/GeoPulse.git) —
#                    использовать вместе с DEPLOY_KEY_FILE для приватного репо.
#   DEPLOY_KEY_FILE  путь к ФАЙЛУ с приватным deploy-ключом, который уже лежит на
#                    сервере (например /root/geopulse_deploy). Скрипт скопирует его
#                    в ~deploy/.ssh/geopulse_deploy и пропишет в ~deploy/.ssh/config.
#   APP_DIR          каталог проекта (по умолчанию /opt/geopulse).
#   DEPLOY_USER      имя пользователя деплоя (по умолчанию deploy).
#   SSH_PORT         порт SSH (по умолчанию 22) — он же открывается в UFW.
#   BRANCH           ветка (по умолчанию main).
#
# Идемпотентность: повторный запуск на уже настроенном сервере безопасен —
# установка Docker пропускается, пользователь/права/ключи не дублируются,
# .env не перезаписывается, правила UFW повторно не добавляются.
# ============================================================================
set -euo pipefail

DEPLOY_PUBKEY="${DEPLOY_PUBKEY:-}"
GHCR_USER="${GHCR_USER:-azamat-davletbaev}"
GHCR_TOKEN="${GHCR_TOKEN:-}"
GITHUB_REPO="${GITHUB_REPO:-https://github.com/azamat-davletbaev/GeoPulse.git}"
REPO_SSH_URL="${REPO_SSH_URL:-git@github.com:azamat-davletbaev/GeoPulse.git}"
DEPLOY_KEY_FILE="${DEPLOY_KEY_FILE:-}"
APP_DIR="${APP_DIR:-/opt/geopulse}"
DEPLOY_USER="${DEPLOY_USER:-deploy}"
SSH_PORT="${SSH_PORT:-22}"
BRANCH="${BRANCH:-main}"

log()  { printf '\n\033[1;34m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m[ВНИМАНИЕ] %s\033[0m\n' "$*"; }
die()  { printf '\033[1;31m[ОШИБКА] %s\033[0m\n' "$*" >&2; exit 1; }

# ----------------------------------------------------------------------------
# 0. Root / sudo. Всё дальнейшее выполняется от root.
# ----------------------------------------------------------------------------
if [ "$(id -u)" -ne 0 ]; then
  warn "Скрипт требует root. Перезапускаю себя через sudo с сохранением переменных..."
  exec sudo --preserve-env=DEPLOY_PUBKEY,GHCR_USER,GHCR_TOKEN,GITHUB_REPO,REPO_SSH_URL,DEPLOY_KEY_FILE,APP_DIR,DEPLOY_USER,SSH_PORT,BRANCH \
    bash "$0" "$@"
fi

[ -f /etc/os-release ] || die "/etc/os-release не найден — это не Ubuntu?"
# shellcheck disable=SC1091
. /etc/os-release
[ "${ID:-}" = "ubuntu" ] || warn "Ожидалась Ubuntu, обнаружено ID=${ID:-unknown}. Продолжаю на свой риск."
case "${VERSION_ID:-}" in
  24.04) log "ОС: Ubuntu ${VERSION_ID} (${PRETTY_NAME:-})" ;;
  *)     warn "Проверялось на Ubuntu 24.04, обнаружено ${VERSION_ID:-unknown}." ;;
esac

export DEBIAN_FRONTEND=noninteractive

# ----------------------------------------------------------------------------
# 1. Обновление системы и базовые пакеты.
# ----------------------------------------------------------------------------
log "1/9 apt-get update && apt-get upgrade"
apt-get update
apt-get upgrade -y

log "1/9 Базовые пакеты: git, curl, ca-certificates, gnupg, ufw"
apt-get install -y --no-install-recommends \
  ca-certificates curl gnupg git ufw openssl jq

# ----------------------------------------------------------------------------
# 2. Docker Engine + Compose plugin из официального репозитория docker.com.
#    Ставим именно docker-compose-plugin (команда `docker compose`), а не
#    legacy-пакет docker-compose из репозитория Ubuntu.
# ----------------------------------------------------------------------------
log "2/9 Docker Engine + docker-compose-plugin"
if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
  log "Docker и compose plugin уже установлены — шаг пропущен"
else
  install -m 0755 -d /etc/apt/keyrings
  # Ключ репозитория Docker (идемпотентно перезаписывается).
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc

  DOCKER_ARCH="$(dpkg --print-architecture)"
  DOCKER_CODENAME="${UBUNTU_CODENAME:-${VERSION_CODENAME:-noble}}"
  cat >/etc/apt/sources.list.d/docker.list <<EOF
deb [arch=${DOCKER_ARCH} signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${DOCKER_CODENAME} stable
EOF

  apt-get update
  apt-get install -y --no-install-recommends \
    docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

  # Любой ранее установленный legacy-пакет docker-compose не нужен.
  if dpkg -l docker-compose 2>/dev/null | grep -q '^ii'; then
    warn "Обнаружен legacy-пакет docker-compose — удаляю (используется docker compose plugin)"
    apt-get remove -y docker-compose || true
  fi
fi

systemctl enable --now docker
docker --version
docker compose version

# ----------------------------------------------------------------------------
# 3. Пользователь deploy + группа docker.
# ----------------------------------------------------------------------------
log "3/9 Пользователь ${DEPLOY_USER} и группа docker"
if id -u "$DEPLOY_USER" >/dev/null 2>&1; then
  log "Пользователь ${DEPLOY_USER} уже существует"
else
  adduser --disabled-password --gecos "GeoPulse deploy" "$DEPLOY_USER"
fi

# Идемпотентно: повторное добавление в группу ничего не ломает.
usermod -aG docker "$DEPLOY_USER"
# Домашний каталог должен существовать, даже если пользователь создан раньше вручную.
install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 755 "/home/${DEPLOY_USER}"

# ----------------------------------------------------------------------------
# 4. SSH: ключ для deploy, отключение парольного входа.
# ----------------------------------------------------------------------------
log "4/9 SSH-доступ по ключу"
SSH_DIR="/home/${DEPLOY_USER}/.ssh"
AUTH_KEYS="${SSH_DIR}/authorized_keys"
install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 700 "$SSH_DIR"
touch "$AUTH_KEYS"

if [ -n "$DEPLOY_PUBKEY" ]; then
  case "$DEPLOY_PUBKEY" in
    ssh-*|ecdsa-*) : ;;
    *) die "DEPLOY_PUBKEY не похож на публичный SSH-ключ (ожидается строка, начинающаяся с ssh-...)" ;;
  esac
  # Идемпотентно: добавляем ключ, только если его ещё нет.
  if grep -qF "$DEPLOY_PUBKEY" "$AUTH_KEYS"; then
    log "Ключ уже присутствует в ${AUTH_KEYS}"
  else
    printf '%s\n' "$DEPLOY_PUBKEY" >>"$AUTH_KEYS"
    log "Ключ добавлен в ${AUTH_KEYS}"
  fi
else
  warn "DEPLOY_PUBKEY не передан — ключ не добавлен."
fi
chown -R "$DEPLOY_USER":"$DEPLOY_USER" "$SSH_DIR"
chmod 600 "$AUTH_KEYS"

# Парольная аутентификация отключается ТОЛЬКО если есть хотя бы один ключ,
# иначе можно потерять доступ к серверу.
HAVE_KEY=0
{ [ -s "$AUTH_KEYS" ] && HAVE_KEY=1; } || true
{ [ -s /root/.ssh/authorized_keys ] && HAVE_KEY=1; } || true

SSHD_DROPIN="/etc/ssh/sshd_config.d/99-geopulse-hardening.conf"
if [ "$HAVE_KEY" -eq 1 ]; then
  log "4/9 Отключаю парольный вход SSH (${SSHD_DROPIN})"
  # Директория drop-in может отсутствовать на минимальных образах.
  install -d -m 755 /etc/ssh/sshd_config.d
  cat >"$SSHD_DROPIN" <<'EOF'
# GeoPulse: только вход по ключу. Файл создан scripts/bootstrap-server.sh.
PubkeyAuthentication yes
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin prohibit-password
EOF
  chmod 644 "$SSHD_DROPIN"

  # Проверяем конфиг ДО перезапуска: сломанный sshd = потеря доступа.
  if ! command -v sshd >/dev/null 2>&1; then
    warn "sshd не найден в PATH — настройки SSH пропущены (проверьте openssh-server)"
  elif sshd -t; then
    systemctl reload ssh 2>/dev/null || systemctl reload sshd 2>/dev/null || systemctl restart ssh
    log "sshd перезагружен, вход по паролю отключён"
  else
    rm -f "$SSHD_DROPIN"
    die "sshd -t не принял конфиг — настройки SSH не изменены (доступ сохранён)."
  fi
else
  warn "Ни у ${DEPLOY_USER}, ни у root нет authorized_keys — парольный вход НЕ отключаю"
  warn "Передайте DEPLOY_PUBKEY и запустите скрипт повторно."
fi

# ----------------------------------------------------------------------------
# 5. Клонирование репозитория в $APP_DIR.
#    Вариант A (по умолчанию): публичный репозиторий по HTTPS.
#    Вариант B (приватный): deploy-ключ, переданный файлом в DEPLOY_KEY_FILE.
# ----------------------------------------------------------------------------
log "5/9 Клонирование репозитория в ${APP_DIR}"
install -d -o root -g root -m 755 "$(dirname "$APP_DIR")"

CLONE_URL="$GITHUB_REPO"
if [ -n "$DEPLOY_KEY_FILE" ]; then
  # --- Вариант B: приватный репозиторий через deploy-ключ GitHub ---
  [ -f "$DEPLOY_KEY_FILE" ] || die "DEPLOY_KEY_FILE=${DEPLOY_KEY_FILE} не найден"
  DEPLOY_SSH_KEY="/home/${DEPLOY_USER}/.ssh/geopulse_deploy"
  install -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 600 "$DEPLOY_KEY_FILE" "$DEPLOY_SSH_KEY"
  SSH_CONFIG="/home/${DEPLOY_USER}/.ssh/config"
  touch "$SSH_CONFIG"
  if ! grep -q 'Host github.com' "$SSH_CONFIG"; then
    cat >>"$SSH_CONFIG" <<EOF
Host github.com
    HostName github.com
    User git
    IdentityFile ${DEPLOY_SSH_KEY}
    IdentitiesOnly yes
EOF
  fi
  chown "$DEPLOY_USER":"$DEPLOY_USER" "$SSH_CONFIG"
  chmod 600 "$SSH_CONFIG"
  # known_hosts заранее, чтобы git не спрашивал подтверждение в неинтерактивном режиме.
  # Редирект выполняется внутри sudo-шелла: файл сразу принадлежит deploy.
  sudo -u "$DEPLOY_USER" bash -c "ssh-keyscan -H github.com >>'${SSH_DIR}/known_hosts' 2>/dev/null" || true
  chown "$DEPLOY_USER":"$DEPLOY_USER" "${SSH_DIR}/known_hosts" 2>/dev/null || true
  chmod 600 "${SSH_DIR}/known_hosts" 2>/dev/null || true
  CLONE_URL="$REPO_SSH_URL"
  log "Использую deploy-ключ: ${DEPLOY_SSH_KEY} -> ${CLONE_URL}"
else
  log "Использую HTTPS (публичный репозиторий): ${CLONE_URL}"
fi

if [ -d "${APP_DIR}/.git" ]; then
  log "Репозиторий уже клонирован — обновляю remote и делаю fetch"
  sudo -u "$DEPLOY_USER" git -C "$APP_DIR" remote set-url origin "$CLONE_URL"
  sudo -u "$DEPLOY_USER" git -C "$APP_DIR" fetch --prune origin
else
  if [ -d "$APP_DIR" ] && [ -n "$(ls -A "$APP_DIR" 2>/dev/null)" ]; then
    die "Каталог ${APP_DIR} существует и не пуст, но это не git-репозиторий. Освободите каталог и повторите запуск."
  fi
  install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 755 "$APP_DIR"
  sudo -u "$DEPLOY_USER" git clone --branch "$BRANCH" "$CLONE_URL" "$APP_DIR"
fi
# Владелец каталога — deploy: он запускает deploy.sh и делает git pull.
chown -R "$DEPLOY_USER":"$DEPLOY_USER" "$APP_DIR"

# ----------------------------------------------------------------------------
# 6. .env из .env.example.
#    Пароли генерируются случайно (hex — безопасно для sed), DOMAIN/FALLBACK_IP
#    остаются пустыми: их заполняет человек.
# ----------------------------------------------------------------------------
log "6/9 Файл ${APP_DIR}/.env"
ENV_FILE="${APP_DIR}/.env"
ENV_EXAMPLE="${APP_DIR}/.env.example"
# TODO-verify: .env.example в репозитории есть (проверено), но если его удалят —
# скрипт падает с понятной ошибкой, шаблон придётся положить рядом вручную.
[ -f "$ENV_EXAMPLE" ] || die ".env.example не найден в ${APP_DIR}. Добавьте шаблон в репозиторий."

if [ -f "$ENV_FILE" ]; then
  log ".env уже существует — НЕ перезаписываю (идемпотентность)"
else
  cp "$ENV_EXAMPLE" "$ENV_FILE"
  PG_PASS="$(openssl rand -hex 24)"
  CH_PASS="$(openssl rand -hex 24)"
  # Заменяем только плейсхолдеры паролей; остальное заполняет человек.
  sed -i "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=${PG_PASS}|" "$ENV_FILE"
  sed -i "s|^CLICKHOUSE_PASSWORD=.*|CLICKHOUSE_PASSWORD=${CH_PASS}|" "$ENV_FILE"
  log "Сгенерированы случайные пароли POSTGRES_PASSWORD и CLICKHOUSE_PASSWORD"
fi

# DOCKER_TAG — тег образов для docker compose (см. docker-compose.prod.yml).
# Добавляем, если строки нет (совместимость со старым .env, где был TAG=latest).
if ! grep -q '^DOCKER_TAG=' "$ENV_FILE"; then
  {
    printf '\n# Тег образов ghcr.io. Обновляется deploy.sh при каждом релизе\n'
    printf '# (значение — короткий SHA коммита; для отката задаётся вручную).\n'
    printf 'DOCKER_TAG=latest\n'
  } >>"$ENV_FILE"
  log "В .env добавлена строка DOCKER_TAG=latest"
fi

chown "$DEPLOY_USER":"$DEPLOY_USER" "$ENV_FILE"
chmod 600 "$ENV_FILE"   # секреты читает только deploy/docker compose

NEEDS_FILL=0
if grep -qE '^DOMAIN=$' "$ENV_FILE"; then NEEDS_FILL=1; fi
if grep -qE '^FALLBACK_IP=45\.12\.4\.88$' "$ENV_FILE"; then NEEDS_FILL=1; fi

# ----------------------------------------------------------------------------
# 7. docker login ghcr.io (нужен только для приватных пакетов).
# ----------------------------------------------------------------------------
log "7/9 docker login ghcr.io"
if [ -n "$GHCR_TOKEN" ]; then
  if printf '%s' "$GHCR_TOKEN" | sudo -u "$DEPLOY_USER" docker login ghcr.io --username "$GHCR_USER" --password-stdin; then
    log "Вход в ghcr.io выполнен от ${GHCR_USER} (креденшелы в /home/${DEPLOY_USER}/.docker/config.json)"
  else
    die "docker login ghcr.io не удался: проверьте, что PAT имеет право read:packages и не истёк."
  fi
else
  warn "GHCR_TOKEN не передан — вход в ghcr.io не выполнен."
  warn "Если пакеты geopulse-* приватные, сервер не сможет их тянуть:"
  warn "  либо сделайте пакеты публичными (DEPLOY.md, раздел 0),"
  warn "  либо выполните от ${DEPLOY_USER}: docker login ghcr.io -u ${GHCR_USER}"
fi

# ----------------------------------------------------------------------------
# 8. UFW: deny incoming, разрешены только SSH/80/443.
# ----------------------------------------------------------------------------
log "8/9 UFW"
ufw default deny incoming
ufw default allow outgoing
# Повторное добавление уже существующего правила — no-op (идемпотентно).
ufw allow "${SSH_PORT}/tcp"
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable
ufw status verbose

# ----------------------------------------------------------------------------
# 9. Итоговая инструкция.
# ----------------------------------------------------------------------------
log "9/9 Готово. Что делать дальше"
cat <<EOF

-----------------------------------------------------------------------------
Сервер подготовлен. Осталось:

1) Заполнить секреты (файл принадлежит ${DEPLOY_USER}, читать только под ним):
     sudo -u ${DEPLOY_USER} nano ${ENV_FILE}
   Обязательно:
     DOMAIN=            # DuckDNS-домен без https:// (например geopulse-demo.duckdns.org)
     FALLBACK_IP=       # публичный IP этого сервера (резервный вход по HTTP)
   Пароли POSTGRES_PASSWORD/CLICKHOUSE_PASSWORD уже сгенерированы случайно.

2) Не закрывая текущую сессию, проверить вход по ключу:
     ssh -p ${SSH_PORT} ${DEPLOY_USER}@<IP_СЕРВЕРА>

3) Первый деплой (тот же скрипт, что запускает CI/CD):
     sudo -u ${DEPLOY_USER} IMAGE_TAG=latest bash ${APP_DIR}/deploy.sh

4) Первичный импорт данных Volve (одноразово, после первого деплоя):
     cd ${APP_DIR} && sudo -u ${DEPLOY_USER} docker compose -f docker-compose.prod.yml run --rm ingestion

5) Сид координат устьев скважин (нужен для маркеров на карте):
     cd ${APP_DIR} && sudo -u ${DEPLOY_USER} docker compose -f docker-compose.prod.yml exec -T postgres \\
       psql -U <POSTGRES_USER> -d <POSTGRES_DB> < sql/seed_well_locations.sql

6) Секреты GitHub Actions (Settings -> Secrets and variables -> Actions):
     SSH_HOST          — IP/домен сервера
     SSH_USER          — ${DEPLOY_USER}
     SSH_PRIVATE_KEY   — приватная пара к DEPLOY_PUBKEY
     DEPLOY_TOKEN_GHCR — не нужен, если deploy.sh не делает docker login
                         (при публичных пакетах ghcr).

Caddy выпустит сертификат Let's Encrypt, только если DOMAIN уже указывает на IP
этого сервера, а порты 80/443 открыты (UFW их разрешает).

ВНИМАНИЕ: группа docker применена к ${DEPLOY_USER}; команды docker требуют
перелогина или запуска через sudo -u ${DEPLOY_USER} (как в пунктах выше).
-----------------------------------------------------------------------------
EOF

if [ "$NEEDS_FILL" -eq 1 ]; then
  warn "В ${ENV_FILE} остались незаполненные DOMAIN/FALLBACK_IP — деплой без них не пройдёт."
fi
