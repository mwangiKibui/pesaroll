-- A legal employer inside an organization. Country selects the statutory rule pack.
create table companies (
    id                 uuid primary key,
    organization_id    uuid not null references organizations (id),
    legal_name         text not null,
    country_code       char(2) not null,
    currency           char(3) not null,
    registration_no    text,
    kra_pin_enc        bytea,                         -- encrypted at the field level
    nssf_employer_no   text,
    sha_employer_no    text,
    nita_no            text,
    status             text not null default 'active' check (status in ('active', 'archived')),
    created_at         timestamptz not null default now(),
    updated_at         timestamptz not null default now(),
    unique (organization_id, id)                      -- target for composite foreign keys
);

alter table companies enable row level security;

create policy companies_tenant on companies
    using (organization_id = app_org_id());

create policy companies_scope on companies as restrictive
    using (id = any (app_company_ids()));
