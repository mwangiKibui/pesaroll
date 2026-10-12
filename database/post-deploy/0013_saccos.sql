-- Kenya's SACCOs and branches to start with (ONB-07). Re-runnable upserts. Branches from the
-- SACCOs' own pages and press (Stima: stima-sacco.com; Mentor: its Murang'a reports, 2026);
-- satellite offices count as branches. The back office (BO-11) adds SACCOs and corrects these.

insert into saccos (country_code, code, name, legal_name, sort_order) values
  ('KE', 'STIMA',  'Stima SACCO',  'Stima DT Sacco Society Limited', 1),
  ('KE', 'MENTOR', 'Mentor SACCO', 'Mentor Sacco Society Limited', 2)
on conflict (country_code, code) do update set
  name = excluded.name, legal_name = excluded.legal_name, sort_order = excluded.sort_order, active = true;

insert into sacco_branches (country_code, sacco_code, code, name, sort_order) values
  ('KE', 'STIMA', '001', 'Head Office, Parklands, Nairobi', 1),
  ('KE', 'STIMA', '002', 'Electricity House, Nairobi CBD', 2),
  ('KE', 'STIMA', '003', 'Kimathi Street, Nairobi CBD', 3),
  ('KE', 'STIMA', '004', 'Kawi Complex, South C, Nairobi', 4),
  ('KE', 'STIMA', '005', 'Mombasa', 5),
  ('KE', 'STIMA', '006', 'Kisumu', 6),
  ('KE', 'STIMA', '007', 'Nakuru', 7),
  ('KE', 'STIMA', '008', 'Eldoret', 8),
  ('KE', 'STIMA', '009', 'Embu', 9),
  ('KE', 'STIMA', '010', 'Meru', 10),
  ('KE', 'STIMA', '011', 'Kisii', 11),
  ('KE', 'STIMA', '012', 'Olkaria', 12),
  ('KE', 'MENTOR', '001', 'Murang''a (Head Office)', 1),
  ('KE', 'MENTOR', '002', 'Kenol', 2),
  ('KE', 'MENTOR', '003', 'Thika', 3),
  ('KE', 'MENTOR', '004', 'Nairobi', 4),
  ('KE', 'MENTOR', '005', 'Ithanga', 5),
  ('KE', 'MENTOR', '006', 'Kandara', 6),
  ('KE', 'MENTOR', '007', 'Kangari', 7),
  ('KE', 'MENTOR', '008', 'Kangema', 8),
  ('KE', 'MENTOR', '009', 'Kiriaini', 9),
  ('KE', 'MENTOR', '010', 'Kahuro', 10)
on conflict (country_code, sacco_code, code) do update set name = excluded.name, sort_order = excluded.sort_order, active = true;
