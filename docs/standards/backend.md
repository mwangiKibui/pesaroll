# Backend standards (PLAT-09)

Status: draft for review.

## Structure

- A modular monolith: one API process and one worker process, built from the same modules.
- One project per module under `api/src/Modules/` (for example `Pesaroll.Payroll.Engine`, later `Pesaroll.Organizations`, `Pesaroll.Leave`). A module exposes a small public surface; other modules don't reach into its internals or its tables.
- `Pesaroll.Shared` holds only cross-cutting code: errors, tenancy context, money helpers, clock.
- The payroll engine is a pure function: pay inputs plus a rule pack version in, every earning, deduction and employer cost out. No database or clock access inside it.

## API

- REST over JSON, with an OpenAPI document generated from the code. The web app's TypeScript client is generated from it.
- Routes are plural nouns: `/api/companies/{companyId}/employees`. Company-scoped routes carry the company in the path.
- Every endpoint declares the permission it needs; the check runs on the server before any work. Permission keys live in `Pesaroll.Shared.Tenancy.Permissions` and the permissions table (`post-deploy/0014_access_roles.sql`); roles bundle them, and code checks keys, never role names.
  - A person's permissions come from their security groups, each for all companies or chosen ones, and are read once per request (`TenantContext.Grants`).
  - Changes to a company's own records (its employees, paying accounts, payroll history, the company itself) need the permission for that company: `Access.Require(tenant, Permissions.Employees, companyId)`.
  - Organization-wide setup (structure, locations, schedules, leave, earnings and deductions, new companies) needs it for every company: `Access.Require(tenant, Permissions.Structure)`.
  - Uploads declare theirs on the `ImportSpec` (`Permission`, and `PerCompany` when rows belong to a company).
- Lists are paged (`?page=&pageSize=`, maximum 200) and filterable by company with the company selector's values.
- Errors use one shape (see `errors.md`):

```json
{ "code": "PAY-2001", "message": "Wanjiru Kamau has no KRA PIN, so PAYE can't be filed.", "reference": "7F3K92QD", "fields": {} }
```

## Data access

- EF Core with Npgsql for reading and writing data only. No EF migrations, no `EnsureCreated`: the schema belongs to `database/`.
- Every request runs in a transaction that first sets `app.org_id`, `app.company_ids` (and `app.staff_id`, `app.grant_id` for back office access) so row-level security applies.
- IDs are UUID version 7, generated in the app with `Guid.CreateVersion7()`.

## Money and dates

- `decimal` for every amount and rate. Rounding is defined per country and pay item in the rule pack, applied in one place.
- Dates are `DateOnly`; timestamps are `DateTimeOffset` in UTC. Pay periods are date ranges.

## Background work

- Hangfire, storing jobs in PostgreSQL. Long work (imports, pay runs, PDFs, notifications) is always a job, never inside a request.
- Jobs are idempotent: running one twice gives the same result.

## Testing

- xUnit. The golden payroll suite (worked examples reviewed by a Kenyan payroll accountant) runs on every change and must pass 100%.
- Tenancy tests run against a real PostgreSQL (Testcontainers) and prove one organization can't read another's rows.

## Logging

- Serilog, structured. Every log line carries the request reference and organization id, never salaries, ID numbers, KRA PINs or bank details.
