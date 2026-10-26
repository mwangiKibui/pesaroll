-- PAY-04: runs follow the schedule's cut-off. A run records the cut-off it used with what it applied
-- to (both or neither); someone who joined after it is left out as after_cutoff; a line owed from
-- the last period gives that period's first and last days (both or neither, in order).
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  amina constant uuid := '00000000-0000-7000-8000-000000000f41';
  sched constant uuid := '00000000-0000-7000-8000-000000000f02';
  run1 constant uuid := '00000000-0000-7000-8000-000000000f12';
  person1 uuid := uuidv7();
  someone uuid;
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1::text, true);
  select id into someone from users limit 1;
  insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, cutoff_day, all_companies)
  values (sched, org1, 'Cut-off test monthly', 'monthly', 'last_working_day', 25, true);
  insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date)
  values (amina, org1, ke1, 'CUT-01', 'Amina', 'Otieno', '2026-10-27');
  insert into pay_runs (id, organization_id, company_id, pay_schedule_id, period_start, period_end, pay_date, created_by, cutoff_on, cutoff_applies_to)
  values (run1, org1, ke1, sched, '2026-10-01', '2026-10-31', '2026-10-30', someone, '2026-10-25', '{new_employees,leavers}');
  begin
    update pay_runs set cutoff_applies_to = null where id = run1;
    raise exception 'a cut-off without what it applied to';
  exception when check_violation then null;
  end;

  insert into pay_run_people (id, organization_id, company_id, pay_run_id, employee_id, staff_no, full_name, included, excluded_reason)
  values (person1, org1, ke1, run1, amina, 'CUT-01', 'Amina Otieno', false, 'after_cutoff');

  insert into pay_run_lines (organization_id, company_id, pay_run_id, person_id, kind, code, name, amount, owed_from, owed_to, position)
  values (org1, ke1, run1, person1, 'earning', 'BASIC', 'Basic pay', 5000, '2026-09-01', '2026-09-30', 1);
  begin
    insert into pay_run_lines (organization_id, company_id, pay_run_id, person_id, kind, code, name, amount, owed_from, position)
    values (org1, ke1, run1, person1, 'earning', 'BASIC', 'Basic pay', 5000, '2026-09-01', 2);
    raise exception 'a line owed from a period without its last day';
  exception when check_violation then null;
  end;
  begin
    insert into pay_run_lines (organization_id, company_id, pay_run_id, person_id, kind, code, name, amount, owed_from, owed_to, position)
    values (org1, ke1, run1, person1, 'earning', 'BASIC', 'Basic pay', 5000, '2026-09-30', '2026-09-01', 2);
    raise exception 'a line owed from a period that ends before it starts';
  exception when check_violation then null;
  end;
end $$;
rollback;
