-- Pesaroll database schema: the desired state. No migrations.
-- pgschema compares this with the live database and applies the difference.
-- Files are included in dependency order.

\i functions/tenancy.sql
\i platform/deploy_scripts.sql
\i platform/error_codes.sql
\i reference/countries.sql
\i reference/banks.sql
\i reference/saccos.sql
\i reference/statutory_items.sql
\i reference/statutory_leave.sql
\i reference/rule_packs.sql
\i reference/workflow_kinds.sql
\i identity/signup_roles.sql
\i identity/users.sql
\i identity/signup_role_check.sql
\i tenancy/organizations.sql
\i identity/memberships.sql
\i tenancy/leave_plans.sql
\i tenancy/locations.sql
\i tenancy/pay_schedules.sql
\i tenancy/companies.sql
\i tenancy/location_companies.sql
\i tenancy/pay_schedule_companies.sql
\i tenancy/pay_schedule_reminders.sql
\i tenancy/payees.sql
\i tenancy/pay_items.sql
\i tenancy/pay_item_companies.sql
\i tenancy/leave_plan_rules.sql
\i tenancy/structure.sql
\i tenancy/structure_companies.sql
\i tenancy/employees.sql
\i tenancy/employee_pay_items.sql
\i tenancy/payroll_history.sql
\i tenancy/notification_deliveries.sql
\i tenancy/sensitive_views.sql
\i identity/access.sql
\i identity/invitations.sql
\i identity/team.sql
\i identity/users_policies.sql
\i identity/sessions.sql
\i identity/signin_functions.sql
\i tenancy/onboarding_functions.sql
\i tenancy/approvals.sql
\i tenancy/pay_runs.sql
