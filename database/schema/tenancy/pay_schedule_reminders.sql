-- Reminders to run payroll (ONB-06): before each pay day of a schedule, an email or SMS to an
-- address or number someone entered. Days before the pay day (at 8:00 Kenya time), or hours
-- before the pay time for daily pay. Sent through notification_deliveries by the worker.
create table pay_schedule_reminders (
    id               uuid primary key,
    organization_id  uuid not null,
    pay_schedule_id  uuid not null,
    amount           integer not null,
    unit             text not null check (unit in ('days', 'hours')),   -- hours only for daily pay (the API checks)
    channel          text not null check (channel in ('email', 'sms')),
    recipient        text not null check (char_length(recipient) between 3 and 254),   -- an email, or +254…
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),
    unique (organization_id, id),
    foreign key (organization_id, pay_schedule_id) references pay_schedules (organization_id, id) on delete cascade,
    check ((unit = 'days' and amount between 1 and 14) or (unit = 'hours' and amount between 1 and 12))
);

alter table pay_schedule_reminders enable row level security;

create policy pay_schedule_reminders_tenant on pay_schedule_reminders
    using (organization_id = app_org_id());

-- Seen with their schedule: people limited to some companies see the reminders of the schedules
-- those companies use (pay_schedules' own policies decide).
create policy pay_schedule_reminders_scope on pay_schedule_reminders as restrictive for select
    using (exists (select 1 from pay_schedules s where s.id = pay_schedule_id));

grant select, insert, update, delete on table pay_schedule_reminders to pesaroll_app;

-- Each reminder is sent once per pay date: the worker claims the row first, then queues the
-- message, so a retry or a second run never sends twice.
create table pay_schedule_reminder_sends (
    organization_id  uuid not null,
    reminder_id      uuid not null,
    pay_date         date not null,
    delivery_id      uuid,                       -- the queued message (notification_deliveries)
    created_at       timestamptz not null default now(),
    primary key (reminder_id, pay_date),
    foreign key (organization_id, reminder_id) references pay_schedule_reminders (organization_id, id) on delete cascade
);

alter table pay_schedule_reminder_sends enable row level security;

create policy pay_schedule_reminder_sends_tenant on pay_schedule_reminder_sends
    using (organization_id = app_org_id());

grant select, insert on table pay_schedule_reminder_sends to pesaroll_app;
grant update (delivery_id) on table pay_schedule_reminder_sends to pesaroll_app;

-- For the worker, which runs for no organization: every reminder's timing and its organization's
-- companies, across organizations. Nothing about where reminders go; the worker reads that inside
-- each organization, under row-level security.
create or replace function pay_schedule_reminder_timings()
returns table (
    reminder_id uuid, organization_id uuid, company_ids uuid[], amount integer, unit text,
    frequency text, pay_day_rule text, pay_day integer, pay_weekday integer, first_pay_date date, pay_time time)
language sql stable security definer set search_path = public
as $$
    select r.id, r.organization_id,
           array(select c.id from companies c where c.organization_id = r.organization_id and c.status = 'active'),
           r.amount, r.unit, s.frequency, s.pay_day_rule, s.pay_day, s.pay_weekday, s.first_pay_date, s.pay_time
    from pay_schedule_reminders r
    join pay_schedules s on s.id = r.pay_schedule_id
    where s.status = 'active'
$$;

revoke all on function pay_schedule_reminder_timings() from public;
grant execute on function pay_schedule_reminder_timings() to pesaroll_app;
