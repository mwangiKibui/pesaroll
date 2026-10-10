-- ONB-06: companies added before schedules existed get the suggested schedule as their default, as
-- new companies do: "Monthly", last working day, cut-off on the 25th, for all companies (one per
-- organization). Re-runnable: only companies without a default schedule.
with orgs as (
    select distinct c.organization_id
    from companies c
    where c.default_pay_schedule_id is null and c.status = 'active'
), made as (
    insert into pay_schedules (id, organization_id, name, all_companies)
    select gen_random_uuid(), o.organization_id,
           -- "Monthly", or "Monthly 2"… if the organization already named one so
           (select n from (select 'Monthly' as n, 1 as k union all select 'Monthly ' || g, g from generate_series(2, 99) g) names
            where not exists (select 1 from pay_schedules s where s.organization_id = o.organization_id and s.status = 'active'
                              and lower(btrim(s.name)) = lower(names.n))
            order by k limit 1),
           true
    from orgs o
    where not exists (select 1 from pay_schedules s where s.organization_id = o.organization_id and s.all_companies and s.status = 'active')
    returning id, organization_id
), chosen as (
    select organization_id, id from made
    union all
    (select distinct on (s.organization_id) s.organization_id, s.id
     from pay_schedules s join orgs o on o.organization_id = s.organization_id
     where s.all_companies and s.status = 'active'
     order by s.organization_id, s.created_at)
)
update companies c set default_pay_schedule_id = ch.id
from chosen ch
where c.organization_id = ch.organization_id and c.default_pay_schedule_id is null and c.status = 'active';
