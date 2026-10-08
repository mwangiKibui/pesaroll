-- The tenant. Billing sits here. Created in the organization step (ONB-02) by
-- create_organization, which records who onboarded it (not an "owner": people keep the role they
-- chose at sign-up; full access comes from the Administrators group, ACC-06).
create table organizations (
    id            uuid primary key,
    name          text not null check (char_length(btrim(name)) between 2 and 120),
    onboarded_by  uuid not null references users (id),
    created_at  timestamptz not null default now(),
    updated_at  timestamptz not null default now()
);

alter table organizations enable row level security;

create policy organizations_tenant on organizations
    using (id = app_org_id());

-- The app role reads and writes; row-level security decides which rows.
grant select, insert, update, delete on table organizations to pesaroll_app;
