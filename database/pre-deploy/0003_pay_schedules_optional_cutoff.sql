-- ONB-06: the cut-off becomes optional. Adds its two columns before the schema is applied, so
-- daily schedules (which have no cut-off) can be set to match before the new checks arrive.
-- On a fresh database the table doesn't exist yet, so this does nothing.
do $$
begin
  if exists (select 1 from information_schema.tables where table_schema = current_schema() and table_name = 'pay_schedules')
     and not exists (select 1 from information_schema.columns
                     where table_schema = current_schema() and table_name = 'pay_schedules' and column_name = 'uses_cutoff') then
    alter table pay_schedules add column uses_cutoff boolean not null default true;
    alter table pay_schedules add column cutoff_applies_to text[] not null
      default '{overtime,new_employees,leavers,unpaid_leave,pay_changes,one_off}';
    update pay_schedules set uses_cutoff = false, cutoff_applies_to = '{}' where frequency = 'daily';
  end if;
end $$;
