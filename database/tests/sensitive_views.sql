-- ACC-05: every view of pay is recorded (who, whose, when; never amounts). The app may add and read
-- its organization's records, never change or delete them, and never see another organization's.
begin;
do $$
declare
  amina constant uuid := '00000000-0000-7000-8000-0000000c6101';
  sid   constant uuid := '00000000-0000-7000-8000-0000000c6102';
  otis  constant uuid := '00000000-0000-7000-8000-0000000c6103';
  sid2  constant uuid := '00000000-0000-7000-8000-0000000c6104';
  someone constant uuid := '00000000-0000-7000-8000-0000000c61e1';
  org uuid;
  other uuid;
  msg text;
begin
  insert into users (id, email, first_name, last_name) values (amina, 'amina.views@example.com', 'Amina', 'Otieno'), (otis, 'otis.views@example.com', 'Otis', 'Ouma');
  insert into sessions (id, user_id, token_hash, expires_at) values (sid, amina, '\x0c61', now() + interval '7 days'), (sid2, otis, '\x0c62', now() + interval '7 days');
  perform set_config('app.user_id', otis::text, true);
  set local role pesaroll_app;
  other := create_organization('Other Views Group', sid2);
  reset role;
  perform set_config('app.user_id', amina::text, true);
  set local role pesaroll_app;
  org := create_organization('Views Test Group', sid);
  reset role;

  perform set_config('app.org_id', org::text, true);
  set local role pesaroll_app;
  insert into sensitive_views (id, organization_id, user_id, kind, subject_ids, reference) values (uuidv7(), org, amina, 'employee_pay', array[someone], 'ABCD1234');
  if (select count(*) from sensitive_views) <> 1 then raise exception 'the view was not recorded'; end if;
  reset role;

  -- Never changed or deleted by the app.
  begin
    set local role pesaroll_app;
    update sensitive_views set subject_ids = '{}';
    reset role;
    raise exception 'the app changed a view record';
  exception when insufficient_privilege then reset role;
  end;
  begin
    set local role pesaroll_app;
    delete from sensitive_views;
    reset role;
    raise exception 'the app deleted a view record';
  exception when insufficient_privilege then reset role;
  end;

  -- Another organization neither sees them nor adds to them.
  perform set_config('app.org_id', other::text, true);
  set local role pesaroll_app;
  if exists (select 1 from sensitive_views) then raise exception 'another organization sees the views'; end if;
  begin
    insert into sensitive_views (id, organization_id, user_id, kind, subject_ids) values (uuidv7(), org, otis, 'employee_pay', array[someone]);
    raise exception 'another organization added a view record';
  exception when insufficient_privilege then null;
  end;
  reset role;

  -- Only known kinds.
  perform set_config('app.org_id', org::text, true);
  begin
    set local role pesaroll_app;
    insert into sensitive_views (id, organization_id, user_id, kind, subject_ids) values (uuidv7(), org, amina, 'gossip', array[someone]);
    reset role;
    raise exception 'an unknown kind was recorded';
  exception when check_violation then reset role;
  end;
end
$$;
rollback;
