-- Sign-up roles come from the list: an unknown code is refused by the database itself.
begin;
do $$
begin
  if (select count(*) from signup_roles where active) <> 4 then
    raise exception 'expected 4 active sign-up roles, found %', (select count(*) from signup_roles where active);
  end if;
  begin
    insert into users (id, email, name, signup_role)
    values ('00000000-0000-7000-8000-0000000000e1', 'typed-role@example.com', 'Typed Role', 'ceo');
    raise exception 'a typed role that is not on the list was accepted';
  exception when foreign_key_violation then
    null; -- expected
  end;
end $$;
rollback;
