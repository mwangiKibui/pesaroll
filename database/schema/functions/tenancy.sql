-- The API sets these per transaction:
--   set local app.org_id = '<uuid>';
--   set local app.company_ids = '<uuid>,<uuid>';
-- Row-level security policies read them through these helpers.

create or replace function app_org_id() returns uuid
language sql stable
as $$ select nullif(current_setting('app.org_id', true), '')::uuid $$;

create or replace function app_company_ids() returns uuid[]
language sql stable
as $$ select coalesce(string_to_array(nullif(current_setting('app.company_ids', true), ''), ',')::uuid[], '{}') $$;
