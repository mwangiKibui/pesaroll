-- ONB-07: who a deduction's money is paid to. Payees belong to the organization (shared by its
-- companies and items), are paid by bank, M-Pesa paybill, cheque or cash with only that way's
-- details, have one name each, go only on deductions and employer costs, and can't be deleted
-- while an item uses them. A person's member or account number goes on their assignment.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  stima constant uuid := '00000000-0000-7000-8000-000000000701';
  bank  constant uuid := '00000000-0000-7000-8000-000000000702';
  shares constant uuid := '00000000-0000-7000-8000-000000000621';
  amina constant uuid := '00000000-0000-7000-8000-000000000821';
  seen int;
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  insert into payees (id, organization_id, name, method, paybill, paybill_account, our_reference)
  values (stima, org1, 'Stima SACCO', 'mpesa_paybill', '510800', 'EMP-4471', 'EMP-4471');
  insert into payees (id, organization_id, name, method, institution, country_code, bank_code, branch_code, account_number)
  values (bank, org1, 'Kenya Union of Hotel Workers', 'bank', 'bank', 'KE', '68', '047', '0123456789012');
  insert into payees (id, organization_id, name, method, institution, country_code, sacco_code, sacco_branch_code, account_number)
  values ('00000000-0000-7000-8000-000000000708', org1, 'Welfare Fund', 'bank', 'sacco', 'KE', 'STIMA', '005', 'ST-00451');
  insert into payees (id, organization_id, name, method, cheque_payable_to)
  values ('00000000-0000-7000-8000-000000000703', org1, 'Jubilee Insurance', 'cheque', 'Jubilee Health Insurance Ltd');
  insert into payees (id, organization_id, name, method) values ('00000000-0000-7000-8000-000000000704', org1, 'Welfare committee', 'cash');

  begin
    insert into payees (id, organization_id, name, method, paybill) values ('00000000-0000-7000-8000-000000000705', org1, 'stima sacco', 'mpesa_paybill', '510801');
    raise exception 'two payees share a name';
  exception when unique_violation then null;
  end;
  begin
    insert into payees (id, organization_id, name, method, paybill) values ('00000000-0000-7000-8000-000000000706', org1, 'Half bank', 'bank', '510800');
    raise exception 'a bank payee was saved without its bank details';
  exception when check_violation then null;
  end;
  begin
    insert into payees (id, organization_id, name, method, institution, country_code, account_number) values ('00000000-0000-7000-8000-000000000709', org1, 'No SACCO', 'bank', 'sacco', 'KE', 'ST-1');
    raise exception 'a SACCO transfer was saved without the SACCO';
  exception when check_violation then null;
  end;
  begin
    insert into payees (id, organization_id, name, method, institution, country_code, sacco_code, sacco_branch_code, account_number)
    values ('00000000-0000-7000-8000-000000000710', org1, 'Wrong branch', 'bank', 'sacco', 'KE', 'MENTOR', '012', 'MT-1');   -- Mentor has no 012
    raise exception 'a SACCO branch that does not exist was saved';
  exception when foreign_key_violation then null;
  end;
  if (select count(*) from sacco_branches where country_code = 'KE' and sacco_code = 'STIMA') <> 12 then raise exception 'Stima SACCO should have 12 branches'; end if;
  begin
    insert into payees (id, organization_id, name, method) values ('00000000-0000-7000-8000-000000000707', org1, 'No paybill', 'mpesa_paybill');
    raise exception 'a paybill payee was saved without its paybill';
  exception when check_violation then null;
  end;

  insert into pay_items (id, organization_id, code, name, kind, calc_method, default_amount, all_companies, payee_id)
  values (shares, org1, 'SACCO3', 'SACCO shares', 'deduction', 'fixed', 1500, true, stima);
  if (select reference_source from payees where id = stima) <> 'typed' then raise exception 'member numbers should be typed unless said'; end if;
  insert into payees (id, organization_id, name, method, paybill, reference_source)
  values ('00000000-0000-7000-8000-000000000711', org1, 'HELB', 'mpesa_paybill', '200800', 'national_id');
  begin
    insert into payees (id, organization_id, name, method, reference_source) values ('00000000-0000-7000-8000-000000000712', org1, 'Odd', 'cash', 'shoe_size');
    raise exception 'an unknown way of identifying people was saved';
  exception when check_violation then null;
  end;
  begin
    insert into pay_items (id, organization_id, code, name, kind, taxable, calc_method, all_companies, payee_id)
    values ('00000000-0000-7000-8000-000000000622', org1, 'BONUS3', 'Bonus', 'earning', true, 'each_run', true, stima);
    raise exception 'an earning was given a payee';
  exception when check_violation then null;
  end;
  begin
    delete from payees where id = stima;
    raise exception 'a payee in use was deleted';
  exception when foreign_key_violation then null;
  end;

  insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date)
  values (amina, org1, ke1, 'PAYEE-01', 'Amina', 'Otieno', '2024-01-15');
  insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, amount, starts_on, reference)
  values ('00000000-0000-7000-8000-000000000921', org1, ke1, shares, amina, 2000, '2026-11-01', 'M-10234');

  -- Another organization sees none of them.
  perform set_config('app.org_id', '00000000-0000-7000-8000-000000000002', true);
  perform set_config('app.company_ids', '00000000-0000-7000-8000-0000000000b1', true);
  select count(*) into seen from payees;
  if seen <> 0 then raise exception 'another organization saw % payees', seen; end if;
end
$$;
rollback;
