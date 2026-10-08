-- A legal employer inside an organization (ONB-03). Its country (countries) decides its currency,
-- the statutory numbers it records (company_identifiers) and the rule pack its payroll uses.
create table companies (
    id                 uuid primary key,
    organization_id    uuid not null references organizations (id),
    legal_name         text not null check (char_length(btrim(legal_name)) between 2 and 160),
    trading_name       text,
    country_code       char(2) not null references countries (code),
    currency           char(3) not null,
    industry_code      text references industries (code),
    address_line       text,                          -- building and street
    town               text,
    region_code        text,                          -- county, district or region (country_regions)
    postal_address     text,                          -- P.O. Box
    postal_code        text,
    phone              text,
    email              text,
    status             text not null default 'active' check (status in ('active', 'archived')),
    created_at         timestamptz not null default now(),
    updated_at         timestamptz not null default now(),
    unique (organization_id, id)                      -- target for composite foreign keys
);

-- No two active companies in one organization share a name.
create unique index companies_unique_name on companies (organization_id, lower(btrim(legal_name))) where status = 'active';

alter table companies enable row level security;

create policy companies_tenant on companies
    using (organization_id = app_org_id());

create policy companies_scope on companies as restrictive
    using (id = any (app_company_ids()));

-- The app role reads and writes; row-level security decides which rows.
grant select, insert, update, delete on table companies to pesaroll_app;

-- A company's statutory and registration numbers, one row per field the country defines
-- (country_company_fields). Kept apart from companies so every country fits.
create table company_identifiers (
    id               uuid primary key,
    organization_id  uuid not null,
    company_id       uuid not null,
    field_key        text not null,
    value            text not null check (char_length(value) between 1 and 60),
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (company_id, field_key),
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade
);

alter table company_identifiers enable row level security;

create policy company_identifiers_tenant on company_identifiers
    using (organization_id = app_org_id());

create policy company_identifiers_scope on company_identifiers as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert, update, delete on table company_identifiers to pesaroll_app;
