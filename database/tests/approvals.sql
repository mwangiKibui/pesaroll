-- WF-01..03: approval workflows and requests. Kinds are reference data; each organization sets up
-- its own workflows (nothing seeded), for all companies or one; requests and their history stay in
-- their organization and company; history can't be changed; a decided request can't change; the
-- approvers for a step are the active members holding a permission for the company, or a line
-- manager linked to a sign-in.
begin;
do $$
declare
  amina constant uuid := '00000000-0000-7000-8000-0000000d1101';   -- onboards; Administrator
  sid   constant uuid := '00000000-0000-7000-8000-0000000d1102';
  mary  constant uuid := '00000000-0000-7000-8000-0000000d1103';   -- Approver for Westlands only
  john  constant uuid := '00000000-0000-7000-8000-0000000d1104';   -- Payroll; an employee whose manager is Mary
  co_a  constant uuid := '00000000-0000-7000-8000-0000000d11a1';
  co_b  constant uuid := '00000000-0000-7000-8000-0000000d11a2';
  other_org constant uuid := '00000000-0000-7000-8000-000000000002';   -- Org Two from the seed
  org uuid;
  mary_m uuid := uuidv7();
  john_m uuid := uuidv7();
  wf_all uuid := uuidv7();
  wf_a uuid := uuidv7();
  req uuid := uuidv7();
  mary_e uuid := uuidv7();
  john_e uuid := uuidv7();
  sched uuid;
  plan uuid;
  msg text;
