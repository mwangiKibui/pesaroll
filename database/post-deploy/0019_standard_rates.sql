-- PAY-11: organizations with companies get the usual rates paid by the hour, for all companies, as
-- new organizations do with their first company (PayItemsEndpoints.StandardRates): overtime at time
-- and a half, rest days and public holidays at double time. Re-runnable: only organizations without
-- an item of that code, archived or not, so one the organization removed stays removed.
insert into pay_items (id, organization_id, code, name, kind, taxable, calc_method, unit, multiplier, all_companies)
select gen_random_uuid(), o.id, r.code, r.name, 'earning', true, 'per_unit', 'hour', r.multiplier, true
from organizations o
cross join (values ('OVERTIME', 'Overtime', 1.5), ('REST-DAY', 'Rest day and public holiday', 2.0)) as r (code, name, multiplier)
where exists (select 1 from companies c where c.organization_id = o.id and c.status = 'active')
  and not exists (select 1 from pay_items p where p.organization_id = o.id and p.code = r.code);
