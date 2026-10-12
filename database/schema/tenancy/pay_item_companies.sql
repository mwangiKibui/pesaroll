-- Which companies use an earning or deduction that isn't for all companies (ONB-07).
create table pay_item_companies (
    organization_id  uuid not null,
    pay_item_id      uuid not null,
    company_id       uuid not null,
    created_at       timestamptz not null default now(),
    primary key (pay_item_id, company_id),
    foreign key (organization_id, pay_item_id) references pay_items (organization_id, id) on delete cascade,
    foreign key (organization_id, company_id) references companies (organization_id, id) on delete cascade
);

alter table pay_item_companies enable row level security;

create policy pay_item_companies_tenant on pay_item_companies
    using (organization_id = app_org_id());

create policy pay_item_companies_scope on pay_item_companies as restrictive
    using (company_id = any (app_company_ids()));

grant select, insert, delete on table pay_item_companies to pesaroll_app;

-- On pay_items, here because it reads pay_item_companies. Only for reading: a new item has no
-- links yet when it is written (the API checks who may write).
create policy pay_items_scope on pay_items as restrictive for select
    using (all_companies or exists (
        select 1 from pay_item_companies pc where pc.pay_item_id = pay_items.id and pc.company_id = any (app_company_ids())));
