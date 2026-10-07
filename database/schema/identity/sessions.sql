-- Signed-in sessions. The app holds a 15-minute access token (a JWT naming the session) and a
-- refresh token in an HttpOnly cookie; only the refresh token's SHA-256 hash is stored here.
-- Each refresh swaps in a new token and keeps the old hash in previous_token_hash, so a replaced
-- token used again is spotted and ends the session (rotate_session_token).
-- Sessions end after 12 hours idle or 7 days in total, or at sign-out.
create table sessions (
    id               uuid primary key,
    user_id          uuid not null references users (id),
    organization_id  uuid references organizations (id),   -- current organization, null until one exists
    token_hash       bytea not null unique,
    previous_token_hash bytea,                                -- the refresh token this one replaced
    created_at       timestamptz not null default now(),
    last_seen_at     timestamptz not null default now(),
    expires_at       timestamptz not null,
    revoked_at       timestamptz,
    ip               inet,
    user_agent       text
);

create index sessions_previous_token_hash on sessions (previous_token_hash);

alter table sessions enable row level security;

create policy sessions_owner on sessions
    using (user_id = app_user_id());

grant select, insert, update on table sessions to pesaroll_app;
