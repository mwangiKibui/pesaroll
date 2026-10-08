---
name: build
description: Plan-first workflow for building any Pesaroll task. Use whenever starting work on a board task (e.g. "build AUTH-01", "work on PAY-03", "let's do the leave balances") or any new feature, fix or schema change. Produces a plan with problem statement, implementation steps, risks and success criteria, gets it approved, then builds test-first (TDD) and checks; when there is something to try by hand, gives local test steps and waits for the user to confirm before opening the pull request.
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
- UI: the screens and the components they use (docs/standards/ui.md); new components go on /dev/components; phone width; placeholders on every field; every dropdown and search filter is the styled, searchable `SelectField` or `CompanySelect` (never a native select), on web and on phones; copy in the translation files
- tests first: for each step, the failing tests written before its code (backend xUnit unit tests,
  integration tests where the database or an endpoint is involved, frontend Vitest unit tests),
  plus tenancy (every new customer table) and golden payroll cases if pay is touched

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
- **Test-driven, always.** For each behaviour, red, green, refactor:
  1. Red: write a test for the behaviour and run it. It must fail, for the reason you expect.
  2. Green: write the least code that makes it pass.
  3. Refactor: tidy the code and the test with every test still passing.
  Never write production code without a failing test that needs it. A bug fix starts with a test that reproduces the bug.
- **Backend:** xUnit unit tests for logic (services, validation, calculations) in `api/tests/<Module>.Tests`,
  fast and without the database. Integration tests (`WebApplicationFactory<Program>` and the test database) for
  endpoints, row-level security and anything the database enforces. Database rules also get a test in `database/tests/`.
- **Frontend:** Vitest and React Testing Library unit tests next to the component (`Component.test.tsx`), testing what
  the user sees and does, not implementation details. Mock the API at the client boundary. The first frontend task sets them up.
- Prove the tests can fail: for security and money logic, break the code on purpose once and confirm the test catches it, then restore it.

## 5. Check

- Run `scripts/check.sh` (CI is off until the MVP 1 items are done). All checks must pass.
- Commit with the task ID first in the message; push the branch. Don't open the pull request yet if step 6 applies.

## 6. Let the user test it locally (only when there's something to try)

If the change has behaviour a person can try on their machine (a screen, an API endpoint, a flow, a schema change visible in the app), hand over step-by-step local test instructions and wait for the user to confirm it works. Skip this step for changes with nothing to try by hand (docs, skills, scripts, refactors fully covered by tests) and go straight to step 7.

Write the instructions so someone who didn't build it can follow them exactly:

```markdown
## Test <TASK-ID> locally

**Start**
1. `git fetch && git checkout <branch>`
2. `docker compose up -d` (set `DB_PORT=55432` if 5432 is taken)
3. `docker compose run --rm db-tools deploy`   <- only if the schema changed
4. <any seed or setup step, with the exact command>

**Try it**
1. Open http://localhost:5173/<page> (or `curl …` for an API-only change)
2. Do <action> → you should see <result>
3. <the main failure case> → you should see <error message and code>

**Clean up**
`docker compose down` (add `-v` to wipe the local database)
```

- Cover the success checks from the plan, including at least one failure case (wrong input, no permission, another organization's data).
- Before handing over, run through the instructions yourself in Docker so they're known to work.
- Wait for the user. If they report a problem, fix it on the same branch, rerun `scripts/check.sh`, and send updated instructions.

## 7. Open the pull request and hand over

- Once the user confirms it works locally (or step 6 didn't apply), open the pull request. Its description maps back to the plan: what was built per step, how each success check was verified (including the user's local test), what's left. Link the plan comment.
- Report to the user in plain words: what changed, what was verified, what needs their review or decision.
- When the pull request is merged: `scripts/task-stage.sh <TASK-ID> "Done"` (or keep "In progress" if steps remain).
