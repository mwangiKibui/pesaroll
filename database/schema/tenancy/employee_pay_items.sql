-- Who gets an earning or deduction (ONB-07): at one company, everyone (employee_id null, people
-- hired later too) or one person, with their amount (fixed items; null: the item's default) or
-- rate (% of basic; null: the item's rate) and the dates it applies (ends_on: a loan's last
-- month). Basic pay and the law's items need none: they apply to everyone. Payroll (PAY-03/04)
-- reads these; items entered each payroll (overtime) just say who can have them.
create table employee_pay_items (
    id               uuid primary key,
    organization_id  uuid not null,
    company_id       uuid not null,
    pay_item_id      uuid not null,
    employee_id      uuid,                                                -- null: everyone at the company
    amount           numeric(18, 2) check (amount >= 0),
    rate             numeric(9, 4) check (rate > 0 and rate <= 100),
    starts_on        date not null,
    ends_on          date,
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    foreign key (organization_id, pay_item_id) references pay_items (organization_id, id) on delete cascade,
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade,
    foreign key (company_id, employee_id) references employees (company_id, id) on delete cascade,
    check (ends_on is null or ends_on >= starts_on),
    check (amount is null or rate is null)
);

-- One "everyone" per item and company; one row per item and person.
create unique index employee_pay_items_one_everyone on employee_pay_items (pay_item_id, company_id) where employee_id is null;
create unique index employee_pay_items_one_each on employee_pay_items (pay_item_id, employee_id) where employee_id is not null;
create index employee_pay_items_employee on employee_pay_items (employee_id) where employee_id is not null;

alter table employee_pay_items enable row level security;

create policy employee_pay_items_tenant on employee_pay_items
    using (organization_id = app_org_id());

create policy employee_pay_items_scope on employee_pay_items as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert, update, delete on table employee_pay_items to pesaroll_app;

-- What the item allows: not one set by law (ONB-3545), only at companies it serves (ONB-3546), and
-- an amount only for fixed items, a rate only for percentage ones, neither for items entered each
-- payroll (ONB-3547). Runs as its owner, so the checks see the item whatever the session's scope.
create or replace function employee_pay_items_fit()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
    item pay_items%rowtype;
begin
    select * into item from pay_items where id = new.pay_item_id and organization_id = new.organization_id;
    if item.locked_key is not null then
        raise exception 'ONB-3545';
    end if;
    if not item.all_companies and not exists (
        select 1 from pay_item_companies pc where pc.pay_item_id = item.id and pc.company_id = new.company_id) then
        raise exception 'ONB-3546';
    end if;
    if (new.amount is not null and item.calc_method <> 'fixed') or (new.rate is not null and item.calc_method <> 'percent_of_basic') then
        raise exception 'ONB-3547';
    end if;
    return new;
end
$$;

create trigger employee_pay_items_fit before insert or update on employee_pay_items
    for each row execute function employee_pay_items_fit();
