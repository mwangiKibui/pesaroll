-- PLAT-03: the audit log. Every change to an organization's data is recorded by the database itself
-- (the trigger audit_change on each table, below), so writes from EF, bulk updates, SQL functions and
-- jobs are all caught: the table and row, insert, update or delete, the columns an update changed,
-- the values before and after, who (app.user_id), the request (app.request_ref: the reference errors
-- carry, or the job) and whether it came through the app or straight to the database (a deploy).
-- Salaries and amounts of pay, ID numbers, KRA PINs, NSSF and SHA numbers, bank and M-Pesa accounts and
-- secrets are never recorded (CLAUDE.md rule 8): their value reads "masked"; their history is in the
-- dated tables (compensations) the entry points to. An entry with a company is seen by people with
-- that company; one without (a schedule, a group) only by someone who sees all the organization's
-- companies. The app may read entries, never write, change or delete them, and nobody may change or
-- delete them (PLAT-9003). Kept 7 years (the Tax Procedures Act's 5, with margin).
create table audit_log (
    id               uuid primary key default uuidv7(),
    organization_id  uuid not null,                     -- no foreign key: entries outlive what they record
    company_id       uuid,
    kind             text not null check (kind in ('change')),
    action           text not null check (action in ('insert', 'update', 'delete')),
    table_name       text not null,
    record_id        uuid,                              -- null for a link table without an id (its keys are in the values)
    changed          text[],                            -- an update: the columns it changed
    old_values       jsonb,                             -- an update: the changed columns before; a delete: the row
    new_values       jsonb,                             -- an update: the changed columns after; an insert: the row
    user_id          uuid,
    request_ref      text,
    source           text not null check (source in ('app', 'database')),
    at               timestamptz not null default now()
);

create index audit_log_by_time on audit_log (organization_id, at desc);
create index audit_log_by_record on audit_log (table_name, record_id);

alter table audit_log enable row level security;

-- Whether the person sees every company of their organization (row-level security on companies
-- would hide the ones they don't, so this looks past it).
create or replace function app_sees_all_companies()
returns boolean
language sql stable security definer set search_path = public
as $$
    select not exists (select 1 from companies c where c.organization_id = app_org_id() and not (c.id = any (app_company_ids())))
$$;

create policy audit_log_tenant on audit_log
    using (organization_id = app_org_id());

create policy audit_log_scope on audit_log as restrictive
    using (case when company_id is null then app_sees_all_companies() else company_id = any (app_company_ids()) end);

grant select on table audit_log to pesaroll_app;

create or replace function audit_log_keep()
returns trigger
language plpgsql
as $$
begin
    raise exception 'PLAT-9003';
end
$$;

create trigger audit_log_keep before update or delete on audit_log
    for each row execute function audit_log_keep();

create trigger audit_log_keep_all before truncate on audit_log
    for each statement execute function audit_log_keep();

-- Columns whose values are never recorded, as table.column.
create or replace function audit_masked()
returns text[]
language sql immutable
as $$
    select array[
        'employees.national_id_enc', 'employees.national_id_lookup', 'employees.national_id_last4',
        'employees.kra_pin_enc', 'employees.kra_pin_lookup', 'employees.kra_pin_last4', 'employees.nssf_no', 'employees.sha_no',
        'compensations.basic_amount',
        'employee_pay_items.amount',
        'employee_payment_accounts.account_no_enc', 'employee_payment_accounts.account_last4',
        'employee_payment_accounts.mpesa_phone_enc', 'employee_payment_accounts.mpesa_last4',
        'company_payment_accounts.account_no_enc', 'company_payment_accounts.account_last4',
        'payees.account_number',
        'pay_run_inputs.amount',
        'pay_runs.gross_pay', 'pay_runs.paye', 'pay_runs.nssf_employee', 'pay_runs.shif', 'pay_runs.ahl_employee', 'pay_runs.other_deductions',
        'pay_runs.net_pay', 'pay_runs.nssf_employer', 'pay_runs.ahl_employer', 'pay_runs.nita', 'pay_runs.other_employer_costs',
        'payroll_history_batches.gross_total', 'payroll_history_batches.paye_total',
        'invitations.token_hash'
    ]
$$;

-- Tables with organization_id that don't record their changes: worked out again on every calculation
-- (a run's people and lines: the run records it), an import's rows (the batch records it), sign-in
-- sessions, delivery records, looks at pay (sensitive_views, a record of its own) and the log itself.
create or replace function audit_left_out()
returns text[]
language sql immutable
as $$
    select array['pay_run_people', 'pay_run_lines', 'payroll_history_lines', 'sessions', 'notification_deliveries',
        'pay_schedule_reminder_sends', 'sensitive_views', 'audit_log']
$$;

-- Records a change to a row of an organization's data. Rows of no organization (built-in roles) and
-- updates that change nothing (updated_at aside) are not recorded.
create or replace function audit_change()
returns trigger
language plpgsql security definer set search_path = public
as $$
declare
    old_row jsonb := case when tg_op <> 'INSERT' then to_jsonb(old) end;
    new_row jsonb := case when tg_op <> 'DELETE' then to_jsonb(new) end;
    row_values jsonb := coalesce(new_row, old_row);
    org uuid := coalesce(row_values->>'organization_id', case when tg_table_name = 'organizations' then row_values->>'id' end)::uuid;
    changed text[];
    masked text;
begin
    if org is null then
        return null;
    end if;
    if tg_op = 'UPDATE' then
        select array_agg(n.key order by n.key) into changed
        from jsonb_each(new_row) n
        where n.key <> 'updated_at' and n.value is distinct from old_row->n.key;
        if changed is null then
            return null;
        end if;
        old_row := (select jsonb_object_agg(key, value) from jsonb_each(old_row) where key = any (changed));
        new_row := (select jsonb_object_agg(key, value) from jsonb_each(new_row) where key = any (changed));
    end if;
    foreach masked in array audit_masked() loop
        if split_part(masked, '.', 1) = tg_table_name then
            masked := split_part(masked, '.', 2);
            if old_row->>masked is not null then old_row := jsonb_set(old_row, array[masked], '"masked"'); end if;
            if new_row->>masked is not null then new_row := jsonb_set(new_row, array[masked], '"masked"'); end if;
        end if;
    end loop;
    insert into audit_log (organization_id, company_id, kind, action, table_name, record_id, changed, old_values, new_values, user_id, request_ref, source)
    values (org, (row_values->>'company_id')::uuid, 'change', lower(tg_op), tg_table_name, (row_values->>'id')::uuid, changed, old_row, new_row,
        nullif(current_setting('app.user_id', true), '')::uuid, nullif(current_setting('app.request_ref', true), ''),
        case when current_setting('role', true) = 'pesaroll_app' then 'app' else 'database' end);
    return null;
end
$$;
