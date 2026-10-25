-- Kenya's wage orders (PAY-11). Re-runnable upserts. The lowest rate (general labourer) per wage
-- area from the Regulation of Wages (General) (Amendment) Order, 2024, Legal Notice No. 164 of 2024,
-- in force from 1 November 2024. Hours: 8 a day, 225 a month (52 hours a week). A new order is a
-- new effective_from; past orders stay, so past runs keep the minimum they were checked against.
insert into wage_orders (country_code, effective_from, source, hours_per_day, hours_per_month) values
  ('KE', date '2024-11-01', 'Regulation of Wages (General) (Amendment) Order, 2024 (L.N. 164 of 2024)', 8, 225)
on conflict (country_code, effective_from) do update set
  source = excluded.source, hours_per_day = excluded.hours_per_day, hours_per_month = excluded.hours_per_month;

insert into minimum_wages (country_code, effective_from, wage_area_code, monthly, daily, hourly) values
  ('KE', date '2024-11-01', 'cities',         16113.75,  775.39, 144.054),
  ('KE', date '2024-11-01', 'municipalities', 14866.92,  712.32, 131.652),
  ('KE', date '2024-11-01', 'other',          8596.494,  435.66,  80.507)
on conflict (country_code, effective_from, wage_area_code) do update set
  monthly = excluded.monthly, daily = excluded.daily, hourly = excluded.hourly;
