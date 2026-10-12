-- ONB-07: Kenya's statutory items are reference data; an organization's earnings and deductions
-- stay inside it and its company scope; items set by law (and basic pay) are locked; each kind and
-- way of working an item out keeps only its own fields.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  org2 constant uuid := '00000000-0000-7000-8000-000000000002';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  ke2  constant uuid := '00000000-0000-7000-8000-0000000000b1';
  house constant uuid := '00000000-0000-7000-8000-000000000601';
  sacco constant uuid := '00000000-0000-7000-8000-000000000602';
  shif  uuid;
begin
  if (select count(*) from statutory_items where country_code = 'KE') <> 7 then raise exception 'Kenya should have 7 statutory items'; end if;
  if (select string_agg(key, ',' order by sort_order) from statutory_items where country_code = 'KE' and reduces_taxable_pay)
     <> 'shif,nssf_employee,ahl_employee' then raise exception 'SHIF, NSSF and Housing Levy (employee) come off before tax'; end if;

  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  insert into pay_items (id, organization_id, code, name, kind, taxable, calc_method, default_amount)
  values (house, org1, 'HOUSE', 'House allowance', 'earning', true, 'fixed', 15000);
  insert into pay_item_companies (organization_id, pay_item_id, company_id) values (org1, house, ke1);
  insert into pay_items (id, organization_id, code, name, kind, before_tax, calc_method, all_companies)
  values (sacco, org1, 'SACCO', 'SACCO savings', 'deduction', false, 'fixed', true);
  -- Made for the organization by the deploy (it already had companies): locked, for all companies.
  select id into shif from pay_items where organization_id = org1 and locked_key = 'shif' and country_code = 'KE';
  if shif is null then raise exception 'the organization should have its SHIF item'; end if;
  begin
    insert into pay_items (id, organization_id, code, name, kind, calc_method, locked_key, country_code, all_companies)
    values ('00000000-0000-7000-8000-000000000603', org1, 'SHIF2', 'SHIF again', 'deduction', 'by_law', 'shif', 'KE', true);
    raise exception 'an organization got a second SHIF item';
  exception when unique_violation then null;
  end;

  begin
    insert into pay_items (id, organization_id, code, name, kind, calc_method) values ('00000000-0000-7000-8000-000000000604', org1, 'HOUSE', 'Twin', 'earning', 'fixed');
    raise exception 'two items in one organization share a code';
  exception when unique_violation then null;
  end;
  begin
    insert into pay_items (id, organization_id, code, name, kind, before_tax, calc_method) values ('00000000-0000-7000-8000-000000000605', org1, 'BONUS', 'Bonus', 'earning', true, 'each_run');
    raise exception 'an earning was taken before tax';
  exception when check_violation then null;
  end;
  begin
    insert into pay_items (id, organization_id, code, name, kind, calc_method) values ('00000000-0000-7000-8000-000000000606', org1, 'PENSION', 'Pension', 'deduction', 'percent_of_basic');
    raise exception 'a percentage item was saved without its rate';
  exception when check_violation then null;
  end;
  begin
    insert into pay_items (id, organization_id, code, name, kind, calc_method) values ('00000000-0000-7000-8000-000000000607', org1, 'MYTAX', 'My tax', 'deduction', 'by_law');
    raise exception 'an own item claimed to be set by law';
  exception when check_violation then null;
  end;
  begin
    insert into pay_items (id, organization_id, code, name, kind, calc_method, locked_key, country_code) values ('00000000-0000-7000-8000-000000000608', org1, 'FAKE', 'Fake', 'deduction', 'by_law', 'church_tax', 'KE');
    raise exception 'an item locked to an unknown statutory item was saved';
  exception when foreign_key_violation then null;
  end;

  -- Locked items can't be changed or deleted by the app; own items can.
  update pay_items set default_amount = 18000 where id = house;
  begin
    update pay_items set name = 'Health levy' where id = shif;
    raise exception 'a statutory item was changed';
  exception when raise_exception then null;
  end;
  begin
    delete from pay_items where id = shif;
    raise exception 'a statutory item was deleted';
  exception when raise_exception then null;
  end;

  -- Someone who only sees the Uganda company sees what all companies use, not Kenya's own items.
  perform set_config('app.company_ids', ug1::text, true);
  if exists (select 1 from pay_items where id = house) then raise exception 'company scope ignored for pay items'; end if;
  if not exists (select 1 from pay_items where id = sacco) then raise exception 'an all-companies item is hidden'; end if;

  -- Organization two sees nothing of organization one's.
  perform set_config('app.org_id', org2::text, true);
  perform set_config('app.company_ids', ke2::text, true);
  if exists (select 1 from pay_items where id in (house, sacco, shif)) then raise exception 'org two sees org one''s pay items'; end if;
  begin
    insert into pay_item_companies (organization_id, pay_item_id, company_id) values (org1, sacco, ke2);
    raise exception 'org two linked a company to org one''s item';
  exception when insufficient_privilege or foreign_key_violation then null;
  end;
end $$;
rollback;
