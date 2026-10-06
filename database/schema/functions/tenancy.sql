-- The API sets these on every connection (TenantSessionInterceptor):
--   app.user_id      the signed-in person
--   app.org_id       their current organization
--   app.company_ids  the companies they may see, comma separated
-- Row-level security policies read them through these helpers.

create or replace function app_user_id() returns uuid
language sql stable
as $$ select nullif(current_setting('app.user_id', true), '')::uuid $$;

create or replace function app_org_id() returns uuid
language sql stable
as $$ select nullif(current_setting('app.org_id', true), '')::uuid $$;

create or replace function app_company_ids() returns uuid[]
language sql stable
as $$ select coalesce(string_to_array(nullif(current_setting('app.company_ids', true), ''), ',')::uuid[], '{}') $$;

grant execute on function app_user_id() to pesaroll_app;
grant execute on function app_org_id() to pesaroll_app;
grant execute on function app_company_ids() to pesaroll_app;
