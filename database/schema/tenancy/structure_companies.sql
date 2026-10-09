-- Which companies use a division, department, position or unit that isn't for all companies (ONB-05).
create table division_companies (
    organization_id  uuid not null,
    division_id      uuid not null,
    company_id       uuid not null,
    created_at       timestamptz not null default now(),
    primary key (division_id, company_id),
    foreign key (organization_id, division_id) references divisions (organization_id, id) on delete cascade,
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade
);

create table department_companies (
    organization_id  uuid not null,
    department_id    uuid not null,
    company_id       uuid not null,
    created_at       timestamptz not null default now(),
    primary key (department_id, company_id),
    foreign key (organization_id, department_id) references departments (organization_id, id) on delete cascade,
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade
);

create table position_companies (
    organization_id  uuid not null,
    position_id      uuid not null,
    company_id       uuid not null,
    created_at       timestamptz not null default now(),
    primary key (position_id, company_id),
    foreign key (organization_id, position_id) references positions (organization_id, id) on delete cascade,
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade
);

create table unit_companies (
    organization_id  uuid not null,
    unit_id          uuid not null,
    company_id       uuid not null,
    created_at       timestamptz not null default now(),
    primary key (unit_id, company_id),
    foreign key (organization_id, unit_id) references units (organization_id, id) on delete cascade,
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade
);

alter table division_companies enable row level security;
alter table department_companies enable row level security;
alter table position_companies enable row level security;
alter table unit_companies enable row level security;

create policy division_companies_tenant on division_companies using (organization_id = app_org_id());
create policy department_companies_tenant on department_companies using (organization_id = app_org_id());
create policy position_companies_tenant on position_companies using (organization_id = app_org_id());
create policy unit_companies_tenant on unit_companies using (organization_id = app_org_id());

create policy division_companies_scope on division_companies as restrictive using (company_id = any (app_company_ids()));
create policy department_companies_scope on department_companies as restrictive using (company_id = any (app_company_ids()));
create policy position_companies_scope on position_companies as restrictive using (company_id = any (app_company_ids()));
create policy unit_companies_scope on unit_companies as restrictive using (company_id = any (app_company_ids()));

grant select, insert, delete on table division_companies, department_companies, position_companies, unit_companies to pesaroll_app;

-- On the structure tables, here because they read the links. People limited to some companies see
-- what those companies use. Only for reading: a new row has no links yet when it is written.
create policy divisions_scope on divisions as restrictive for select
    using (all_companies or exists (select 1 from division_companies x where x.division_id = divisions.id and x.company_id = any (app_company_ids())));
create policy departments_scope on departments as restrictive for select
    using (all_companies or exists (select 1 from department_companies x where x.department_id = departments.id and x.company_id = any (app_company_ids())));
create policy positions_scope on positions as restrictive for select
    using (all_companies or exists (select 1 from position_companies x where x.position_id = positions.id and x.company_id = any (app_company_ids())));
create policy units_scope on units as restrictive for select
    using (all_companies or exists (select 1 from unit_companies x where x.unit_id = units.id and x.company_id = any (app_company_ids())));
