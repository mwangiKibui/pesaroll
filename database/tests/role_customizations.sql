-- ACC-04: an organization changes what a standard group can do (its own version of the role, used by
-- every group with that role); Back to standard removes it; another organization is untouched; only
-- someone who manages the team for every company may; and someone must keep managing the team.
begin;
do $$
declare
  amina constant uuid := '00000000-0000-7000-8000-0000000c4101';
  sid   constant uuid := '00000000-0000-7000-8000-0000000c4102';
  mary  constant uuid := '00000000-0000-7000-8000-0000000c4103';
  otis  constant uuid := '00000000-0000-7000-8000-0000000c4104';
  sid2  constant uuid := '00000000-0000-7000-8000-0000000c4105';
  co_a  constant uuid := '00000000-0000-7000-8000-0000000c41a1';
  org uuid;
  other uuid;
  mary_m uuid := uuidv7();
  msg text;
begin
  insert into users (id, email, first_name, last_name) values (amina, 'amina.roles@example.com', 'Amina', 'Otieno'),
    (mary, 'mary.roles@example.com', 'Mary', 'Wanjiku'), (otis, 'otis.roles@example.com', 'Otis', 'Ouma');
  insert into sessions (id, user_id, token_hash, expires_at) values (sid, amina, '\x0c41', now() + interval '7 days'),
                                                                    (sid2, otis, '\x0c42', now() + interval '7 days');
  perform set_config('app.user_id', otis::text, true);
  set local role pesaroll_app;
  other := create_organization('Other Roles Group', sid2);
  reset role;
  perform set_config('app.user_id', amina::text, true);
  set local role pesaroll_app;
  org := create_organization('Roles Test Group', sid);
  reset role;
  insert into companies (id, organization_id, legal_name, country_code, currency) values (co_a, org, 'Westlands Ltd', 'KE', 'KES');
  insert into memberships (id, organization_id, user_id) values (mary_m, org, mary);
  perform set_config('app.org_id', org::text, true);

  -- Mary is Payroll for Westlands (a scoped Payroll group).
  set local role pesaroll_app;
  perform set_member_access(mary_m, array['payroll'], false, array[co_a]);
  -- Payroll here may also view locations and structure, and no longer pay items.
  perform set_role_access('payroll', array['employees.manage', 'schedules.manage', 'payment_accounts.manage', 'payroll_history.manage',
                                           'locations.view', 'structure.view']);
  reset role;
  if exists (select 1 from member_grants(mary, org) where permission_key like 'pay_items.%') then
    raise exception 'Payroll still has pay items after the change';
  end if;
  if not exists (select 1 from member_grants(mary, org) where permission_key = 'locations.view' and company_ids = array[co_a]) then
    raise exception 'Payroll for Westlands did not get View on locations for Westlands';
  end if;
  if not exists (select 1 from member_grants(mary, org) where permission_key = 'employees.view') then
    raise exception 'Manage on employees no longer brings View';
  end if;
  if (select array_agg(permission_key order by permission_key) from role_permissions rp join roles r on r.id = rp.role_id
      where r.key = 'payroll' and r.organization_id is null) is distinct from
     array['employees.manage', 'leave.view', 'locations.view', 'pay_items.manage', 'pay.manage', 'payment_accounts.manage', 'payroll_history.manage', 'schedules.manage', 'structure.view'] then
    raise exception 'the standard Payroll role was changed';
  end if;

  -- Row-level security: the other organization sees none of it.
  perform set_config('app.org_id', other::text, true);
  set local role pesaroll_app;
  if exists (select 1 from role_customizations) or exists (select 1 from role_customization_permissions) then
    raise exception 'another organization sees this one''s changes';
  end if;
  reset role;
  perform set_config('app.org_id', org::text, true);

  -- Back to standard.
  set local role pesaroll_app;
  perform reset_role_access('payroll');
  reset role;
  if not exists (select 1 from member_grants(mary, org) where permission_key = 'pay_items.manage') then
    raise exception 'Back to standard did not give Payroll its pay items again';
  end if;

  -- The organization can't lock itself out: Administrators keep Manage on Team access unless someone else has it.
  begin
    set local role pesaroll_app;
    perform set_role_access('administrators', array['employees.manage']);
    reset role;
    raise exception 'took team access away from the only administrators';
  exception when others then
    get stacked diagnostics msg = message_text;
    reset role;
    if msg <> 'ACC-8007' then raise exception 'expected ACC-8007, got %', msg; end if;
  end;

  -- Only someone who manages the team for every company may.
  perform set_config('app.user_id', mary::text, true);
  begin
    set local role pesaroll_app;
    perform set_role_access('hr', array['employees.view']);
    reset role;
    raise exception 'Mary changed what HR can do';
  exception when others then
    get stacked diagnostics msg = message_text;
    reset role;
    if msg <> 'ACC-8001' then raise exception 'expected ACC-8001, got %', msg; end if;
  end;
end
$$;
rollback;
