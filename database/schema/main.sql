-- Pesaroll database schema: the desired state. No migrations.
-- pgschema compares this with the live database and applies the difference.
-- Files are included in dependency order.

\i functions/tenancy.sql
\i platform/deploy_scripts.sql
\i platform/error_codes.sql
\i reference/countries.sql
\i reference/banks.sql
\i identity/signup_roles.sql
\i identity/users.sql
\i identity/signup_role_check.sql
\i tenancy/organizations.sql
\i tenancy/locations.sql
\i tenancy/pay_schedules.sql
\i tenancy/companies.sql
\i tenancy/location_companies.sql
\i tenancy/pay_schedule_companies.sql
\i tenancy/structure.sql
\i tenancy/structure_companies.sql
\i tenancy/notification_deliveries.sql
\i identity/memberships.sql
\i identity/users_policies.sql
\i identity/sessions.sql
\i identity/signin_functions.sql
\i tenancy/onboarding_functions.sql
