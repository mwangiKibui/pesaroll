-- ONB-02: create_organization makes the organization and the person's membership and switches
-- their session, all at once, for the signed-in person only; one onboarded organization each.
begin;
set local role pesaroll_app;
do $$
declare
  amina constant uuid := '00000000-0000-7000-8000-0000000000e1';
  sid constant uuid := '00000000-0000-7000-8000-0000000000e2';
  org uuid;
  msg text;
begin
  -- Sign-up as the app does it: the person's own row and session.
  perform set_config('app.user_id', amina::text, true);
  insert into users (id, email, first_name, last_name) values (amina, 'amina@example.com', 'Amina', 'Otieno');
  insert into sessions (id, user_id, token_hash, expires_at) values (sid, amina, '\x0e', now() + interval '7 days');

  org := create_organization('  Mama Oliech Group  ', sid);

  perform set_config('app.org_id', org::text, true);
  if (select name from organizations where id = org) is distinct from 'Mama Oliech Group' then
    raise exception 'the name was not saved trimmed';
  end if;
  if (select onboarded_by from organizations where id = org) is distinct from amina then
    raise exception 'onboarded_by is not the person who onboarded it';
  end if;
  if not exists (select 1 from memberships where organization_id = org and user_id = amina and status = 'active') then
    raise exception 'no active membership for the person who onboarded it';
  end if;
  if (select organization_id from sessions where id = sid) is distinct from org then
    raise exception 'the session was not switched to the new organization';
  end if;

  begin
    perform create_organization('Second Org', sid);
    raise exception 'a second organization was allowed';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'ONB-3502' then raise exception 'second organization: expected ONB-3502, got %', msg; end if;
  end;

  -- Someone else, not signed in as Amina, can't see her organization.
  perform set_config('app.user_id', '00000000-0000-7000-8000-0000000000f2', true);
  perform set_config('app.org_id', '00000000-0000-7000-8000-000000000002', true);
  if exists (select 1 from organizations where id = org) then
    raise exception 'another organization can see the new one';
  end if;
end $$;
rollback;

-- Names must be 2 to 120 characters after trimming, and nobody can act without signing in.
begin;
set local role pesaroll_app;
do $$
declare
  bea constant uuid := '00000000-0000-7000-8000-0000000000e3';
  sid constant uuid := '00000000-0000-7000-8000-0000000000e4';
  bad text;
  msg text;
begin
  perform set_config('app.user_id', bea::text, true);
  insert into users (id, email, first_name, last_name) values (bea, 'bea@example.com', 'Bea', 'Njeri');
  insert into sessions (id, user_id, token_hash, expires_at) values (sid, bea, '\x0f', now() + interval '7 days');
  foreach bad in array array['', '   ', 'A', repeat('x', 121)] loop
    begin
      perform create_organization(bad, sid);
      raise exception 'name % was allowed', quote_literal(bad);
    exception when others then
      get stacked diagnostics msg = message_text;
      if msg <> 'ONB-3501' then raise exception 'name %: expected ONB-3501, got %', quote_literal(bad), msg; end if;
    end;
  end loop;

  perform set_config('app.user_id', '', true);
  begin
    perform create_organization('Nobody Ltd', sid);
    raise exception 'creating without signing in was allowed';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'AUTH-1007' then raise exception 'signed out: expected AUTH-1007, got %', msg; end if;
  end;
end $$;
rollback;
