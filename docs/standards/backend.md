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
- Every endpoint declares what it needs, and `EndpointAccessTests` fails the build if one doesn't: `.RequirePermission(Permissions.X)`, `.SignedIn()` (any member: the company selector's list, `/me`, reference lists like countries and banks) or `.Public()` (sign-up, sign-in, invitations, health). `RequirePermission` turns away someone with the permission for no company (ACC-8001) before any work. Permission keys live in `Pesaroll.Shared.Tenancy.Permissions` and the permissions table (`post-deploy/0014_access_roles.sql`); roles bundle them, and code checks keys, never role names.
  - Permissions come in **areas** (`permission_areas`: Companies, Employees, Locations, ...), each with a **View** key (`employees.view`) and a **Manage** key (`employees.manage`). Reads need View; changes need Manage; Manage brings View (`member_grants`). A new area is rows in the catalogue, not code.
  - Groups: the four standard ones (Administrators, HR, Payroll, Approvers), whose permissions an organization may change for itself (`role_customizations`, Back to standard), and groups an organization adds (ACC-08): a role it owns (`roles.organization_id`, row-level security), with its permissions in the same tables. Group keys are never a fixed list in code: check them against the organization's groups (`GroupKeysAsync`, `access_group`).
  - A person's permissions come from their security groups, each for all companies or chosen ones, and are read once per request (`TenantContext.Grants`), so a group change applies on the next request.
  - Reads of a company's own records narrow to the companies where the person has View, not every company they belong to: `tenant.CompaniesFor(Permissions.EmployeesView)`, and asking for another company answers ACC-8001. Row-level security stays the floor underneath.
  - Changes to a company's own records (its employees, paying accounts, payroll history, the company itself) need the permission for that company: `Access.Require(tenant, Permissions.Employees, companyId)`.
  - Organization-wide setup (structure, locations, schedules, leave, earnings and deductions, new companies) needs it for every company: `Access.Require(tenant, Permissions.Structure)`.
  - Uploads declare theirs on the `ImportSpec` (`Permission`, and `PerCompany` when rows belong to a company).
- Sessions (AUTH-01, AUTH-05): a 15-minute access token naming a session, and a refresh cookie whose hash alone is stored; every request checks the session, so ending one works at once. People list their own active sessions (`GET /api/auth/sessions`, the one asking marked, the device named from the user agent by `Devices.Describe`) and end one (`POST /api/auth/sessions/{id}/end`) or every other (`POST /api/auth/sessions/end-others`); row-level security keeps sessions to their owner, and someone else's answers AUTH-1017.
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
