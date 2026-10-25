-- Pay runs (PAY-03): one company's pay for one schedule and period, worked out with the rule pack in
-- force on the pay date and approved through the pay_run workflow (WF-01). A run moves forward only:
--   draft → calculating (a job) → calculated → awaiting_approval → approved
-- A declined or cancelled request sends it back to calculated; a run that isn't sent can be cancelled.
-- Once sent its figures can't change, and an approved or cancelled run never changes (PAY-2014): a
-- mistake is fixed with a correction run (kind correction, for chosen people), never an edit.
-- Periods are calendar months for now; weekly, fortnightly and daily runs come with PAY-10.
create table pay_runs (
    id                   uuid primary key,
    organization_id      uuid not null,
    company_id           uuid not null,
    pay_schedule_id      uuid not null,
    kind                 text not null default 'regular' check (kind in ('regular', 'correction')),
    corrects_pay_run_id  uuid,                                   -- corrections: the approved run they correct
    employee_ids         uuid[],                                 -- corrections: who is worked out again
    period_start         date not null,
    period_end           date not null,
    pay_date             date not null,
    status               text not null default 'draft'
                         check (status in ('draft', 'calculating', 'calculated', 'awaiting_approval', 'approved', 'cancelled')),
    rule_pack_version    text references rule_packs (version),
    calculation_error    text,                                   -- the error code when the last calculation failed (PAY-2010)
    decline_reason       text,                                   -- why the last approval request was declined
    people               int not null default 0,                 -- paid in the run
    gross_pay            numeric(18, 2) not null default 0,
    paye                 numeric(18, 2) not null default 0,
    nssf_employee        numeric(18, 2) not null default 0,
    shif                 numeric(18, 2) not null default 0,
    ahl_employee         numeric(18, 2) not null default 0,
    other_deductions     numeric(18, 2) not null default 0,
    net_pay              numeric(18, 2) not null default 0,
    nssf_employer        numeric(18, 2) not null default 0,
    ahl_employer         numeric(18, 2) not null default 0,
    nita                 numeric(18, 2) not null default 0,
    other_employer_costs numeric(18, 2) not null default 0,
    created_by           uuid not null references users (id),
    created_at           timestamptz not null default now(),
    calculated_by        uuid references users (id),
    calculated_at        timestamptz,
    sent_by              uuid references users (id),
    sent_at              timestamptz,
    approved_by          uuid references users (id),
    approved_at          timestamptz,
    cancelled_by         uuid references users (id),
    cancelled_at         timestamptz,
    unique (organization_id, id),
    unique (company_id, id),
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade,
    foreign key (organization_id, pay_schedule_id) references pay_schedules (organization_id, id),
    foreign key (company_id, corrects_pay_run_id) references pay_runs (company_id, id),
    check (period_end >= period_start),
    check ((kind = 'correction') = (corrects_pay_run_id is not null)),
    check ((kind = 'correction') = (employee_ids is not null and cardinality(employee_ids) > 0)),
    check ((status = 'approved') = (approved_at is not null)),
    check ((status = 'cancelled') = (cancelled_at is not null))
);

-- One regular run per company, schedule and period that isn't cancelled.
create unique index pay_runs_one_regular on pay_runs (company_id, pay_schedule_id, period_start) where kind = 'regular' and status <> 'cancelled';
create index pay_runs_company on pay_runs (company_id, period_start desc);

alter table pay_runs enable row level security;

create policy pay_runs_tenant on pay_runs
    using (organization_id = app_org_id());

create policy pay_runs_scope on pay_runs as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert, update on table pay_runs to pesaroll_app;

create or replace function pay_runs_keep_final()
returns trigger
language plpgsql
as $$
begin
    if old.status in ('approved', 'cancelled') then
        raise exception 'PAY-2014';
    end if;
    return new;
end
$$;

create trigger pay_runs_keep_final before update on pay_runs
    for each row execute function pay_runs_keep_final();

