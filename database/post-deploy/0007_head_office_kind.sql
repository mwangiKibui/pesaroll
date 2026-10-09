-- ONB-04: Head offices made before locations had a type (on 8 and 9 Oct 2026) become head offices
-- rather than branches. Re-runnable; later locations keep whatever type people chose.
update locations set kind = 'head_office'
where name = 'Head office' and kind = 'branch' and created_at < '2026-10-10';
