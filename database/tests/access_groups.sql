-- ACC-01, ACC-02, ACC-06: a new organization starts with Administrators, HR, Payroll and Approvers,
-- the person who onboarded it in Administrators; a person sees the companies their groups cover
-- and may do what their groups' roles allow, for those companies; groups stay in their organization.
begin;
do $$
declare
  amina constant uuid := '00000000-0000-7000-8000-0000000ac0e1';
  sid   constant uuid := '00000000-0000-7000-8000-0000000ac0e2';
  mary  constant uuid := '00000000-0000-7000-8000-0000000ac0e3';
  co_a  constant uuid := '00000000-0000-7000-8000-0000000ac0a1';
  co_b  constant uuid := '00000000-0000-7000-8000-0000000ac0a2';
  hr_west constant uuid := '00000000-0000-7000-8000-0000000ac0b1';
  org uuid;
  mary_membership uuid := uuidv7();
  grants record;
begin
  -- Onboarding, as the app does it.
  insert into users (id, email, first_name, last_name) values (amina, 'amina.acc@example.com', 'Amina', 'Otieno'),
                                                             (mary, 'mary.acc@example.com', 'Mary', 'Wanjiku');
  insert into sessions (id, user_id, token_hash, expires_at) values (sid, amina, '\x0ac0', now() + interval '7 days');
  perform set_config('app.user_id', amina::text, true);
  set local role pesaroll_app;
  org := create_organization('Access Test Group', sid);
  reset role;
  insert into companies (id, organization_id, legal_name, country_code, currency)
  values (co_a, org, 'Westlands Ltd', 'KE', 'KES'), (co_b, org, 'Mombasa Ltd', 'KE', 'KES');

  if (select array_agg(default_key order by default_key) from security_groups where organization_id = org)
     is distinct from array['administrators', 'approvers', 'hr', 'payroll'] then
    raise exception 'the starting groups are not Administrators, HR, Payroll and Approvers';
  end if;
  if not exists (select 1 from security_group_members gm join security_groups g on g.id = gm.security_group_id
                 join memberships m on m.id = gm.membership_id where g.default_key = 'administrators' and g.organization_id = org and m.user_id = amina) then
    raise exception 'the person who onboarded it is not in Administrators';
  end if;
  if member_company_ids(amina, org) is distinct from array[co_a, co_b] then
    raise exception 'an administrator does not see every company';
  end if;
  select * into grants from member_grants(amina, org) where permission_key = 'team.manage';
  if grants is null or not grants.all_companies then
    raise exception 'an administrator cannot manage the team for all companies';
  end if;

  -- Running it again changes nothing.
  perform create_default_groups(org, amina);
  if (select count(*) from security_groups where organization_id = org) <> 4
     or (select count(*) from security_group_members gm join security_groups g on g.id = gm.security_group_id where g.organization_id = org) <> 1 then
    raise exception 'create_default_groups is not safe to run again';
  end if;

  -- Mary: HR for Westlands only.
  insert into memberships (id, organization_id, user_id) values (mary_membership, org, mary);
  if member_company_ids(mary, org) <> '{}' then
    raise exception 'a member in no group sees companies';
  end if;
  insert into security_groups (id, organization_id, name, all_companies) values (hr_west, org, 'HR · Westlands', false);
  insert into security_group_companies (organization_id, security_group_id, company_id) values (org, hr_west, co_a);
  insert into security_group_roles (organization_id, security_group_id, role_id) select org, hr_west, id from roles where key = 'hr' and organization_id is null;
  insert into security_group_members (id, organization_id, security_group_id, membership_id) values (uuidv7(), org, hr_west, mary_membership);

  if member_company_ids(mary, org) is distinct from array[co_a] then
    raise exception 'HR for Westlands does not see exactly Westlands';
  end if;
  select * into grants from member_grants(mary, org) where permission_key = 'employees.manage';
  if grants is null or grants.all_companies or grants.company_ids is distinct from array[co_a] then
    raise exception 'HR for Westlands cannot manage exactly Westlands'' employees';
  end if;
  if exists (select 1 from member_grants(mary, org) where permission_key in ('team.manage', 'companies.manage', 'pay_items.manage')) then
    raise exception 'HR can manage the team, companies or earnings and deductions';
  end if;

  -- Leaving the group, or being removed from the organization, ends it.
  update security_group_members set removed_at = now() where membership_id = mary_membership;
  if member_company_ids(mary, org) <> '{}' or exists (select 1 from member_grants(mary, org)) then
    raise exception 'access stayed after leaving the group';
  end if;

  -- Another organization sees none of these groups.
  set local role pesaroll_app;
  perform set_config('app.org_id', '00000000-0000-7000-8000-000000000002', true);
  if exists (select 1 from security_groups where organization_id = org) or exists (select 1 from security_group_members where organization_id = org) then
    raise exception 'another organization can see the groups';
  end if;
  perform set_config('app.org_id', org::text, true);
  if (select count(*) from security_groups) <> 5 then
    raise exception 'the organization does not see its own groups';
  end if;
  reset role;
end $$;
rollback;
