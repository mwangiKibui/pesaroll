-- Sign-up roles come from the list by id; "Other" needs a typed role and only "Other" allows one.
begin;
do $$
declare
  ceo   constant uuid := '0199c0de-5a5e-7000-8000-000000000102';
  other constant uuid := '0199c0de-5a5e-7000-8000-000000000502';
begin
  if (select count(*) from signup_roles where active) <> 15 then
    raise exception 'expected 15 active sign-up roles, found %', (select count(*) from signup_roles where active);
  end if;

  -- a role that is on the list
  insert into users (id, email, name, signup_role_id) values (gen_random_uuid(), 'ceo@example.com', 'CEO', ceo);

  -- "Other" with a typed role
  insert into users (id, email, name, signup_role_id, signup_role_other)
  values (gen_random_uuid(), 'other@example.com', 'Other', other, 'Head Chef');

  -- an id that isn't on the list
  begin
    insert into users (id, email, name, signup_role_id) values (gen_random_uuid(), 'x1@example.com', 'X', gen_random_uuid());
    raise exception 'an unknown role id was accepted';
  exception when foreign_key_violation then null;
  end;

  -- "Other" without a typed role
  begin
    insert into users (id, email, name, signup_role_id) values (gen_random_uuid(), 'x2@example.com', 'X', other);
    raise exception '"Other" without a typed role was accepted';
  exception when check_violation then null;
  end;

  -- a typed role with a listed role
  begin
    insert into users (id, email, name, signup_role_id, signup_role_other)
    values (gen_random_uuid(), 'x3@example.com', 'X', ceo, 'Boss');
    raise exception 'a typed role was accepted with a listed role';
  exception when check_violation then null;
  end;
end $$;
rollback;
