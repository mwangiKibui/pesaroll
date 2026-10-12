-- Kenya's banks and branches to start with (ONB-03). Re-runnable upserts. Codes from the Kenya
-- Bankers Association's "Bank and Branches" list (July 2023). BO-11 will maintain the full list.

insert into banks (country_code, code, name, legal_name, sort_order) values
  ('KE', '68', 'Equity Bank', 'Equity Bank (Kenya) Limited', 1),
  ('KE', '01', 'KCB Bank',    'Kenya Commercial Bank Limited', 2),
  ('KE', '07', 'NCBA Bank',   'NCBA Bank Kenya PLC', 3),
  ('KE', '03', 'Absa Bank',   'Absa Bank Kenya PLC', 4),
  ('KE', '57', 'I&M Bank',    'I&M Bank Limited', 5)
on conflict (country_code, code) do update set
  name = excluded.name, legal_name = excluded.legal_name, sort_order = excluded.sort_order, active = true;

-- How long each bank's account numbers are (digits only), from the banks' own pages where they say
-- (Absa 10, I&M 14) and published examples and guides otherwise (Equity 13, NCBA 10 to 12); KCB's
-- isn't confirmed, so it stays wide. The back office corrects these (BO-11).
update banks b set account_digits_min = v.min, account_digits_max = v.max
from (values ('68', 13, 13), ('03', 10, 10), ('57', 14, 14), ('07', 10, 12), ('01', 10, 16)) as v(code, min, max)
where b.country_code = 'KE' and b.code = v.code;

insert into bank_branches (country_code, bank_code, code, name, sort_order) values
  ('KE', '68', '047', 'Moi Avenue, Nairobi', 1),
  ('KE', '68', '025', 'Mombasa', 2),
  ('KE', '68', '029', 'Kisumu', 3),
  ('KE', '01', '100', 'Moi Avenue, Nairobi', 1),
  ('KE', '01', '102', 'Treasury Square, Mombasa', 2),
  ('KE', '01', '105', 'Kisumu', 3),
  ('KE', '07', '101', 'City Centre, Nairobi', 1),
  ('KE', '07', '020', 'Moi Avenue, Mombasa', 2),
  ('KE', '07', '112', 'Kisumu', 3),
  ('KE', '03', '075', 'Moi Avenue, Nairobi', 1),
  ('KE', '03', '032', 'Port Mombasa', 2),
  ('KE', '03', '009', 'Kisumu', 3),
  ('KE', '57', '000', 'Kenyatta Avenue, Nairobi', 1),
  ('KE', '57', '005', 'Nyerere Avenue, Mombasa', 2),
  ('KE', '57', '007', 'Kisumu', 3)
on conflict (country_code, bank_code, code) do update set
  name = excluded.name, sort_order = excluded.sort_order, active = true;
