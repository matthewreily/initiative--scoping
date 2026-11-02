#!/usr/bin/env bash
# Restore a pg_dump snapshot (custom format, e.g. from `pull-dev-db --save`) into the local
# compose `postgres` service, replacing the public schema.
#
#   docker compose run --rm restore-db                          # newest backups/*.dump
#   docker compose run --rm restore-db backups/dev-<stamp>.dump # a specific snapshot
#
# Paths are relative to the repo root (./backups is mounted at /backups). Data Protection
# keys are cleared (they are KMS-wrapped in GCP and unusable locally); the app recreates
# them, and applies any newer migrations, on its next start.
set -euo pipefail

TARGET_HOST="${TARGET_HOST:-postgres}"
TARGET_PORT="${TARGET_PORT:-5432}"
TARGET_DB="${TARGET_DB:-initiative_scoping}"
TARGET_USER="${TARGET_USER:-postgres}"
TARGET_PASSWORD="${TARGET_PASSWORD:-devpass}"
BACKUP_DIR="${BACKUP_DIR:-/backups}"

log() { printf '\n==> %s\n' "$*"; }

dump="${1:-}"
if [[ -z "$dump" ]]; then
  dump="$(ls -1t "$BACKUP_DIR"/*.dump 2>/dev/null | head -n 1 || true)"
  if [[ -z "$dump" ]]; then
    echo "No snapshot found in ./backups. Create one with: docker compose run --rm pull-dev-db --save" >&2
    exit 1
  fi
elif [[ ! -f "$dump" && -f "$BACKUP_DIR/${dump#backups/}" ]]; then
  dump="$BACKUP_DIR/${dump#backups/}"
fi
if [[ ! -f "$dump" ]]; then
  echo "Snapshot not found: $1 (put it in ./backups)" >&2
  exit 1
fi

export PGPASSWORD="$TARGET_PASSWORD"
psql_target() { psql -h "$TARGET_HOST" -p "$TARGET_PORT" -U "$TARGET_USER" -d "$TARGET_DB" -v ON_ERROR_STOP=1 "$@"; }

log "Restoring $(basename "$dump") into $TARGET_HOST:$TARGET_PORT/$TARGET_DB (replacing schema public)"
for _ in $(seq 1 30); do pg_isready -h "$TARGET_HOST" -p "$TARGET_PORT" -U "$TARGET_USER" -q && break; sleep 1; done
psql_target -q -c 'DROP SCHEMA public CASCADE; CREATE SCHEMA public;'
pg_restore -h "$TARGET_HOST" -p "$TARGET_PORT" -U "$TARGET_USER" -d "$TARGET_DB" \
  --no-owner --no-privileges --exit-on-error "$dump"
psql_target -q -c 'TRUNCATE "DataProtectionKeys";'

log "Done. Start the app (docker compose up --build, or --profile dev up); pending migrations run on startup."
psql_target -tA \
  -c 'SELECT count(*) || '"'"' initiatives, '"'"' || (SELECT count(*) FROM "UserAccounts") || '"'"' users'"'"' FROM "Initiatives";'
