-- How the organization is organised (ONB-05): divisions (optional grouping, often across companies),
-- departments (optionally in a division and under a parent department), positions (the jobs people
-- are hired into) and units (teams in a department). Each serves all the organization's companies
-- or the chosen ones (the *_companies tables). Codes are unique per level in the organization.

create table divisions (
    id               uuid primary key,
    organization_id  uuid not null references organizations (id),
    code             text not null check (code ~ '^[A-Z0-9][A-Z0-9-]{1,19}$'),
    name             text not null check (char_length(btrim(name)) between 2 and 120),
    all_companies    boolean not null default false,
    status           text not null default 'active' check (status in ('active', 'archived')),
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (organization_id, id)
);

create unique index divisions_unique_code on divisions (organization_id, code) where status = 'active';

create table departments (
    id               uuid primary key,
    organization_id  uuid not null references organizations (id),
    code             text not null check (code ~ '^[A-Z0-9][A-Z0-9-]{1,19}$'),
    name             text not null check (char_length(btrim(name)) between 2 and 120),
    division_id      uuid,
    parent_id        uuid check (parent_id <> id),              -- builds a tree: Kitchen under Operations
    cost_centre      text check (char_length(cost_centre) <= 40), -- kept for accounting exports later
    all_companies    boolean not null default false,
    status           text not null default 'active' check (status in ('active', 'archived')),
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (organization_id, id),
    foreign key (organization_id, division_id) references divisions (organization_id, id),
    foreign key (organization_id, parent_id) references departments (organization_id, id)
);

create unique index departments_unique_code on departments (organization_id, code) where status = 'active';

create table positions (
    id               uuid primary key,
    organization_id  uuid not null references organizations (id),
    code             text not null check (code ~ '^[A-Z0-9][A-Z0-9-]{1,19}$'),
    title            text not null check (char_length(btrim(title)) between 2 and 120),
    department_id    uuid,                                       -- none: shared across departments
    grade            text check (char_length(grade) <= 20),       -- G4, 3, Senior: approvals and pay bands later
    all_companies    boolean not null default false,
    status           text not null default 'active' check (status in ('active', 'archived')),
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (organization_id, id),
    foreign key (organization_id, department_id) references departments (organization_id, id)
);

create unique index positions_unique_code on positions (organization_id, code) where status = 'active';

create table units (
    id               uuid primary key,
    organization_id  uuid not null references organizations (id),
    code             text not null check (code ~ '^[A-Z0-9][A-Z0-9-]{1,19}$'),
    name             text not null check (char_length(btrim(name)) between 2 and 120),
    department_id    uuid not null,
    all_companies    boolean not null default false,
    status           text not null default 'active' check (status in ('active', 'archived')),
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (organization_id, id),
    foreign key (organization_id, department_id) references departments (organization_id, id)
);

create unique index units_unique_code on units (organization_id, code) where status = 'active';

-- The department tree has no loops: a department can't sit under itself or anything below it. It
-- reads every department of the organization (security definer), so a loop through departments the
-- person can't see is still caught.
create function check_department_parent() returns trigger
    language plpgsql security definer set search_path = public
as $$
declare
    ancestor uuid := new.parent_id;
    steps int := 0;
begin
    while ancestor is not null loop
        if ancestor = new.id then
            raise exception 'ONB-3523' using errcode = 'check_violation';
        end if;
        steps := steps + 1;
        if steps > 100 then
            raise exception 'ONB-3523' using errcode = 'check_violation';
        end if;
        select d.parent_id into ancestor from departments d where d.id = ancestor and d.organization_id = new.organization_id;
    end loop;
    return new;
end;
$$;

create trigger departments_no_loops before insert or update of parent_id on departments
    for each row execute function check_department_parent();

alter table divisions enable row level security;
alter table departments enable row level security;
alter table positions enable row level security;
alter table units enable row level security;

create policy divisions_tenant on divisions using (organization_id = app_org_id());
create policy departments_tenant on departments using (organization_id = app_org_id());
create policy positions_tenant on positions using (organization_id = app_org_id());
create policy units_tenant on units using (organization_id = app_org_id());

grant select, insert, update, delete on table divisions, departments, positions, units to pesaroll_app;
