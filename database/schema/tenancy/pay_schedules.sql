-- How often and when people are paid (ONB-06). A schedule belongs to the organization and serves
-- all its companies (all_companies) or the ones in pay_schedule_companies. Monthly for now; weekly
-- and daily come with shift work in Release 3. The pay date comes from pay_day_rule: the last
-- working day, the last day of the month, or a fixed day (pay_day), moved back to the Friday when it
-- falls on a weekend (the API works the dates out; pay runs, PAY-03, use them). Changes after the
-- cut-off day roll into the next month. Each company points at its default schedule
-- (companies.default_pay_schedule_id), which employees get unless told otherwise.
create table pay_schedules (
    id               uuid primary key,
    organization_id  uuid not null references organizations (id),
    name             text not null check (char_length(btrim(name)) between 2 and 60),
    frequency        text not null default 'monthly' check (frequency in ('monthly')),
    pay_day_rule     text not null default 'last_working_day'
                     check (pay_day_rule in ('last_working_day', 'last_day', 'fixed_day')),
    pay_day          integer check (pay_day between 1 and 28),   -- only for a fixed day
    cutoff_day       integer not null default 25 check (cutoff_day between 1 and 28),
    all_companies    boolean not null default false,             -- also companies added later
    status           text not null default 'active' check (status in ('active', 'archived')),
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (organization_id, id),
    check ((pay_day_rule = 'fixed_day') = (pay_day is not null))
);

-- Names are unique in the organization, whatever the case or spacing.
create unique index pay_schedules_unique_name on pay_schedules (organization_id, lower(btrim(name))) where status = 'active';

alter table pay_schedules enable row level security;

create policy pay_schedules_tenant on pay_schedules
    using (organization_id = app_org_id());

grant select, insert, update, delete on table pay_schedules to pesaroll_app;
