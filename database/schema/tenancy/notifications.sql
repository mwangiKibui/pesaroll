-- Every email and SMS Pesaroll sends for an organization (NTF-01), such as payroll reminders
-- (ONB-06). The worker sends each one (Postmark for email, Africa's Talking for SMS) and records
-- how it went. Sent messages are also the usage billing counts (BILL-10): units are what the
-- provider bills, SMS parts or 1 for an email. Once queued, only the delivery fields change and
-- nothing is deleted. Templates never carry salaries; data holds only the values they fill in.
create table notifications (
    id                   uuid primary key,
    organization_id      uuid not null references organizations (id),
    channel              text not null check (channel in ('email', 'sms')),
    recipient            text not null check (char_length(recipient) between 3 and 254),   -- an email, or +254…
    template             text not null,
    data                 jsonb not null default '{}',
    language             text not null default 'en' check (language in ('en', 'sw')),
    status               text not null default 'queued' check (status in ('queued', 'sent', 'failed')),
    attempts             integer not null default 0,
    units                integer check (units > 0),
    provider_message_id  text,
    error_code           text,                       -- NTF-05xx, for support
    error                text,                       -- the provider's reason
    created_at           timestamptz not null default now(),
    updated_at           timestamptz not null default now(),
    sent_at              timestamptz,
    check ((status = 'sent') = (sent_at is not null and units is not null))
);

-- Monthly usage per organization and channel.
create index notifications_usage on notifications (organization_id, channel, sent_at) where status = 'sent';

alter table notifications enable row level security;

create policy notifications_tenant on notifications
    using (organization_id = app_org_id());

grant select, insert on table notifications to pesaroll_app;
grant update (status, attempts, units, provider_message_id, error_code, error, updated_at, sent_at) on table notifications to pesaroll_app;
