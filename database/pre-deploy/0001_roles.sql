-- The role the API and worker work as. It doesn't own the tables, so row-level security
-- applies to it. The app connects as its database user and runs `set role pesaroll_app`
-- on every connection (TenantSessionInterceptor), so that user must be a member.
do $$
begin
  if not exists (select 1 from pg_roles where rolname = 'pesaroll_app') then
    create role pesaroll_app nologin;
  end if;
end $$;

grant pesaroll_app to current_user;
