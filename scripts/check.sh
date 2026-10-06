#!/usr/bin/env bash
# Runs the same checks CI will run, locally and in Docker. Run before opening a pull request.
# CI (GitHub Actions) is switched off until the MVP 1 items are done.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "== API: build and test (.NET 10)"
docker run --rm -v "$ROOT":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test api/Pesaroll.slnx

echo "== Web: lint and build"
docker run --rm -v "$ROOT":/src -v pesaroll_check_node_modules:/src/web/node_modules -v pesaroll_pnpm_store:/pnpm-store -e npm_config_store_dir=/pnpm-store -w /src/web node:22-alpine \
  sh -c "corepack enable && corepack prepare pnpm@8.10.2 --activate >/dev/null && pnpm install --frozen-lockfile && pnpm lint && pnpm build"

echo "== Database: apply schema and run tests"
"$ROOT/database/tests/run.sh"

echo "All checks passed"
