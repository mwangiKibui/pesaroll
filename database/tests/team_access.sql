-- ONB-11: an administrator moves a colleague to another group and companies, or removes them; the
-- organization always keeps an administrator; only administrators may, and only in their organization.
begin;
do $$
declare
  amina constant uuid := '00000000-0000-7000-8000-0000000c1101';
  sid   constant uuid := '00000000-0000-7000-8000-0000000c1102';
  mary  constant uuid := '00000000-0000-7000-8000-0000000c1103';
  co_a  constant uuid := '00000000-0000-7000-8000-0000000c11a1';
  co_b  constant uuid := '00000000-0000-7000-8000-0000000c11a2';
  org uuid;
  amina_m uuid;
  mary_m uuid := uuidv7();
  msg text;
begin
  insert into users (id, email, first_name, last_name) values (amina, 'amina.team@example.com', 'Amina', 'Otieno'),
                                                             (mary, 'mary.team@example.com', 'Mary', 'Wanjiku');
  insert into sessions (id, user_id, token_hash, expires_at) values (sid, amina, '\x0c11', now() + interval '7 days');
  perform set_config('app.user_id', amina::text, true);
  set local role pesaroll_app;
  org := create_organization('Team Test Group', sid);
  reset role;
  insert into companies (id, organization_id, legal_name, country_code, currency)
  values (co_a, org, 'Westlands Ltd', 'KE', 'KES'), (co_b, org, 'Mombasa Ltd', 'KE', 'KES');
  select id into amina_m from memberships where organization_id = org and user_id = amina;
  insert into memberships (id, organization_id, user_id) values (mary_m, org, mary);
  perform set_config('app.org_id', org::text, true);

  -- Mary: Payroll for Mombasa, then HR for every company.
  set local role pesaroll_app;
  perform set_member_access(mary_m, array['payroll'], false, array[co_b]);
  reset role;
  if member_company_ids(mary, org) is distinct from array[co_b]
     or not exists (select 1 from member_grants(mary, org) where permission_key = 'pay_items.manage') then
    raise exception 'Mary is not Payroll for Mombasa';
  end if;
  set local role pesaroll_app;
  perform set_member_access(mary_m, array['hr'], true, null);
  reset role;
  if member_company_ids(mary, org) is distinct from array[co_a, co_b]
     or exists (select 1 from member_grants(mary, org) where permission_key = 'pay_items.manage')
     or (select count(*) from security_group_members where membership_id = mary_m and removed_at is null) <> 1 then
    raise exception 'Mary did not move to HR for every company, out of Payroll';
  end if;

  -- HR and Payroll for every company at once; then back to HR only, which takes Payroll away.
  set local role pesaroll_app;
  perform set_member_access(mary_m, array['hr', 'payroll'], true, null);
  reset role;
  if not exists (select 1 from member_grants(mary, org) where permission_key = 'pay_items.manage')
     or not exists (select 1 from member_grants(mary, org) where permission_key = 'structure.manage')
     or (select count(*) from security_group_members where membership_id = mary_m and removed_at is null) <> 2 then
    raise exception 'Mary is not in HR and Payroll';
  end if;
  set local role pesaroll_app;
  perform set_member_access(mary_m, array['hr'], true, null);
  begin
    perform set_member_access(mary_m, array[]::text[], true, null);
    raise exception 'access with no group was allowed';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'ACC-8009' then raise exception 'no group: expected ACC-8009, got %', msg; end if;
  end;
  reset role;
  if exists (select 1 from member_grants(mary, org) where permission_key = 'pay_items.manage')
     or (select count(*) from security_group_members where membership_id = mary_m and removed_at is null) <> 1 then
    raise exception 'changing to HR only did not take Payroll away';
  end if;

  -- Mary, not an administrator, can't change anyone.
  perform set_config('app.user_id', mary::text, true);
  set local role pesaroll_app;
  begin
    perform remove_member(amina_m);
    raise exception 'someone who is not an administrator removed someone';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'ACC-8001' then raise exception 'not an administrator: expected ACC-8001, got %', msg; end if;
  end;

  -- The last administrator can't be moved or removed.
  perform set_config('app.user_id', amina::text, true);
  begin
    perform set_member_access(amina_m, array['hr'], true, null);
    raise exception 'the last administrator was moved';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'ACC-8007' then raise exception 'last administrator moved: expected ACC-8007, got %', msg; end if;
  end;
  begin
    perform remove_member(amina_m);
    raise exception 'the last administrator was removed';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'ACC-8007' then raise exception 'last administrator removed: expected ACC-8007, got %', msg; end if;
  end;

  -- With Mary an administrator too, Amina can step down; then Mary removes nobody unknown.
  perform set_member_access(mary_m, array['administrators'], true, null);
  perform set_member_access(amina_m, array['payroll'], true, null);
  perform set_config('app.user_id', mary::text, true);
  perform remove_member(amina_m);
  reset role;
  if (select status from memberships where id = amina_m) <> 'removed' or member_company_ids(amina, org) <> '{}' then
    raise exception 'removing did not end Amina''s access';
  end if;
  set local role pesaroll_app;
  begin
    perform remove_member(amina_m);
    raise exception 'someone already removed was removed again';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'ACC-8008' then raise exception 'removed twice: expected ACC-8008, got %', msg; end if;
  end;

  -- Another organization can't touch Mary.
  perform set_config('app.org_id', '00000000-0000-7000-8000-000000000002', true);
  begin
    perform set_member_access(mary_m, array['hr'], true, null);
    raise exception 'another organization changed Mary';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg not in ('ACC-8001', 'ACC-8008') then raise exception 'another organization: expected ACC-8001 or ACC-8008, got %', msg; end if;
  end;
  reset role;
end $$;
rollback;
