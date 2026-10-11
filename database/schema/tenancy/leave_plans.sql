-- Leave types and plans (ONB-08). Types belong to the organization (all its companies); statutory
-- ones (locked_key) come with the first company and can't be deleted or renamed by the app. A plan
-- bundles one rule per type, serves all companies or the ones in leave_plan_companies, and is the
-- default of companies (companies.default_leave_plan_id) and of pay schedules
-- (pay_schedules.default_leave_plan_id, which comes first for people on that schedule).
create table leave_types (
    id               uuid primary key,
    organization_id  uuid not null references organizations (id),
    code             text not null check (code ~ '^[A-Z0-9][A-Z0-9-]{1,19}$'),
    name             text not null check (char_length(btrim(name)) between 2 and 80),
    counts           text not null default 'working_days' check (counts in ('working_days', 'calendar_days')),
    paid             boolean not null default true,
    needs_proof      boolean not null default false,          -- a doctor's note, a birth or adoption document
    when_used_up     text not null default 'stop' check (when_used_up in ('stop', 'unpaid')),
    locked_key       text,
    country_code     char(2),
    status           text not null default 'active' check (status in ('active', 'archived')),
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (organization_id, id),
    foreign key (country_code, locked_key) references statutory_leave (country_code, key),
    check ((locked_key is null) = (country_code is null))
);

create unique index leave_types_unique_code on leave_types (organization_id, code) where status = 'active';
create unique index leave_types_one_locked on leave_types (organization_id, locked_key, country_code) where locked_key is not null;

alter table leave_types enable row level security;

create policy leave_types_tenant on leave_types
    using (organization_id = app_org_id());

grant select, insert, update, delete on table leave_types to pesaroll_app;

-- The app can't delete statutory leave types or change what makes them statutory; deploys can.
create or replace function leave_types_keep_locked()
returns trigger
language plpgsql
as $$
begin
    if old.locked_key is not null and current_user = 'pesaroll_app'
       and (tg_op = 'DELETE' or (new.code, new.name, new.counts, new.paid, new.locked_key, new.country_code, new.status)
                                 is distinct from (old.code, old.name, old.counts, old.paid, old.locked_key, old.country_code, old.status)) then
        raise exception 'ONB-3535';
    end if;
    return case when tg_op = 'DELETE' then old else new end;
end
$$;

create trigger leave_types_keep_locked before update or delete on leave_types
    for each row execute function leave_types_keep_locked();

create table leave_plans (
    id               uuid primary key,
    organization_id  uuid not null references organizations (id),
    name             text not null check (char_length(btrim(name)) between 2 and 80),
    leave_year       text not null default 'calendar' check (leave_year in ('calendar', 'anniversary')),
    hours_per_day    numeric(4, 2) not null default 8 check (hours_per_day between 1 and 24),   -- turns days into hours and back
    all_companies    boolean not null default false,
    status           text not null default 'active' check (status in ('active', 'archived')),
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (organization_id, id)
);

create unique index leave_plans_unique_name on leave_plans (organization_id, lower(btrim(name))) where status = 'active';

alter table leave_plans enable row level security;

create policy leave_plans_tenant on leave_plans
    using (organization_id = app_org_id());

grant select, insert, update, delete on table leave_plans to pesaroll_app;
