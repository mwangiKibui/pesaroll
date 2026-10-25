-- Countries are data, not code (ONB-03). A country's company fields (statutory numbers, with
-- format checks and examples), regions, currency and banks live here, so rolling out a country is
-- configuration (BO-11, with PAY-01 rule packs), not a release. Shared by everyone; customers read only.

create table countries (
    code             char(2) primary key,                -- ISO 3166-1 alpha-2
    name_en          text not null,
    name_sw          text not null,
    currency         char(3) not null,                   -- ISO 4217
    phone_code       text not null,
    region_label_en  text not null,                      -- County, District, Region…
    region_label_sw  text not null,
    available        boolean not null default false,     -- people can pick it and run its payroll
    sort_order       int not null default 0
);

-- The numbers a company in this country registers with (tax, social security, health, levies…).
create table country_company_fields (
    country_code          char(2) not null references countries (code),
    key                   text not null check (key ~ '^[a-z][a-z0-9_]*$'),
    label_en              text not null,
    label_sw              text not null,
    hint_en               text,
    hint_sw               text,
    example               text,
    pattern               text,                          -- regex the trimmed, upper-cased value must match
    required_for_payroll  boolean not null default false, -- missing → payroll can't run (readiness)
    sort_order            int not null default 0,
    active                boolean not null default true,
    primary key (country_code, key)
);

-- Counties, districts or regions, for company and location addresses.
create table country_regions (
    country_code  char(2) not null references countries (code),
    code          text not null,
    name          text not null,
    sort_order    int not null default 0,
    primary key (country_code, code)
);

-- Industries drive later setup suggestions (structure, pay items).
create table industries (
    code        text primary key check (code ~ '^[a-z][a-z0-9_]*$'),
    name_en     text not null,
    name_sw     text not null,
    sort_order  int not null default 0,
    active      boolean not null default true
);

-- Minimum-wage areas (ONB-04): wage orders set rates by area, not county (Nakuru county has a city
-- and rural areas), so a location records its area. Towns listed here suggest the area; the
-- rates themselves live in minimum_wages (PAY-11).
create table country_wage_areas (
    country_code    char(2) not null references countries (code),
    code            text not null check (code ~ '^[a-z][a-z0-9_]*$'),
    name_en         text not null,
    name_sw         text not null,
    description_en  text not null,
    description_sw  text not null,
    towns           text[] not null default '{}',
    sort_order      int not null default 0,
    active          boolean not null default true,
    primary key (country_code, code)
);

grant select on table countries to pesaroll_app;
grant select on table country_wage_areas to pesaroll_app;
grant select on table country_company_fields to pesaroll_app;
grant select on table country_regions to pesaroll_app;
grant select on table industries to pesaroll_app;
