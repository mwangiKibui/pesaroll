-- Re-runnable: the permissions the app checks (ACC-01) and the system roles that bundle them.
-- Keys match Pesaroll.Shared.Access.Permissions in the API. Role ids are fixed, the same in every
-- environment. A permission or grant taken off these lists is removed.

insert into permissions (key, description) values
  ('companies.manage',        'Add, change and delete companies.'),
  ('payment_accounts.manage', 'Add and change the accounts a company pays from.'),
  ('locations.manage',        'Add, change and delete locations.'),
  ('structure.manage',        'Add, change and delete divisions, departments, units and positions.'),
  ('leave.manage',            'Add, change and delete leave types and leave plans.'),
  ('schedules.manage',        'Add, change and delete payroll schedules.'),
  ('pay_items.manage',        'Add, change and delete earnings, deductions and payees, and who gets them.'),
  ('employees.manage',        'Add, change, delete and upload employees.'),
  ('payroll_history.manage',  'Upload and delete payroll history.'),
  ('team.manage',             'Invite people, and change their groups and companies.')
on conflict (key) do update set description = excluded.description;

delete from permissions where key not in ('companies.manage', 'payment_accounts.manage', 'locations.manage', 'structure.manage', 'leave.manage',
  'schedules.manage', 'pay_items.manage', 'employees.manage', 'payroll_history.manage', 'team.manage');

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
  ('payroll', 'payment_accounts.manage'), ('payroll', 'schedules.manage'), ('payroll', 'pay_items.manage'), ('payroll', 'employees.manage'),
  ('payroll', 'payroll_history.manage');
-- Approver: nothing to change yet; it approves pay runs when they come (PAY).

insert into role_permissions (role_id, permission_key)
select r.id, w.permission_key from wanted w join roles r on r.key = w.role_key and r.organization_id is null
on conflict do nothing;

delete from role_permissions rp
using roles r
where r.id = rp.role_id and r.organization_id is null
  and not exists (select 1 from wanted w where w.role_key = r.key and w.permission_key = rp.permission_key);
