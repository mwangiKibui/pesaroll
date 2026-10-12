-- ONB-07: organizations with companies get Basic pay and their companies' countries' statutory
-- items, locked and for all companies, as new organizations do with their first company.
-- Re-runnable: only what is missing.
insert into pay_items (id, organization_id, code, name, kind, taxable, calc_method, locked_key, all_companies)
select gen_random_uuid(), o.id, 'BASIC', 'Basic pay', 'earning', true, 'fixed', 'basic', true
from organizations o
where exists (select 1 from companies c where c.organization_id = o.id and c.status = 'active')
  and not exists (select 1 from pay_items p where p.organization_id = o.id and p.locked_key = 'basic');

insert into pay_items (id, organization_id, code, name, kind, before_tax, calc_method, locked_key, country_code, all_companies)
select gen_random_uuid(), x.organization_id, s.code, s.name_en, s.kind, s.reduces_taxable_pay, 'by_law', s.key, s.country_code, true
from (select distinct organization_id, country_code from companies where status = 'active') x
join statutory_items s on s.country_code = x.country_code and s.active
where not exists (select 1 from pay_items p where p.organization_id = x.organization_id and p.locked_key = s.key and p.country_code = s.country_code);
