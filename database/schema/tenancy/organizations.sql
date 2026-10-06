-- The tenant. Billing sits here.
create table organizations (
    id          uuid primary key,
    name        text not null,
    created_by  uuid not null references users (id),
    created_at  timestamptz not null default now(),
    updated_at  timestamptz not null default now()
);

alter table organizations enable row level security;

create policy organizations_tenant on organizations
    using (id = app_org_id());
