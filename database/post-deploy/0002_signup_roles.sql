-- Re-runnable: the sign-up role options. To retire one, set active = false; never delete a
-- code that users already have.
insert into signup_roles (code, name_en, name_sw, description_en, description_sw, sort_order) values
  ('owner',      'Owner or director',            'Mmiliki au mkurugenzi',              'I run the business',            'Ninaendesha biashara',                    1),
  ('hr_payroll', 'HR or payroll',                'Rasilimali watu au mishahara',       'I manage people and pay',       'Ninasimamia wafanyakazi na mishahara',    2),
  ('accountant', 'Accountant or payroll bureau', 'Mhasibu au kampuni ya mishahara',    'I run payroll for clients',     'Ninashughulikia mishahara ya wateja',     3),
  ('other',      'Something else',               'Nyingine',                           'Another role in the business',  'Nafasi nyingine katika biashara',         4)
on conflict (code) do update set
  name_en = excluded.name_en, name_sw = excluded.name_sw,
  description_en = excluded.description_en, description_sw = excluded.description_sw,
  sort_order = excluded.sort_order;
