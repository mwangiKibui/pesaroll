# Audit log actions (PLAT-03)

The audit log (`audit_log`) holds two kinds of entry:

- **Changes** (`kind = 'change'`): every insert, update and delete of an organization's data, recorded by the database (see `backend.md`, "Audit log").
- **Actions** (`kind = 'action'`): what a person did, by name, such as "Brian Mwangi worked out the Monthly payroll for October 2026". The app records these with `Audit.RecordAsync` (`api/src/Pesaroll.Shared/Audit`). That call goes through the database's `audit_record`, which takes the person, organization and request from the connection.

This page is the catalogue of actions, like `errors.md` is for error codes.

## Rules

- **Catalogue first.** Every action is listed here, in `AuditActions` (with the details it may carry) and in `web/src/i18n/locales/*.json` under `audit.actions`. `AuditActionsTests` fails when one of them is missing.
- **Keys:** `subject.verb`, lower case, such as `pay_run.calculated`. A key never changes meaning once released.
- **Safe details only.** Details are only the keys listed for the action, never personal data (CLAUDE.md rule 8). That rules out amounts, ID numbers, KRA PINs, accounts and free text a person typed, such as a reason. `AuditActions.Check` refuses anything else.
- **The subject:** what the action was done to (its type, id and name at the time). The name is kept for when the subject is renamed or deleted.
- **Company:** the action's company, so people see only their companies' actions. Organization-wide actions (schedules, workflows, team) have none; only people who see every company see them.
- **The same transaction.** Record in the same transaction as what the action did, so neither happens without the other.
- **Every new feature** that does something a customer would ask "who did that?" about adds its actions here. Payslip views and leave requests come with those features.

## Actions

Pay run details are `kind` (regular or correction), `periodStart`, `periodEnd` and `payDate`. The subject is the run, named by its schedule.

| Key | When | Company | Details |
| --- | --- | --- | --- |
| `pay_run.started` | Payroll starts a run | the run's | run |
| `pay_run.calculated` | The run is worked out (by the job, as the person who asked) | the run's | run, `people` |
| `pay_run.sent` | Sent for approval | the run's | run, `people` |
| `pay_run.approved` | The approval request is approved | the run's | run |
| `pay_run.declined` | The approval request is declined | the run's | run |
| `pay_run.withdrawn` | The approval request is cancelled, and the run goes back to Payroll | the run's | run |
| `pay_run.cancelled` | Payroll cancels a run that wasn't approved | the run's | run |
| `pay_run.corrected` | A correction of an approved run is started (the subject is the approved run) | the run's | run, `correctionId`, `people` |
| `employee.added` | An employee is added by hand | the employee's | `staffNo` |
| `employee.removed` | An employee is deleted | the employee's | `staffNo` |
| `employee.pay_changed` | Their pay changes, on the employee form or as a pay change from a date | the employee's | `staffNo`, `validFrom` |
| `employee.bank_changed` | The account they are paid into changes | the employee's | `staffNo`, `method` (bank or mpesa) |
| `records.imported` | A spreadsheet upload is saved (employees, locations, structure, payroll history) | none | `things`, `rows` |
| `pay_schedule.added` | A payroll schedule is added | none | `frequency` |
| `pay_schedule.changed` | A payroll schedule is changed | none | `frequency` |
| `pay_schedule.removed` | A payroll schedule is deleted | none | `frequency` |
| `pay_schedule.cutoff_on` | Its cut-off is switched on (with `added` or `changed`) | none | `frequency` |
| `pay_schedule.cutoff_off` | Its cut-off is switched off | none | `frequency` |
| `workflow.saved` | A company's approval workflow is set up or changed (the subject is the workflow, named by its kind) | the workflow's | `kind` |
| `workflow.switched_off` | A workflow is switched off | the workflow's | `kind` |
| `team.access_changed` | A person's groups or companies change (the subject is the person) | none | `groups` (group keys), `allCompanies` |
| `team.member_removed` | A person is removed from the organization | none | none |
| `pay.viewed` | Someone is shown people's pay (recorded with `sensitive_views`, ACC-05) | none | `people` |

## Reading the log

All of these need `audit_log.view` (the Audit area; Administrator has it). Every list is narrowed to the companies the person has it for, and entries with no company show only to someone who has it for every company.

- `GET /api/audit-log`: entries newest first, filtered by `companyId`, `action`, `userId`, `subjectId` and `from`/`to`, with the person's name.
- `GET /api/audit-log/summary?action=&from=&to=`: how many entries and how many different people, for questions like "how many people approved payrolls this month?".
- `GET /api/audit-log/records/{table}/{id}`: one record's history: the changes to the row and the actions done to it.
