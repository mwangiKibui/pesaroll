-- ONB-04: after deploy, every company that existed without locations has a "Head office" main
-- location from its own organization, used by that company (post-deploy 0006).
do $$
declare
  bad text;
begin
  select string_agg(c.legal_name, ', ') into bad
  from companies c
  left join locations l on l.id = c.main_location_id
  where c.status = 'active'
    and (l.id is null or l.name <> 'Head office' or l.kind <> 'head_office' or l.organization_id <> c.organization_id
         or not exists (select 1 from location_companies lc where lc.location_id = l.id and lc.company_id = c.id));
  if bad is not null then raise exception 'companies without a proper Head office: %', bad; end if;
end $$;
