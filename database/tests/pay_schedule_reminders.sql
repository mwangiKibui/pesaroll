-- ONB-06: reminders to run payroll stay with their schedule's organization and company scope, keep
-- to their limits, and are sent once per pay date. The worker finds due reminders across
-- organizations through one narrow function that never returns who they go to.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  org2 constant uuid := '00000000-0000-7000-8000-000000000002';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  ke2  constant uuid := '00000000-0000-7000-8000-0000000000b1';
  kenya constant uuid := '00000000-0000-7000-8000-000000000501';
  group1 constant uuid := '00000000-0000-7000-8000-000000000502';
  r1 constant uuid := '00000000-0000-7000-8000-000000000511';
  r2 constant uuid := '00000000-0000-7000-8000-000000000512';
  n integer;
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  insert into pay_schedules (id, organization_id, name) values (kenya, org1, 'Kenya reminders');
  insert into pay_schedule_companies (organization_id, pay_schedule_id, company_id) values (org1, kenya, ke1);
  insert into pay_schedules (id, organization_id, name, all_companies) values (group1, org1, 'Group reminders', true);
  insert into pay_schedule_reminders (id, organization_id, pay_schedule_id, amount, unit, channel, recipient)
  values (r1, org1, kenya, 3, 'days', 'email', 'payroll@example.com'),
         (r2, org1, group1, 2, 'days', 'sms', '+254712345678');

  begin
    insert into pay_schedule_reminders (id, organization_id, pay_schedule_id, amount, unit, channel, recipient)
    values ('00000000-0000-7000-8000-000000000513', org1, kenya, 15, 'days', 'email', 'a@example.com');
    raise exception 'a reminder more than 14 days before was saved';
  exception when check_violation then null;
  end;
  begin
    insert into pay_schedule_reminders (id, organization_id, pay_schedule_id, amount, unit, channel, recipient)
    values ('00000000-0000-7000-8000-000000000514', org1, kenya, 13, 'hours', 'email', 'a@example.com');
    raise exception 'a reminder more than 12 hours before was saved';
  exception when check_violation then null;
  end;
  begin
    insert into pay_schedule_reminders (id, organization_id, pay_schedule_id, amount, unit, channel, recipient)
    values ('00000000-0000-7000-8000-000000000515', org1, kenya, 1, 'days', 'fax', 'a@example.com');
    raise exception 'a reminder on an unknown channel was saved';
  exception when check_violation then null;
  end;

  -- Once per reminder and pay date.
  insert into pay_schedule_reminder_sends (organization_id, reminder_id, pay_date) values (org1, r1, '2026-10-30');
  begin
    insert into pay_schedule_reminder_sends (organization_id, reminder_id, pay_date) values (org1, r1, '2026-10-30');
    raise exception 'a reminder was logged twice for one pay date';
  exception when unique_violation then null;
  end;

  -- Someone who only sees the Uganda company doesn't see the Kenya schedule's reminders.
  perform set_config('app.company_ids', ug1::text, true);
  if exists (select 1 from pay_schedule_reminders where id = r1) then raise exception 'company scope ignored for reminders'; end if;
  if not exists (select 1 from pay_schedule_reminders where id = r2) then raise exception 'an all-companies schedule''s reminder is hidden'; end if;

  -- Organization two sees none, and can't add one to organization one's schedule.
  perform set_config('app.org_id', org2::text, true);
  perform set_config('app.company_ids', ke2::text, true);
  if exists (select 1 from pay_schedule_reminders where id in (r1, r2)) then raise exception 'org two sees org one''s reminders'; end if;
  begin
    insert into pay_schedule_reminders (id, organization_id, pay_schedule_id, amount, unit, channel, recipient)
    values ('00000000-0000-7000-8000-000000000516', org1, group1, 1, 'days', 'email', 'x@example.com');
    raise exception 'org two added a reminder to org one''s schedule';
  exception when insufficient_privilege then null;
  end;

  -- The worker, with no organization set, finds every reminder's timing and its organization's
  -- companies, but not where it goes.
  perform set_config('app.org_id', '', true);
  perform set_config('app.company_ids', '', true);
  select count(*) into n from pay_schedule_reminder_timings() t where t.reminder_id in (r1, r2) and t.organization_id = org1
    and t.company_ids @> array[ke1, ug1] and t.frequency = 'monthly' and t.pay_day_rule = 'last_working_day';
  if n <> 2 then raise exception 'the worker should find both reminders with their timing (found %)', n; end if;
  if exists (select 1 from information_schema.routines r join information_schema.parameters p using (specific_name)
             where r.routine_name = 'pay_schedule_reminder_timings' and p.parameter_name in ('recipient', 'channel')) then
    raise exception 'the timings function tells who reminders go to';
  end if;

  -- Deleting a schedule takes its reminders with it.
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  delete from pay_schedules where id = group1;
  if exists (select 1 from pay_schedule_reminders where id = r2) then raise exception 'a deleted schedule''s reminder stayed'; end if;
end $$;
rollback;
