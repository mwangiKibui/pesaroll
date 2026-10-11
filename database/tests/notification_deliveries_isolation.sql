-- NTF-01: messages stay inside their organization; once queued, only their delivery can change
-- (never who they go to or what they say), and none can be deleted, since they are also the usage
-- that billing counts. A sent message has its billed units.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  org2 constant uuid := '00000000-0000-7000-8000-000000000002';
  mail constant uuid := '00000000-0000-7000-8000-000000000401';
  sms  constant uuid := '00000000-0000-7000-8000-000000000402';
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', '', true);
  insert into notification_deliveries (id, organization_id, channel, recipient, template, data)
  values (mail, org1, 'email', 'wanjiru@example.com', 'payroll_reminder', '{"schedule": "Monthly"}'),
         (sms, org1, 'sms', '+254712345678', 'payroll_reminder', '{}');
  if (select status from notification_deliveries where id = mail) <> 'queued' then raise exception 'a new message should be queued'; end if;

  update notification_deliveries set status = 'sent', units = 2, provider_message_id = 'ATXid_1', sent_at = now(), attempts = 1 where id = sms;
  begin
    update notification_deliveries set status = 'sent', sent_at = now() where id = mail;
    raise exception 'a message was marked sent without its billed units';
  exception when check_violation then null;
  end;
  begin
    insert into notification_deliveries (id, organization_id, channel, recipient, template) values ('00000000-0000-7000-8000-000000000403', org1, 'fax', '+254712345678', 'payroll_reminder');
    raise exception 'a message on an unknown channel was saved';
  exception when check_violation then null;
  end;
  begin
    update notification_deliveries set recipient = 'someone@else.com' where id = mail;
    raise exception 'a queued message''s recipient was changed';
  exception when insufficient_privilege then null;
  end;
  begin
    delete from notification_deliveries where id = sms;
    raise exception 'a message was deleted';
  exception when insufficient_privilege then null;
  end;

  -- Organization two sees and changes nothing of organization one's.
  perform set_config('app.org_id', org2::text, true);
  if exists (select 1 from notification_deliveries where id in (mail, sms)) then raise exception 'org two sees org one''s messages'; end if;
  update notification_deliveries set status = 'failed' where id = mail;
  perform set_config('app.org_id', org1::text, true);
  if (select status from notification_deliveries where id = mail) <> 'queued' then raise exception 'org two changed org one''s message'; end if;
  begin
    perform set_config('app.org_id', org2::text, true);
    insert into notification_deliveries (id, organization_id, channel, recipient, template) values ('00000000-0000-7000-8000-000000000404', org1, 'sms', '+254712345678', 'payroll_reminder');
    raise exception 'org two queued a message as org one';
  exception when insufficient_privilege then null;
  end;
end $$;
rollback;
