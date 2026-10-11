-- Kenya's statutory leave (ONB-08), Employment Act 2007 and the Employment (Amendment) Act 2021.
-- Re-runnable upserts. Sick leave and pre-adoptive leave to confirm with an HR or legal adviser.
insert into statutory_leave (country_code, key, code, name_en, name_sw, full_pay_days, half_pay_days, counts, earn_method, earn_amount, waiting_months, needs_proof, sort_order) values
  ('KE', 'annual',       'ANNUAL',    'Annual leave',       'Likizo ya mwaka',          21, 0, 'working_days',  'monthly',   1.75, 0, false, 1),
  ('KE', 'sick',         'SICK',      'Sick leave',         'Likizo ya ugonjwa',         7, 7, 'working_days',  'yearly',    7,    2, true,  2),
  ('KE', 'maternity',    'MATERNITY', 'Maternity leave',    'Likizo ya uzazi',          90, 0, 'calendar_days', 'each_time', 90,   0, true,  3),
  ('KE', 'paternity',    'PATERNITY', 'Paternity leave',    'Likizo ya baba',           14, 0, 'calendar_days', 'each_time', 14,   0, false, 4),
  ('KE', 'pre_adoptive', 'ADOPTION',  'Pre-adoptive leave', 'Likizo ya kuasili',        30, 0, 'calendar_days', 'each_time', 30,   0, true,  5)
on conflict (country_code, key) do update set
  code = excluded.code, name_en = excluded.name_en, name_sw = excluded.name_sw, full_pay_days = excluded.full_pay_days,
  half_pay_days = excluded.half_pay_days, counts = excluded.counts, earn_method = excluded.earn_method, earn_amount = excluded.earn_amount,
  waiting_months = excluded.waiting_months, needs_proof = excluded.needs_proof, sort_order = excluded.sort_order, active = true;
