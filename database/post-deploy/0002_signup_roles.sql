-- Re-runnable: the sign-up role groups and roles. Ids are fixed so they're the same in
-- every environment. To retire a role, remove it from this list: it is marked inactive
-- below, never deleted, because accounts may already point at it.

insert into signup_role_groups (code, name_en, name_sw, sort_order) values
  ('leadership', 'Leadership',           'Uongozi',                       1),
  ('hr_payroll', 'HR and payroll',       'Rasilimali watu na mishahara',  2),
  ('finance',    'Finance',              'Fedha',                         3),
  ('operations', 'Operations and admin', 'Shughuli na utawala',           4),
  ('other',      'Other',                'Nyingine',                      5)
on conflict (code) do update set
  name_en = excluded.name_en, name_sw = excluded.name_sw, sort_order = excluded.sort_order;

insert into signup_roles (id, code, group_code, name_en, name_sw, description_en, description_sw, sort_order, is_other) values
  ('0199c0de-5a5e-7000-8000-000000000101', 'founder',             'leadership', 'Founder or co-founder',                 'Mwanzilishi au mwanzilishi mwenza',      'I started the business',                   'Nilianzisha biashara',                               1, false),
  ('0199c0de-5a5e-7000-8000-000000000102', 'ceo',                 'leadership', 'CEO or Managing Director',              'Mkurugenzi Mtendaji',                    'I lead the business day to day',           'Ninaongoza biashara kila siku',                      2, false),
  ('0199c0de-5a5e-7000-8000-000000000103', 'general_manager',     'leadership', 'General Manager',                       'Meneja Mkuu',                            'I manage the business''s operations',      'Ninasimamia shughuli za biashara',                   3, false),
  ('0199c0de-5a5e-7000-8000-000000000104', 'director',            'leadership', 'Director',                              'Mkurugenzi',                             'I sit on the board or lead a division',    'Mimi ni mjumbe wa bodi au ninaongoza kitengo',       4, false),
  ('0199c0de-5a5e-7000-8000-000000000201', 'hr_manager',          'hr_payroll', 'HR Manager or Head of People',          'Meneja wa Rasilimali Watu',              'I lead HR and people matters',             'Ninaongoza masuala ya wafanyakazi',                  5, false),
  ('0199c0de-5a5e-7000-8000-000000000202', 'hr_officer',          'hr_payroll', 'HR Officer',                            'Afisa wa Rasilimali Watu',               'I handle day-to-day HR work',              'Ninashughulikia kazi za kila siku za wafanyakazi',   6, false),
  ('0199c0de-5a5e-7000-8000-000000000203', 'payroll_officer',     'hr_payroll', 'Payroll Officer',                       'Afisa wa Mishahara',                     'I prepare and run payroll',                'Ninaandaa na kulipa mishahara',                      7, false),
  ('0199c0de-5a5e-7000-8000-000000000301', 'finance_manager',     'finance',    'Finance Manager or CFO',                'Meneja wa Fedha',                        'I lead finance',                           'Ninaongoza masuala ya fedha',                        8, false),
  ('0199c0de-5a5e-7000-8000-000000000302', 'accountant',          'finance',    'Accountant (in-house)',                 'Mhasibu wa kampuni',                     'I keep the company''s books',              'Ninatunza hesabu za kampuni',                        9, false),
  ('0199c0de-5a5e-7000-8000-000000000303', 'external_accountant', 'finance',    'External accountant or payroll bureau', 'Mhasibu wa nje au kampuni ya mishahara', 'I run payroll for clients',                'Ninashughulikia mishahara ya wateja',               10, false),
  ('0199c0de-5a5e-7000-8000-000000000401', 'operations_manager',  'operations', 'Operations Manager',                    'Meneja wa Shughuli',                     'I run operations',                         'Ninasimamia shughuli',                              11, false),
  ('0199c0de-5a5e-7000-8000-000000000402', 'office_manager',      'operations', 'Office or Administration Manager',      'Meneja wa Ofisi',                        'I run the office and admin',               'Ninasimamia ofisi na utawala',                      12, false),
  ('0199c0de-5a5e-7000-8000-000000000403', 'branch_manager',      'operations', 'Branch or Restaurant Manager',          'Meneja wa Tawi',                         'I manage a branch, outlet or restaurant',  'Ninasimamia tawi, duka au mgahawa',                 13, false),
  ('0199c0de-5a5e-7000-8000-000000000501', 'employee',            'other',      'Employee',                              'Mfanyakazi',                             'I work in the business',                   'Ninafanya kazi katika biashara',                    14, false),
  ('0199c0de-5a5e-7000-8000-000000000502', 'other',               'other',      'Other',                                 'Nyingine',                               'I''ll type my role',                       'Nitaandika nafasi yangu',                           15, true)
on conflict (id) do update set
  code = excluded.code, group_code = excluded.group_code,
  name_en = excluded.name_en, name_sw = excluded.name_sw,
  description_en = excluded.description_en, description_sw = excluded.description_sw,
  sort_order = excluded.sort_order, is_other = excluded.is_other, active = true;

-- Retire anything no longer in the list above.
update signup_roles set active = false
where id not in (
  '0199c0de-5a5e-7000-8000-000000000101', '0199c0de-5a5e-7000-8000-000000000102',
  '0199c0de-5a5e-7000-8000-000000000103', '0199c0de-5a5e-7000-8000-000000000104',
  '0199c0de-5a5e-7000-8000-000000000201', '0199c0de-5a5e-7000-8000-000000000202',
  '0199c0de-5a5e-7000-8000-000000000203', '0199c0de-5a5e-7000-8000-000000000301',
  '0199c0de-5a5e-7000-8000-000000000302', '0199c0de-5a5e-7000-8000-000000000303',
  '0199c0de-5a5e-7000-8000-000000000401', '0199c0de-5a5e-7000-8000-000000000402',
  '0199c0de-5a5e-7000-8000-000000000403', '0199c0de-5a5e-7000-8000-000000000501',
  '0199c0de-5a5e-7000-8000-000000000502');
