-- Employees (ONB-09). A person employed by one company; seen by people who may see that company.
-- The national ID number and KRA PIN are encrypted by the app (FieldEncryptor) with a keyed lookup
-- (FieldEncryptor.Lookup, per organization) that stops two current employees sharing one; only
-- the last four characters are kept readable, for screens. Someone with an exit date has left
-- (kept for P9A cards and history); their numbers can come back on a new hire.
create table employees (
    id                 uuid primary key,
    organization_id    uuid not null,
    company_id         uuid not null,
    staff_no           text not null check (staff_no ~ '^[A-Z0-9][A-Z0-9/-]{0,19}$'),
    first_name         text not null check (char_length(btrim(first_name)) between 1 and 60),
    middle_name        text check (char_length(btrim(middle_name)) between 1 and 60),
    last_name          text not null check (char_length(btrim(last_name)) between 1 and 60),
    date_of_birth      date,
    phone              text,                          -- +254…
    email              text,
    national_id_enc    bytea,
    national_id_lookup text,
    national_id_last4  text,
    kra_pin_enc        bytea,
    kra_pin_lookup     text,
    kra_pin_last4      text,
    nssf_no            text,
    sha_no             text,
    hire_date          date not null,
    exit_date          date,
    created_at         timestamptz not null default now(),
    updated_at         timestamptz not null default now(),
    unique (organization_id, id),
    unique (company_id, id),
    unique (company_id, staff_no),
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade,
    check (exit_date is null or exit_date >= hire_date),
    check ((national_id_enc is null) = (national_id_lookup is null) or national_id_enc is null),
    check ((kra_pin_enc is null) = (kra_pin_lookup is null) or kra_pin_enc is null)
);

-- No two current employees in an organization share an ID number or KRA PIN.
create unique index employees_one_national_id on employees (organization_id, national_id_lookup) where national_id_lookup is not null and exit_date is null;
create unique index employees_one_kra_pin on employees (organization_id, kra_pin_lookup) where kra_pin_lookup is not null and exit_date is null;
create index employees_names on employees (company_id, last_name, first_name);

alter table employees enable row level security;

create policy employees_tenant on employees
    using (organization_id = app_org_id());

create policy employees_scope on employees as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert, update, delete on table employees to pesaroll_app;

-- An employee's job over time (ONB-09): where they sit, who they report to, how they're paid and
-- which leave plan they follow. One current row (valid_to empty); a change closes it and adds a new one.
create table employments (
    id               uuid primary key,
    organization_id  uuid not null,
    company_id       uuid not null,
    employee_id      uuid not null,
    valid_from       date not null,
    valid_to         date,
    position_id      uuid,
    department_id    uuid,
    unit_id          uuid,
    location_id      uuid,
    line_manager_id  uuid,
    pay_schedule_id  uuid not null,
    leave_plan_id    uuid not null,
    employment_type  text not null check (employment_type in ('permanent', 'contract', 'casual', 'intern')),
    contract_end     date,                                  -- fixed-term contracts only
    created_at       timestamptz not null default now(),
    foreign key (company_id, employee_id) references employees (company_id, id) on delete cascade,
    foreign key (organization_id, position_id) references positions (organization_id, id),
    foreign key (organization_id, department_id) references departments (organization_id, id),
    foreign key (organization_id, unit_id) references units (organization_id, id),
    foreign key (organization_id, location_id) references locations (organization_id, id),
    foreign key (organization_id, line_manager_id) references employees (organization_id, id) on delete set null (line_manager_id),
    foreign key (organization_id, pay_schedule_id) references pay_schedules (organization_id, id),
    foreign key (organization_id, leave_plan_id) references leave_plans (organization_id, id),
    check (valid_to is null or valid_to >= valid_from),
    check (contract_end is null or employment_type = 'contract'),
    check (line_manager_id is distinct from employee_id)
);

create unique index employments_one_current on employments (employee_id) where valid_to is null;

alter table employments enable row level security;

create policy employments_tenant on employments
    using (organization_id = app_org_id());

create policy employments_scope on employments as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert, update, delete on table employments to pesaroll_app;

-- Basic pay over time (ONB-09): a monthly salary, a daily rate or an hourly rate. One current row.
create table compensations (
    id               uuid primary key,
    organization_id  uuid not null,
    company_id       uuid not null,
    employee_id      uuid not null,
    valid_from       date not null,
    valid_to         date,
    basic_amount     numeric(18, 2) not null check (basic_amount > 0),
    pay_basis        text not null check (pay_basis in ('monthly', 'daily', 'hourly')),
    currency         char(3) not null,
    created_at       timestamptz not null default now(),
    foreign key (company_id, employee_id) references employees (company_id, id) on delete cascade,
    check (valid_to is null or valid_to >= valid_from)
);

create unique index compensations_one_current on compensations (employee_id) where valid_to is null;

alter table compensations enable row level security;

create policy compensations_tenant on compensations
    using (organization_id = app_org_id());

create policy compensations_scope on compensations as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert, update, delete on table compensations to pesaroll_app;

-- Where an employee's pay goes (ONB-09, PAY-06/07): a bank account (number encrypted, last four
-- kept) or M-Pesa (phone encrypted). One current row; a change closes it and adds a new one.
create table employee_payment_accounts (
    id               uuid primary key,
    organization_id  uuid not null,
    company_id       uuid not null,
    employee_id      uuid not null,
    method           text not null check (method in ('bank', 'mpesa')),
    country_code     char(2),
    bank_code        text,
    branch_code      text,
    account_no_enc   bytea,
    account_last4    text check (account_last4 ~ '^[0-9]{4}$'),
    mpesa_phone_enc  bytea,
    mpesa_last4      text check (mpesa_last4 ~ '^[0-9]{4}$'),
    valid_from       date not null,
    valid_to         date,
    created_at       timestamptz not null default now(),
    foreign key (company_id, employee_id) references employees (company_id, id) on delete cascade,
    foreign key (country_code, bank_code, branch_code) references bank_branches (country_code, bank_code, code),
    check (
        (method = 'bank' and bank_code is not null and branch_code is not null and account_no_enc is not null and account_last4 is not null
            and mpesa_phone_enc is null)
     or (method = 'mpesa' and mpesa_phone_enc is not null and mpesa_last4 is not null and bank_code is null and account_no_enc is null)
    )
);

create unique index employee_payment_accounts_one_current on employee_payment_accounts (employee_id) where valid_to is null;

alter table employee_payment_accounts enable row level security;

create policy employee_payment_accounts_tenant on employee_payment_accounts
    using (organization_id = app_org_id());

create policy employee_payment_accounts_scope on employee_payment_accounts as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert, update, delete on table employee_payment_accounts to pesaroll_app;
