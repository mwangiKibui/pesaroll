-- ACC-01, ACC-02, ACC-06: who may do what, and for which companies.
-- Permissions are the actions the app checks, by key (defined in code, listed here). Roles bundle
-- permissions; system roles (Administrator, HR, Payroll, Approver) have no organization. A security
-- group belongs to an organization and carries roles for all its companies or chosen ones; people
-- join groups through their membership. A person's access is everything their current groups give,
-- each for the group's companies. Permissions and system roles are loaded by
-- post-deploy/0014_access_roles.sql.

-- ACC-04: permissions come in areas (Employees, Locations, ...), each with View and Manage; Manage
-- brings View (member_grants). Organizations choose a level per area for each group.
create table permission_areas (
    key         text primary key check (key ~ '^[a-z_]+$'),
    name_en     text not null,
    name_sw     text not null,
    sort_order  integer not null
);

create table permissions (
    key          text primary key check (key ~ '^[a-z_]+\.[a-z_]+$'),
    description  text not null,
    area         text not null references permission_areas (key) on delete cascade,
    level        text not null check (level in ('view', 'manage')),
    check (key = area || '.' || level)
);

create table roles (
    id               uuid primary key,                -- fixed ids for system roles, the same in every environment
    organization_id  uuid references organizations (id) on delete cascade,   -- null: a system role (an organization's own come with ACC-08)
    key              text not null check (key ~ '^[a-z_]+$'),
    name_en          text not null,
    name_sw          text not null
);

create unique index roles_one_system_key on roles (key) where organization_id is null;

create table role_permissions (
    role_id         uuid not null references roles (id) on delete cascade,
    permission_key  text not null references permissions (key) on delete cascade,
    primary key (role_id, permission_key)
);

create table security_groups (
    id               uuid primary key,
    organization_id  uuid not null references organizations (id) on delete cascade,
    name             text not null check (char_length(name) between 1 and 120),
    description      text check (char_length(description) <= 300),
    default_key      text check (default_key in ('administrators', 'hr', 'payroll', 'approvers')),   -- the groups every organization starts with
    all_companies    boolean not null default true,   -- else security_group_companies
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (organization_id, id)
);

create unique index security_groups_one_default on security_groups (organization_id, default_key) where default_key is not null;
create unique index security_groups_one_name on security_groups (organization_id, lower(name));

create table security_group_companies (
    organization_id    uuid not null,
    security_group_id  uuid not null,
    company_id         uuid not null,
    primary key (security_group_id, company_id),
    foreign key (organization_id, security_group_id) references security_groups (organization_id, id) on delete cascade,
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade
);

create table security_group_roles (
    organization_id    uuid not null,
    security_group_id  uuid not null,
    role_id            uuid not null references roles (id),
    primary key (security_group_id, role_id),
    foreign key (organization_id, security_group_id) references security_groups (organization_id, id) on delete cascade
);

-- Who is in a group, over time: one current row per person and group (removed_at empty); leaving
-- closes it, so who had access when stays known.
create table security_group_members (
    id                 uuid primary key,
    organization_id    uuid not null,
    security_group_id  uuid not null,
    membership_id      uuid not null references memberships (id) on delete cascade,
    added_by           uuid references users (id),
    added_at           timestamptz not null default now(),
    removed_by         uuid references users (id),
    removed_at         timestamptz,
    foreign key (organization_id, security_group_id) references security_groups (organization_id, id) on delete cascade,
    check (removed_at is null or removed_at >= added_at)
);

create unique index security_group_members_one_current on security_group_members (security_group_id, membership_id) where removed_at is null;
create index security_group_members_membership on security_group_members (membership_id) where removed_at is null;

alter table security_groups enable row level security;
alter table security_group_companies enable row level security;
alter table security_group_roles enable row level security;
alter table security_group_members enable row level security;

create policy security_groups_tenant on security_groups using (organization_id = app_org_id());
create policy security_group_companies_tenant on security_group_companies using (organization_id = app_org_id());
create policy security_group_roles_tenant on security_group_roles using (organization_id = app_org_id());
create policy security_group_members_tenant on security_group_members using (organization_id = app_org_id());

grant select on table permission_areas to pesaroll_app;
grant select on table permissions to pesaroll_app;
grant select on table roles to pesaroll_app;
grant select on table role_permissions to pesaroll_app;
-- Changing groups and their members comes with Team access (ONB-11); read-only until then.
grant select on table security_groups to pesaroll_app;
grant select on table security_group_companies to pesaroll_app;
grant select on table security_group_roles to pesaroll_app;
grant select on table security_group_members to pesaroll_app;

-- ACC-06: the groups every organization starts with, all for all companies: Administrators
-- (everything), HR, Payroll and Approvers, each carrying the system role of the same name. The
-- person who onboarded it joins Administrators. Safe to run again: what exists is kept.
create or replace function create_default_groups(p_organization_id uuid, p_admin_user_id uuid)
returns void
language plpgsql security definer set search_path = public
as $$
declare
    d record;
    v_group uuid;
begin
    for d in select * from (values
        ('administrators', 'Administrators', 'Everything, for every company.', 'admin'),
        ('hr',             'HR',             'People, structure, locations and leave.', 'hr'),
        ('payroll',        'Payroll',        'Earnings and deductions, payees, payroll schedules and payroll history.', 'payroll'),
        ('approvers',      'Approvers',      'Approve pay runs (with Payroll runs).', 'approver')) as t(default_key, name, description, role_key)
    loop
        select g.id into v_group from security_groups g where g.organization_id = p_organization_id and g.default_key = d.default_key;
        if v_group is null then
            v_group := uuidv7();
            insert into security_groups (id, organization_id, name, description, default_key, all_companies)
            values (v_group, p_organization_id, d.name, d.description, d.default_key, true);
        end if;
        insert into security_group_roles (organization_id, security_group_id, role_id)
        select p_organization_id, v_group, r.id from roles r where r.key = d.role_key and r.organization_id is null
        on conflict do nothing;
    end loop;

    if p_admin_user_id is not null then
        insert into security_group_members (id, organization_id, security_group_id, membership_id, added_by)
        select uuidv7(), p_organization_id, g.id, m.id, p_admin_user_id
        from security_groups g
        join memberships m on m.organization_id = g.organization_id and m.user_id = p_admin_user_id and m.status = 'active'
        where g.organization_id = p_organization_id and g.default_key = 'administrators'
          and not exists (select 1 from security_group_members x where x.security_group_id = g.id and x.membership_id = m.id and x.removed_at is null);
    end if;
end
$$;

-- What a person may do in an organization, per permission: for all companies, or for which. Read at
-- the start of every request (SessionService); only an active member's current groups count.
-- Manage on an area brings View on it, for the same companies.
create or replace function member_grants(p_user_id uuid, p_organization_id uuid)
returns table (permission_key text, all_companies boolean, company_ids uuid[])
language sql stable security definer set search_path = public
as $$
    with groups as (
        select g.id, g.all_companies
        from security_group_members gm
        join memberships m on m.id = gm.membership_id
        join security_groups g on g.id = gm.security_group_id
        where m.user_id = p_user_id and m.organization_id = p_organization_id and m.status = 'active'
          and gm.removed_at is null and g.organization_id = p_organization_id
    )
    , held as (
        select rp.permission_key, g.id as group_id, g.all_companies
        from groups g
        join security_group_roles gr on gr.security_group_id = g.id
        join role_permissions rp on rp.role_id = gr.role_id
    )
    , expanded as (
        select permission_key, group_id, all_companies from held
        union
        select v.key, h.group_id, h.all_companies
        from held h
        join permissions m on m.key = h.permission_key and m.level = 'manage'
        join permissions v on v.area = m.area and v.level = 'view'
    )
    select e.permission_key,
           bool_or(e.all_companies),
           coalesce(array_agg(distinct gc.company_id) filter (where gc.company_id is not null), '{}')
    from expanded e
    left join security_group_companies gc on gc.security_group_id = e.group_id and not e.all_companies
    group by e.permission_key
$$;

revoke all on function create_default_groups(uuid, uuid) from public;
revoke all on function member_grants(uuid, uuid) from public;
grant execute on function member_grants(uuid, uuid) to pesaroll_app;
