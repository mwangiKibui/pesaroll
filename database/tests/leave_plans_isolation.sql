-- ONB-08: Kenya's statutory leave is reference data; leave types and plans stay inside their
-- organization and company scope; statutory types are locked; each rule keeps to its way of
-- earning; a company's or schedule's default plan is its organization's and can't be deleted.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  org2 constant uuid := '00000000-0000-7000-8000-000000000002';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  ke2  constant uuid := '00000000-0000-7000-8000-0000000000b1';
  daily constant uuid := '00000000-0000-7000-8000-000000000701';
  study constant uuid := '00000000-0000-7000-8000-000000000702';
  annual uuid;
  standard uuid;
begin
  if (select count(*) from statutory_leave where country_code = 'KE') <> 5 then raise exception 'Kenya should have 5 statutory leave types'; end if;
  if not exists (select 1 from statutory_leave where country_code = 'KE' and key = 'sick' and full_pay_days = 7 and half_pay_days = 7 and waiting_months = 2) then
    raise exception 'sick leave should be 7 days full and 7 half pay after 2 months';
  end if;

  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  -- Made by the deploy for an organization that already had companies.
  select id into annual from leave_types where organization_id = org1 and locked_key = 'annual';
  select id into standard from leave_plans where organization_id = org1 and name = 'Standard';
  if annual is null or standard is null then raise exception 'the organization should have its locked types and Standard plan'; end if;

  insert into leave_types (id, organization_id, code, name) values (study, org1, 'STUDY', 'Study leave');
  insert into leave_plans (id, organization_id, name, hours_per_day) values (daily, org1, 'Daily-paid', 8);
  insert into leave_plan_companies (organization_id, leave_plan_id, company_id) values (org1, daily, ke1);
  insert into leave_plan_rules (id, organization_id, leave_plan_id, leave_type_id, earn_method, amount, unit)
  values ('00000000-0000-7000-8000-000000000711', org1, daily, annual, 'per_day_worked', 0.0805, 'days'),
         ('00000000-0000-7000-8000-000000000712', org1, daily, study, 'yearly', 10, 'days');

  begin
    insert into leave_plan_rules (id, organization_id, leave_plan_id, leave_type_id, earn_method, amount, unit)
    values ('00000000-0000-7000-8000-000000000713', org1, daily, study, 'monthly', 1, 'days');
    raise exception 'a plan gave one leave type twice';
  exception when unique_violation then null;
  end;
  begin
    insert into leave_plan_rules (id, organization_id, leave_plan_id, leave_type_id, earn_method, amount, unit, half_pay)
    values ('00000000-0000-7000-8000-000000000714', org1, standard, study, 'monthly', 1, 'days', 1);
    raise exception 'half pay was given on leave earned monthly';
  exception when check_violation then null;
  end;
  begin
    insert into leave_plan_rules (id, organization_id, leave_plan_id, leave_type_id, earn_method, amount, unit)
    values ('00000000-0000-7000-8000-000000000715', org1, standard, study, 'unlimited', 5, 'days');
    raise exception 'leave with no limit was given an amount';
  exception when check_violation then null;
  end;
  begin
    insert into leave_plan_rules (id, organization_id, leave_plan_id, leave_type_id, earn_method, amount, unit)
    values ('00000000-0000-7000-8000-000000000716', org1, standard, study, 'yearly', 5, 'weeks');
    raise exception 'leave was counted in weeks';
  exception when check_violation then null;
  end;

  -- Statutory types can't be deleted or renamed by the app; others can.
  begin
    delete from leave_types where id = annual;
    raise exception 'a statutory leave type was deleted';
  exception when raise_exception then null;
  end;
  begin
    update leave_types set name = 'Holiday' where id = annual;
    raise exception 'a statutory leave type was renamed';
  exception when raise_exception then null;
  end;
  update leave_types set needs_proof = true where id = annual;   -- its handling can change
  begin
    delete from leave_types where id = study;
    raise exception 'a leave type still in a plan was deleted';
  exception when foreign_key_violation then null;
  end;

  -- Defaults: the company's and the schedule's, from the same organization; kept while default.
  update companies set default_leave_plan_id = standard where id = ke1;
  update pay_schedules set default_leave_plan_id = daily where organization_id = org1;
  begin
    delete from leave_plans where id = standard;
    raise exception 'a company''s default leave plan was deleted';
  exception when foreign_key_violation then null;
  end;

  -- Someone who only sees the Uganda company doesn't see the Kenya-only plan or its rules.
  perform set_config('app.company_ids', ug1::text, true);
  if exists (select 1 from leave_plans where id = daily) then raise exception 'company scope ignored for leave plans'; end if;
  if exists (select 1 from leave_plan_rules where leave_plan_id = daily) then raise exception 'company scope ignored for leave rules'; end if;
  if not exists (select 1 from leave_plans where id = standard) then raise exception 'an all-companies plan is hidden'; end if;

  -- Organization two sees none of it and can't point at it.
  perform set_config('app.org_id', org2::text, true);
  perform set_config('app.company_ids', ke2::text, true);
  if exists (select 1 from leave_plans where id in (daily, standard)) or exists (select 1 from leave_types where id in (annual, study)) then
    raise exception 'org two sees org one''s leave';
  end if;
  begin
    update companies set default_leave_plan_id = standard where id = ke2;
    raise exception 'a company took another organization''s leave plan';
  exception when foreign_key_violation then null;
  end;
end $$;
rollback;
