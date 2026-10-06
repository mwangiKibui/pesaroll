#!/usr/bin/env bash
# End-to-end test of the database project, all in Docker:
#   1. deploy (pre-deploy, pgschema apply, post-deploy) to a fresh PostgreSQL 18
#   2. plan again: must report no changes, proving the deploy is repeatable
#   3. run the SQL tests in tests/ (tenancy and row-level security)
set -euo pipefail
DB_DIR="$(cd "$(dirname "$0")/.." && pwd)"
RUN="pesaroll-dbtest-$$"
IMAGE="${PG_IMAGE:-postgres:18}"

docker network create "$RUN" >/dev/null
docker run -d --rm --name "$RUN-db" --network "$RUN" -e POSTGRES_PASSWORD=test "$IMAGE" >/dev/null
trap 'docker stop "$RUN-db" >/dev/null 2>&1; docker network rm "$RUN" >/dev/null 2>&1' EXIT
docker build -q -t pesaroll-db-tools "$DB_DIR" >/dev/null
until docker exec "$RUN-db" pg_isready -U postgres >/dev/null 2>&1; do sleep 1; done
sleep 2

tools() {
  docker run --rm --network "$RUN" -v "$DB_DIR":/db:ro \
    -e PGHOST="$RUN-db" -e PGDATABASE=postgres -e PGUSER=postgres -e PGPASSWORD=test -e PGSSLMODE=disable \
    pesaroll-db-tools "$@"
}
psql() { docker exec -i "$RUN-db" psql -U postgres -v ON_ERROR_STOP=1 -q "$@"; }

echo "== Deploy to a fresh database"
tools deploy

echo "== Plan again (must be empty)"
PLAN="$(tools plan)"
if ! grep -qi "no changes" <<<"$PLAN"; then
  echo "$PLAN"
  echo "FAIL: a second plan after deploy should have no changes"
  exit 1
fi
echo "  no changes"

echo "== Deploy again (must be repeatable)"
tools deploy >/dev/null

echo "== Seed test data"
psql <<'SQL'
insert into users (id, email, name) values
  ('00000000-0000-7000-8000-0000000000f1', 'owner1@example.com', 'Owner One'),
  ('00000000-0000-7000-8000-0000000000f2', 'owner2@example.com', 'Owner Two');
insert into organizations (id, name, created_by) values
  ('00000000-0000-7000-8000-000000000001', 'Org One', '00000000-0000-7000-8000-0000000000f1'),
  ('00000000-0000-7000-8000-000000000002', 'Org Two', '00000000-0000-7000-8000-0000000000f2');
insert into companies (id, organization_id, legal_name, country_code, currency) values
  ('00000000-0000-7000-8000-0000000000a1', '00000000-0000-7000-8000-000000000001', 'One Kenya Ltd', 'KE', 'KES'),
  ('00000000-0000-7000-8000-0000000000a2', '00000000-0000-7000-8000-000000000001', 'One Uganda Ltd', 'UG', 'UGX'),
  ('00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-000000000002', 'Two Kenya Ltd', 'KE', 'KES');
SQL

echo "== Tests"
for f in "$DB_DIR"/tests/*.sql; do
  docker exec -i "$RUN-db" psql -U postgres -v ON_ERROR_STOP=1 -q < "$f" && echo "  pass  $(basename "$f")"
done
echo "All database tests passed"
