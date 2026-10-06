-- Proves one organization can't see another's companies. Run as a non-owner role
-- (row-level security doesn't apply to table owners). Fails with an exception on a leak.
begin;
set local role pesaroll_app;
set local app.org_id = '00000000-0000-7000-8000-000000000001';
set local app.company_ids = '00000000-0000-7000-8000-0000000000a1';
do $$
begin
  if exists (select 1 from companies where organization_id <> app_org_id()) then
    raise exception 'tenancy leak: another organization''s company is visible';
  end if;
  if (select count(*) from companies) <> 1 then
    raise exception 'expected exactly 1 visible company, got %', (select count(*) from companies);
  end if;
end $$;
rollback;
