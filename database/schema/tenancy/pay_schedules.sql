-- How often and when people are paid (ONB-06). A schedule belongs to the organization and serves
-- all its companies (all_companies) or the ones in pay_schedule_companies.
--   monthly      on pay_day_rule: the last working day, the last day of the month, or a fixed day
--                (pay_day), moved back to the Friday when it falls on a weekend; changes after the
--                cut-off day of the month roll into the next month
--   weekly       every pay_weekday (Monday 1 to Friday 5); cut-off cutoff_days_before the pay day
--   fortnightly  every other pay_weekday, counted from first_pay_date; cut-off as weekly
--   daily        every working day at pay_time (reminders count hours back from it); no cut-off
-- The cut-off is optional (uses_cutoff): when used, changes after it in what cutoff_applies_to
-- lists (overtime, new employees… or the organization's own items) go into the next payroll;
-- without one, everything up to the pay run counts. Daily pay has none.
-- The API works the dates out; pay runs (PAY-03) use them. Each company points at its default
-- schedule (companies.default_pay_schedule_id), which employees get unless told otherwise.
create table pay_schedules (
    id                  uuid primary key,
    organization_id     uuid not null references organizations (id),
    name                text not null check (char_length(btrim(name)) between 2 and 60),
    frequency           text not null default 'monthly' check (frequency in ('monthly', 'weekly', 'fortnightly', 'daily')),
    pay_day_rule        text default 'last_working_day'
                        check (pay_day_rule in ('last_working_day', 'last_day', 'fixed_day')),
    pay_day             integer check (pay_day between 1 and 28),          -- monthly, only for a fixed day
    cutoff_day          integer default 25 check (cutoff_day between 1 and 28),   -- monthly
    pay_weekday         integer check (pay_weekday between 1 and 5),       -- weekly and fortnightly (ISO: Monday 1)
    first_pay_date      date,                                              -- fortnightly
    cutoff_days_before  integer check (cutoff_days_before between 0 and 13),   -- weekly and fortnightly
    pay_time            time,                                              -- daily
    uses_cutoff         boolean not null default true,
    cutoff_applies_to   text[] not null default '{overtime,new_employees,leavers,unpaid_leave,pay_changes,one_off}',   -- or the organization's own items
    all_companies       boolean not null default false,                    -- also companies added later
    status              text not null default 'active' check (status in ('active', 'archived')),
    created_at          timestamptz not null default now(),
    updated_at          timestamptz not null default now(),
    unique (organization_id, id),
    check ((pay_day_rule = 'fixed_day') = (pay_day is not null)),
    -- A cut-off covers at least one thing; without one, it covers nothing.
    check (uses_cutoff = (cardinality(cutoff_applies_to) > 0)),
    -- Each frequency has its own fields and none of the others' (a missing field is false, not unknown).
    check (coalesce(case frequency
        when 'monthly' then pay_day_rule is not null and uses_cutoff = (cutoff_day is not null)
            and pay_weekday is null and first_pay_date is null and cutoff_days_before is null and pay_time is null
        when 'weekly' then pay_weekday is not null and uses_cutoff = (cutoff_days_before is not null) and coalesce(cutoff_days_before, 0) <= 6
            and pay_day_rule is null and pay_day is null and cutoff_day is null and first_pay_date is null and pay_time is null
        when 'fortnightly' then pay_weekday is not null and uses_cutoff = (cutoff_days_before is not null)
            and first_pay_date is not null and extract(isodow from first_pay_date) = pay_weekday
            and pay_day_rule is null and pay_day is null and cutoff_day is null and pay_time is null
        when 'daily' then pay_time is not null and not uses_cutoff
            and pay_day_rule is null and pay_day is null and cutoff_day is null and pay_weekday is null
            and first_pay_date is null and cutoff_days_before is null
    end, false))
);

-- Names are unique in the organization, whatever the case or spacing.
create unique index pay_schedules_unique_name on pay_schedules (organization_id, lower(btrim(name))) where status = 'active';

alter table pay_schedules enable row level security;

create policy pay_schedules_tenant on pay_schedules
    using (organization_id = app_org_id());

grant select, insert, update, delete on table pay_schedules to pesaroll_app;
