-- Re-runnable: the permissions the app checks (ACC-01) and the system roles that bundle them.
-- Keys match Pesaroll.Shared.Access.Permissions in the API. Role ids are fixed, the same in every
-- environment. A permission or grant taken off these lists is removed.

-- ACC-04: areas, each with View and Manage. Removing an area removes its permissions and grants.
insert into permission_areas (key, name_en, name_sw, sort_order) values
  ('companies',        'Companies',                        'Kampuni',                 1),
  ('employees',        'Employees',                        'Wafanyakazi',             2),
  ('locations',        'Locations',                        'Maeneo',                  3),
  ('structure',        'Org structure',                    'Muundo wa shirika',       4),
  ('leave',            'Leave types and plans',            'Aina na mipango ya likizo', 5),
  ('schedules',        'Payroll schedules',                'Ratiba za mishahara',     6),
  ('pay_items',        'Earnings, deductions and payees',  'Mapato, makato na wapokeaji', 7),
  ('payment_accounts', 'Paying accounts',                  'Akaunti za malipo',       8),
  ('payroll_history',  'Payroll history',                  'Historia ya mishahara',   9),
  ('team',             'Team access',                      'Ufikiaji wa timu',        10)
on conflict (key) do update set name_en = excluded.name_en, name_sw = excluded.name_sw, sort_order = excluded.sort_order;

delete from permission_areas where key not in ('companies', 'employees', 'locations', 'structure', 'leave', 'schedules', 'pay_items', 'payment_accounts', 'payroll_history', 'team');

insert into permissions (key, description, area, level) values
  ('companies.view',          'See companies'' registration and statutory details. Every member sees the company names they work with.', 'companies', 'view'),
  ('companies.manage',        'Add, change and delete companies.', 'companies', 'manage'),
  ('employees.view',          'See employees and their details.', 'employees', 'view'),
  ('employees.manage',        'Add, change, delete and upload employees.', 'employees', 'manage'),
  ('locations.view',          'See locations.', 'locations', 'view'),
  ('locations.manage',        'Add, change and delete locations.', 'locations', 'manage'),
  ('structure.view',          'See divisions, departments, units and positions.', 'structure', 'view'),
  ('structure.manage',        'Add, change and delete divisions, departments, units and positions.', 'structure', 'manage'),
  ('leave.view',              'See leave types and leave plans.', 'leave', 'view'),
  ('leave.manage',            'Add, change and delete leave types and leave plans.', 'leave', 'manage'),
  ('schedules.view',          'See payroll schedules.', 'schedules', 'view'),
  ('schedules.manage',        'Add, change and delete payroll schedules.', 'schedules', 'manage'),
  ('pay_items.view',          'See earnings, deductions and payees, and who gets them.', 'pay_items', 'view'),
  ('pay_items.manage',        'Add, change and delete earnings, deductions and payees, and who gets them.', 'pay_items', 'manage'),
  ('payment_accounts.view',   'See the accounts a company pays from.', 'payment_accounts', 'view'),
  ('payment_accounts.manage', 'Add and change the accounts a company pays from.', 'payment_accounts', 'manage'),
  ('payroll_history.view',    'See payroll history.', 'payroll_history', 'view'),
  ('payroll_history.manage',  'Upload and delete payroll history.', 'payroll_history', 'manage'),
  ('team.view',               'See who uses Pesaroll and what each group can do.', 'team', 'view'),
  ('team.manage',             'Invite people, change their groups and companies, and change what groups can do.', 'team', 'manage')
on conflict (key) do update set description = excluded.description, area = excluded.area, level = excluded.level;

delete from permissions p where not exists (select 1 from (values
  ('companies.view'), ('companies.manage'), ('employees.view'), ('employees.manage'), ('locations.view'), ('locations.manage'),
  ('structure.view'), ('structure.manage'), ('leave.view'), ('leave.manage'), ('schedules.view'), ('schedules.manage'),
  ('pay_items.view'), ('pay_items.manage'), ('payment_accounts.view'), ('payment_accounts.manage'),
  ('payroll_history.view'), ('payroll_history.manage'), ('team.view'), ('team.manage')) k (key) where k.key = p.key);

insert into roles (id, organization_id, key, name_en, name_sw) values
  ('0199c0de-acc0-7000-8000-000000000001', null, 'admin',    'Administrator', 'Msimamizi'),
  ('0199c0de-acc0-7000-8000-000000000002', null, 'hr',       'HR',            'Rasilimali watu'),
  ('0199c0de-acc0-7000-8000-000000000003', null, 'payroll',  'Payroll',       'Mishahara'),
  ('0199c0de-acc0-7000-8000-000000000004', null, 'approver', 'Approver',      'Mwidhinishaji')
on conflict (id) do update set key = excluded.key, name_en = excluded.name_en, name_sw = excluded.name_sw;

create temporary table wanted (role_key text, permission_key text) on commit drop;
insert into wanted values
  ('admin', 'companies.manage'), ('admin', 'payment_accounts.manage'), ('admin', 'locations.manage'), ('admin', 'structure.manage'),
  ('admin', 'leave.manage'), ('admin', 'schedules.manage'), ('admin', 'pay_items.manage'), ('admin', 'employees.manage'),
  ('admin', 'payroll_history.manage'), ('admin', 'team.manage'),
  ('hr', 'locations.manage'), ('hr', 'structure.manage'), ('hr', 'leave.manage'), ('hr', 'employees.manage'),
  -- The employee form picks a payroll schedule (HR), and a department, position, location and leave plan (Payroll).
  ('hr', 'schedules.view'),
  ('payroll', 'payment_accounts.manage'), ('payroll', 'schedules.manage'), ('payroll', 'pay_items.manage'), ('payroll', 'employees.manage'),
  ('payroll', 'payroll_history.manage'), ('payroll', 'locations.view'), ('payroll', 'structure.view'), ('payroll', 'leave.view');
-- Approver: nothing to change yet; it approves pay runs when they come (PAY).

insert into role_permissions (role_id, permission_key)
select r.id, w.permission_key from wanted w join roles r on r.key = w.role_key and r.organization_id is null
on conflict do nothing;

delete from role_permissions rp
using roles r
where r.id = rp.role_id and r.organization_id is null
  and not exists (select 1 from wanted w where w.role_key = r.key and w.permission_key = rp.permission_key);
