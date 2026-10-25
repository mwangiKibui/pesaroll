-- PAY-03: pay runs. One regular run per company, schedule and period; a correction points at the run
-- it corrects; runs, their people and lines stay in their organization and company; figures can't
-- change once sent for approval, and an approved or cancelled run never changes (PAY-2014).
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  amina constant uuid := '00000000-0000-7000-8000-000000000e31';
  sched constant uuid := '00000000-0000-7000-8000-000000000e01';
  run1 constant uuid := '00000000-0000-7000-8000-000000000e11';
  run2 constant uuid := '00000000-0000-7000-8000-000000000e12';
  fix1 constant uuid := '00000000-0000-7000-8000-000000000e13';
  person constant uuid := '00000000-0000-7000-8000-000000000e21';
  someone uuid;
  seen int;
  msg text;
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  select id into someone from users limit 1;
  insert into pay_schedules (id, organization_id, name, all_companies) values (sched, org1, 'Pay run test monthly', true);
  insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date)
  values (amina, org1, ke1, 'RUN-01', 'Amina', 'Otieno', '2024-01-15');

  insert into pay_runs (id, organization_id, company_id, pay_schedule_id, period_start, period_end, pay_date, created_by)
  values (run1, org1, ke1, sched, '2026-10-01', '2026-10-31', '2026-10-30', someone);

  -- One regular run per company, schedule and period, unless it was cancelled.
  begin
    insert into pay_runs (id, organization_id, company_id, pay_schedule_id, period_start, period_end, pay_date, created_by)
    values (uuidv7(), org1, ke1, sched, '2026-10-01', '2026-10-31', '2026-10-30', someone);
    raise exception 'two regular runs for October';
  exception when unique_violation then null;
  end;
  -- A correction says which run it corrects; a regular run doesn't.
  begin
    insert into pay_runs (id, organization_id, company_id, pay_schedule_id, kind, period_start, period_end, pay_date, created_by)
    values (uuidv7(), org1, ke1, sched, 'correction', '2026-10-01', '2026-10-31', '2026-10-30', someone);
    raise exception 'a correction without the run it corrects';
  exception when check_violation then null;
  end;
  begin
    insert into pay_runs (id, organization_id, company_id, pay_schedule_id, period_start, period_end, pay_date, created_by)
    values (uuidv7(), org1, ke1, sched, '2026-11-30', '2026-11-01', '2026-11-30', someone);
    raise exception 'a period that ends before it starts';
  exception when check_violation then null;
  end;

  insert into pay_run_people (id, organization_id, company_id, pay_run_id, employee_id, staff_no, full_name, included,
    basic_pay, gross_pay, taxable_pay, paye, total_deductions, net_pay, employer_costs)
  values (person, org1, ke1, run1, amina, 'RUN-01', 'Amina Otieno', true, 100000, 100000, 89750, 19308.35, 29558.35, 70441.65, 6050);
  insert into pay_run_lines (organization_id, company_id, pay_run_id, person_id, kind, code, name, statutory_key, base, rate, amount, position)
  values (org1, ke1, run1, person, 'deduction', 'PAYE', 'PAYE', 'paye', 89750, null, 19308.35, 1);
  begin
    insert into pay_run_people (id, organization_id, company_id, pay_run_id, employee_id, staff_no, full_name, included, excluded_reason)
    values (uuidv7(), org1, ke1, run1, amina, 'RUN-01', 'Amina Otieno', false, 'no_pay');
    raise exception 'one person twice in a run';
  exception when unique_violation then null;
  end;
  begin
    insert into pay_run_people (id, organization_id, company_id, pay_run_id, employee_id, staff_no, full_name, included)
    values (uuidv7(), org1, ke1, run1, amina, 'RUN-01', 'Amina Otieno', false);
    raise exception 'someone left out without a reason';
  exception when check_violation then null;
  end;

  -- While it waits for approval its figures can't change.
  update pay_runs set status = 'awaiting_approval' where id = run1;
  begin
    delete from pay_run_people where id = person;
    raise exception 'people removed while waiting for approval';
  exception when raise_exception then
    get stacked diagnostics msg = message_text;
    if msg <> 'PAY-2014' then raise; end if;
  end;
  begin
    delete from pay_run_lines where pay_run_id = run1;
    raise exception 'lines deleted while waiting for approval';
  exception when raise_exception then
    get stacked diagnostics msg = message_text;
    if msg <> 'PAY-2014' then raise; end if;
  end;

  -- Approved: the run, its people and lines never change.
  update pay_runs set status = 'approved', approved_by = someone, approved_at = now() where id = run1;
  begin
    update pay_runs set gross_pay = 1 where id = run1;
    raise exception 'an approved run changed';
  exception when raise_exception then
    get stacked diagnostics msg = message_text;
    if msg <> 'PAY-2014' then raise; end if;
  end;
  begin
    insert into pay_run_lines (organization_id, company_id, pay_run_id, person_id, kind, code, name, amount, position)
    values (org1, ke1, run1, person, 'earning', 'BONUS', 'Bonus', 1000, 9);
    raise exception 'a line added to an approved run';
  exception when raise_exception then
    get stacked diagnostics msg = message_text;
    if msg <> 'PAY-2014' then raise; end if;
  end;
  begin
    delete from pay_runs where id = run1;
    raise exception 'the app deleted a pay run';
  exception when insufficient_privilege then null;
  end;

  -- A correction of it, for October again.
  insert into pay_runs (id, organization_id, company_id, pay_schedule_id, kind, corrects_pay_run_id, employee_ids,
    period_start, period_end, pay_date, created_by)
  values (fix1, org1, ke1, sched, 'correction', run1, array[amina], '2026-10-01', '2026-10-31', '2026-10-30', someone);

  -- Cancelled is final too, and frees the period for a new regular run.
  insert into pay_runs (id, organization_id, company_id, pay_schedule_id, period_start, period_end, pay_date, created_by)
  values (run2, org1, ke1, sched, '2026-11-01', '2026-11-30', '2026-11-30', someone);
  update pay_runs set status = 'cancelled', cancelled_by = someone, cancelled_at = now() where id = run2;
  begin
    update pay_runs set status = 'draft' where id = run2;
    raise exception 'a cancelled run came back';
  exception when raise_exception then
    get stacked diagnostics msg = message_text;
    if msg <> 'PAY-2014' then raise; end if;
  end;
  insert into pay_runs (id, organization_id, company_id, pay_schedule_id, period_start, period_end, pay_date, created_by)
  values (uuidv7(), org1, ke1, sched, '2026-11-01', '2026-11-30', '2026-11-30', someone);

  -- Someone who may only see Uganda sees none of Kenya's runs; another organization sees nothing.
  perform set_config('app.company_ids', ug1::text, true);
  select (select count(*) from pay_runs) + (select count(*) from pay_run_people) + (select count(*) from pay_run_lines) into seen;
  if seen <> 0 then raise exception 'company scope: saw % pay run rows', seen; end if;
  perform set_config('app.org_id', '00000000-0000-7000-8000-000000000002', true);
  perform set_config('app.company_ids', '00000000-0000-7000-8000-0000000000b1', true);
  select (select count(*) from pay_runs) + (select count(*) from pay_run_people) + (select count(*) from pay_run_lines) into seen;
  if seen <> 0 then raise exception 'another organization saw % pay run rows', seen; end if;
end
$$;
rollback;
