#!/usr/bin/env bash
# Бэкап PostgreSQL (GeoPulse) в каталог backup/ с датой в имени файла.
# Формат — pg_dump custom (-Fc, сжатый), подходит для pg_restore.
#
# Запуск вручную:
#   bash scripts/backup-pg.sh [каталог_бэкапов]
#
# Пример cron (ежедневно в 02:30):
#   30 2 * * * cd /opt/geopulse && bash scripts/backup-pg.sh /opt/geopulse/backup >> /var/log/geopulse-backup.log 2>&1
set -euo pipefail

BACKUP_DIR="${1:-$(cd "$(dirname "$0")/.." && pwd)/backup}"
mkdir -p "$BACKUP_DIR"

DB="${POSTGRES_DB:-geopulse_core}"
USER="${POSTGRES_USER:-geopulse}"
CONTAINER="${POSTGRES_CONTAINER:-geopulse-postgres}"
STAMP="$(date +%Y%m%d-%H%M%S)"

# Пароль не нужен: pg_dump выполняется внутри контейнера от имени пользователя БД.
docker exec "$CONTAINER" pg_dump -U "$USER" -d "$DB" -Fc > "$BACKUP_DIR/geopulse-$STAMP.dump"

# Ротация: оставляем последние 14 бэкапов.
ls -1 "$BACKUP_DIR"/geopulse-*.dump 2>/dev/null | sort | head -n -14 | xargs -r rm -f

echo "Бэкап создан: $BACKUP_DIR/geopulse-$STAMP.dump"
