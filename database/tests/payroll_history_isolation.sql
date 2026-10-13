-- ONB-10: imported payroll history. One row per employee per month (the first of the month), in a
-- batch that goes with its rows; kept inside the organization and its company scope.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  amina constant uuid := '00000000-0000-7000-8000-000000000831';
  batch constant uuid := '00000000-0000-7000-8000-000000000a01';
  someone uuid;
  seen int;
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  select id into someone from users limit 1;
  insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date)
  values (amina, org1, ke1, 'HIST-01', 'Amina', 'Otieno', '2024-01-15');
  insert into payroll_history_batches (id, organization_id, company_id, file_name, row_count, first_period, last_period, gross_total, paye_total, imported_by)
  values (batch, org1, ke1, 'history.xlsx', 2, '2026-01-01', '2026-02-01', 170000, 30000, someone);
  insert into payroll_history_lines (id, organization_id, company_id, batch_id, employee_id, period, basic_pay, gross_pay, nssf_employee, shif,
    housing_levy_employee, paye, net_pay)
  values ('00000000-0000-7000-8000-000000000b01', org1, ke1, batch, amina, '2026-01-01', 85000, 85000, 4320, 2338, 1275, 15000, 62067),
         ('00000000-0000-7000-8000-000000000b02', org1, ke1, batch, amina, '2026-02-01', 85000, 85000, 4320, 2338, 1275, 15000, 62067);

  begin
    insert into payroll_history_lines (id, organization_id, company_id, batch_id, employee_id, period, basic_pay, gross_pay, nssf_employee, shif, paye)
    values ('00000000-0000-7000-8000-000000000b03', org1, ke1, batch, amina, '2026-01-01', 1, 1, 0, 0, 0);
    raise exception 'one person got the same month twice';
  exception when unique_violation then null;
  end;
  begin
    insert into payroll_history_lines (id, organization_id, company_id, batch_id, employee_id, period, basic_pay, gross_pay, nssf_employee, shif, paye)
    values ('00000000-0000-7000-8000-000000000b04', org1, ke1, batch, amina, '2026-03-15', 1, 1, 0, 0, 0);
    raise exception 'a month that is not the first of the month was saved';
  exception when check_violation then null;
  end;
  begin
    insert into payroll_history_lines (id, organization_id, company_id, batch_id, employee_id, period, basic_pay, gross_pay, nssf_employee, shif, paye)
    values ('00000000-0000-7000-8000-000000000b05', org1, ke1, batch, amina, '2026-04-01', -1, 1, 0, 0, 0);
    raise exception 'a negative amount was saved';
  exception when check_violation then null;
  end;

  -- Someone who may only see Uganda sees none of Kenya's history.
  perform set_config('app.company_ids', ug1::text, true);
  select count(*) into seen from payroll_history_lines;
  if seen <> 0 then raise exception 'company scope: saw % history rows', seen; end if;
  perform set_config('app.org_id', '00000000-0000-7000-8000-000000000002', true);
  perform set_config('app.company_ids', '00000000-0000-7000-8000-0000000000b1', true);
  select count(*) into seen from payroll_history_batches;
  if seen <> 0 then raise exception 'another organization saw % batches', seen; end if;

  -- Deleting a batch takes its rows.
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  delete from payroll_history_batches where id = batch;
  select count(*) into seen from payroll_history_lines;
  if seen <> 0 then raise exception 'a deleted batch left % rows', seen; end if;
end
$$;
rollback;