begin
  -- Kinds: pay runs need a workflow before one can be sent; leave isn't built yet.
  if not exists (select 1 from workflow_kinds where key = 'pay_run' and required and suggested_steps -> 0 ->> 'permission' = 'pay_run_approvals.manage') then
    raise exception 'the pay run kind is missing, not required, or its suggested setup is not the Pay run approvals permission';
  end if;
  if not exists (select 1 from workflow_kinds where key = 'leave' and not required) then
    raise exception 'the leave kind is missing or required before leave requests exist';
  end if;

  insert into users (id, email, first_name, last_name) values
    (amina, 'amina.wf@example.com', 'Amina', 'Otieno'), (mary, 'mary.wf@example.com', 'Mary', 'Wanjiku'), (john, 'john.wf@example.com', 'John', 'Kamau');
  insert into sessions (id, user_id, token_hash, expires_at) values (sid, amina, '\x0d11', now() + interval '7 days');
  perform set_config('app.user_id', amina::text, true);
  set local role pesaroll_app;
  org := create_organization('Workflow Test Group', sid);
  reset role;
  insert into companies (id, organization_id, legal_name, country_code, currency)
  values (co_a, org, 'Westlands Ltd', 'KE', 'KES'), (co_b, org, 'Mombasa Ltd', 'KE', 'KES');
  insert into memberships (id, organization_id, user_id) values (mary_m, org, mary), (john_m, org, john);
  perform set_config('app.org_id', org::text, true);
  set local role pesaroll_app;
  perform set_member_access(mary_m, array['approvers'], false, array[co_a]);
  perform set_member_access(john_m, array['payroll'], true, null);
  reset role;

  -- Nothing is set up for a new organization.
  if exists (select 1 from workflows where organization_id = org) then
    raise exception 'a new organization got workflows it did not set up';
  end if;

  -- Who holds a permission for a company: Amina (Administrator, every company) and Mary (Westlands).
  perform set_config('app.company_ids', co_a || ',' || co_b, true);
  set local role pesaroll_app;
  if array(select members_with_permission(co_a, 'pay_run_approvals.manage') order by 1) is distinct from array[amina, mary] then
    raise exception 'Westlands approvers are %, not Amina and Mary', array(select members_with_permission(co_a, 'pay_run_approvals.manage') order by 1);
  end if;
  if array(select members_with_permission(co_b, 'pay_run_approvals.manage')) is distinct from array[amina] then
    raise exception 'Mombasa approvers should be Amina only';
  end if;
  if exists (select 1 from members_with_permission(co_a, 'pay_run_approvals.manage') u where u = john) then
    raise exception 'Payroll can approve pay runs by default';
  end if;

  -- A workflow for every company and one for Westlands; never two active for the same scope.
  insert into workflows (id, organization_id, kind, company_id, allow_self_approval, set_up_by)
  values (wf_all, org, 'pay_run', null, false, amina), (wf_a, org, 'pay_run', co_a, true, amina);
  insert into workflow_steps (organization_id, workflow_id, position, approvers, permission_key, fallback_permission_key)
  values (org, wf_all, 1, 'permission', 'pay_run_approvals.manage', 'companies.manage'),
         (org, wf_a, 1, 'permission', 'pay_run_approvals.manage', 'companies.manage');
  begin
    insert into workflows (id, organization_id, kind, company_id, set_up_by) values (uuidv7(), org, 'pay_run', null, amina);
    raise exception 'two active all-company pay run workflows';
  exception when unique_violation then null;
  end;
  begin
    insert into workflows (id, organization_id, kind, company_id, set_up_by) values (uuidv7(), org, 'pay_run', co_a, amina);
    raise exception 'two active Westlands pay run workflows';
  exception when unique_violation then null;
  end;
  begin
    insert into workflow_steps (organization_id, workflow_id, position, approvers) values (org, wf_all, 2, 'permission');
    raise exception 'a permission step without its permission';
  exception when check_violation then null;
  end;
  begin
    insert into workflow_steps (organization_id, workflow_id, position, approvers, permission_key) values (org, wf_all, 1, 'permission', 'team.manage');
    raise exception 'two steps in the same place';
  exception when unique_violation then null;
  end;
  begin
    insert into workflows (id, organization_id, kind, set_up_by) values (uuidv7(), org, 'expenses_xyz', amina);
    raise exception 'a workflow for an unknown kind';
  exception when foreign_key_violation then null;
  end;
  -- Named people must be members of this organization.
  insert into workflow_steps (id, organization_id, workflow_id, position, approvers) values ('00000000-0000-7000-8000-0000000d1150', org, wf_a, 2, 'people');
  insert into workflow_step_people (organization_id, step_id, membership_id) values (org, '00000000-0000-7000-8000-0000000d1150', john_m);
  reset role;
  begin
    insert into workflow_step_people (organization_id, step_id, membership_id)
    select other_org, '00000000-0000-7000-8000-0000000d1150', id from memberships where organization_id = other_org limit 1;
    raise exception 'a step names a member of another organization';
  exception when foreign_key_violation then null;
  end;

  -- Scoped to Mombasa: the all-company workflow, not Westlands'.
  perform set_config('app.company_ids', co_b::text, true);
  set local role pesaroll_app;
  if array(select id from workflows) is distinct from array[wf_all] then
    raise exception 'someone scoped to Mombasa sees %', array(select id from workflows);
  end if;
  if exists (select 1 from workflow_steps where workflow_id = wf_a) then
    raise exception 'someone scoped to Mombasa sees Westlands'' steps';
  end if;
  -- Another organization sees none of it.
  perform set_config('app.org_id', other_org::text, true);
  perform set_config('app.company_ids', '00000000-0000-7000-8000-0000000000b1', true);
  if exists (select 1 from workflows where organization_id = org) or exists (select 1 from workflow_steps where organization_id = org) then
    raise exception 'another organization sees the workflows';
  end if;
  reset role;

  -- A request at Westlands, sent by John, for Mary to decide.
  perform set_config('app.org_id', org::text, true);
  perform set_config('app.company_ids', co_a || ',' || co_b, true);
  set local role pesaroll_app;
  insert into approval_requests (id, organization_id, company_id, kind, subject_id, workflow_id, sent_by)
  values (req, org, co_a, 'pay_run', uuidv7(), wf_a, john);
  insert into approval_request_approvers (organization_id, company_id, request_id, step, user_id) values (org, co_a, req, 1, mary);
  insert into approval_actions (organization_id, company_id, request_id, step, action, by_user, reference) values (org, co_a, req, 1, 'sent', john, 'REF00001');
  if (select status from approval_requests where id = req) <> 'submitted' then raise exception 'a new request is not submitted'; end if;
  begin
    insert into approval_requests (id, organization_id, company_id, kind, subject_id, workflow_id, sent_by)
    select uuidv7(), org, co_a, 'pay_run', subject_id, wf_a, john from approval_requests where id = req;
    raise exception 'two open requests for the same pay run';
  exception when unique_violation then null;
  end;

  -- History can't be changed or deleted by the app; the request can't be deleted.
  begin
    update approval_actions set reason = 'edited' where request_id = req;
    raise exception 'the app changed approval history';
  exception when insufficient_privilege then null;
  end;
  begin
    delete from approval_actions where request_id = req;
    raise exception 'the app deleted approval history';
  exception when insufficient_privilege then null;
  end;
  begin
    delete from approval_requests where id = req;
    raise exception 'the app deleted a request';
  exception when insufficient_privilege then null;
  end;

  -- Decided once; then it can't change.
  update approval_requests set status = 'approved', decided_by = mary, decided_at = now() where id = req;
  begin
    update approval_requests set status = 'declined', decline_reason = 'late' where id = req;
    raise exception 'a decided request changed';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'WF-5003' then raise exception 'expected WF-5003, got %', msg; end if;
  end;

  -- Scoped to Mombasa: Westlands' request and its history are hidden.
  perform set_config('app.company_ids', co_b::text, true);
  if exists (select 1 from approval_requests where id = req) or exists (select 1 from approval_actions where request_id = req)
     or exists (select 1 from approval_request_approvers where request_id = req) then
    raise exception 'someone scoped to Mombasa sees a Westlands request';
  end if;
  reset role;

  -- Even the owner can't rewrite history.
  begin
    update approval_actions set reason = 'edited' where request_id = req;
    raise exception 'approval history was changed';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'WF-5006' then raise exception 'expected WF-5006, got %', msg; end if;
  end;

  -- Line manager: John's manager is Mary's employee record, linked to her sign-in.
  insert into pay_schedules (id, organization_id, name, all_companies) values (uuidv7(), org, 'WF monthly', true) returning id into sched;
  insert into leave_plans (id, organization_id, name, all_companies) values (uuidv7(), org, 'WF leave', true) returning id into plan;
  insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date, membership_id)
  values (mary_e, org, co_a, 'WF-1', 'Mary', 'Wanjiku', date '2024-01-01', mary_m),
         (john_e, org, co_a, 'WF-2', 'John', 'Kamau', date '2024-01-01', null);
  insert into employments (id, organization_id, company_id, employee_id, valid_from, pay_schedule_id, leave_plan_id, employment_type, line_manager_id)
  values (uuidv7(), org, co_a, john_e, date '2024-01-01', sched, plan, 'permanent', mary_e);
  begin
    insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date, membership_id)
    values (uuidv7(), org, co_b, 'WF-3', 'Mary', 'Again', date '2024-01-01', mary_m);
    raise exception 'one sign-in linked to two employees';
  exception when unique_violation then null;
  end;
  perform set_config('app.company_ids', co_a::text, true);
  set local role pesaroll_app;
  if line_manager_user(john_e, date '2026-10-25') is distinct from mary then
    raise exception 'John''s line manager is %, not Mary', line_manager_user(john_e, date '2026-10-25');
  end if;
  if line_manager_user(mary_e, date '2026-10-25') is not null then
    raise exception 'Mary has no line manager';
  end if;
  reset role;
  update memberships set status = 'removed' where id = mary_m;
  set local role pesaroll_app;
  if line_manager_user(john_e, date '2026-10-25') is not null then
    raise exception 'a removed member is still a line manager who can approve';
  end if;
  reset role;
end
$$;
rollback;
