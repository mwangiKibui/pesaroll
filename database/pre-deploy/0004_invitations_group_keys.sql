-- ONB-11 follow-up: an invitation carries one or more groups (HR and Payroll for one person).
-- Adds group_keys and copies each invitation's single group_key into it before the schema drops
-- group_key. On a fresh database the table doesn't exist yet, so this does nothing.
do $$
begin
  if exists (select 1 from information_schema.columns
             where table_schema = current_schema() and table_name = 'invitations' and column_name = 'group_key')
     and not exists (select 1 from information_schema.columns
                     where table_schema = current_schema() and table_name = 'invitations' and column_name = 'group_keys') then
    alter table invitations add column group_keys text[];
    update invitations set group_keys = array[group_key];
  end if;
end $$;
