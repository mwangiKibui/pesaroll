-- Payroll history imported from the company's previous system (ONB-10): one row per employee per
-- month, the first of the month, with the figures a P9A tax card and year-to-date totals need.
-- Imported and read-only: kept apart from Pesaroll's own pay runs and never re-filed. A batch is
-- one upload (who, when, which file, what it held); deleting it takes its rows. Columns follow
-- docs/research/payroll-history-import.md §5.1 (P9A letters in brackets).
create table payroll_history_batches (
    id               uuid primary key,
    organization_id  uuid not null,
    company_id       uuid not null,
    file_name        text not null check (char_length(file_name) between 1 and 255),
    row_count        int not null check (row_count > 0),
    first_period     date not null,
    last_period      date not null,
    gross_total      numeric(18, 2) not null,
    paye_total       numeric(18, 2) not null,
    imported_by      uuid not null references users (id),
    created_at       timestamptz not null default now(),
    unique (organization_id, id),
    unique (company_id, id),
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade,
    check (last_period >= first_period)
);

create table payroll_history_lines (
    id                     uuid primary key,
    organization_id        uuid not null,
    company_id             uuid not null,
    batch_id               uuid not null,
    employee_id            uuid not null,
    period                 date not null check (period = date_trunc('month', period)::date),
    pay_date               date,
    basic_pay              numeric(18, 2) not null check (basic_pay >= 0),         -- A
    cash_allowances        numeric(18, 2) check (cash_allowances >= 0),
    non_cash_benefits      numeric(18, 2) check (non_cash_benefits >= 0),          -- B
    value_of_quarters      numeric(18, 2) check (value_of_quarters >= 0),          -- C
    gross_pay              numeric(18, 2) not null check (gross_pay >= 0),         -- D, taxable gross
    pension                numeric(18, 2) check (pension >= 0),                    -- E
    nssf_tier1             numeric(18, 2) check (nssf_tier1 >= 0),
    nssf_tier2             numeric(18, 2) check (nssf_tier2 >= 0),
    nssf_employee          numeric(18, 2) not null check (nssf_employee >= 0),
    nssf_employer          numeric(18, 2) check (nssf_employer >= 0),
    shif                   numeric(18, 2) not null check (shif >= 0),              -- G (NHIF before Oct 2024)
    housing_levy_employee  numeric(18, 2) check (housing_levy_employee >= 0),      -- F
    housing_levy_employer  numeric(18, 2) check (housing_levy_employer >= 0),
    post_retirement_medical numeric(18, 2) check (post_retirement_medical >= 0),   -- H
    mortgage_interest      numeric(18, 2) check (mortgage_interest >= 0),          -- I
    taxable_pay            numeric(18, 2) check (taxable_pay >= 0),                -- K
    personal_relief        numeric(18, 2) check (personal_relief >= 0),
    insurance_relief       numeric(18, 2) check (insurance_relief >= 0),           -- N
    paye                   numeric(18, 2) not null check (paye >= 0),              -- O
    other_deductions       numeric(18, 2) check (other_deductions >= 0),
    net_pay                numeric(18, 2) check (net_pay >= 0),
    nita                   numeric(18, 2) check (nita >= 0),
    notes                  text check (char_length(notes) <= 200),
    created_at             timestamptz not null default now(),
    foreign key (company_id, batch_id) references payroll_history_batches (company_id, id) on delete cascade,
    foreign key (company_id, employee_id) references employees (company_id, id) on delete cascade
);

-- One row per person per month, whichever batch brought it.
create unique index payroll_history_one_month on payroll_history_lines (employee_id, period);
create index payroll_history_company_period on payroll_history_lines (company_id, period);

alter table payroll_history_batches enable row level security;
alter table payroll_history_lines enable row level security;

create policy payroll_history_batches_tenant on payroll_history_batches using (organization_id = app_org_id());
create policy payroll_history_batches_scope on payroll_history_batches as restrictive using (company_id = any (app_company_ids()));
create policy payroll_history_lines_tenant on payroll_history_lines using (organization_id = app_org_id());
create policy payroll_history_lines_scope on payroll_history_lines as restrictive using (company_id = any (app_company_ids()));

grant select, insert, delete on table payroll_history_batches to pesaroll_app;
grant select, insert, delete on table payroll_history_lines to pesaroll_app;
