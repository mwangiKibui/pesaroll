-- Refresh tokens: each use swaps in a new token; using a replaced token again ends the
-- session (someone else may hold a copy); sign-out by token ends it too.
begin;
set local role pesaroll_app;
do $$
declare
  owner1 constant uuid := '00000000-0000-7000-8000-0000000000f1';
  sid constant uuid := '00000000-0000-7000-8000-0000000000d1';
  sid2 constant uuid := '00000000-0000-7000-8000-0000000000d2';
  row record;
begin
  perform set_config('app.user_id', owner1::text, true);
  insert into sessions (id, user_id, token_hash, expires_at)
  values (sid, owner1, '\x01', now() + interval '7 days'),
         (sid2, owner1, '\x0a', now() + interval '7 days');
  perform set_config('app.user_id', '', true);

  select * into row from rotate_session_token('\x01', '\x02');
  if row.session_id is distinct from sid or row.user_id is distinct from owner1 then
    raise exception 'a live refresh token was not rotated (got %)', row;
  end if;
  if row.expires_at < now() + interval '6 days' then
    raise exception 'rotation must keep the session''s 7-day end (got %)', row.expires_at;
  end if;

  select * into row from rotate_session_token('\x02', '\x03');
  if row.session_id is distinct from sid then raise exception 'the new token did not rotate'; end if;

  -- \x02 was replaced by \x03: using it again is reuse.
  select * into row from rotate_session_token('\x02', '\x04');
  if row.session_id is not null then raise exception 'a replaced token was accepted'; end if;
  select * into row from rotate_session_token('\x03', '\x05');
  if row.session_id is not null then raise exception 'reuse did not end the session'; end if;

  if exists (select 1 from rotate_session_token('\xff', '\x06')) then
    raise exception 'an unknown token was accepted';
  end if;

  perform end_session_by_token('\x0a');
  if exists (select 1 from rotate_session_token('\x0a', '\x0b')) then
    raise exception 'a signed-out session could still refresh';
  end if;
end $$;
rollback;
