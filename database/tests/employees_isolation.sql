-- ONB-09: employees belong to one company and are seen only by people who may see it; their ID
-- numbers and KRA PINs are stored encrypted with a lookup that stops duplicates among current
-- staff; each has one current job, pay and account; job details come from their organization.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  org2 constant uuid := '00000000-0000-7000-8000-000000000002';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  ke2  constant uuid := '00000000-0000-7000-8000-0000000000b1';
  amina constant uuid := '00000000-0000-7000-8000-000000000801';
  otieno constant uuid := '00000000-0000-7000-8000-000000000802';
  schedule uuid;
  plan uuid;
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  select id into schedule from pay_schedules where organization_id = org1 limit 1;
  select id into plan from leave_plans where organization_id = org1 limit 1;

  insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date, kra_pin_enc, kra_pin_lookup, kra_pin_last4,
    email, phone, nssf_no, sha_no)
  values (amina, org1, ke1, 'EMP-001', 'Amina', 'Otieno', '2024-01-15', '\x01', 'lookup-a', '678Z',
    'amina@example.com', '+254712345678', '201234567', 'CR1234567-8');
  insert into employments (id, organization_id, company_id, employee_id, valid_from, pay_schedule_id, leave_plan_id, employment_type)
  values ('00000000-0000-7000-8000-000000000811', org1, ke1, amina, '2024-01-15', schedule, plan, 'permanent');
  insert into compensations (id, organization_id, company_id, employee_id, valid_from, basic_amount, pay_basis, currency)
  values ('00000000-0000-7000-8000-000000000821', org1, ke1, amina, '2024-01-15', 85000, 'monthly', 'KES');
  insert into employee_payment_accounts (id, organization_id, company_id, employee_id, method, mpesa_phone_enc, mpesa_last4, valid_from)
  values ('00000000-0000-7000-8000-000000000831', org1, ke1, amina, 'mpesa', '\x02', '5678', '2024-01-15');

  begin
    insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date) values (otieno, org1, ke1, 'EMP-001', 'Otieno', 'Kamau', '2024-02-01');
    raise exception 'two employees in one company share a staff number';
  exception when unique_violation then null;
  end;
  begin
    insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date, kra_pin_lookup)
    values (otieno, org1, ug1, 'EMP-002', 'Otieno', 'Kamau', '2024-02-01', 'lookup-a');
    raise exception 'two current employees share a KRA PIN';
  exception when unique_violation then null;
  end;
  begin
    insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date, email)
    values (otieno, org1, ug1, 'EMP-002', 'Otieno', 'Kamau', '2024-02-01', 'amina@example.com');
    raise exception 'two current employees share an email address';
  exception when unique_violation then null;
  end;
  begin
    insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date, phone)
    values (otieno, org1, ug1, 'EMP-002', 'Otieno', 'Kamau', '2024-02-01', '+254712345678');
    raise exception 'two current employees share a phone number';
  exception when unique_violation then null;
  end;
  begin
    insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date, nssf_no)
    values (otieno, org1, ug1, 'EMP-002', 'Otieno', 'Kamau', '2024-02-01', '201234567');
    raise exception 'two current employees share an NSSF number';
  exception when unique_violation then null;
  end;
  begin
    insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date, sha_no)
    values (otieno, org1, ug1, 'EMP-002', 'Otieno', 'Kamau', '2024-02-01', 'CR1234567-8');
    raise exception 'two current employees share a SHA number';
  exception when unique_violation then null;
  end;
  begin
    insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date, exit_date) values (otieno, org1, ke1, 'EMP-002', 'Otieno', 'Kamau', '2024-02-01', '2024-01-01');
    raise exception 'someone left before they joined';
  exception when check_violation then null;
  end;
  begin
    insert into employments (id, organization_id, company_id, employee_id, valid_from, pay_schedule_id, leave_plan_id, employment_type)
    values ('00000000-0000-7000-8000-000000000812', org1, ke1, amina, '2025-01-01', schedule, plan, 'permanent');
    raise exception 'an employee had two current jobs';
  exception when unique_violation then null;
  end;
  begin
    insert into compensations (id, organization_id, company_id, employee_id, valid_from, basic_amount, pay_basis, currency)
    values ('00000000-0000-7000-8000-000000000822', org1, ke1, amina, '2025-01-01', 900, 'yearly', 'KES');
    raise exception 'pay was given a basis no schedule has';
  exception when check_violation then null;
  end;
  begin
    insert into employments (id, organization_id, company_id, employee_id, valid_from, pay_schedule_id, leave_plan_id, employment_type, contract_end)
    values ('00000000-0000-7000-8000-000000000813', org1, ke1, amina, '2024-01-15', schedule, plan, 'permanent', '2025-01-01');
    raise exception 'a permanent job had a contract end';
  exception when check_violation then null;
  end;

  -- A former employee's numbers can come back on a new hire.
  update employees set exit_date = '2025-06-30' where id = amina;
  insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date, kra_pin_lookup)
  values (otieno, org1, ug1, 'EMP-002', 'Amina', 'Otieno', '2025-09-01', 'lookup-a');

  -- Someone who only sees the Uganda company sees only its employees and their details.
  perform set_config('app.company_ids', ug1::text, true);
  if exists (select 1 from employees where id = amina) or exists (select 1 from compensations where employee_id = amina)
     or exists (select 1 from employments where employee_id = amina) or exists (select 1 from employee_payment_accounts where employee_id = amina) then
    raise exception 'company scope ignored for employees';
  end if;
  if not exists (select 1 from employees where id = otieno) then raise exception 'the Uganda employee is hidden'; end if;

  -- Organization two sees none of them and can't give its job a schedule of organization one's.
  perform set_config('app.org_id', org2::text, true);
  perform set_config('app.company_ids', ke2::text, true);
  if exists (select 1 from employees where id in (amina, otieno)) then raise exception 'org two sees org one''s employees'; end if;
  insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date)
  values ('00000000-0000-7000-8000-000000000803', org2, ke2, 'EMP-001', 'Wanjiru', 'Kamau', '2024-01-01');
  begin
    insert into employments (id, organization_id, company_id, employee_id, valid_from, pay_schedule_id, leave_plan_id, employment_type)
    values ('00000000-0000-7000-8000-000000000814', org2, ke2, '00000000-0000-7000-8000-000000000803', '2024-01-01', schedule, plan, 'permanent');
    raise exception 'org two used org one''s schedule';
  exception when foreign_key_violation then null;
  end;
end $$;
rollback;
