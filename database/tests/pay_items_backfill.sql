-- ONB-07: after deploy, every organization with companies has Basic pay and its companies'
-- countries' statutory items, locked and for all companies (post-deploy 0010).
do $$
declare
  bad text;
begin
  select string_agg(distinct o.name, ', ') into bad
  from organizations o
  join companies c on c.organization_id = o.id and c.status = 'active'
  where not exists (select 1 from pay_items p where p.organization_id = o.id and p.locked_key = 'basic' and p.all_companies)
     or exists (select 1 from statutory_items s where s.country_code = c.country_code and s.active
                and not exists (select 1 from pay_items p where p.organization_id = o.id and p.locked_key = s.key
                                and p.country_code = s.country_code and p.all_companies));
  if bad is not null then raise exception 'organizations without their locked pay items: %', bad; end if;
end $$;

-- PAY-11: and the usual rates paid by the hour, overtime and rest days (post-deploy 0019).
do $$
declare
  bad text;
begin
  select string_agg(distinct o.name, ', ') into bad
  from organizations o
  join companies c on c.organization_id = o.id and c.status = 'active'
  cross join (values ('OVERTIME'), ('REST-DAY')) r (code)
  where not exists (select 1 from pay_items p where p.organization_id = o.id and p.code = r.code);
  if bad is not null then raise exception 'organizations without the usual hourly rates: %', bad; end if;
end $$;
