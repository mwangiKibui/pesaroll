-- ACC-04: permissions are grouped in areas, each with View and Manage; Manage brings View. The
-- standard roles carry Manage on their areas; a person gets View wherever they have Manage, for the
-- same companies.
begin;
do $$
declare
  amina constant uuid := '00000000-0000-7000-8000-0000000ac4e1';
  sid   constant uuid := '00000000-0000-7000-8000-0000000ac4e2';
  mary  constant uuid := '00000000-0000-7000-8000-0000000ac4e3';
  co_a  constant uuid := '00000000-0000-7000-8000-0000000ac4a1';
  co_b  constant uuid := '00000000-0000-7000-8000-0000000ac4a2';
  hr_west constant uuid := '00000000-0000-7000-8000-0000000ac4b1';
  org uuid;
  mary_membership uuid := uuidv7();
  grants record;
begin
  -- The catalogue: every permission is an area's View or Manage, and every area has both.
  if exists (select 1 from permissions p where p.key <> p.area || '.' || p.level) then
    raise exception 'a permission key is not <area>.<view|manage>';
  end if;
  if exists (select 1 from permission_areas a
             where (select count(*) from permissions p where p.area = a.key) <> 2) then
    raise exception 'an area does not have exactly View and Manage';
  end if;
  if (select array_agg(key order by sort_order) from permission_areas)
     is distinct from array['companies', 'employees', 'locations', 'structure', 'leave', 'schedules', 'pay_items', 'payment_accounts', 'payroll_history', 'team'] then
    raise exception 'the areas are not the ones planned, in order';
  end if;

  -- The standard roles: Manage on their areas.
  if (select array_agg(rp.permission_key order by rp.permission_key) from role_permissions rp join roles r on r.id = rp.role_id
      where r.key = 'hr' and r.organization_id is null)
     is distinct from array['employees.manage', 'leave.manage', 'locations.manage', 'schedules.view', 'structure.manage'] then
    raise exception 'HR is not: manage employees, leave, locations and structure; view schedules';
  end if;
  if (select array_agg(rp.permission_key order by rp.permission_key) from role_permissions rp join roles r on r.id = rp.role_id
      where r.key = 'payroll' and r.organization_id is null)
     is distinct from array['employees.manage', 'leave.view', 'locations.view', 'pay_items.manage', 'payment_accounts.manage', 'payroll_history.manage', 'schedules.manage', 'structure.view'] then
    raise exception 'Payroll is not: manage employees, pay items, paying accounts, payroll history and schedules; view leave, locations and structure';
  end if;
  if (select count(*) from role_permissions rp join roles r on r.id = rp.role_id join permissions p on p.key = rp.permission_key
      where r.key = 'admin' and r.organization_id is null and p.level = 'manage')
     <> (select count(*) from permissions where level = 'manage') then
    raise exception 'an administrator does not manage every area';
  end if;
  if exists (select 1 from role_permissions rp join roles r on r.id = rp.role_id where r.key = 'approver' and r.organization_id is null) then
    raise exception 'an approver has setup access';
  end if;

  -- Manage brings View, for the same companies.
  insert into users (id, email, first_name, last_name) values (amina, 'amina.areas@example.com', 'Amina', 'Otieno'),
                                                             (mary, 'mary.areas@example.com', 'Mary', 'Wanjiku');
  insert into sessions (id, user_id, token_hash, expires_at) values (sid, amina, '\x0ac4', now() + interval '7 days');
  perform set_config('app.user_id', amina::text, true);
  set local role pesaroll_app;
  org := create_organization('Access Areas Test', sid);
  reset role;
  insert into companies (id, organization_id, legal_name, country_code, currency)
  values (co_a, org, 'Westlands Ltd', 'KE', 'KES'), (co_b, org, 'Mombasa Ltd', 'KE', 'KES');
  insert into memberships (id, organization_id, user_id) values (mary_membership, org, mary);
  insert into security_groups (id, organization_id, name, all_companies) values (hr_west, org, 'HR Westlands', false);
  insert into security_group_companies (organization_id, security_group_id, company_id) values (org, hr_west, co_a);
  insert into security_group_roles (organization_id, security_group_id, role_id)
  select org, hr_west, id from roles where key = 'hr' and organization_id is null;
  insert into security_group_members (id, organization_id, security_group_id, membership_id) values (uuidv7(), org, hr_west, mary_membership);

  select * into grants from member_grants(mary, org) where permission_key = 'employees.view';
  if grants is null or grants.all_companies or grants.company_ids is distinct from array[co_a] then
    raise exception 'HR for Westlands does not get View on employees for Westlands only';
  end if;
  if exists (select 1 from member_grants(mary, org) where permission_key = 'payroll_history.view') then
    raise exception 'HR got View on payroll history';
  end if;
  if (select count(*) from member_grants(amina, org) where permission_key like '%.view' and all_companies)
     <> (select count(*) from permissions where level = 'view') then
    raise exception 'an administrator does not view every area';
  end if;
end
$$;
rollback;
