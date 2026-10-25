-- PAY-11: pay rates. Usual days or hours only on a daily or hourly rate; items paid by the hour or day
-- are earnings with a unit and a multiplier or an amount a unit; days and hours entered in a run stay
-- in its organization and company and can't change once it is sent (PAY-2014); the 2024 wage order
-- is in place for every Kenyan wage area.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  mary constant uuid := '00000000-0000-7000-8000-000000000d31';
  sched constant uuid := '00000000-0000-7000-8000-000000000d01';
  run1 constant uuid := '00000000-0000-7000-8000-000000000d11';
  overtime constant uuid := '00000000-0000-7000-8000-000000000d41';
  someone uuid;
  seen int;
  msg text;
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  select id into someone from users limit 1;
  insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, cutoff_day, pay_weekday, cutoff_days_before, all_companies)
  values (sched, org1, 'Rates test weekly', 'weekly', null, null, 5, 2, true);
  insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date)
  values (mary, org1, ke1, 'RATE-01', 'Mary', 'Akinyi', '2024-01-15');

  -- Usual days on a daily rate; none on a salary.
  insert into compensations (id, organization_id, company_id, employee_id, valid_from, basic_amount, pay_basis, currency, usual_quantity)
  values (uuidv7(), org1, ke1, mary, '2024-01-15', 450, 'daily', 'KES', 6);
  begin
    insert into compensations (id, organization_id, company_id, employee_id, valid_from, valid_to, basic_amount, pay_basis, currency, usual_quantity)
    values (uuidv7(), org1, ke1, mary, '2023-01-01', '2023-12-31', 30000, 'monthly', 'KES', 26);
    raise exception 'usual days on a monthly salary';
  exception when check_violation then null;
  end;

  -- Paid by the hour: an earning with a unit, and a multiplier or an amount a unit, not both.
  insert into pay_items (id, organization_id, code, name, kind, taxable, calc_method, unit, multiplier, all_companies)
  values (overtime, org1, 'RATE-OT', 'Overtime', 'earning', true, 'per_unit', 'hour', 1.5, true);
  insert into pay_items (id, organization_id, code, name, kind, taxable, calc_method, unit, default_amount, all_companies)
  values (uuidv7(), org1, 'RATE-NIGHT', 'Night shift', 'earning', true, 'per_unit', 'hour', 200, true);
  begin
    insert into pay_items (id, organization_id, code, name, kind, calc_method, unit, multiplier, default_amount, all_companies)
    values (uuidv7(), org1, 'RATE-BOTH', 'Both', 'earning', 'per_unit', 'hour', 1.5, 200, true);
    raise exception 'a multiplier and an amount a unit';
  exception when check_violation then null;
  end;
  begin
    insert into pay_items (id, organization_id, code, name, kind, calc_method, unit, multiplier, all_companies)
    values (uuidv7(), org1, 'RATE-DED', 'Docked hours', 'deduction', 'per_unit', 'hour', 1, true);
    raise exception 'a deduction paid by the hour';
  exception when check_violation then null;
  end;
  begin
    insert into pay_items (id, organization_id, code, name, kind, calc_method, multiplier, all_companies)
    values (uuidv7(), org1, 'RATE-NOUNIT', 'No unit', 'earning', 'per_unit', 1.5, true);
    raise exception 'paid by the hour without a unit';
  exception when check_violation then null;
  end;
  begin
    insert into pay_items (id, organization_id, code, name, kind, calc_method, default_amount, multiplier, all_companies)
    values (uuidv7(), org1, 'RATE-FIX', 'Fixed', 'earning', 'fixed', 100, 1.5, true);
    raise exception 'a multiplier on a fixed item';
  exception when check_violation then null;
  end;

  -- A person's own multiplier, but not on an item that isn't paid by the hour or day (ONB-3547).
  insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, multiplier, starts_on)
  values (uuidv7(), org1, ke1, overtime, mary, 2, '2026-01-01');
  begin
    insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, multiplier, starts_on)
    values (uuidv7(), org1, ke1, (select id from pay_items where organization_id = org1 and locked_key = 'basic'), mary, 2, '2026-01-01');
    raise exception 'a multiplier on basic pay';
  exception when raise_exception then
    get stacked diagnostics msg = message_text;
    if msg not in ('ONB-3545', 'ONB-3547') then raise; end if;
  end;

  -- Days and hours in a run: once per person and pay.
  insert into pay_runs (id, organization_id, company_id, pay_schedule_id, period_start, period_end, pay_date, created_by)
  values (run1, org1, ke1, sched, '2026-10-03', '2026-10-09', '2026-10-09', someone);
  insert into pay_run_inputs (id, organization_id, company_id, pay_run_id, employee_id, pay_item_id, quantity, source, entered_by)
  values (uuidv7(), org1, ke1, run1, mary, null, 5, 'entered', someone),
         (uuidv7(), org1, ke1, run1, mary, overtime, 6.5, 'copied', someone);
  begin
    insert into pay_run_inputs (id, organization_id, company_id, pay_run_id, employee_id, pay_item_id, quantity, source, entered_by)
    values (uuidv7(), org1, ke1, run1, mary, null, 4, 'entered', someone);
    raise exception 'basic pay days twice for one person';
  exception when unique_violation then null;
  end;
  begin
    insert into pay_run_inputs (id, organization_id, company_id, pay_run_id, employee_id, quantity, source, entered_by)
    values (uuidv7(), org1, ke1, run1, mary, -1, 'entered', someone);
    raise exception 'negative days';
  exception when check_violation then null;
  end;

  -- Left out for want of days is a reason; a line paid by the day keeps its quantity and unit rate together.
  insert into pay_run_people (id, organization_id, company_id, pay_run_id, employee_id, staff_no, full_name, included, excluded_reason)
  values (uuidv7(), org1, ke1, run1, mary, 'RATE-01', 'Mary Akinyi', false, 'no_time');
  delete from pay_run_people where pay_run_id = run1;
  insert into pay_run_people (id, organization_id, company_id, pay_run_id, employee_id, staff_no, full_name, included,
    basic_pay, gross_pay, taxable_pay, paye, total_deductions, net_pay, employer_costs)
  values (mary, org1, ke1, run1, mary, 'RATE-01', 'Mary Akinyi', true, 2250, 2250, 2250, 0, 0, 2250, 0);
  insert into pay_run_lines (organization_id, company_id, pay_run_id, person_id, kind, code, name, amount, quantity, unit_rate, position)
  values (org1, ke1, run1, mary, 'earning', 'BASIC', 'Basic pay', 2250, 5, 450, 1);
  begin
    insert into pay_run_lines (organization_id, company_id, pay_run_id, person_id, kind, code, name, amount, quantity, position)
    values (org1, ke1, run1, mary, 'earning', 'RATE-OT', 'Overtime', 100, 1, 2);
    raise exception 'a quantity without its unit rate';
  exception when check_violation then null;
  end;

  -- Sent: what was entered can't change.
  update pay_runs set status = 'awaiting_approval' where id = run1;
  begin
    update pay_run_inputs set quantity = 6 where pay_run_id = run1 and pay_item_id is null;
    raise exception 'days changed while waiting for approval';
  exception when raise_exception then
    get stacked diagnostics msg = message_text;
    if msg <> 'PAY-2014' then raise; end if;
  end;

  -- Someone who may only see Uganda sees nothing entered in Kenya; another organization sees nothing.
  perform set_config('app.company_ids', ug1::text, true);
  select count(*) into seen from pay_run_inputs;
  if seen <> 0 then raise exception 'company scope: saw % inputs', seen; end if;
  perform set_config('app.org_id', '00000000-0000-7000-8000-000000000002', true);
  perform set_config('app.company_ids', '00000000-0000-7000-8000-0000000000b1', true);
  select count(*) into seen from pay_run_inputs;
  if seen <> 0 then raise exception 'another organization saw % inputs', seen; end if;

  -- The 2024 wage order covers every Kenyan wage area.
  select count(*) into seen from minimum_wages m join wage_orders w using (country_code, effective_from)
  where m.country_code = 'KE' and w.effective_from = '2024-11-01';
  if seen <> (select count(*) from country_wage_areas where country_code = 'KE' and active) then
    raise exception 'the 2024 wage order has % areas', seen;
  end if;
  if (select hourly from minimum_wages where country_code = 'KE' and effective_from = '2024-11-01' and wage_area_code = 'cities') <> 144.054 then
    raise exception 'the cities hourly minimum lost its third decimal';
  end if;
end
$$;
rollback;
