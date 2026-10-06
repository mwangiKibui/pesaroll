-- Signed-in sessions. The cookie holds a random token; only its SHA-256 hash is stored.
-- Sessions end after 12 hours idle or 7 days in total (enforced by find_session).
create table sessions (
    id               uuid primary key,
    user_id          uuid not null references users (id),
    organization_id  uuid references organizations (id),   -- current organization, null until one exists
    token_hash       bytea not null unique,
    created_at       timestamptz not null default now(),
    last_seen_at     timestamptz not null default now(),
    expires_at       timestamptz not null,
    revoked_at       timestamptz,
    ip               inet,
    user_agent       text
);

alter table sessions enable row level security;

create policy sessions_owner on sessions
    using (user_id = app_user_id());

grant select, insert, update on table sessions to pesaroll_app;
