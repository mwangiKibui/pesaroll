-- Pesaroll database schema: the desired state. No migrations.
-- pgschema compares this with the live database and applies the difference.
-- Files are included in dependency order.

\i functions/tenancy.sql
\i platform/deploy_scripts.sql
\i platform/error_codes.sql
\i identity/signup_roles.sql
\i identity/users.sql
\i tenancy/organizations.sql
\i tenancy/companies.sql
\i identity/memberships.sql
\i identity/users_policies.sql
\i identity/sessions.sql
\i identity/signin_functions.sql
