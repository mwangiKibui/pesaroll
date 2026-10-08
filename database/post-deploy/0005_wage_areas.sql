-- Kenya's minimum-wage areas (ONB-04). Re-runnable upserts. From the Regulation of Wages (General)
-- (Amendment) Orders (2024; 2026 adds Nakuru and Eldoret as cities). Reviewed by the payroll
-- accountant (BIZ-08); BO-11 will edit them.
insert into country_wage_areas (country_code, code, name_en, name_sw, description_en, description_sw, towns, sort_order) values
  ('KE', 'cities', 'Cities', 'Majiji',
   'Nairobi, Mombasa, Kisumu, Nakuru and Eldoret cities.',
   'Majiji ya Nairobi, Mombasa, Kisumu, Nakuru na Eldoret.',
   array['Nairobi', 'Mombasa', 'Kisumu', 'Nakuru', 'Eldoret'], 1),
  ('KE', 'municipalities', 'Former municipalities and town councils', 'Manispaa za zamani na mabaraza ya miji',
   'Former municipalities and the town councils of Mavoko, Ruiru and Limuru.',
   'Manispaa za zamani na mabaraza ya miji ya Mavoko, Ruiru na Limuru.',
   array['Mavoko', 'Ruiru', 'Limuru'], 2),
  ('KE', 'other', 'All other areas', 'Maeneo mengine yote',
   'Anywhere that is neither a city, a former municipality nor one of those town councils.',
   'Popote ambapo si jiji, manispaa ya zamani wala mojawapo ya mabaraza hayo ya miji.',
   '{}', 3)
on conflict (country_code, code) do update set
  name_en = excluded.name_en, name_sw = excluded.name_sw, description_en = excluded.description_en,
  description_sw = excluded.description_sw, towns = excluded.towns, sort_order = excluded.sort_order, active = true;
