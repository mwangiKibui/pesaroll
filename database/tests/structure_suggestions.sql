-- ONB-05: every industry has a starting structure; every suggested position names a department
-- suggested for the same industry.
begin;
set local role pesaroll_app;
do $$
declare
  bad text;
begin
  select string_agg(i.code, ', ') into bad from industries i
  where i.active and not exists (select 1 from industry_structures s where s.industry_code = i.code and s.kind = 'department');
  if bad is not null then raise exception 'industries without suggested departments: %', bad; end if;

  select string_agg(p.industry_code || '/' || p.name, ', ') into bad from industry_structures p
  where p.kind = 'position' and not exists (
    select 1 from industry_structures d where d.industry_code = p.industry_code and d.kind = 'department' and d.name = p.department_name);
  if bad is not null then raise exception 'positions in departments that are not suggested: %', bad; end if;

  if not exists (select 1 from industry_structures where industry_code = 'restaurants_bars' and kind = 'position' and name = 'Waiter' and department_name = 'Service') then
    raise exception 'restaurants should suggest a waiter in Service';
  end if;
end $$;
rollback;
