-- ONB-06: after deploy, every company that existed before schedules has a default schedule from its
-- own organization that it uses: the suggested "Monthly" for all companies (post-deploy 0008).
do $$
declare
  bad text;
begin
  select string_agg(c.legal_name, ', ') into bad
  from companies c
  left join pay_schedules s on s.id = c.default_pay_schedule_id
  where c.status = 'active'
    and (s.id is null or s.organization_id <> c.organization_id or s.name <> 'Monthly'
         or (s.frequency, s.pay_day_rule, s.cutoff_day) <> ('monthly', 'last_working_day', 25) or not s.all_companies);
  if bad is not null then raise exception 'companies without a proper default schedule: %', bad; end if;
end $$;
