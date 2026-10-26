-- PAY-04: amounts entered for a run and the accountant's edits in review. An entry is days or hours,
-- an amount, or both (basic pay); amounts are never below 0 and a reason is never blank. A person's
-- statutory deductions for one run are known groups, once per person, with a reason; like everything
-- entered, they can't change once the run is sent (PAY-2014), and stay in their organization and company.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  ruth constant uuid := '00000000-0000-7000-8000-000000000f31';
  sched constant uuid := '00000000-0000-7000-8000-000000000f01';
  run1 constant uuid := '00000000-0000-7000-8000-000000000f11';
  someone uuid;
  seen int;
  msg text;
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  select id into someone from users limit 1;
  insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, cutoff_day, all_companies)
  values (sched, org1, 'Edits test monthly', 'monthly', 'last_working_day', 25, true);
  insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date)
  values (ruth, org1, ke1, 'EDIT-01', 'Ruth', 'Chebet', '2024-01-15');
  insert into pay_runs (id, organization_id, company_id, pay_schedule_id, period_start, period_end, pay_date, created_by)
  values (run1, org1, ke1, sched, '2026-10-01', '2026-10-31', '2026-10-30', someone);

  -- Basic pay set for the run with why; a statutory amount by its locked item.
  insert into pay_run_inputs (id, organization_id, company_id, pay_run_id, employee_id, pay_item_id, amount, reason, source, entered_by)
  values (uuidv7(), org1, ke1, run1, ruth, null, 80000, 'Unpaid leave agreed', 'entered', someone),
         (uuidv7(), org1, ke1, run1, ruth, (select id from pay_items where organization_id = org1 and locked_key = 'shif'), 150, 'Agreed', 'entered', someone);
  begin
    insert into pay_run_inputs (id, organization_id, company_id, pay_run_id, employee_id, pay_item_id, source, entered_by)
    values (uuidv7(), org1, ke1, run1, ruth, (select id from pay_items where organization_id = org1 and locked_key = 'paye'), 'entered', someone);
    raise exception 'an entry with neither days nor an amount';
  exception when check_violation then null;
  end;
  begin
    update pay_run_inputs set amount = -1 where pay_run_id = run1 and pay_item_id is null;
    raise exception 'an amount below 0';
  exception when check_violation then null;
  end;
  begin
    update pay_run_inputs set reason = '  ' where pay_run_id = run1 and pay_item_id is null;
    raise exception 'a blank reason';
  exception when check_violation then null;
  end;

  -- Statutory deductions for one run: known groups, with a reason, once per person.
  insert into pay_run_deduction_choices (id, organization_id, company_id, pay_run_id, employee_id, deductions, reason, set_by)
  values (uuidv7(), org1, ke1, run1, ruth, '{paye}', 'Contributes through another employer', someone);
  begin
    insert into pay_run_deduction_choices (id, organization_id, company_id, pay_run_id, employee_id, deductions, reason, set_by)
    values (uuidv7(), org1, ke1, run1, ruth, '{paye}', 'Again', someone);
    raise exception 'two choices for one person in one run';
  exception when unique_violation then null;
  end;
  begin
    update pay_run_deduction_choices set deductions = '{paye,tips}' where pay_run_id = run1;
    raise exception 'an unknown group';
  exception when check_violation then null;
  end;
  begin
    update pay_run_deduction_choices set reason = ' ' where pay_run_id = run1;
    raise exception 'a choice without a reason';
  exception when check_violation then null;
  end;

  -- Sent: neither amounts nor choices change.
  update pay_runs set status = 'awaiting_approval' where id = run1;
  begin
    update pay_run_deduction_choices set deductions = '{}' where pay_run_id = run1;
    raise exception 'a choice changed while waiting for approval';
  exception when raise_exception then
    get stacked diagnostics msg = message_text;
    if msg <> 'PAY-2014' then raise; end if;
  end;
  begin
    delete from pay_run_inputs where pay_run_id = run1;
    raise exception 'amounts removed while waiting for approval';
  exception when raise_exception then
    get stacked diagnostics msg = message_text;
    if msg <> 'PAY-2014' then raise; end if;
  end;

  -- Someone who may only see Uganda sees no choice made in Kenya; another organization sees nothing.
  perform set_config('app.company_ids', ug1::text, true);
  select count(*) into seen from pay_run_deduction_choices;
  if seen <> 0 then raise exception 'company scope: saw % choices', seen; end if;
  perform set_config('app.org_id', '00000000-0000-7000-8000-000000000002', true);
  perform set_config('app.company_ids', '00000000-0000-7000-8000-0000000000b1', true);
  select count(*) into seen from pay_run_deduction_choices;
  if seen <> 0 then raise exception 'another organization saw % choices', seen; end if;
end $$;
rollback;
