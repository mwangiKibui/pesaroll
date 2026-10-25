-- Kenya's rule packs (PAY-01): the files in rule-packs/, which the payroll engine's tests also read.
-- Re-runnable: a published pack is never changed. Editing a published file fails the deploy
-- (PAY-2011); a change in the law is a new file with a new version and start date.
\set ke_2025_02 `cat rule-packs/KE-2025.02.json`
\set ke_2026_02 `cat rule-packs/KE-2026.02.json`
select publish_rule_pack('KE', 'KE-2025.02', date '2025-02-01', :'ke_2025_02'::jsonb);   -- NSSF Year 3
select publish_rule_pack('KE', 'KE-2026.02', date '2026-02-01', :'ke_2026_02'::jsonb);   -- NSSF Year 4
