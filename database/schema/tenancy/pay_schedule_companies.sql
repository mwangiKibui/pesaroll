-- Which companies use a schedule that isn't for all companies (ONB-06).
create table pay_schedule_companies (
    organization_id  uuid not null,
    pay_schedule_id  uuid not null,
    company_id       uuid not null,
    created_at       timestamptz not null default now(),
    primary key (pay_schedule_id, company_id),
    foreign key (organization_id, pay_schedule_id) references pay_schedules (organization_id, id) on delete cascade,
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade
);

alter table pay_schedule_companies enable row level security;

create policy pay_schedule_companies_tenant on pay_schedule_companies
    using (organization_id = app_org_id());

create policy pay_schedule_companies_scope on pay_schedule_companies as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert, delete on table pay_schedule_companies to pesaroll_app;

-- On pay_schedules, here because it reads pay_schedule_companies.
-- People limited to some companies see the schedules those companies use. Only for reading: a new
-- schedule has no links yet when it is written (the API checks who may write).
create policy pay_schedules_scope on pay_schedules as restrictive for select
    using (all_companies or exists (
        select 1 from pay_schedule_companies sc where sc.pay_schedule_id = pay_schedules.id and sc.company_id = any (app_company_ids())));
