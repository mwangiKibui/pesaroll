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
    main_location_id   uuid,                          -- its one main location (ONB-04); can't be deleted while main
    default_pay_schedule_id uuid,                     -- the schedule its employees get (ONB-06); can't be deleted while default
    status             text not null default 'active' check (status in ('active', 'archived')),
    created_at         timestamptz not null default now(),
    updated_at         timestamptz not null default now(),
    unique (organization_id, id),                     -- target for composite foreign keys
    foreign key (organization_id, main_location_id) references locations (organization_id, id),
    foreign key (organization_id, default_pay_schedule_id) references pay_schedules (organization_id, id)
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

-- Where a company's payroll is paid from (ONB-03): a bank account or an M-Pesa B2C shortcode.
-- Bank and M-Pesa files take the debit account from here, never from the person exporting.
-- History is kept: the app can only close an account or move the default, never rewrite or
-- delete one, so what a past payroll was paid from can always be shown. Editing an account is
-- closing it and adding the new one, which points back at it (replaces_id).
-- The account number is encrypted by the app (FieldEncryptor);
-- only its last four digits are stored in the clear, for display.
create table company_payment_accounts (
    id               uuid primary key,
    organization_id  uuid not null,
    company_id       uuid not null,
    method           text not null check (method in ('bank', 'mpesa')),
    country_code     char(2),
    bank_code        text,
    branch_code      text,
    account_name     text check (char_length(btrim(account_name)) between 2 and 120),
    account_no_enc   bytea,
    account_last4    text check (account_last4 ~ '^[0-9]{4}$'),
    mpesa_shortcode  text,
    is_default       boolean not null default false,
    created_by       uuid not null references users (id),
    created_at       timestamptz not null default now(),
    closed_at        timestamptz,
    closed_by        uuid references users (id),
    replaces_id      uuid references company_payment_accounts (id),   -- editing is closing this row and adding a new one
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade,
    foreign key (country_code, bank_code, branch_code) references bank_branches (country_code, bank_code, code),
    check (
        (method = 'bank' and country_code is not null and bank_code is not null and branch_code is not null
            and account_name is not null and account_no_enc is not null and account_last4 is not null and mpesa_shortcode is null)
     or (method = 'mpesa' and mpesa_shortcode ~ '^[0-9]{5,7}$'
            and bank_code is null and branch_code is null and account_no_enc is null and account_last4 is null)
    )
);

-- One current default per company.
create unique index company_payment_accounts_one_default on company_payment_accounts (company_id)
    where is_default and closed_at is null;

alter table company_payment_accounts enable row level security;

create policy company_payment_accounts_tenant on company_payment_accounts
    using (organization_id = app_org_id());

create policy company_payment_accounts_scope on company_payment_accounts as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert on table company_payment_accounts to pesaroll_app;
grant update (is_default, closed_at, closed_by) on table company_payment_accounts to pesaroll_app;
