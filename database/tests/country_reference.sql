-- ONB-03: countries are data. Kenya is available with its company fields and 47 counties; other
-- countries are prepared but switched off; every field's example passes its own format check.
begin;
set local role pesaroll_app;
do $$
declare
  bad text;
begin
  if not (select available from countries where code = 'KE') then raise exception 'Kenya is not available'; end if;
  if exists (select 1 from countries where available and code <> 'KE') then raise exception 'only Kenya should be available for now'; end if;
  if (select currency from countries where code = 'KE') <> 'KES' then raise exception 'Kenya currency is not KES'; end if;
  if (select count(*) from country_regions where country_code = 'KE') <> 47 then raise exception 'Kenya should have 47 counties'; end if;
  if (select name from country_regions where country_code = 'KE' and code = '047') <> 'Nairobi' then raise exception 'county 047 is not Nairobi'; end if;

  if (select array_agg(key order by key) from country_company_fields where country_code = 'KE' and required_for_payroll)
     <> array['kra_pin', 'nssf_employer_no', 'sha_employer_no'] then
    raise exception 'Kenya payroll needs KRA PIN, NSSF and SHA numbers';
  end if;

  select string_agg(country_code || '.' || key, ', ') into bad
  from country_company_fields
  where example is not null and pattern is not null and upper(example) !~ pattern;
  if bad is not null then raise exception 'examples that fail their own format: %', bad; end if;

  if 'P051234567X' !~ (select pattern from country_company_fields where country_code = 'KE' and key = 'kra_pin') then
    raise exception 'a valid KRA PIN is refused';
  end if;
  if 'P05123456X' ~ (select pattern from country_company_fields where country_code = 'KE' and key = 'kra_pin') then
    raise exception 'a KRA PIN with 8 digits is accepted';
  end if;

  if (select count(*) from industries where active) < 10 then raise exception 'industries list is missing'; end if;

  -- ONB-04: Kenya's three minimum-wage areas; the cities suggest themselves from the town name.
  if (select array_agg(code order by sort_order) from country_wage_areas where country_code = 'KE' and active)
     <> array['cities', 'municipalities', 'other'] then
    raise exception 'Kenya should have the cities, municipalities and other-areas wage areas';
  end if;
  if not (select 'Eldoret' = any (towns) from country_wage_areas where country_code = 'KE' and code = 'cities') then
    raise exception 'Eldoret is a city for minimum wages (2026 order)';
  end if;
end $$;
rollback;
