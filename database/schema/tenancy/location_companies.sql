-- Which companies use a location that isn't for all companies (ONB-04).
create table location_companies (
    organization_id  uuid not null,
    location_id      uuid not null,
    company_id       uuid not null,
    created_at       timestamptz not null default now(),
    primary key (location_id, company_id),
    foreign key (organization_id, location_id) references locations (organization_id, id) on delete cascade,
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade
);

alter table location_companies enable row level security;

create policy location_companies_tenant on location_companies
    using (organization_id = app_org_id());

create policy location_companies_scope on location_companies as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert, delete on table location_companies to pesaroll_app;

-- On locations, here because it reads location_companies.
-- People limited to some companies see the locations those companies use. Only for reading: a new
-- location has no links yet when it is written (the API checks who may write).
create policy locations_scope on locations as restrictive for select
    using (all_companies or exists (
        select 1 from location_companies lc where lc.location_id = locations.id and lc.company_id = any (app_company_ids())));
