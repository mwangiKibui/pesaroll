-- Approval workflows and requests (WF-01..03). An organization sets up a workflow per kind
-- (workflow_kinds) for all its companies (company_id null) or one company, whose own replaces the
-- all-company one there. A workflow is a list of steps, each approved in order. Changing a workflow
-- makes a new one and switches the old off, so a request always follows the steps it was sent with.
--   approvers  permission    active members holding permission_key for the request's company
--              people        the members in workflow_step_people
--              line_manager  the line manager of the employee the request is about, by their sign-in
-- When a step finds nobody, fallback_permission_key's holders approve it. The sender is never an
-- approver unless allow_self_approval, an admin's explicit choice (small teams with one accountant).
create table workflows (
    id                   uuid primary key,
    organization_id      uuid not null references organizations (id),
    kind                 text not null references workflow_kinds (key),
    company_id           uuid,                                            -- null: every company
    allow_self_approval  boolean not null default false,
    active               boolean not null default true,
    set_up_by            uuid not null references users (id),
    created_at           timestamptz not null default now(),
    switched_off_at      timestamptz,
    unique (organization_id, id),
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade,
    check (active = (switched_off_at is null))
);

-- One active workflow per kind for every company, and per kind for each company.
create unique index workflows_one_for_all on workflows (organization_id, kind) where active and company_id is null;
create unique index workflows_one_per_company on workflows (organization_id, kind, company_id) where active and company_id is not null;

alter table workflows enable row level security;

create policy workflows_tenant on workflows
    using (organization_id = app_org_id());

create policy workflows_scope on workflows as restrictive
    using (company_id is null or company_id = any (app_company_ids()));

grant select, insert, update on table workflows to pesaroll_app;

create table workflow_steps (
    id                       uuid primary key default uuidv7(),
    organization_id          uuid not null,
    workflow_id              uuid not null,
    position                 int not null check (position between 1 and 5),
    approvers                text not null check (approvers in ('permission', 'people', 'line_manager')),
    permission_key           text references permissions (key),
    fallback_permission_key  text references permissions (key),
    unique (organization_id, id),
    unique (workflow_id, position),
    foreign key (organization_id, workflow_id) references workflows (organization_id, id) on delete cascade,
    check ((approvers = 'permission') = (permission_key is not null))
);

alter table workflow_steps enable row level security;

create policy workflow_steps_tenant on workflow_steps
    using (organization_id = app_org_id());

create policy workflow_steps_scope on workflow_steps as restrictive
    using (exists (select 1 from workflows w where w.id = workflow_id));

grant select, insert on table workflow_steps to pesaroll_app;

create table workflow_step_people (
    organization_id  uuid not null,
    step_id          uuid not null,
    membership_id    uuid not null,
    primary key (step_id, membership_id),
    foreign key (organization_id, step_id) references workflow_steps (organization_id, id) on delete cascade,
    foreign key (organization_id, membership_id) references memberships (organization_id, id) on delete cascade
);

alter table workflow_step_people enable row level security;

create policy workflow_step_people_tenant on workflow_step_people
    using (organization_id = app_org_id());

create policy workflow_step_people_scope on workflow_step_people as restrictive
    using (exists (select 1 from workflow_steps s where s.id = step_id));

grant select, insert on table workflow_step_people to pesaroll_app;

-- Something sent for approval: what (kind, subject_id: the pay run, the leave request), at which
-- company, about whom (employee_id, for line managers), by whom, and where it is. Once approved,
-- declined or cancelled it never changes (WF-5003), and the app can't delete it.
create table approval_requests (
    id               uuid primary key,
    organization_id  uuid not null,
    company_id       uuid not null,
    kind             text not null references workflow_kinds (key),
    subject_id       uuid not null,
    employee_id      uuid,
    workflow_id      uuid not null,
    status           text not null default 'submitted' check (status in ('submitted', 'approved', 'declined', 'cancelled')),
    current_step     int not null default 1,
    sent_by          uuid not null references users (id),
    sent_at          timestamptz not null default now(),
    decided_by       uuid references users (id),
    decided_at       timestamptz,
    decline_reason   text check (char_length(btrim(decline_reason)) between 1 and 500),
    unique (company_id, id),
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade,
    foreign key (organization_id, workflow_id) references workflows (organization_id, id),
    foreign key (organization_id, employee_id) references employees (organization_id, id),
    check ((status = 'submitted') = (decided_at is null)),
    check ((status = 'declined') = (decline_reason is not null))
);

