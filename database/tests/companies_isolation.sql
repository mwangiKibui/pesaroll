-- ONB-03: companies and their statutory numbers stay inside their organization, and within it,
-- inside the companies the person may see.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  org2 constant uuid := '00000000-0000-7000-8000-000000000002';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  ke2  constant uuid := '00000000-0000-7000-8000-0000000000b1';
begin
  -- Organization one, seeing both its companies, records a KRA PIN.
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  insert into company_identifiers (id, organization_id, company_id, field_key, value)
  values ('00000000-0000-7000-8000-0000000000d1', org1, ke1, 'kra_pin', 'P051234567X');

  -- Organization two sees neither the company nor the number.
  perform set_config('app.org_id', org2::text, true);
  perform set_config('app.company_ids', ke2::text, true);
  if exists (select 1 from company_identifiers where company_id = ke1) then raise exception 'org two sees org one''s numbers'; end if;
  if exists (select 1 from companies where id = ke1) then raise exception 'org two sees org one''s company'; end if;
  begin
    insert into company_identifiers (id, organization_id, company_id, field_key, value)
    values ('00000000-0000-7000-8000-0000000000d2', org1, ke1, 'nssf_employer_no', 'X123');
    raise exception 'org two wrote into org one''s company';
  exception when insufficient_privilege then null;
  end;

  -- Inside organization one, someone scoped to the Uganda company only doesn't see Kenya's numbers.
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ug1::text, true);
  if exists (select 1 from company_identifiers where company_id = ke1) then raise exception 'company scope ignored for numbers'; end if;

  -- A company's country must be one we know.
  perform set_config('app.company_ids', ke1 || ',' || ug1 || ',00000000-0000-7000-8000-0000000000a9', true);
  begin
    insert into companies (id, organization_id, legal_name, country_code, currency)
    values ('00000000-0000-7000-8000-0000000000a9', org1, 'Nowhere Ltd', 'ZZ', 'XXX');
    raise exception 'a company in an unknown country was saved';
  exception when foreign_key_violation then null;
  end;
end $$;
rollback;
