#!/usr/bin/env bash
# Database project commands. Runs inside the tools image (see Dockerfile) with this folder at /db.
# Connection comes from the standard PostgreSQL variables: PGHOST, PGPORT, PGDATABASE, PGUSER,
# PGPASSWORD, PGSSLMODE.
#
#   plan     show what would change in the database to match schema/ (changes nothing)
#   deploy   run new pre-deploy scripts, apply the schema, then run all post-deploy scripts
#
# Example (local database from compose.yml):
#   docker run --rm --network pesaroll_default -v "$PWD/database":/db \
#     -e PGHOST=db -e PGDATABASE=pesaroll -e PGUSER=pesaroll -e PGPASSWORD=pesaroll \
#     pesaroll-db-tools plan
set -euo pipefail
cd /db
: "${PGDATABASE:?set PGDATABASE}" "${PGUSER:?set PGUSER}"

psql_run() { psql -v ON_ERROR_STOP=1 -q "$@"; }

# pgschema validates the desired schema in a scratch database. We point it at a scratch
# database on the same server instead of its built-in temporary PostgreSQL, which would
# download binaries at runtime. The scratch database is created on first use.
export PGSCHEMA_PLAN_HOST="${PGSCHEMA_PLAN_HOST:-${PGHOST:-localhost}}"
export PGSCHEMA_PLAN_PORT="${PGSCHEMA_PLAN_PORT:-${PGPORT:-5432}}"
export PGSCHEMA_PLAN_DB="${PGSCHEMA_PLAN_DB:-${PGDATABASE}_plan}"
export PGSCHEMA_PLAN_USER="${PGSCHEMA_PLAN_USER:-$PGUSER}"
export PGSCHEMA_PLAN_PASSWORD="${PGSCHEMA_PLAN_PASSWORD:-${PGPASSWORD:-}}"
export PGSCHEMA_PLAN_SSLMODE="${PGSCHEMA_PLAN_SSLMODE:-${PGSSLMODE:-prefer}}"

ensure_plan_db() {
  if [ -z "$(psql -tAc "select 1 from pg_database where datname = '$PGSCHEMA_PLAN_DB'")" ]; then
    psql_run -c "create database \"$PGSCHEMA_PLAN_DB\""
  fi
}

plan() {
  pgschema plan --db "$PGDATABASE" --user "$PGUSER" --schema public \
    --file schema/main.sql --output-human stdout --no-color
}

pre_deploy() {
  # Each pre-deploy script runs once; deploy_scripts records which have run.
  # Same definition as schema/platform/deploy_scripts.sql, so the schema apply sees no change.
  psql_run <<'SQL'
create table if not exists deploy_scripts (
    name        text primary key,
    applied_at  timestamptz not null default now()
);
SQL
  for f in pre-deploy/*.sql; do
    [ -e "$f" ] || continue
    name="$(basename "$f")"
    if [ -z "$(psql -tAc "select 1 from deploy_scripts where name = '$name'")" ]; then
      echo "pre-deploy: $name"
      psql_run -1 -f "$f" -c "insert into deploy_scripts (name) values ('$name')"
    fi
  done
}

apply_schema() {
  pgschema apply --db "$PGDATABASE" --user "$PGUSER" --schema public \
    --file schema/main.sql --auto-approve --no-color
}

post_deploy() {
  # Post-deploy scripts are re-runnable (upserts, grants) and run on every deploy.
  for f in post-deploy/*.sql; do
    [ -e "$f" ] || continue
    echo "post-deploy: $(basename "$f")"
    psql_run -1 -f "$f"
  done
}

case "${1:-}" in
  plan)   ensure_plan_db; plan ;;
  deploy) ensure_plan_db; pre_deploy; apply_schema; post_deploy; echo "Deploy complete" ;;
  *)      echo "usage: db.sh plan|deploy" >&2; exit 2 ;;
esac
