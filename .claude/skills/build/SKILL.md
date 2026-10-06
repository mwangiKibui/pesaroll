---
name: build
description: Plan-first workflow for building any Pesaroll task. Use whenever starting work on a board task (e.g. "build AUTH-01", "work on PAY-03", "let's do the leave balances") or any new feature, fix or schema change. Produces a plan with problem statement, implementation steps, risks and success criteria, gets it approved, then builds, checks and opens a pull request.
---

# Build a Pesaroll task

Every piece of work starts with a plan. Do not write code, schema or UI until the plan is approved.

## 1. Find the task

- Identify the task ID (e.g. `AUTH-01`). If the request has none, find the matching issue:
  `gh issue list -R mwangiKibui/pesaroll --search "<words> in:title" --state open`
- Read the issue, its parent module issue and anything it depends on ("Depends on" / blocked-by). If a dependency isn't done, say so before planning.
- Read what the plan must respect: `CLAUDE.md`, `docs/standards/`, and for schema work the conventions and checklist on the Schema Design page.
- Move the task to **Planning**: `scripts/task-stage.sh <TASK-ID> "Planning"`

## 2. Write the plan

Use exactly these four sections. Keep each tight: a reviewer should read the whole plan in under five minutes.

```markdown
## Plan: <TASK-ID> <task title>

### Problem statement
Who has the problem, what they can't do today, and why it matters for Pesaroll.
Name the users (customer admin, employee, line manager, back office role, partner)
and what changes for them when this ships. One short paragraph.

### Implementation steps
Numbered, in build order, each small enough for one pull request or less.
For each step: what changes, and where (database/schema, api/, web/, docs).
Cover every item in the planning checklist that applies:
- per company: how it works when companies in one organization differ (company selector)
- permissions: which customer roles and back office roles can do it, checked on the server
- schema: tables, row-level security, grants, effective dating; pre/post-deploy scripts
- errors: the error codes it adds to docs/standards/errors.md
- notifications: what it sends (in-app, push, email, SMS), with no salaries in push or email
- reports: what it feeds or needs
- tests: unit, tenancy (every new customer table), golden payroll cases if pay is touched

### Risks
What could go wrong and what we do about it. At minimum consider: data leaking
between organizations, wrong money or statutory figures, personal data exposure,
breaking existing features, and anything this depends on that isn't ready.
Format: risk, then mitigation.

### Success
How we know it's done, as checks anyone can verify: behaviour a user sees,
tests that pass, numbers that match. Include "scripts/check.sh passes".
List what is explicitly out of scope.
```

## 3. Get approval

- Show the plan to the user and stop. Ask for approval or changes.
- Revise until approved. Do not start building on silence or partial agreement.
- On approval:
  - Post the plan on the task's issue: `gh issue comment <number> -R mwangiKibui/pesaroll --body-file <plan.md>`
  - For steps that are a pull request on their own, create sub-issues under the task:
    `gh issue create -R mwangiKibui/pesaroll --title "[<TASK-ID>] <step>" --body "<what and done-when>" --parent <task issue number>`
  - `scripts/task-stage.sh <TASK-ID> "Ready to build"`

## 4. Build

- Branch from an up-to-date `main`: `<TASK-ID>/<short-name>` (e.g. `AUTH-01/api`). Never commit to `main`.
- `scripts/task-stage.sh <TASK-ID> "In progress"`
- Follow the plan's steps in order and the rules in `CLAUDE.md`. If the plan turns out wrong, stop, explain what changed, and get the revised plan approved before continuing.
- Prove the tests can fail: for security and money logic, break the code on purpose once and confirm the test catches it, then restore it.

## 5. Check and hand over

- Run `scripts/check.sh` (CI is off until the MVP 1 items are done). All checks must pass.
- Commit with the task ID first in the message; push the branch.
- Open a pull request whose description maps back to the plan: what was built per step, how each success check was verified, what's left. Link the plan comment.
- Report to the user in plain words: what changed, what was verified, what needs their review or decision.
- When the pull request is merged: `scripts/task-stage.sh <TASK-ID> "Done"` (or keep "In progress" if steps remain).
