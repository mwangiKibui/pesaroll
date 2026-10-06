-- The role the API and worker use. It is not the table owner, so row-level security applies to it.
do $$
begin
  if not exists (select 1 from pg_roles where rolname = 'pesaroll_app') then
    create role pesaroll_app nologin;
  end if;
end $$;
