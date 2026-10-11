-- ONB-08: organizations with companies get their countries' statutory leave types (locked),
-- Unpaid leave, and a Standard plan at the legal minimums for all companies, as each company's
-- default, as new organizations do with their first company. Re-runnable: only what is missing.
insert into leave_types (id, organization_id, code, name, counts, needs_proof, locked_key, country_code)
select gen_random_uuid(), x.organization_id, s.code, s.name_en, s.counts, s.needs_proof, s.key, s.country_code
from (select distinct organization_id, country_code from companies where status = 'active') x
join statutory_leave s on s.country_code = x.country_code and s.active
where not exists (select 1 from leave_types t where t.organization_id = x.organization_id and t.locked_key = s.key and t.country_code = s.country_code);

insert into leave_types (id, organization_id, code, name, paid)
select gen_random_uuid(), o.id, 'UNPAID', 'Unpaid leave', false
from organizations o
where exists (select 1 from companies c where c.organization_id = o.id and c.status = 'active')
  and not exists (select 1 from leave_types t where t.organization_id = o.id and t.code = 'UNPAID');

with made as (
    insert into leave_plans (id, organization_id, name, all_companies)
    select gen_random_uuid(), o.id, 'Standard', true
    from organizations o
    where exists (select 1 from companies c where c.organization_id = o.id and c.status = 'active')
      and not exists (select 1 from leave_plans p where p.organization_id = o.id)
    returning id, organization_id
)
insert into leave_plan_rules (id, organization_id, leave_plan_id, leave_type_id, earn_method, amount, half_pay, waiting_months)
select gen_random_uuid(), m.organization_id, m.id, t.id,
       coalesce(s.earn_method, 'unlimited'),
       case when s.key is null then null else s.earn_amount end,
       coalesce(s.half_pay_days, 0), coalesce(s.waiting_months, 0)
from made m
join leave_types t on t.organization_id = m.organization_id and (t.locked_key is not null or t.code = 'UNPAID')
left join statutory_leave s on s.country_code = t.country_code and s.key = t.locked_key;

update companies c set default_leave_plan_id = (
    select p.id from leave_plans p where p.organization_id = c.organization_id and p.all_companies and p.status = 'active'
    order by p.created_at limit 1)
where c.default_leave_plan_id is null and c.status = 'active';
