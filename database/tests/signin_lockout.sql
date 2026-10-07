-- Five wrong passwords in a row lock the account for 15 minutes; a right one resets the count.
begin;
set local role pesaroll_app;
do $$
declare
  owner1 constant uuid := '00000000-0000-7000-8000-0000000000f1';
  locked timestamptz;
begin
  if (select count(*) from find_account_for_signin('owner1@example.com')) <> 1 then
    raise exception 'find_account_for_signin did not find owner1';
  end if;
  for attempt in 1..4 loop
    locked := record_signin_result(owner1, false);
    if locked is not null then raise exception 'locked after only % wrong attempts', attempt; end if;
  end loop;
  locked := record_signin_result(owner1, false);
  if locked is null or locked < now() + interval '14 minutes' then
    raise exception 'not locked for 15 minutes after 5 wrong attempts (locked_until %)', locked;
  end if;
  locked := record_signin_result(owner1, true);
  if locked is not null then raise exception 'a right password did not clear the lock'; end if;
end $$;
rollback;
