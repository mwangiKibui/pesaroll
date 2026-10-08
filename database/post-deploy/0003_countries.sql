-- Countries, their company fields, regions and industries (ONB-03). Re-runnable upserts.
-- Kenya is available. Uganda, Tanzania and Rwanda are prepared but switched off: their fields and
-- formats must be reviewed by the payroll accountant before rollout (BO-11 will edit all of this).

insert into countries (code, name_en, name_sw, currency, phone_code, region_label_en, region_label_sw, available, sort_order) values
  ('KE', 'Kenya',    'Kenya',    'KES', '+254', 'County',   'Kaunti',  true,  1),
  ('UG', 'Uganda',   'Uganda',   'UGX', '+256', 'District', 'Wilaya',  false, 2),
  ('TZ', 'Tanzania', 'Tanzania', 'TZS', '+255', 'Region',   'Mkoa',    false, 3),
  ('RW', 'Rwanda',   'Rwanda',   'RWF', '+250', 'District', 'Wilaya',  false, 4)
on conflict (code) do update set
  name_en = excluded.name_en, name_sw = excluded.name_sw, currency = excluded.currency, phone_code = excluded.phone_code,
  region_label_en = excluded.region_label_en, region_label_sw = excluded.region_label_sw,
  available = excluded.available, sort_order = excluded.sort_order;

insert into country_company_fields (country_code, key, label_en, label_sw, hint_en, hint_sw, example, pattern, required_for_payroll, sort_order) values
  ('KE', 'kra_pin', 'KRA PIN', 'PIN ya KRA',
   'The company''s PIN from KRA iTax. Used for PAYE, Housing Levy and the P10 return.',
   'PIN ya kampuni kutoka KRA iTax. Inatumika kwa PAYE, Ushuru wa Nyumba na fomu ya P10.',
   'P051234567X', '^[AP][0-9]{9}[A-Z]$', true, 1),
  ('KE', 'nssf_employer_no', 'NSSF employer number', 'Nambari ya mwajiri ya NSSF',
   'From your NSSF registration. Used for the monthly NSSF return.',
   'Kutoka usajili wako wa NSSF. Inatumika kwa fomu ya kila mwezi ya NSSF.',
   null, '^[A-Z0-9/-]{4,20}$', true, 2),
  ('KE', 'sha_employer_no', 'SHA employer number', 'Nambari ya mwajiri ya SHA',
   'From the Social Health Authority. Used for SHIF contributions.',
   'Kutoka Mamlaka ya Afya ya Jamii. Inatumika kwa michango ya SHIF.',
   null, '^[A-Z0-9/-]{4,20}$', true, 3),
  ('KE', 'business_registration_no', 'Business registration number', 'Nambari ya usajili wa biashara',
   'From your Business Registration Service (BRS) certificate.',
   'Kutoka cheti chako cha Huduma ya Usajili wa Biashara (BRS).',
   null, '^[A-Z0-9][A-Z0-9./ -]{3,24}$', false, 4),
  ('KE', 'nita_registration_no', 'NITA registration number', 'Nambari ya usajili ya NITA',
   'For the NITA training levy.',
   'Kwa ushuru wa mafunzo wa NITA.',
   null, '^[A-Z0-9/-]{3,20}$', false, 5),

  ('UG', 'tin', 'URA TIN', 'TIN ya URA', 'From the Uganda Revenue Authority. Used for PAYE.', 'Kutoka Mamlaka ya Mapato ya Uganda. Inatumika kwa PAYE.', '1000123456', '^[0-9]{10}$', true, 1),
  ('UG', 'nssf_employer_no', 'NSSF employer number', 'Nambari ya mwajiri ya NSSF', 'From NSSF Uganda.', 'Kutoka NSSF Uganda.', null, '^[A-Z0-9/-]{4,20}$', true, 2),
  ('UG', 'business_registration_no', 'URSB registration number', 'Nambari ya usajili ya URSB', 'From the Uganda Registration Services Bureau.', 'Kutoka Ofisi ya Huduma za Usajili Uganda.', null, '^[A-Z0-9][A-Z0-9./ -]{3,24}$', false, 3),

  ('TZ', 'tin', 'TRA TIN', 'TIN ya TRA', 'From the Tanzania Revenue Authority. Used for PAYE and SDL.', 'Kutoka Mamlaka ya Mapato Tanzania. Inatumika kwa PAYE na SDL.', '123-456-789', '^[0-9]{3}-?[0-9]{3}-?[0-9]{3}$', true, 1),
  ('TZ', 'nssf_employer_no', 'NSSF employer number', 'Nambari ya mwajiri ya NSSF', 'From NSSF Tanzania (PSSSF for the public sector).', 'Kutoka NSSF Tanzania (PSSSF kwa sekta ya umma).', null, '^[A-Z0-9/-]{4,20}$', true, 2),
  ('TZ', 'wcf_registration_no', 'WCF registration number', 'Nambari ya usajili ya WCF', 'From the Workers Compensation Fund.', 'Kutoka Mfuko wa Fidia kwa Wafanyakazi.', null, '^[A-Z0-9/-]{3,20}$', true, 3),
  ('TZ', 'business_registration_no', 'BRELA registration number', 'Nambari ya usajili ya BRELA', 'From the Business Registrations and Licensing Agency.', 'Kutoka Wakala wa Usajili wa Biashara na Leseni.', null, '^[A-Z0-9][A-Z0-9./ -]{3,24}$', false, 4),

  ('RW', 'tin', 'RRA TIN', 'TIN ya RRA', 'From the Rwanda Revenue Authority. Used for PAYE.', 'Kutoka Mamlaka ya Mapato Rwanda. Inatumika kwa PAYE.', '123456789', '^[0-9]{9}$', true, 1),
  ('RW', 'rssb_employer_no', 'RSSB employer number', 'Nambari ya mwajiri ya RSSB', 'From the Rwanda Social Security Board.', 'Kutoka Bodi ya Hifadhi ya Jamii Rwanda.', null, '^[A-Z0-9/-]{4,20}$', true, 2),
  ('RW', 'business_registration_no', 'RDB registration number', 'Nambari ya usajili ya RDB', 'From the Rwanda Development Board.', 'Kutoka Bodi ya Maendeleo ya Rwanda.', null, '^[A-Z0-9][A-Z0-9./ -]{3,24}$', false, 3)
