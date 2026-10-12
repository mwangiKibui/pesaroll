-- ONB-07: who gets an earning or deduction: everyone at a company or one person there, with
-- their amount or rate and dates. Kept inside the organization and its company scope; items set
-- by law can't be given out (they apply to everyone); an item only goes to companies it serves;
-- each way of working an item out keeps only its own fields.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  house constant uuid := '00000000-0000-7000-8000-000000000611';
  sacco constant uuid := '00000000-0000-7000-8000-000000000612';
  pension constant uuid := '00000000-0000-7000-8000-000000000613';
  overtime constant uuid := '00000000-0000-7000-8000-000000000614';
  amina constant uuid := '00000000-0000-7000-8000-000000000811';
  okello constant uuid := '00000000-0000-7000-8000-000000000812';
  shif uuid;
  seen int;
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  insert into pay_items (id, organization_id, code, name, kind, taxable, calc_method, default_amount)
  values (house, org1, 'HOUSE2', 'House allowance', 'earning', true, 'fixed', 15000);
  insert into pay_item_companies (organization_id, pay_item_id, company_id) values (org1, house, ke1);   -- Kenya only
  insert into pay_items (id, organization_id, code, name, kind, calc_method, all_companies)
  values (sacco, org1, 'SACCO2', 'SACCO savings', 'deduction', 'fixed', true);
  insert into pay_items (id, organization_id, code, name, kind, before_tax, calc_method, rate, all_companies)
  values (pension, org1, 'PENSION2', 'Pension', 'deduction', true, 'percent_of_basic', 5, true);
  insert into pay_items (id, organization_id, code, name, kind, taxable, calc_method, all_companies)
  values (overtime, org1, 'OVERTIME2', 'Overtime', 'earning', true, 'each_run', true);
  select id into shif from pay_items where organization_id = org1 and locked_key = 'shif';
  insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date)
  values (amina, org1, ke1, 'EPI-01', 'Amina', 'Otieno', '2024-01-15'), (okello, org1, ug1, 'EPI-02', 'Okello', 'Odongo', '2024-01-15');

  -- Everyone at a company (people hired later too), and one person with their own amount.
  insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, amount, starts_on)
  values ('00000000-0000-7000-8000-000000000901', org1, ke1, house, null, 15000, '2026-11-01');
  insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, amount, starts_on, ends_on)
  values ('00000000-0000-7000-8000-000000000902', org1, ke1, sacco, amina, 2000, '2026-11-01', '2027-03-31');
  insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, rate, starts_on)
  values ('00000000-0000-7000-8000-000000000903', org1, ug1, pension, okello, 7.5, '2026-11-01');
  insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, starts_on)
  values ('00000000-0000-7000-8000-000000000904', org1, ke1, overtime, null, '2026-11-01');

  begin
    insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, starts_on)
    values ('00000000-0000-7000-8000-000000000905', org1, ke1, shif, amina, '2026-11-01');
    raise exception 'an item set by law was given out';
  exception when raise_exception then
    if sqlerrm <> 'ONB-3545' then raise; end if;
  end;
  begin
    insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, amount, starts_on)
    values ('00000000-0000-7000-8000-000000000906', org1, ug1, house, okello, 15000, '2026-11-01');
    raise exception 'an item went to a company it does not serve';
  exception when raise_exception then
    if sqlerrm <> 'ONB-3546' then raise; end if;
  end;
  begin
    insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, rate, starts_on)
    values ('00000000-0000-7000-8000-000000000907', org1, ke1, sacco, null, 5, '2026-11-01');
    raise exception 'a fixed item was given a rate';
  exception when raise_exception then
    if sqlerrm <> 'ONB-3547' then raise; end if;
  end;
  begin
    insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, amount, starts_on)
    values ('00000000-0000-7000-8000-000000000908', org1, ug1, overtime, okello, 500, '2026-11-01');
    raise exception 'an item entered each payroll was given an amount';
  exception when raise_exception then
    if sqlerrm <> 'ONB-3547' then raise; end if;
  end;
  begin
    insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, amount, starts_on, ends_on)
    values ('00000000-0000-7000-8000-000000000909', org1, ug1, sacco, okello, 1000, '2026-11-01', '2026-10-01');
    raise exception 'an item ended before it started';
  exception when check_violation then null;
  end;
  begin
    insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, amount, starts_on)
    values ('00000000-0000-7000-8000-000000000910', org1, ke1, sacco, amina, 3000, '2026-12-01');
    raise exception 'one person got the same item twice';
  exception when unique_violation then null;
  end;
  begin
    insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, amount, starts_on)
    values ('00000000-0000-7000-8000-000000000911', org1, ke1, house, null, 9000, '2026-12-01');
    raise exception 'a company got "everyone" twice for one item';
  exception when unique_violation then null;
  end;
  begin
    insert into employee_pay_items (id, organization_id, company_id, pay_item_id, employee_id, rate, starts_on)
    values ('00000000-0000-7000-8000-000000000912', org1, ug1, pension, amina, 5, '2026-11-01');   -- Amina works at ke1
    raise exception 'someone got an item at a company they do not work for';
  exception when foreign_key_violation then null;
  end;

  -- Someone who may only see Uganda sees only Uganda's.
  perform set_config('app.company_ids', ug1::text, true);
  select count(*) into seen from employee_pay_items;
  if seen <> 1 then raise exception 'company scope: saw % assignments, expected 1', seen; end if;

  -- Another organization sees none.
  perform set_config('app.org_id', '00000000-0000-7000-8000-000000000002', true);
  perform set_config('app.company_ids', '00000000-0000-7000-8000-0000000000b1', true);
  select count(*) into seen from employee_pay_items;
  if seen <> 0 then raise exception 'another organization saw % assignments', seen; end if;

  -- An employee's items go with them.
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  delete from employees where id = amina;
  select count(*) into seen from employee_pay_items where employee_id = amina;
  if seen <> 0 then raise exception 'a deleted employee kept % items', seen; end if;
end
$$;
rollback;
