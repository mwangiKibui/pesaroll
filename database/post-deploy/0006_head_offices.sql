-- ONB-04: companies added before locations existed get a "Head office" main location from their
-- address, as new companies do. Re-runnable: only companies without a main location.
with missing as (
    select c.id as company_id, c.organization_id, c.country_code, c.region_code, c.town, c.address_line,
           gen_random_uuid() as location_id
    from companies c
    where c.main_location_id is null and c.status = 'active'
), made as (
    insert into locations (id, organization_id, code, name, country_code, region_code, town, address_line, wage_area_code)
    select m.location_id, m.organization_id, 'HQ-' || upper(left(md5(m.company_id::text), 6)), 'Head office',
           m.country_code, m.region_code, m.town, m.address_line,
           (select a.code from country_wage_areas a
            where a.country_code = m.country_code and m.town is not null and lower(m.town) = any (select lower(t) from unnest(a.towns) t))
    from missing m
    returning id
), linked as (
    insert into location_companies (organization_id, location_id, company_id)
    select m.organization_id, m.location_id, m.company_id from missing m
)
update companies c set main_location_id = m.location_id
from missing m where c.id = m.company_id;
