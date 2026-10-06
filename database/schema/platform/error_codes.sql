-- The error catalogue (PLAT-14). Rows are loaded by post-deploy/error_codes.sql.
create table error_codes (
    code         text primary key check (code ~ '^[A-Z]{2,4}-[0-9]{4}$'),
    module       text not null,
    severity     text not null check (severity in ('info', 'warning', 'error', 'critical')),
    message_en   text not null,
    message_sw   text,
    fixable_by   text not null check (fixable_by in ('customer', 'support', 'engineering')),
    playbook     text not null,
    retired_at   timestamptz
);

-- The app reads the catalogue; only deploys write it.
grant select on table error_codes to pesaroll_app;
