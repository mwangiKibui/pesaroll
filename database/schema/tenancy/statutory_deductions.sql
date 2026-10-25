-- Which statutory deductions a weekly, fortnightly or daily schedule makes (PAY-10). Monthly payroll
-- makes every one; for other schedules the organization says what it does, and until it does, none
-- are made (off by default). Many small employers make none, or only some, for casual workers; the
-- law may still require them, and the choice is the customer's, recorded with who made it and when.
--   makes_deductions  false: none; true: the items listed (paye, shif, nssf, housing_levy, nita)
--   applies_to        everyone on the schedule, or only the employee_ids chosen (none for the rest)
-- A choice applies from starts_on until a later one; on the same day the latest set_at wins. Rows are
-- added, never changed, so the history stays. The schedule belongs to the organization, so this is
-- the same at each company using it; a person's own choice (below) differs per person.
create table pay_schedule_deductions (
    id                uuid primary key,
    organization_id   uuid not null,
    pay_schedule_id   uuid not null,
    starts_on         date not null,
    makes_deductions  boolean not null,
    items             text[] not null default '{}' check (items <@ array['paye', 'shif', 'nssf', 'housing_levy', 'nita']),
    applies_to        text not null default 'everyone' check (applies_to in ('everyone', 'chosen')),
    employee_ids      uuid[],
    set_by            uuid not null references users (id),
    set_at            timestamptz not null default now(),
    foreign key (organization_id, pay_schedule_id) references pay_schedules (organization_id, id) on delete cascade,
    check (makes_deductions = (cardinality(items) > 0)),
    check (makes_deductions or applies_to = 'everyone'),
    check ((applies_to = 'chosen') = (employee_ids is not null and cardinality(employee_ids) > 0))
);

create index pay_schedule_deductions_schedule on pay_schedule_deductions (pay_schedule_id, starts_on desc, set_at desc);

alter table pay_schedule_deductions enable row level security;

create policy pay_schedule_deductions_tenant on pay_schedule_deductions
    using (organization_id = app_org_id());

grant select, insert on table pay_schedule_deductions to pesaroll_app;

-- A person's own statutory deductions, replacing their schedule's from starts_on: the items listed
-- (none when empty), or back to the schedule's (items null). Added, never changed.
create table employee_statutory_deductions (
    id                uuid primary key,
    organization_id   uuid not null,
    company_id        uuid not null,
    employee_id       uuid not null,
    starts_on         date not null,
    items             text[] check (items <@ array['paye', 'shif', 'nssf', 'housing_levy', 'nita']),
    set_by            uuid not null references users (id),
    set_at            timestamptz not null default now(),
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade,
    foreign key (company_id, employee_id) references employees (company_id, id) on delete cascade
);

create index employee_statutory_deductions_employee on employee_statutory_deductions (employee_id, starts_on desc, set_at desc);

alter table employee_statutory_deductions enable row level security;

create policy employee_statutory_deductions_tenant on employee_statutory_deductions
    using (organization_id = app_org_id());

create policy employee_statutory_deductions_scope on employee_statutory_deductions as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert on table employee_statutory_deductions to pesaroll_app;
