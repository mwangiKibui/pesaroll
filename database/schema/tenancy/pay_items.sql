-- Earnings, deductions and employer costs (ONB-07), shown as "Earnings and deductions". An item
-- belongs to the organization and serves all its companies (all_companies) or the ones in
-- pay_item_companies. Locked items (locked_key) are Basic pay and the law's items
-- (statutory_items): made with the first company, never changed or deleted by the app.
--   taxable      earnings only: added to the pay PAYE is worked out on
--   before_tax   deductions only: taken off before PAYE is worked out (pension, within the rule
--                pack's caps)
--   calc_method  fixed (an amount per person, default_amount suggests it), percent_of_basic
--                (rate), each_run (entered each payroll: overtime, commission), by_law (the rule
--                pack works it out; statutory items only), per_unit (PAY-11: earnings paid by the
--                hour or day entered each run, at multiplier times the person's own hourly or daily
--                rate, or default_amount a unit)
create table pay_items (
    id               uuid primary key,
    organization_id  uuid not null references organizations (id),
    code             text not null check (code ~ '^[A-Z0-9][A-Z0-9-]{1,19}$'),
    name             text not null check (char_length(btrim(name)) between 2 and 80),
    kind             text not null check (kind in ('earning', 'deduction', 'employer_cost')),
    taxable          boolean not null default false,
    before_tax       boolean not null default false,
    calc_method      text not null check (calc_method in ('fixed', 'percent_of_basic', 'each_run', 'by_law', 'per_unit')),
    default_amount   numeric(18, 2) check (default_amount >= 0),
    rate             numeric(9, 4) check (rate > 0 and rate <= 100),     -- percent of basic pay
    unit             text check (unit in ('hour', 'day')),               -- per_unit items
    multiplier       numeric(6, 4) check (multiplier > 0 and multiplier <= 10),   -- per_unit: 1.5 is time and a half
    locked_key       text,                                               -- basic, or a statutory item's key
    country_code     char(2),                                            -- the statutory item's country
    all_companies    boolean not null default false,
    status           text not null default 'active' check (status in ('active', 'archived')),
    payee_id         uuid,                                               -- who the money goes to (deductions, employer costs)
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (organization_id, id),
    foreign key (country_code, locked_key) references statutory_items (country_code, key),
    foreign key (organization_id, payee_id) references payees (organization_id, id),   -- a payee in use can't be deleted
    check (payee_id is null or kind <> 'earning'),
    check (kind = 'earning' or not taxable),
    check (kind = 'deduction' or not before_tax),
    check ((calc_method = 'percent_of_basic') = (rate is not null)),
    check (calc_method in ('fixed', 'per_unit') or default_amount is null),
    check ((calc_method = 'per_unit') = (unit is not null)),
    check (calc_method = 'per_unit' or multiplier is null),
    check (calc_method <> 'per_unit' or (kind = 'earning' and (multiplier is null) <> (default_amount is null))),
    check ((calc_method = 'by_law') = (locked_key is not null and locked_key <> 'basic')),
    check ((locked_key = 'basic') = (locked_key is not null and country_code is null))
);

-- Codes are unique in the organization (always stored upper case); one locked item of each kind.
create unique index pay_items_unique_code on pay_items (organization_id, code) where status = 'active';
create unique index pay_items_one_locked on pay_items (organization_id, locked_key, country_code) where locked_key is not null;

alter table pay_items enable row level security;

create policy pay_items_tenant on pay_items
    using (organization_id = app_org_id());

grant select, insert, update, delete on table pay_items to pesaroll_app;

-- The app can't change or delete locked items; deploys (other roles) still can.
create or replace function pay_items_keep_locked()
returns trigger
language plpgsql
as $$
begin
    if old.locked_key is not null and current_user = 'pesaroll_app' then
        raise exception 'ONB-3531';
    end if;
    return case when tg_op = 'DELETE' then old else new end;
end
$$;

create trigger pay_items_keep_locked before update or delete on pay_items
    for each row execute function pay_items_keep_locked();
