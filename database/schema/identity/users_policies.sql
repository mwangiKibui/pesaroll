-- Members of the current organization can see each other (names on approvals, org chart).
create policy users_same_org on users
    for select
    using (exists (
        select 1 from memberships m
        where m.user_id = users.id
          and m.organization_id = app_org_id()
          and m.status = 'active'));
