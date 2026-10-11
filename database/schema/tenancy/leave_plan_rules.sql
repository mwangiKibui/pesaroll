-- Which companies use a leave plan that isn't for all companies (ONB-08).
create table leave_plan_companies (
    organization_id  uuid not null,
    leave_plan_id    uuid not null,
    company_id       uuid not null,
    created_at       timestamptz not null default now(),
    primary key (leave_plan_id, company_id),
    foreign key (organization_id, leave_plan_id) references leave_plans (organization_id, id) on delete cascade,
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade
);

alter table leave_plan_companies enable row level security;

create policy leave_plan_companies_tenant on leave_plan_companies
    using (organization_id = app_org_id());

create policy leave_plan_companies_scope on leave_plan_companies as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert, delete on table leave_plan_companies to pesaroll_app;

-- On leave_plans, here because it reads leave_plan_companies (only for reading, as elsewhere).
create policy leave_plans_scope on leave_plans as restrictive for select
    using (all_companies or exists (
        select 1 from leave_plan_companies lc where lc.leave_plan_id = leave_plans.id and lc.company_id = any (app_company_ids())));

-- How a plan gives one leave type (ONB-08), for every way people are paid:
--   yearly           a set number each leave year (pro-rated for joiners unless turned off)
--   monthly, weekly  built up each month or week
--   per_day_worked, per_hour_worked   built up by what is worked (daily-paid, shift work)
--   each_time        per occasion (maternity, paternity, compassionate)
--   unlimited        no limit (unpaid leave)
-- amount is in unit (days, or hours through the plan's hours_per_day). half_pay is a second part on
-- half pay (sick leave: 7 + 7). The API keeps plans at or above the law (statutory_leave).
create table leave_plan_rules (
    id               uuid primary key,
    organization_id  uuid not null,
    leave_plan_id    uuid not null,
    leave_type_id    uuid not null,
    earn_method      text not null check (earn_method in ('yearly', 'monthly', 'weekly', 'per_day_worked', 'per_hour_worked', 'each_time', 'unlimited')),
    amount           numeric(9, 4) check (amount > 0),
    unit             text not null default 'days' check (unit in ('days', 'hours')),
    half_pay         numeric(9, 4) not null default 0 check (half_pay >= 0),
    prorate          boolean not null default true,
    carry_over_max   numeric(7, 2) not null default 0 check (carry_over_max >= 0),
    waiting_months   int not null default 0 check (waiting_months between 0 and 24),
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (leave_plan_id, leave_type_id),
    foreign key (organization_id, leave_plan_id) references leave_plans (organization_id, id) on delete cascade,
    foreign key (organization_id, leave_type_id) references leave_types (organization_id, id),   -- a type in a plan stays
    check ((earn_method = 'unlimited') = (amount is null)),
    check (half_pay = 0 or earn_method in ('yearly', 'each_time'))
);

alter table leave_plan_rules enable row level security;

create policy leave_plan_rules_tenant on leave_plan_rules
    using (organization_id = app_org_id());

-- Seen with their plan (leave_plans' policies decide).
create policy leave_plan_rules_scope on leave_plan_rules as restrictive for select
    using (exists (select 1 from leave_plans p where p.id = leave_plan_id));

grant select, insert, update, delete on table leave_plan_rules to pesaroll_app;
