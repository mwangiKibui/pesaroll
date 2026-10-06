-- Pesaroll database schema: the desired state. No migrations.
-- pgschema compares this with the live database and applies the difference.
-- Files are included in dependency order.

\i functions/tenancy.sql
\i platform/error_codes.sql
\i identity/users.sql
\i tenancy/organizations.sql
\i tenancy/companies.sql
\i identity/memberships.sql
