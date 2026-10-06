-- A user's place in an organization. Roles hang off memberships (ACC module).
create table memberships (
    id               uuid primary key,
    organization_id  uuid not null references organizations (id),
    user_id          uuid not null references users (id),
    job_role         text,                            -- the role given at sign-up
    status           text not null default 'active' check (status in ('invited', 'active', 'removed')),
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (organization_id, user_id)
);

alter table memberships enable row level security;

create policy memberships_tenant on memberships
    using (organization_id = app_org_id());
