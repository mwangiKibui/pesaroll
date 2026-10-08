# Pesaroll

HR and payroll for East Africa, Kenya first. One app and one sign-in for customer admins, employees, partners and the Pesaroll back office; what a person sees depends on their roles.

## Repository layout

- `api/` .NET 10 solution (`Pesaroll.slnx`)
  - `src/Pesaroll.Api` ASP.NET Core API
  - `src/Pesaroll.Worker` Hangfire background worker
  - `src/Pesaroll.Shared` cross-cutting code: errors, tenancy context, money
  - `src/Modules/*` one project per module (modular monolith), e.g. `Pesaroll.Payroll.Engine`
  - `tests/*` xUnit tests, including the golden payroll suite
- `web/` React + TypeScript (Vite), one single-page app for all roles
- `database/` the database project: the schema as code, no migrations
- `compose.yml` local development: `docker compose up -d --build` starts the database, deploys the schema, then starts the API and web app
- `deploy/` production Compose file, Caddy and the web Dockerfile for DigitalOcean
- `docs/standards/` backend, database, error-code and UI standards

## Commands

Everything runs in Docker, locally and in production. Nobody needs .NET or Node installed.

```bash
# run the whole app locally: database, API (hot reload), web app (hot reload)
docker compose up -d --build                     # everything; app :5173, API :8080 (/swagger), database :55432
docker compose down -v                           # stop and wipe the local database

# run every check before opening a pull request (API tests, web tests, lint and build, database tests)
scripts/check.sh

# database schema (no migrations): see docs/standards/database.md
docker compose run --rm db-tools plan       # what a schema change would do (compose up applies it automatically)
database/tests/run.sh                       # fresh PostgreSQL 18: deploy, re-plan must be empty, tests

# production images (built from the repo root)
docker build -f api/Dockerfile -t pesaroll-api .
docker build -f api/Dockerfile --build-arg PROJECT=Pesaroll.Worker -t pesaroll-worker .
docker build -f deploy/web.Dockerfile -t pesaroll-web .
```

CI (GitHub Actions) is switched off until the MVP 1 items are done. The workflow waits in `.github/workflows-disabled/`; until then `scripts/check.sh` is the gate.

## Rules that apply to every change

1. **Tenancy.** Every customer table has `organization_id`; company-owned tables also have `company_id`. Row-level security enforces it in the database. Every `DbContext` must add `TenantSessionInterceptor` (see `OrganizationsModule`), which switches each connection to the `pesaroll_app` role and sets `app.org_id` and `app.company_ids` from the request's `TenantContext`. Never query without it, and never use a database role that bypasses row-level security. Every new customer table gets a test like `TenancyTests`.
2. **Per company.** Companies in one organization can differ. Every feature states how it works per company, and every list or setup screen uses the company selector.
3. **No migrations.** Change the schema only in `database/schema/`, including grants (pgschema revokes grants it doesn't see there). EF Core reads and writes data and never creates or alters tables. Renames and data reshaping need a script in `database/pre-deploy/`; reference data goes in `database/post-deploy/` as re-runnable upserts. After any schema change, `database/tests/run.sh` must pass.
4. **Money.** `decimal` in C#, `numeric(18,2)` in PostgreSQL, never `double` or `float`. Statutory rates are data (rule packs), never constants in code.
5. **History.** Anything that affects pay is effective-dated or locked after approval, never overwritten.
6. **Errors.** Every error returned to a user has a code from the catalogue in `docs/standards/errors.md` and a reference. Don't invent codes ad hoc: add them to the catalogue.
7. **Permissions.** Access comes from security groups: a group carries roles (bundles of permissions) and a company scope, and people get access by joining groups. Every endpoint checks a permission on the server; code never checks role or group names. Relationship-based access (a person's own records, a line manager's team) is checked separately. Customer roles and back office roles are separate. Hiding a button is never the protection.
8. **Personal data.** Never put salaries, ID numbers, KRA PINs or bank details in logs, error context, push notifications or emails.
9. **DigitalOcean first.** Don't add services that need extra servers; use DigitalOcean products before outside ones.
10. **UI.** Screens are built from the components in `web/src/components/` and follow `docs/standards/ui.md`: colour tokens only (never hex or palette colours), light only for now (dark values kept and tested), a placeholder on every field, every word through `web/src/i18n/locales/` (English only for now, Swahili kept in step), checked at phone width. See them at http://localhost:5173/dev/components.
11. **Docker.** Every service runs in a container, locally and in production. New services get a Dockerfile and a place in `compose.yml` (local) and `deploy/compose.prod.yml` (production).

## How we work in parallel

- **Branch per task, not per layer.** Short-lived branches off `main`, named after the task: `AUTH-03/api`, `AUTH-03/web`. Merge within days; no long-lived `ui` or `api` branches.
- **Contract first.** When the API and the UI of one feature are built by different people, first merge a small pull request with the endpoint's request and response shapes (returning "not implemented") and the regenerated TypeScript client. Then both sides work in parallel; the UI uses mock responses until the API lands.
- **Feature flags** hide unfinished features on `main` and in staging.
- **Generated files** (the API client, lockfiles) are regenerated after a merge, never hand-merged.
- **Database changes** never conflict on ordering: each branch edits the schema files it needs, and the deploy works from the merged result.
- **Pull requests:** run `scripts/check.sh`, get one review, then merge.

## How work is tracked

- Tasks live in the GitHub project "Pesaroll" (issues titled `[AUTH-03] …`).
- **Plan before building, always.** Use the `build` skill (`.claude/skills/build/SKILL.md`): a plan with problem statement, implementation steps, risks and success, approved by the user before any code. The approved plan is posted on the task's issue. When the work is done and has something to try by hand, give the user local test steps and wait for their confirmation before opening the pull request.
- **Test-driven development.** Write a failing test first, make it pass, then refactor. Backend: xUnit unit tests, plus integration tests for endpoints and the database. Frontend: Vitest and React Testing Library unit tests. Details in the `build` skill.
- Move tasks through the project's stages with `scripts/task-stage.sh <TASK-ID> "<Stage>"` (Not planned, Planning, Ready to build, In progress, Done).
- Branch names and commit messages start with the task ID, e.g. `PLAT-15/database-project`.
- Decisions and designs live in the project docs: Problem Statement, Technical Architecture, Stack Decision, and the Schema Design page. Follow the schema page's conventions and review checklist for any table change.
