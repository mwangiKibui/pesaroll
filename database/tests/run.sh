#!/usr/bin/env bash
# Applies the schema to a throwaway PostgreSQL in Docker and runs the database tests.
# Usage: database/tests/run.sh   (from the repo root or anywhere)
set -euo pipefail
DB_DIR="$(cd "$(dirname "$0")/.." && pwd)"
NAME="pesaroll-dbtest-$$"
IMAGE="${PG_IMAGE:-postgres:17}"

docker run -d --rm --name "$NAME" -e POSTGRES_PASSWORD=test -v "$DB_DIR":/db:ro "$IMAGE" >/dev/null
trap 'docker stop "$NAME" >/dev/null' EXIT
until docker exec "$NAME" pg_isready -U postgres >/dev/null 2>&1; do sleep 1; done
sleep 2

psql() { docker exec -i "$NAME" psql -U postgres -v ON_ERROR_STOP=1 -q "$@"; }

echo "Applying schema"
docker exec -w /db/schema "$NAME" psql -U postgres -v ON_ERROR_STOP=1 -q -f main.sql
echo "Applying post-deploy scripts"
for f in "$DB_DIR"/post-deploy/*.sql; do psql -f "/db/post-deploy/$(basename "$f")"; done

echo "Seeding test data"
psql <<'SQL'
create role pesaroll_app;
grant select, insert, update, delete on all tables in schema public to pesaroll_app;
grant execute on all functions in schema public to pesaroll_app;
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

echo "Running tests"
for f in "$DB_DIR"/tests/*.sql; do
  psql -f "/db/tests/$(basename "$f")" && echo "  pass  $(basename "$f")"
done
echo "All database tests passed"
