-- ONB-02: an organization records who onboarded it, not an owner. Renames the column so its
-- data is kept; pgschema then sees the schema file's onboarded_by already in place.
-- On a fresh database the table doesn't exist yet, so this does nothing.
do $$
begin
  if exists (select 1 from information_schema.columns
             where table_schema = current_schema() and table_name = 'organizations' and column_name = 'created_by') then
    alter table organizations rename column created_by to onboarded_by;
  end if;
end $$;
