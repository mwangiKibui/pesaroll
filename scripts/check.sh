#!/usr/bin/env bash
# Runs the same checks CI will run, locally and in Docker. Run before opening a pull request.
# CI (GitHub Actions) is switched off until the MVP 1 items are done.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "== API: build and test (.NET 10) against PostgreSQL 18 with the schema deployed"
RUN="pesaroll-check-$$"
docker network create "$RUN" >/dev/null
docker run -d --rm --name "$RUN-db" --network "$RUN" -e POSTGRES_PASSWORD=test postgres:18 >/dev/null
trap 'docker stop "$RUN-db" >/dev/null 2>&1; docker network rm "$RUN" >/dev/null 2>&1' EXIT
docker build -q -t pesaroll-db-tools "$ROOT/database" >/dev/null
until docker exec "$RUN-db" pg_isready -U postgres >/dev/null 2>&1; do sleep 1; done
sleep 2
docker run --rm --network "$RUN" -v "$ROOT/database":/db:ro \
  -e PGHOST="$RUN-db" -e PGDATABASE=postgres -e PGUSER=postgres -e PGPASSWORD=test -e PGSSLMODE=disable \
  pesaroll-db-tools deploy >/dev/null
docker run --rm --network "$RUN" -v "$ROOT":/src -w /src \
  -e PESAROLL_TEST_DB="Host=$RUN-db;Database=postgres;Username=postgres;Password=test" \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet test api/Pesaroll.slnx

echo "== Web: tests, lint and build"
docker run --rm -v "$ROOT":/src -v pesaroll_check_node_modules:/src/web/node_modules -v pesaroll_pnpm_store:/pnpm-store -e npm_config_store_dir=/pnpm-store -w /src/web node:22-alpine \
  sh -c "corepack enable && corepack prepare pnpm@8.10.2 --activate >/dev/null && pnpm install --frozen-lockfile && pnpm test && pnpm lint && pnpm build"

echo "== Database: apply schema and run tests"
"$ROOT/database/tests/run.sh"

echo "All checks passed"
