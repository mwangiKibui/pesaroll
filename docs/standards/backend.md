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

## Rule packs (PAY-01)

- A country's statutory rates, bands, caps and rounding are a **rule pack**: a JSON file in `database/rule-packs/` (e.g. `KE-2026.02.json`), published by `post-deploy/0016_rule_packs.sql` into the `rule_packs` table with the first pay date it applies to. A pack applies until the next one for its country starts. Every figure carries its `source`.
- The engine (`Pesaroll.Payroll.Engine`) reads a pack into `RulePack`, refusing a malformed one, and `PayCalculator.Calculate(inputs, pack)` gives every statutory line. `RulePacks.InForceAsync(country, payDate)` (Organizations) picks the pack for a pay date; none gives PAY-2010.
- **Never edit a published pack.** The database refuses changes and deletes (PAY-2011), and editing a published file fails the deploy. A change in the law is a new file with a new version and start date, a new `publish_rule_pack` line in post-deploy, and golden cases for it.
- The engine works on a month's pay. Weekly, fortnightly and daily runs (PAY-10) give it the month to date and take the difference (see Pay runs). `PayCalculator.Calculate(inputs, pack, choice)` makes only the statutory groups in the `StatutoryChoice` (paye, shif, nssf, housing_levy, nita; every one when left out).

## Approvals (WF-01..03)

- One engine (`Pesaroll.Workflows`) approves everything: pay runs first, then leave and later kinds. What can be approved is reference data (`workflow_kinds`, post-deploy `0017_workflow_kinds.sql`): its names, whether it is **required** (can't be sent until set up, WF-5005) and the **suggested setup** the Workflows page pre-fills.
- Each organization sets up its own workflows (`POST /api/workflows`, Approval workflows Manage): for every company or one (a company's own replaces the organization's there), with steps approved in order. A step finds approvers by a **permission** for the company, **named people**, or the employee's **line manager** (by their sign-in, `employees.membership_id`), with a **fallback** permission when that finds nobody. Nothing is created for an organization: `GET /api/workflows/missing` tells setup and Home what still needs setting up.
- The sender never approves their own request unless the workflow allows it (`allow_self_approval`, an admin's choice for small teams); such approvals are recorded as `self_approved`. Changing a workflow makes a new one and switches the old off, so requests keep the steps they were sent with.
- **Adding a kind:** a row in `0017_workflow_kinds.sql`; the feature calls `Approvals.SubmitAsync(kind, companyId, subjectId, employeeId)` after its own permission check, and registers an `IApprovalOutcome` for the kind to react when a request is approved, declined or cancelled. The outcome runs in the decision's transaction (`ApprovalTransaction`), so a failure undoes the decision.
- Deciding (`/api/approvals/{id}/approve|decline|cancel`) is relationship-based: the request's approvers for its current step (fixed when the step starts), with the step's permission checked again; cancelling is the sender's. Every action goes into `approval_actions`, which nobody can change (WF-5006). Emails say what and where, never amounts or the reason for a decline.

## Pay runs (PAY-03)

- A pay run (`Pesaroll.Payroll`, `/api/pay-runs`) is one company's pay for one schedule and period. Monthly runs are started by month; weekly, fortnightly and daily ones by pay date (`payDate`, one the schedule pays on, PAY-2022), covering the week or fortnight up to it, or the day itself (`RunPeriods`). One regular run per company, schedule and period that isn't cancelled (PAY-2012).
- It moves forward only: `draft` → `calculating` (the `PayRunCalculation` job in the worker) → `calculated` → `awaiting_approval` (`Approvals.SubmitAsync("pay_run", …)`) → `approved`. A declined or cancelled request sends it back to `calculated` with the reason; a run that isn't sent can be cancelled.
- Working out pay is a pure function (`PayRunCalculator`) over the company's people, their job and pay in force on the period's last day (or the day they left), the organization's items for the period (a person's own row over the everyone row), and the engine. Basic pay is paid as it is when its basis is the schedule's frequency; anyone else is left out as `other_basis` (hourly pay, days worked and other rates: PAY-11). Taxable earnings join basic pay; before-tax deductions go to the engine as pension; non-taxable earnings and other deductions change net pay only; employer costs add to the employer's cost. Anyone left out is listed with the reason; joiners, leavers and pay below zero are flagged, and pay below zero can't be sent (PAY-2019).
- **Month to date (PAY-10).** Kenya's statutory deductions are monthly, so a run belongs to the tax month of its pay date and its statutory lines are worked out on the month to date: this run's pay plus what the person's approved runs paid earlier in the month (for each schedule and period, the run approved last, so a correction replaces what it corrects; this run's own period is left out). Each line takes what earlier runs didn't (`amount = month_to_date - earlier`, both stored on the line), so four weekly runs add up to a monthly payroll: bands, personal relief, the NSSF cap, the SHIF minimum and NITA once a month. A line below zero (a pension added later in the month) is flagged `refund`. A run can't be worked out while an earlier run in its month on the schedule isn't approved or cancelled (PAY-2023).
- **Which statutory deductions (PAY-10).** Monthly payroll makes every one. A weekly, fortnightly or daily schedule makes none until the organization says otherwise (`POST /api/pay-schedules/{id}/deductions`, Payroll schedules Manage: off by default, then the items chosen, for everyone or chosen people, from a date); a person can have their own (`POST /api/companies/{companyId}/employees/{id}/deductions`, Pay Manage). Choices are added, never changed, with who made them; each person in a run keeps the items made and who chose them, and is flagged `no_deductions` or `some_deductions`. `POST /api/pay-schedules/deductions-example` works out a month of a sample wage with the rule pack in force, so setup can show what a choice means. A voluntary contribution (a casual asking for some of their wage to go to SHA) is an ordinary deduction with the payee.
- **What locks:** once sent, a run's people and lines can't change, and an approved or cancelled run never changes; the database refuses it too (PAY-2014). Item codes and names, the rule pack version and every statutory base and rate are copied into the run. A mistake is a **correction**: a new run for the chosen people and the same month, showing what each person's pay was last approved at.
- Access: `pay_runs.manage` to start, calculate, send, cancel and correct, `pay_runs.view` to look; approvers see a run that waits for them or that they approved, through its request, never a draft. Every look at people's pay (`/people`) is recorded in `sensitive_views`.

## Background work

- Hangfire, storing jobs in PostgreSQL. Long work (imports, pay runs, PDFs, notifications) is always a job, never inside a request.
- Jobs are idempotent: running one twice gives the same result.

## Testing

- xUnit. The golden payroll suite (`api/tests/Pesaroll.Payroll.Engine.Tests/Golden/`, worked examples reviewed by a Kenyan payroll accountant) runs on every change and must pass 100%. It reads the same rule pack files the database publishes.
- Tenancy tests run against a real PostgreSQL (Testcontainers) and prove one organization can't read another's rows.

## Logging

- Serilog, structured. Every log line carries the request reference and organization id, never salaries, ID numbers, KRA PINs or bank details.
