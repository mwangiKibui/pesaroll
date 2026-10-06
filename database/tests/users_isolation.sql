-- A person sees their own row and members of their current organization, nobody else.
-- Seed: f1 and f3 are members of org 1; f2 is a member of org 2.
begin;
set local role pesaroll_app;
set local app.user_id = '00000000-0000-7000-8000-0000000000f1';
set local app.org_id = '00000000-0000-7000-8000-000000000001';
do $$
declare
  visible uuid[];
begin
  select array_agg(id order by id) into visible from users;
  if visible <> array['00000000-0000-7000-8000-0000000000f1', '00000000-0000-7000-8000-0000000000f3']::uuid[] then
    raise exception 'users leak or missing: f1 in org 1 sees %', visible;
  end if;
  -- changing someone else's row is refused (no rows match the policy)
  update users set name = 'Hacked' where id = '00000000-0000-7000-8000-0000000000f3';
  if exists (select 1 from users where name = 'Hacked') then
    raise exception 'f1 could change another member''s row';
  end if;
end $$;
rollback;

-- With no signed-in person, nobody is visible.
begin;
set local role pesaroll_app;
do $$
begin
  if exists (select 1 from users) then
    raise exception 'users visible with no signed-in person';
  end if;
end $$;
rollback;
