-- Kenya's statutory items (ONB-07). Re-runnable upserts. SHIF and the Housing Levy have been
-- taken before PAYE since December 2024 (Tax Laws (Amendment) Act 2024), as NSSF always was.
insert into statutory_items (country_code, key, code, name_en, name_sw, kind, reduces_taxable_pay, sort_order) values
  ('KE', 'paye',          'PAYE',    'PAYE',                    'PAYE',                          'deduction',     false, 1),
  ('KE', 'shif',          'SHIF',    'SHIF',                    'SHIF',                          'deduction',     true,  2),
  ('KE', 'nssf_employee', 'NSSF',    'NSSF (employee)',         'NSSF (mfanyakazi)',             'deduction',     true,  3),
  ('KE', 'ahl_employee',  'AHL',     'Housing Levy (employee)', 'Ushuru wa Nyumba (mfanyakazi)', 'deduction',     true,  4),
  ('KE', 'nssf_employer', 'NSSF-ER', 'NSSF (employer)',         'NSSF (mwajiri)',                'employer_cost', false, 5),
  ('KE', 'ahl_employer',  'AHL-ER',  'Housing Levy (employer)', 'Ushuru wa Nyumba (mwajiri)',    'employer_cost', false, 6),
  ('KE', 'nita',          'NITA',    'NITA',                    'NITA',                          'employer_cost', false, 7)
on conflict (country_code, key) do update set
  code = excluded.code, name_en = excluded.name_en, name_sw = excluded.name_sw, kind = excluded.kind,
  reduces_taxable_pay = excluded.reduces_taxable_pay, sort_order = excluded.sort_order, active = true;
