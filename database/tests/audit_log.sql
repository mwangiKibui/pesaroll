-- PLAT-03: every change to customer data is recorded in audit_log: what changed, who (app.user_id), the
-- request (app.request_ref), never the value of a salary, ID number, KRA PIN or bank account (rule 8).
-- Entries are kept in their organization and company, and nobody can change or delete them (PLAT-9003).
-- Every table with organization_id records its changes, unless it is on the list left out.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  owner1 constant uuid := '00000000-0000-7000-8000-0000000000f1';
  wanjiku constant uuid := '00000000-0000-7000-8000-000000000e51';
  pay1 constant uuid := '00000000-0000-7000-8000-000000000e52';
  sched constant uuid := '00000000-0000-7000-8000-000000000e53';
  entry audit_log;
  seen int;
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  perform set_config('app.user_id', owner1::text, true);
  perform set_config('app.request_ref', 'REQ-AUDIT-1', true);

  -- Added: the whole row, its ID number and KRA PIN masked.
  insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date, kra_pin_enc, kra_pin_lookup, kra_pin_last4, nssf_no)
  values (wanjiku, org1, ke1, 'AUD-01', 'Wanjiku', 'Kamau', '2025-02-03', '\x01'::bytea, 'A123456789Z', '789Z', '200300400');
  select * into strict entry from audit_log where table_name = 'employees' and record_id = wanjiku;
  if (entry.kind, entry.action, entry.company_id, entry.user_id, entry.request_ref, entry.source)
     is distinct from ('change', 'insert', ke1, owner1, 'REQ-AUDIT-1', 'app') then
    raise exception 'insert recorded as %', row(entry.kind, entry.action, entry.company_id, entry.user_id, entry.request_ref, entry.source);
  end if;
  if entry.new_values->>'first_name' <> 'Wanjiku' or entry.new_values->>'kra_pin_lookup' <> 'masked' or entry.new_values->>'nssf_no' <> 'masked' or entry.new_values->>'kra_pin_last4' <> 'masked'
     or entry.new_values::text like '%A123456789Z%' or entry.new_values::text like '%200300400%' then
    raise exception 'insert values not as expected or not masked: %', entry.new_values;
  end if;

  -- Pay: the change is recorded, the amounts are not.
  insert into compensations (id, organization_id, company_id, employee_id, valid_from, basic_amount, pay_basis, currency)
  values (pay1, org1, ke1, wanjiku, '2025-02-03', 85000, 'monthly', 'KES');
  update compensations set basic_amount = 97000, reason = 'Annual review' where id = pay1;
  select * into strict entry from audit_log where table_name = 'compensations' and record_id = pay1 and action = 'update';
  if entry.changed <> array['basic_amount', 'reason'] or entry.old_values->>'basic_amount' <> 'masked' or entry.new_values->>'reason' <> 'Annual review' then
    raise exception 'update recorded as % % %', entry.changed, entry.old_values, entry.new_values;
  end if;
  if exists (select 1 from audit_log where table_name = 'compensations' and (coalesce(old_values, '{}')::text || coalesce(new_values, '{}')::text) ~ '85000|97000') then
    raise exception 'a salary reached the audit log';
  end if;

  -- An update that changes nothing records nothing; a delete keeps what was there.
  update employees set first_name = 'Wanjiku' where id = wanjiku;
  if (select count(*) from audit_log where record_id = wanjiku and action = 'update') <> 0 then
    raise exception 'an update that changed nothing was recorded';
  end if;
  delete from compensations where id = pay1;
  select * into strict entry from audit_log where record_id = pay1 and action = 'delete';
  if entry.old_values->>'pay_basis' <> 'monthly' or entry.new_values is not null then
    raise exception 'delete recorded as % %', entry.old_values, entry.new_values;
  end if;

  -- Organization-wide: a schedule belongs to no one company.
  insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, cutoff_day, all_companies)
  values (sched, org1, 'Audit test monthly', 'monthly', 'last_working_day', 25, true);
  if (select company_id from audit_log where record_id = sched and action = 'insert') is not null then
    raise exception 'an organization-wide change was given a company';
  end if;

  -- The app may read the log, never change it.
  begin
    update audit_log set action = 'delete' where record_id = wanjiku;
    raise exception 'the app changed the audit log';
  exception when insufficient_privilege then null;
  end;
  begin
    delete from audit_log where record_id = wanjiku;
    raise exception 'the app deleted from the audit log';
  exception when insufficient_privilege then null;
  end;
  begin
    insert into audit_log (organization_id, kind, action, table_name) values (org1, 'change', 'insert', 'employees');
    raise exception 'the app wrote to the audit log directly';
  exception when insufficient_privilege then null;
  end;

  -- Someone who may only see Uganda sees neither Kenya's entries nor the organization's.
  perform set_config('app.company_ids', ug1::text, true);
  select count(*) into seen from audit_log where record_id in (wanjiku, pay1, sched);
  if seen <> 0 then raise exception 'company scope: saw % entries', seen; end if;
  -- Another organization sees nothing.
  perform set_config('app.org_id', '00000000-0000-7000-8000-000000000002', true);
  perform set_config('app.company_ids', '00000000-0000-7000-8000-0000000000b1', true);
  select count(*) into seen from audit_log where organization_id = org1;
  if seen <> 0 then raise exception 'another organization saw % entries', seen; end if;
end $$;

-- Not even the owner can change or delete an entry.
reset role;
do $$
declare
  msg text;
begin
  begin
    update audit_log set action = 'delete' where table_name = 'employees';
    raise exception 'the owner changed the audit log';
  exception when raise_exception then
    get stacked diagnostics msg = message_text;
    if msg <> 'PLAT-9003' then raise; end if;
  end;
  begin
    delete from audit_log where table_name = 'employees';
    raise exception 'the owner deleted from the audit log';
  exception when raise_exception then
    get stacked diagnostics msg = message_text;
    if msg <> 'PLAT-9003' then raise; end if;
  end;
end $$;

-- Every table with organization_id records its changes, unless it is left out on purpose.
do $$
declare
  missing text;
begin
  select string_agg(c.table_name, ', ' order by c.table_name) into missing
  from information_schema.columns c
  where c.table_schema = 'public' and c.column_name = 'organization_id'
    and c.table_name not in (select table_name from information_schema.views where table_schema = 'public')
    and c.table_name not in (select unnest(audit_left_out()))
    and not exists (select 1 from pg_trigger t join pg_class r on r.oid = t.tgrelid
                    where r.relname = c.table_name and t.tgname = c.table_name || '_audit' and not t.tgisinternal);
  if missing is not null then
    raise exception 'tables that don''t record their changes in the audit log: %', missing;
  end if;
end $$;
rollback;
