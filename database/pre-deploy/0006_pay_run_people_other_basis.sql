-- PAY-10: people left out of a run for pay on another basis are other_basis, not not_monthly. Drops
-- the old check so the rows can be renamed before the schema adds the new one. On a fresh database
-- the table doesn't exist yet, so this does nothing.
do $$
begin
  if exists (select 1 from information_schema.tables where table_schema = current_schema() and table_name = 'pay_run_people') then
    alter table pay_run_people drop constraint if exists pay_run_people_excluded_reason_check;
    update pay_run_people set excluded_reason = 'other_basis' where excluded_reason = 'not_monthly';
  end if;
end $$;
