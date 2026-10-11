-- ONB-08: after deploy, every organization with companies has its countries' statutory leave
-- types (locked), Unpaid leave, and a Standard plan at the legal minimums as each company's default.
do $$
declare
  bad text;
begin
  select string_agg(c.legal_name, ', ') into bad
  from companies c
  left join leave_plans p on p.id = c.default_leave_plan_id
  where c.status = 'active'
    and (p.id is null or p.organization_id <> c.organization_id
         or exists (select 1 from statutory_leave s where s.country_code = c.country_code and s.active
                    and not exists (select 1 from leave_types t join leave_plan_rules r on r.leave_type_id = t.id and r.leave_plan_id = p.id
                                    where t.organization_id = c.organization_id and t.locked_key = s.key and t.country_code = s.country_code))
         or not exists (select 1 from leave_types t where t.organization_id = c.organization_id and t.code = 'UNPAID' and not t.paid));
  if bad is not null then raise exception 'companies without a proper default leave plan: %', bad; end if;
  if exists (select 1 from leave_plan_rules r join leave_types t on t.id = r.leave_type_id
             where t.locked_key = 'sick' and not (r.earn_method = 'yearly' and r.amount = 7 and r.half_pay = 7 and r.waiting_months = 2)) then
    raise exception 'Standard sick leave should be 7 full + 7 half pay after 2 months';
  end if;
end $$;
