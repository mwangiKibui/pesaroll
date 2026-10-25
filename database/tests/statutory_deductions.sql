-- PAY-10: which statutory deductions a weekly, fortnightly or daily schedule makes, and a person's own
-- choice. Each is a dated record of who chose what, added to and never changed; both stay in their
-- organization, and a person's own stays in their company.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  peter constant uuid := '00000000-0000-7000-8000-000000000f31';
  sched constant uuid := '00000000-0000-7000-8000-000000000f01';
  someone uuid;
  seen int;
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  select id into someone from users limit 1;
  insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, cutoff_day, pay_weekday, cutoff_days_before, all_companies)
  values (sched, org1, 'Deductions test weekly', 'weekly', null, null, 5, 2, true);
  insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date)
  values (peter, org1, ke1, 'DED-01', 'Peter', 'Kamau', '2024-01-15');

  -- None, some for everyone, some for chosen people.
  insert into pay_schedule_deductions (id, organization_id, pay_schedule_id, starts_on, makes_deductions, items, set_by)
  values (uuidv7(), org1, sched, '2026-10-01', false, '{}', someone);
  insert into pay_schedule_deductions (id, organization_id, pay_schedule_id, starts_on, makes_deductions, items, set_by)
  values (uuidv7(), org1, sched, '2026-11-01', true, '{shif,housing_levy}', someone);
  insert into pay_schedule_deductions (id, organization_id, pay_schedule_id, starts_on, makes_deductions, items, applies_to, employee_ids, set_by)
  values (uuidv7(), org1, sched, '2026-12-01', true, '{paye,shif,nssf,housing_levy,nita}', 'chosen', array[peter], someone);

  begin
    insert into pay_schedule_deductions (id, organization_id, pay_schedule_id, starts_on, makes_deductions, items, set_by)
    values (uuidv7(), org1, sched, '2026-10-01', true, '{}', someone);
    raise exception 'deductions made with no items';
  exception when check_violation then null;
  end;
  begin
    insert into pay_schedule_deductions (id, organization_id, pay_schedule_id, starts_on, makes_deductions, items, set_by)
    values (uuidv7(), org1, sched, '2026-10-01', true, '{shif,withholding}', someone);
    raise exception 'an item that isn''t statutory';
  exception when check_violation then null;
  end;
  begin
    insert into pay_schedule_deductions (id, organization_id, pay_schedule_id, starts_on, makes_deductions, items, applies_to, set_by)
    values (uuidv7(), org1, sched, '2026-10-01', true, '{shif}', 'chosen', someone);
    raise exception 'chosen people without anyone chosen';
  exception when check_violation then null;
  end;
  begin
    update pay_schedule_deductions set items = '{}' where pay_schedule_id = sched;
    raise exception 'the app changed a recorded choice';
  exception when insufficient_privilege then null;
  end;

  -- A person's own: some items, none, or back to the schedule's (null).
  insert into employee_statutory_deductions (id, organization_id, company_id, employee_id, starts_on, items, set_by)
  values (uuidv7(), org1, ke1, peter, '2026-10-01', '{shif}', someone);
  insert into employee_statutory_deductions (id, organization_id, company_id, employee_id, starts_on, items, set_by)
  values (uuidv7(), org1, ke1, peter, '2026-11-01', null, someone);
  begin
    insert into employee_statutory_deductions (id, organization_id, company_id, employee_id, starts_on, items, set_by)
    values (uuidv7(), org1, ke1, peter, '2026-10-01', '{nhif}', someone);
    raise exception 'an item that isn''t statutory';
  exception when check_violation then null;
  end;
  begin
    delete from employee_statutory_deductions where employee_id = peter;
    raise exception 'the app deleted a recorded choice';
  exception when insufficient_privilege then null;
  end;

  -- Someone who may only see Uganda sees nobody's own choice in Kenya; another organization sees nothing.
  perform set_config('app.company_ids', ug1::text, true);
  select count(*) into seen from employee_statutory_deductions;
  if seen <> 0 then raise exception 'company scope: saw % choices', seen; end if;
  perform set_config('app.org_id', '00000000-0000-7000-8000-000000000002', true);
  perform set_config('app.company_ids', '00000000-0000-7000-8000-0000000000b1', true);
  select (select count(*) from pay_schedule_deductions) + (select count(*) from employee_statutory_deductions) into seen;
  if seen <> 0 then raise exception 'another organization saw % deduction choices', seen; end if;
end
$$;
rollback;
