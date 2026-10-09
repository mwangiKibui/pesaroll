-- Where people work (ONB-04): head office, branches, sites. A location belongs to the organization
-- and serves all its companies (all_companies) or the ones in location_companies. Its county and
-- minimum-wage area drive minimum-wage checks and public holidays. Each company points at its one
-- main location (companies.main_location_id).
create table locations (
    id               uuid primary key,
    organization_id  uuid not null references organizations (id),
    code             text not null check (code ~ '^[A-Z0-9][A-Z0-9-]{1,19}$'),   -- NBO-HQ, MSA-01
    name             text not null check (char_length(btrim(name)) between 2 and 120),
    kind             text not null default 'branch'
                     check (kind in ('head_office', 'branch', 'office', 'warehouse', 'site')),
    country_code     char(2) not null references countries (code),
    region_code      text,                                  -- county (country_regions)
    town             text,
    address_line     text,
    wage_area_code   text,                                  -- country_wage_areas
    all_companies    boolean not null default false,        -- also companies added later
    status           text not null default 'active' check (status in ('active', 'archived')),
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (organization_id, id),
    foreign key (country_code, region_code) references country_regions (country_code, code),
    foreign key (country_code, wage_area_code) references country_wage_areas (country_code, code)
);

-- Codes are unique in the organization (always stored upper case).
create unique index locations_unique_code on locations (organization_id, code) where status = 'active';

alter table locations enable row level security;

create policy locations_tenant on locations
    using (organization_id = app_org_id());

grant select, insert, update, delete on table locations to pesaroll_app;