on conflict (country_code, key) do update set
  label_en = excluded.label_en, label_sw = excluded.label_sw, hint_en = excluded.hint_en, hint_sw = excluded.hint_sw,
  example = excluded.example, pattern = excluded.pattern, required_for_payroll = excluded.required_for_payroll,
  sort_order = excluded.sort_order, active = true;

-- Kenya's 47 counties with their official codes.
insert into country_regions (country_code, code, name, sort_order)
select 'KE', lpad(n::text, 3, '0'), name, n
from unnest(array['Mombasa', 'Kwale', 'Kilifi', 'Tana River', 'Lamu', 'Taita-Taveta', 'Garissa', 'Wajir', 'Mandera',
  'Marsabit', 'Isiolo', 'Meru', 'Tharaka-Nithi', 'Embu', 'Kitui', 'Machakos', 'Makueni', 'Nyandarua', 'Nyeri',
  'Kirinyaga', 'Murang''a', 'Kiambu', 'Turkana', 'West Pokot', 'Samburu', 'Trans Nzoia', 'Uasin Gishu',
  'Elgeyo-Marakwet', 'Nandi', 'Baringo', 'Laikipia', 'Nakuru', 'Narok', 'Kajiado', 'Kericho', 'Bomet', 'Kakamega',
  'Vihiga', 'Bungoma', 'Busia', 'Siaya', 'Kisumu', 'Homa Bay', 'Migori', 'Kisii', 'Nyamira', 'Nairobi'])
  with ordinality as c(name, n)
on conflict (country_code, code) do update set name = excluded.name, sort_order = excluded.sort_order;

insert into industries (code, name_en, name_sw, sort_order) values
  ('restaurants_bars', 'Restaurants, bars and clubs', 'Mikahawa, baa na vilabu', 1),
  ('hotels', 'Hotels and hospitality', 'Hoteli na ukarimu', 2),
  ('retail', 'Shops and retail', 'Maduka na rejareja', 3),
  ('schools', 'Schools and education', 'Shule na elimu', 4),
  ('healthcare', 'Clinics and healthcare', 'Kliniki na afya', 5),
  ('security', 'Security services', 'Huduma za ulinzi', 6),
  ('construction', 'Construction', 'Ujenzi', 7),
  ('manufacturing', 'Manufacturing', 'Viwanda', 8),
  ('agriculture', 'Farming and agriculture', 'Kilimo', 9),
  ('transport', 'Transport and logistics', 'Usafiri na usafirishaji', 10),
  ('professional', 'Professional services', 'Huduma za kitaaluma', 11),
  ('ngo', 'NGOs and non-profits', 'Mashirika yasiyo ya kiserikali', 12),
  ('technology', 'Technology', 'Teknolojia', 13),
  ('finance', 'Finance and insurance', 'Fedha na bima', 14),
  ('other', 'Other', 'Nyingine', 99)
on conflict (code) do update set name_en = excluded.name_en, name_sw = excluded.name_sw, sort_order = excluded.sort_order, active = true;
