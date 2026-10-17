-- ACC-05: every time the API sends someone's pay (or the account they are paid into), who saw whose
-- and when: one row per request, never the amounts (CLAUDE.md rule 8). The app may add and read
-- them, never change or delete them. The first part of the audit log (PLAT-03), which will take
-- them into its retention.
create table sensitive_views (
    id               uuid primary key,
    organization_id  uuid not null references organizations (id) on delete cascade,
    user_id          uuid not null references users (id),
    kind             text not null check (kind in ('employee_pay')),
    subject_ids      uuid[] not null,              -- whose: the employees shown
    reference        text,                         -- the request reference, as errors carry
    viewed_at        timestamptz not null default now()
);

create index sensitive_views_by_time on sensitive_views (organization_id, viewed_at desc);

alter table sensitive_views enable row level security;

create policy sensitive_views_tenant on sensitive_views using (organization_id = app_org_id()) with check (organization_id = app_org_id());

grant select, insert on table sensitive_views to pesaroll_app;