-- Each person a run worked out, with what it used and their totals, or why they were left out:
--   no_pay        no basic pay in force
--   not_monthly   paid weekly, fortnightly, daily or hourly (PAY-10)
--   other_currency  paid in a currency other than the rule pack's
--   not_in_period   (corrections) not employed on this schedule in the period
-- Their name and staff number are copied in, as on the payslip. notes flag what Payroll should
-- check before sending: joined or left in the period (paid the whole month until PAY-04),
-- negative_net (blocks sending, PAY-2019).
create table pay_run_people (
    id                uuid primary key,
    organization_id   uuid not null,
    company_id        uuid not null,
    pay_run_id        uuid not null,
    employee_id       uuid not null,
    staff_no          text not null,
    full_name         text not null,
    included          boolean not null,
    excluded_reason   text check (excluded_reason in ('no_pay', 'not_monthly', 'other_currency', 'not_in_period')),
    notes             text[] not null default '{}',
    employment_id     uuid,
    compensation_id   uuid,
    basic_pay         numeric(18, 2),
    gross_pay         numeric(18, 2),                             -- every earning
    taxable_pay       numeric(18, 2),
    tax_before_relief numeric(18, 2),
    personal_relief   numeric(18, 2),
    paye              numeric(18, 2),
    total_deductions  numeric(18, 2),                             -- statutory and the organization's own
    net_pay           numeric(18, 2),
    employer_costs    numeric(18, 2),
    unique (organization_id, id),
    unique (pay_run_id, employee_id),
    foreign key (company_id, pay_run_id) references pay_runs (company_id, id) on delete cascade,
    foreign key (company_id, employee_id) references employees (company_id, id),
    check (included = (excluded_reason is null)),
    check (not included or (basic_pay is not null and gross_pay is not null and taxable_pay is not null and paye is not null
        and total_deductions is not null and net_pay is not null and employer_costs is not null))
);

create index pay_run_people_employee on pay_run_people (employee_id);

alter table pay_run_people enable row level security;

create policy pay_run_people_tenant on pay_run_people
    using (organization_id = app_org_id());

create policy pay_run_people_scope on pay_run_people as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert, delete on table pay_run_people to pesaroll_app;

-- Every line of a person's pay: basic pay, the organization's earnings, deductions and employer costs
-- (pay_item_id), and each statutory line (statutory_key) with what it was worked out on and its rate.
-- The code and name are copied in, so a run never changes when items do.
create table pay_run_lines (
    id               uuid primary key default uuidv7(),
    organization_id  uuid not null,
    company_id       uuid not null,
    pay_run_id       uuid not null,
    person_id        uuid not null,
    kind             text not null check (kind in ('earning', 'deduction', 'employer_cost')),
    code             text not null,
    name             text not null,
    pay_item_id      uuid,
    statutory_key    text,
    taxable          boolean not null default false,
    before_tax       boolean not null default false,
    base             numeric(18, 2),
    rate             numeric(9, 6),
    amount           numeric(18, 2) not null,
    position         int not null,
    foreign key (organization_id, person_id) references pay_run_people (organization_id, id) on delete cascade,
    foreign key (company_id, pay_run_id) references pay_runs (company_id, id) on delete cascade
);

create index pay_run_lines_person on pay_run_lines (person_id, position);

alter table pay_run_lines enable row level security;

create policy pay_run_lines_tenant on pay_run_lines
    using (organization_id = app_org_id());

create policy pay_run_lines_scope on pay_run_lines as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert, delete on table pay_run_lines to pesaroll_app;

-- A run's people and lines change only while it is being worked out (recalculating replaces them).
-- Deleting a run's company still cascades: deploys and cleanup run as other roles.
create or replace function pay_run_figures_keep()
returns trigger
language plpgsql
as $$
declare
    run_status text;
begin
    if current_user <> 'pesaroll_app' then
        return case when tg_op = 'DELETE' then old else new end;
    end if;
    select status into run_status from pay_runs
    where id = case when tg_op = 'DELETE' then old.pay_run_id else new.pay_run_id end;
    if run_status not in ('draft', 'calculating', 'calculated') then
        raise exception 'PAY-2014';
    end if;
    return case when tg_op = 'DELETE' then old else new end;
end
$$;

create trigger pay_run_people_keep before insert or update or delete on pay_run_people
    for each row execute function pay_run_figures_keep();

create trigger pay_run_lines_keep before insert or update or delete on pay_run_lines
    for each row execute function pay_run_figures_keep();
