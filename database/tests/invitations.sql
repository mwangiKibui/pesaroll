-- ACC-03, AUTH-08: an invitation joins the person to the organization in the group for its role
-- and companies (the starting group for all companies, else one made for exactly those companies);
-- it works once, only for its own email or phone, and not once expired or revoked; it stays in its
-- organization.
begin;
do $$
declare
  amina constant uuid := '00000000-0000-7000-8000-0000000a1101';
  sid   constant uuid := '00000000-0000-7000-8000-0000000a1102';
  mary  constant uuid := '00000000-0000-7000-8000-0000000a1103';
  joy   constant uuid := '00000000-0000-7000-8000-0000000a1104';
  co_a  constant uuid := '00000000-0000-7000-8000-0000000a11a1';
  co_b  constant uuid := '00000000-0000-7000-8000-0000000a11a2';
  inv_hr constant uuid := '00000000-0000-7000-8000-0000000a11b1';
  inv_admin constant uuid := '00000000-0000-7000-8000-0000000a11b2';
  inv_old constant uuid := '00000000-0000-7000-8000-0000000a11b3';
  org uuid;
  got uuid;
  msg text;
  info record;
begin
  insert into users (id, email, first_name, last_name) values (amina, 'amina.inv@example.com', 'Amina', 'Otieno');
  insert into users (id, email, first_name, last_name, signed_up_via) values (mary, 'mary.inv@example.com', 'Mary', 'Wanjiku', 'invitation');
  insert into users (id, phone, first_name, last_name, signed_up_via) values (joy, '+254711000111', 'Joy', 'Achieng', 'invitation');
  insert into sessions (id, user_id, token_hash, expires_at) values (sid, amina, '\x0a11', now() + interval '7 days');
  perform set_config('app.user_id', amina::text, true);
  set local role pesaroll_app;
  org := create_organization('Invitation Test Group', sid);
  reset role;
  insert into companies (id, organization_id, legal_name, country_code, currency)
  values (co_a, org, 'Westlands Ltd', 'KE', 'KES'), (co_b, org, 'Mombasa Ltd', 'KE', 'KES');

  -- Mary: HR for Westlands, by email. Joy: an administrator, by SMS. An old one that expired.
  insert into invitations (id, organization_id, first_name, last_name, email, group_keys, all_companies, invited_by, token_hash, expires_at)
  values (inv_hr, org, 'Mary', 'Wanjiku', 'mary.inv@example.com', array['hr', 'payroll'], false, amina, '\xa1', now() + interval '7 days'),
         (inv_old, org, 'Old', 'Invite', 'old.inv@example.com', array['payroll'], true, amina, '\xa3', now() - interval '1 day');
  insert into invitation_companies (organization_id, invitation_id, company_id) values (org, inv_hr, co_a);
  insert into invitations (id, organization_id, first_name, last_name, phone, group_keys, invited_by, token_hash, expires_at)
  values (inv_admin, org, 'Joy', 'Achieng', '+254711000111', array['administrators'], amina, '\xa2', now() + interval '7 days');

  set local role pesaroll_app;
  select * into info from invitation_by_token('\xa1');
  if info.group_keys is distinct from array['hr', 'payroll'] or info.organization_name <> 'Invitation Test Group' or info.invited_by_name <> 'Amina Otieno' or info.status <> 'open'
     or info.company_names <> array['Westlands Ltd'] or not info.has_account then
    raise exception 'invitation_by_token does not describe the invitation: %', info;
  end if;
  if (select status from invitation_by_token('\xa3')) <> 'expired' or exists (select 1 from invitation_by_token('\xff')) then
    raise exception 'an expired or unknown link is not told apart';
  end if;

  -- Only the person it's for.
  perform set_config('app.user_id', joy::text, true);
  begin
    perform accept_invitation('\xa1');
    raise exception 'someone else accepted Mary''s invitation';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'AUTH-1015' then raise exception 'another person''s invitation: expected AUTH-1015, got %', msg; end if;
  end;

  -- Mary accepts: HR and Payroll, for exactly Westlands, in a group made for each.
  perform set_config('app.user_id', mary::text, true);
  got := accept_invitation('\xa1');
  reset role;
  if got <> org then raise exception 'accepting did not return the organization'; end if;
  if member_company_ids(mary, org) is distinct from array[co_a] then
    raise exception 'Mary does not see exactly Westlands';
  end if;
  if (select array_agg(permission_key order by permission_key) from member_grants(mary, org) where permission_key like '%.manage')
     is distinct from array['employees.manage', 'leave.manage', 'locations.manage', 'pay_items.manage', 'pay.manage', 'payment_accounts.manage', 'payroll_history.manage', 'schedules.manage', 'structure.manage'] then
    raise exception 'Mary is not HR and Payroll: %', (select array_agg(permission_key order by permission_key) from member_grants(mary, org));
  end if;
  if (select count(*) from security_groups where organization_id = org and name in ('HR · Westlands Ltd', 'Payroll · Westlands Ltd') and not all_companies) <> 2 then
    raise exception 'no group was made for HR and for Payroll at Westlands';
  end if;

  -- It works once.
  set local role pesaroll_app;
  begin
    perform accept_invitation('\xa1');
    raise exception 'an invitation was accepted twice';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'AUTH-1013' then raise exception 'used: expected AUTH-1013, got %', msg; end if;
  end;

  -- Joy, by phone: an administrator, in Administrators, for every company.
  perform set_config('app.user_id', joy::text, true);
  perform accept_invitation('\xa2');
  reset role;
  if member_company_ids(joy, org) is distinct from array[co_a, co_b]
     or not exists (select 1 from member_grants(joy, org) where permission_key = 'team.manage' and all_companies) then
    raise exception 'Joy is not an administrator for every company';
  end if;

  -- Expired and revoked links don't work.
  set local role pesaroll_app;
  perform set_config('app.user_id', mary::text, true);
  begin
    perform accept_invitation('\xa3');
    raise exception 'an expired invitation was accepted';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'AUTH-1012' then raise exception 'expired: expected AUTH-1012, got %', msg; end if;
  end;
  reset role;
  update invitations set revoked_at = now(), expires_at = now() + interval '1 day' where id = inv_old;
  set local role pesaroll_app;
  begin
    perform accept_invitation('\xa3');
    raise exception 'a revoked invitation was accepted';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'AUTH-1014' then raise exception 'revoked: expected AUTH-1014, got %', msg; end if;
  end;

  -- Another organization sees none of them.
  perform set_config('app.org_id', '00000000-0000-7000-8000-000000000002', true);
  if exists (select 1 from invitations where organization_id = org) or exists (select 1 from invitation_companies where organization_id = org) then
    raise exception 'another organization can see the invitations';
  end if;
  reset role;
end $$;
rollback;
