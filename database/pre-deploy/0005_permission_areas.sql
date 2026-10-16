-- ACC-04: permissions come in areas, each with View and Manage. Creates the areas and fills each
-- existing permission's area and level before the schema makes them required (post-deploy then adds
-- the View permissions). On a fresh database the permissions table doesn't exist yet, so this does nothing.
do $$
begin
  if exists (select 1 from information_schema.tables where table_schema = current_schema() and table_name = 'permissions')
     and not exists (select 1 from information_schema.columns
                     where table_schema = current_schema() and table_name = 'permissions' and column_name = 'area') then
    create table if not exists permission_areas (
        key         text primary key check (key ~ '^[a-z_]+$'),
        name_en     text not null,
        name_sw     text not null,
        sort_order  integer not null
    );
    insert into permission_areas (key, name_en, name_sw, sort_order) values
      ('companies', 'Companies', 'Kampuni', 1), ('employees', 'Employees', 'Wafanyakazi', 2), ('locations', 'Locations', 'Maeneo', 3),
      ('structure', 'Org structure', 'Muundo wa shirika', 4), ('leave', 'Leave types and plans', 'Aina na mipango ya likizo', 5),
      ('schedules', 'Payroll schedules', 'Ratiba za mishahara', 6), ('pay_items', 'Earnings, deductions and payees', 'Mapato, makato na wapokeaji', 7),
      ('payment_accounts', 'Paying accounts', 'Akaunti za malipo', 8), ('payroll_history', 'Payroll history', 'Historia ya mishahara', 9),
      ('team', 'Team access', 'Ufikiaji wa timu', 10)
    on conflict (key) do nothing;

    alter table permissions add column area text, add column level text;
    update permissions set area = split_part(key, '.', 1), level = split_part(key, '.', 2);
    delete from permissions where area not in (select key from permission_areas) or level not in ('view', 'manage');
    alter table permissions alter column area set not null, alter column level set not null,
      add constraint permissions_area_fkey foreign key (area) references permission_areas (key) on delete cascade,
      add constraint permissions_level_check check (level in ('view', 'manage')),
      add constraint permissions_check check (key = area || '.' || level);
  end if;
end $$;
