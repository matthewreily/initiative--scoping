#!/usr/bin/env bash
# Copy the GCP dev database into the local compose `postgres` service.
#
#   docker compose run --rm pull-dev-db
#
# Source: the Cloud SQL instance named in the `<app>-<env>-db-connection` secret of
# $GCP_PROJECT (default initiative-scoping-dev), reached through the Cloud SQL Auth Proxy
# with the gcloud login mounted from the host. Needs roles/cloudsql.client and
# roles/secretmanager.secretAccessor on that project.
# Target: $TARGET_HOST/$TARGET_DB (defaults to the compose `postgres` service); the public
# schema is replaced. Data Protection keys are not copied (they are wrapped with the dev
# project's KMS key and unusable locally); the app recreates them on first start.
#
# For tests / non-GCP sources set SOURCE_HOST (+ SOURCE_PORT/SOURCE_USER/SOURCE_PASSWORD/
# SOURCE_DB) and the proxy + secret lookup are skipped.
set -euo pipefail

GCP_PROJECT="${GCP_PROJECT:-initiative-scoping-dev}"
SECRET_NAME="${SECRET_NAME:-${GCP_PROJECT}-db-connection}"
TARGET_HOST="${TARGET_HOST:-postgres}"
TARGET_PORT="${TARGET_PORT:-5432}"
TARGET_DB="${TARGET_DB:-initiative_scoping}"
TARGET_USER="${TARGET_USER:-postgres}"
TARGET_PASSWORD="${TARGET_PASSWORD:-devpass}"
DUMP="${DUMP:-/tmp/dev.dump}"

log() { printf '\n==> %s\n' "$*"; }

if [[ -z "${SOURCE_HOST:-}" ]]; then
  log "Reading connection secret $SECRET_NAME from $GCP_PROJECT"
  if ! gcloud auth print-access-token >/dev/null 2>&1; then
    echo "gcloud is not logged in. On the host run: gcloud auth login" >&2
    echo "(Windows: set GCLOUD_CONFIG_DIR=%APPDATA%\\gcloud so the login is mounted into the container.)" >&2
    exit 1
  fi
  conn="$(gcloud secrets versions access latest --secret "$SECRET_NAME" --project "$GCP_PROJECT")"
  get() { printf '%s' "$conn" | tr ';' '\n' | awk -F= -v k="$1" '$1==k {print substr($0, length(k)+2)}'; }
  instance="$(get Host)"; instance="${instance#/cloudsql/}"
  SOURCE_DB="$(get Database)"; SOURCE_USER="$(get Username)"; SOURCE_PASSWORD="$(get Password)"
  SOURCE_HOST=127.0.0.1; SOURCE_PORT=5433

  log "Starting Cloud SQL Auth Proxy for $instance"
  cloud-sql-proxy "$instance" --port "$SOURCE_PORT" --token "$(gcloud auth print-access-token)" &
  proxy_pid=$!
  trap 'kill $proxy_pid 2>/dev/null || true' EXIT
  for _ in $(seq 1 30); do
    PGPASSWORD="$SOURCE_PASSWORD" pg_isready -h "$SOURCE_HOST" -p "$SOURCE_PORT" -U "$SOURCE_USER" -q && break
    sleep 1
  done
fi
SOURCE_PORT="${SOURCE_PORT:-5432}"; SOURCE_DB="${SOURCE_DB:-initiative_scoping}"
SOURCE_USER="${SOURCE_USER:-app}"; SOURCE_PASSWORD="${SOURCE_PASSWORD:-}"

log "Dumping $SOURCE_DB from $SOURCE_HOST:$SOURCE_PORT"
PGPASSWORD="$SOURCE_PASSWORD" pg_dump -h "$SOURCE_HOST" -p "$SOURCE_PORT" -U "$SOURCE_USER" -d "$SOURCE_DB" \
  -Fc --no-owner --no-privileges -f "$DUMP"
ls -la "$DUMP"

log "Restoring into $TARGET_HOST:$TARGET_PORT/$TARGET_DB (replacing schema public)"
export PGPASSWORD="$TARGET_PASSWORD"
for _ in $(seq 1 30); do pg_isready -h "$TARGET_HOST" -p "$TARGET_PORT" -U "$TARGET_USER" -q && break; sleep 1; done
psql -h "$TARGET_HOST" -p "$TARGET_PORT" -U "$TARGET_USER" -d "$TARGET_DB" -v ON_ERROR_STOP=1 -q \
  -c 'DROP SCHEMA public CASCADE; CREATE SCHEMA public;'
pg_restore -h "$TARGET_HOST" -p "$TARGET_PORT" -U "$TARGET_USER" -d "$TARGET_DB" \
  --no-owner --no-privileges --exit-on-error "$DUMP"
psql -h "$TARGET_HOST" -p "$TARGET_PORT" -U "$TARGET_USER" -d "$TARGET_DB" -v ON_ERROR_STOP=1 -q \
  -c 'TRUNCATE "DataProtectionKeys";'
rm -f "$DUMP"

log "Done. Start the app (docker compose --profile dev up); pending migrations run on startup."
psql -h "$TARGET_HOST" -p "$TARGET_PORT" -U "$TARGET_USER" -d "$TARGET_DB" -tA \
  -c 'SELECT count(*) || '"'"' initiatives, '"'"' || (SELECT count(*) FROM "UserAccounts") || '"'"' users'"'"' FROM "Initiatives";'
