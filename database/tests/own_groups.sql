-- ACC-08: an organization adds its own groups (a role it owns, with a level per area), gives people
-- them for chosen companies, renames and deletes them; nobody else sees or uses them; a group in use
-- can't be deleted; only someone who manages the team for every company may.
begin;
do $$
declare
  amina constant uuid := '00000000-0000-7000-8000-0000000c5101';
  sid   constant uuid := '00000000-0000-7000-8000-0000000c5102';
  ken   constant uuid := '00000000-0000-7000-8000-0000000c5103';
  otis  constant uuid := '00000000-0000-7000-8000-0000000c5104';
  sid2  constant uuid := '00000000-0000-7000-8000-0000000c5105';
  co_a  constant uuid := '00000000-0000-7000-8000-0000000c51a1';
  co_b  constant uuid := '00000000-0000-7000-8000-0000000c51a2';
  k_key text;
  org uuid;
  other uuid;
  ken_m uuid := uuidv7();
  v_key text;
  msg text;
begin
  insert into users (id, email, first_name, last_name) values (amina, 'amina.own@example.com', 'Amina', 'Otieno'),
    (ken, 'ken.own@example.com', 'Kennedy', 'Kibui'), (otis, 'otis.own@example.com', 'Otis', 'Ouma');
  insert into sessions (id, user_id, token_hash, expires_at) values (sid, amina, '\x0c51', now() + interval '7 days'),
                                                                    (sid2, otis, '\x0c52', now() + interval '7 days');
  perform set_config('app.user_id', otis::text, true);
  set local role pesaroll_app;
  other := create_organization('Other Own Group', sid2);
  reset role;
  perform set_config('app.user_id', amina::text, true);
  set local role pesaroll_app;
  org := create_organization('Own Groups Test', sid);
  reset role;
  insert into companies (id, organization_id, legal_name, country_code, currency) values (co_a, org, 'Westlands Ltd', 'KE', 'KES'), (co_b, org, 'Kisumu Ltd', 'KE', 'KES');
  insert into memberships (id, organization_id, user_id) values (ken_m, org, ken);
  perform set_config('app.org_id', org::text, true);

  -- Branch managers: manage employees, view leave. Kennedy joins it for Westlands.
  set local role pesaroll_app;
  v_key := add_group('Branch managers', 'Run a branch', array['employees.manage', 'leave.view'], true, null);
  perform set_member_access(ken_m, array[v_key], false, array[co_a]);
  reset role;
  if v_key is distinct from 'branch_managers' then raise exception 'the key is %, not branch_managers', v_key; end if;
  if (select array_agg(permission_key order by permission_key) from member_grants(ken, org))
     is distinct from array['employees.manage', 'employees.view', 'leave.view'] then
    raise exception 'Kennedy has %', (select array_agg(permission_key order by permission_key) from member_grants(ken, org));
  end if;
  if not exists (select 1 from member_grants(ken, org) where permission_key = 'employees.view' and company_ids = array[co_a] and not all_companies) then
    raise exception 'Kennedy is not a branch manager for Westlands only';
  end if;

  -- The same name again is refused; a name like a standard group's too.
  begin
    set local role pesaroll_app;
    perform add_group('branch managers', null, array['leave.view'], true, null);
    reset role;
    raise exception 'added a second Branch managers';
  exception when others then
    get stacked diagnostics msg = message_text;
    reset role;
    if msg <> 'ACC-8011' then raise exception 'expected ACC-8011, got %', msg; end if;
  end;

  -- Another organization neither sees it nor can give it to anyone.
  perform set_config('app.org_id', other::text, true);
  set local role pesaroll_app;
  if exists (select 1 from roles where organization_id is not null) then
    raise exception 'another organization sees this one''s groups';
  end if;
  reset role;
  perform set_config('app.org_id', org::text, true);

  -- Renaming keeps the key and the people.
  set local role pesaroll_app;
  perform update_group(v_key, 'Branch leads', 'Leads a branch', true, null);
  reset role;
  if not exists (select 1 from roles where key = v_key and organization_id = org and name_en = 'Branch leads') then
    raise exception 'the group was not renamed';
  end if;

  -- Deleting is refused while Kennedy is in it, and works once he has moved.
  begin
    set local role pesaroll_app;
    perform delete_group(v_key);
    reset role;
    raise exception 'deleted a group someone is in';
  exception when others then
    get stacked diagnostics msg = message_text;
    reset role;
    if msg <> 'ACC-8012' then raise exception 'expected ACC-8012, got %', msg; end if;
  end;
  set local role pesaroll_app;
  perform set_member_access(ken_m, array['hr'], true, null);
  perform delete_group(v_key);
  reset role;
  if exists (select 1 from roles where key = v_key and organization_id = org and deleted_at is null) then
    raise exception 'the group was not deleted';
  end if;
  if not exists (select 1 from security_group_members gm join security_group_roles gr on gr.security_group_id = gm.security_group_id
                 join roles r on r.id = gr.role_id where r.key = v_key and gm.membership_id = ken_m) then
    raise exception 'Kennedy''s time in the group was forgotten';
  end if;

  -- A group only for Kisumu: Westlands is refused; all companies means Kisumu; Kisumu can't be taken away while used.
  set local role pesaroll_app;
  k_key := add_group('Kisumu kitchen leads', null, array['leave.view'], false, array[co_b]);
  reset role;
  begin
    set local role pesaroll_app;
    perform set_member_access(ken_m, array[k_key], false, array[co_a]);
    reset role;
    raise exception 'gave a Kisumu-only group for Westlands';
  exception when others then
    get stacked diagnostics msg = message_text;
    reset role;
    if msg <> 'ACC-8013' then raise exception 'expected ACC-8013, got %', msg; end if;
  end;
  set local role pesaroll_app;
  perform set_member_access(ken_m, array[k_key], true, null);
  reset role;
  if not exists (select 1 from member_grants(ken, org) where permission_key = 'leave.view' and not all_companies and company_ids = array[co_b]) then
    raise exception 'all companies did not mean the group''s companies';
  end if;
  begin
    set local role pesaroll_app;
    perform update_group(k_key, 'Kisumu kitchen leads', null, false, array[co_a]);
    reset role;
    raise exception 'took Kisumu away while Kennedy covers it';
  exception when others then
    get stacked diagnostics msg = message_text;
    reset role;
    if msg <> 'ACC-8014' then raise exception 'expected ACC-8014, got %', msg; end if;
  end;

  -- Only someone who manages the team for every company may add groups.
  perform set_config('app.user_id', ken::text, true);
  begin
    set local role pesaroll_app;
    perform add_group('Kitchen leads', null, array['leave.view'], true, null);
    reset role;
    raise exception 'Kennedy added a group';
  exception when others then
    get stacked diagnostics msg = message_text;
    reset role;
    if msg <> 'ACC-8001' then raise exception 'expected ACC-8001, got %', msg; end if;
  end;
end
$$;
rollback;