-- One open request per subject.
create unique index approval_requests_one_open on approval_requests (kind, subject_id) where status = 'submitted';
create index approval_requests_company on approval_requests (company_id, status);

alter table approval_requests enable row level security;

create policy approval_requests_tenant on approval_requests
    using (organization_id = app_org_id());

create policy approval_requests_scope on approval_requests as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert, update on table approval_requests to pesaroll_app;

create or replace function approval_requests_keep_decided()
returns trigger
language plpgsql
as $$
begin
    if old.status <> 'submitted' then
        raise exception 'WF-5003';
    end if;
    return new;
end
$$;

create trigger approval_requests_keep_decided before update on approval_requests
    for each row execute function approval_requests_keep_decided();

-- Who can decide each step of a request, worked out when the step starts and never changed after.
create table approval_request_approvers (
    organization_id  uuid not null,
    company_id       uuid not null,
    request_id       uuid not null,
    step             int not null,
    user_id          uuid not null references users (id),
    via_fallback     boolean not null default false,
    primary key (request_id, step, user_id),
    foreign key (company_id, request_id) references approval_requests (company_id, id) on delete cascade
);

create index approval_request_approvers_user on approval_request_approvers (user_id);

alter table approval_request_approvers enable row level security;

create policy approval_request_approvers_tenant on approval_request_approvers
    using (organization_id = app_org_id());

create policy approval_request_approvers_scope on approval_request_approvers as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert on table approval_request_approvers to pesaroll_app;

-- Everything done to a request, in order: the approvals' audit trail (PLAT-03 folds it into the
-- audit log). The app only adds rows; nobody changes one (WF-5006).
create table approval_actions (
    id               uuid primary key default uuidv7(),
    organization_id  uuid not null,
    company_id       uuid not null,
    request_id       uuid not null,
    step             int,
    action           text not null check (action in ('sent', 'approved', 'self_approved', 'declined', 'cancelled')),
    by_user          uuid not null references users (id),
    at               timestamptz not null default now(),
    reason           text check (char_length(btrim(reason)) between 1 and 500),
    reference        text,                                                -- the request reference, for support
    foreign key (company_id, request_id) references approval_requests (company_id, id) on delete cascade
);

create index approval_actions_request on approval_actions (request_id, at);

alter table approval_actions enable row level security;

create policy approval_actions_tenant on approval_actions
    using (organization_id = app_org_id());

create policy approval_actions_scope on approval_actions as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert on table approval_actions to pesaroll_app;

create or replace function approval_actions_keep()
returns trigger
language plpgsql
as $$
begin
    raise exception 'WF-5006';
end
$$;

create trigger approval_actions_keep before update on approval_actions
    for each row execute function approval_actions_keep();

-- The active members of the session's organization who hold a permission for a company (WF-02).
create or replace function members_with_permission(p_company_id uuid, p_permission text)
returns setof uuid
language sql stable security definer set search_path = public
as $$
    select m.user_id
    from memberships m
    where m.organization_id = app_org_id() and m.status = 'active'
      and exists (select 1 from member_grants(m.user_id, m.organization_id) g
                  where g.permission_key = p_permission and (g.all_companies or p_company_id = any (g.company_ids)))
$$;

-- The sign-in of an employee's line manager on a date, if the manager is an active member (WF-02).
-- PL/pgSQL, not SQL: its body is checked when it runs, not when it is created, because pgschema
-- creates functions before adding columns (employees.membership_id) on an existing database.
create or replace function line_manager_user(p_employee_id uuid, p_on date)
returns uuid
language plpgsql stable security definer set search_path = public
as $$
begin
    return (
        select m.user_id
        from employments e
        join employees mgr on mgr.id = e.line_manager_id and mgr.organization_id = e.organization_id
        join memberships m on m.id = mgr.membership_id and m.organization_id = mgr.organization_id and m.status = 'active'
        where e.employee_id = p_employee_id and e.organization_id = app_org_id()
          and e.valid_from <= p_on and (e.valid_to is null or e.valid_to >= p_on)
        order by e.valid_from desc
        limit 1);
end
$$;

revoke all on function members_with_permission(uuid, text) from public;
revoke all on function line_manager_user(uuid, date) from public;
grant execute on function members_with_permission(uuid, text) to pesaroll_app;
grant execute on function line_manager_user(uuid, date) to pesaroll_app;
