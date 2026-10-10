-- ONB-06: payroll schedules stay inside their organization; people scoped to some companies see
-- only the schedules those companies use (or that all companies use); a company's default schedule
-- comes from its own organization and can't be deleted while it is the default; the pay day rules
-- hold.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  org2 constant uuid := '00000000-0000-7000-8000-000000000002';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  ke2  constant uuid := '00000000-0000-7000-8000-0000000000b1';
  mid  constant uuid := '00000000-0000-7000-8000-000000000301';
  own  constant uuid := '00000000-0000-7000-8000-000000000302';
  everyone constant uuid := '00000000-0000-7000-8000-000000000303';
  other constant uuid := '00000000-0000-7000-8000-000000000304';
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  insert into pay_schedules (id, organization_id, name, pay_day_rule, pay_day) values (mid, org1, 'Mid-month', 'fixed_day', 15);
  insert into pay_schedules (id, organization_id, name) values (own, org1, 'Kenya monthly');
  insert into pay_schedules (id, organization_id, name, all_companies) values (everyone, org1, 'Group monthly', true);
  insert into pay_schedule_companies (organization_id, pay_schedule_id, company_id) values (org1, mid, ke1), (org1, own, ke1);
  update companies set default_pay_schedule_id = own where id = ke1;

  -- Monthly on the last working day, cut-off on the 25th, unless said otherwise.
  if not exists (select 1 from pay_schedules where id = own and frequency = 'monthly' and pay_day_rule = 'last_working_day' and pay_day is null and cutoff_day = 25) then
    raise exception 'a schedule should default to monthly, last working day, cut-off 25th';
  end if;
  begin
    insert into pay_schedules (id, organization_id, name) values ('00000000-0000-7000-8000-000000000305', org1, ' kenya MONTHLY ');
    raise exception 'two schedules in one organization share a name';
  exception when unique_violation then null;
  end;
  begin
    insert into pay_schedules (id, organization_id, name, frequency) values ('00000000-0000-7000-8000-000000000306', org1, 'Hourly', 'hourly');
    raise exception 'an hourly schedule was saved';
  exception when check_violation then null;
  end;
  -- Daily: every working day at a pay time (reminders count hours back from it); nothing else.
  insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, cutoff_day, pay_time)
  values ('00000000-0000-7000-8000-000000000318', org1, 'Casuals daily', 'daily', null, null, '17:00');
  begin
    insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, cutoff_day)
    values ('00000000-0000-7000-8000-000000000319', org1, 'No time', 'daily', null, null);
    raise exception 'a daily schedule was saved without its pay time';
  exception when check_violation then null;
  end;
  begin
    insert into pay_schedules (id, organization_id, name, pay_time) values ('00000000-0000-7000-8000-000000000320', org1, 'Monthly at five', '17:00');
    raise exception 'a monthly schedule took a pay time';
  exception when check_violation then null;
  end;
  -- Weekly and every two weeks: a weekday (Monday 1 to Friday 5) and a cut-off in days before it;
  -- every two weeks also its first pay date, on that weekday. No monthly rule or day of the month.
  insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, pay_weekday, cutoff_day, cutoff_days_before)
  values ('00000000-0000-7000-8000-000000000310', org1, 'Casuals weekly', 'weekly', null, 5, null, 2);
  insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, pay_weekday, first_pay_date, cutoff_day, cutoff_days_before)
  values ('00000000-0000-7000-8000-000000000311', org1, 'Fortnightly', 'fortnightly', null, 5, '2026-10-16', null, 3);
  begin
    insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, pay_weekday, cutoff_day, cutoff_days_before)
    values ('00000000-0000-7000-8000-000000000312', org1, 'Saturdays', 'weekly', null, 6, null, 2);
    raise exception 'a weekly schedule paying on a Saturday was saved';
  exception when check_violation then null;
  end;
  begin
    insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, pay_weekday, cutoff_day)
    values ('00000000-0000-7000-8000-000000000321', org1, 'No cut-off', 'weekly', null, 5, null);
    raise exception 'a weekly schedule was saved without its cut-off';
  exception when check_violation then null;
  end;
  begin
    insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, pay_weekday, cutoff_day, cutoff_days_before)
    values ('00000000-0000-7000-8000-000000000313', org1, 'Late weekly', 'weekly', null, 5, null, 7);
    raise exception 'a weekly cut-off a week or more before pay day was saved';
  exception when check_violation then null;
  end;
  begin
    insert into pay_schedules (id, organization_id, name, frequency, pay_weekday, cutoff_days_before)
    values ('00000000-0000-7000-8000-000000000314', org1, 'Half monthly', 'weekly', 5, 2);
    raise exception 'a weekly schedule kept a monthly pay day rule and cut-off day';
  exception when check_violation then null;
  end;
  begin
    insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, pay_weekday, cutoff_day, cutoff_days_before)
    values ('00000000-0000-7000-8000-000000000315', org1, 'No start', 'fortnightly', null, 5, null, 2);
    raise exception 'an every-two-weeks schedule was saved without its first pay date';
  exception when check_violation then null;
  end;
  begin
    insert into pay_schedules (id, organization_id, name, frequency, pay_day_rule, pay_weekday, first_pay_date, cutoff_day, cutoff_days_before)
    values ('00000000-0000-7000-8000-000000000316', org1, 'Wrong start', 'fortnightly', null, 5, '2026-10-15', null, 2);
    raise exception 'an every-two-weeks first pay date off its weekday was saved';
  exception when check_violation then null;
  end;
  begin
    insert into pay_schedules (id, organization_id, name, pay_weekday) values ('00000000-0000-7000-8000-000000000317', org1, 'Monthly Friday', 5);
    raise exception 'a monthly schedule took a weekday';
  exception when check_violation then null;
  end;
  begin
    insert into pay_schedules (id, organization_id, name, pay_day_rule) values ('00000000-0000-7000-8000-000000000307', org1, 'No day', 'fixed_day');
    raise exception 'a fixed-day schedule was saved without its day';
  exception when check_violation then null;
  end;
  begin
    insert into pay_schedules (id, organization_id, name, pay_day_rule, pay_day) values ('00000000-0000-7000-8000-000000000308', org1, 'Day 31', 'fixed_day', 31);
    raise exception 'a pay day after the 28th was saved';
  exception when check_violation then null;
  end;
  begin
    insert into pay_schedules (id, organization_id, name, cutoff_day) values ('00000000-0000-7000-8000-000000000309', org1, 'Late cut-off', 30);
    raise exception 'a cut-off after the 28th was saved';
  exception when check_violation then null;
  end;
  begin
    delete from pay_schedules where id = own;
    raise exception 'a company''s default schedule was deleted';
  exception when foreign_key_violation then null;
  end;

  -- Someone who only sees the Uganda company sees the all-companies schedule, not Kenya's own ones.
  perform set_config('app.company_ids', ug1::text, true);
  if exists (select 1 from pay_schedules where id in (mid, own)) then raise exception 'company scope ignored for schedules'; end if;
  if not exists (select 1 from pay_schedules where id = everyone) then raise exception 'an all-companies schedule is hidden'; end if;
  if exists (select 1 from pay_schedule_companies where company_id = ke1) then raise exception 'company scope ignored for schedule links'; end if;

  -- Organization two sees nothing and can't link or point at organization one's schedules.
  perform set_config('app.org_id', org2::text, true);
  perform set_config('app.company_ids', ke2::text, true);
  if exists (select 1 from pay_schedules where id in (mid, own, everyone)) then raise exception 'org two sees org one''s schedules'; end if;
  insert into pay_schedules (id, organization_id, name) values (other, org2, 'Kenya monthly');   -- same name, other organization
  begin
    update companies set default_pay_schedule_id = own where id = ke2;
    raise exception 'a company took another organization''s schedule as default';
  exception when foreign_key_violation then null;
  end;
  begin
    insert into pay_schedule_companies (organization_id, pay_schedule_id, company_id) values (org1, own, ke2);
    raise exception 'org two linked a company to org one''s schedule';
  exception when insufficient_privilege or foreign_key_violation then null;
  end;
end $$;
rollback;
