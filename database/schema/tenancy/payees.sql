-- Who a deduction's or employer cost's money is paid to (ONB-07): a SACCO, union, insurer or
-- lender, shared by the organization's companies and items. Paid by bank or SACCO transfer
-- (institution bank: bank, branch and the payee's account number; institution sacco: the SACCO,
-- its branch and the account number there), M-Pesa paybill (paybill and account), cheque (payable to)
-- or cash; only that way's details are kept. our_reference: the organization's number with them,
-- such as an employer code. Remittances (PAY-09) are paid and recorded against these.
create table payees (
    id                 uuid primary key,
    organization_id    uuid not null references organizations (id),
    name               text not null check (char_length(btrim(name)) between 2 and 120),
    method             text not null check (method in ('bank', 'mpesa_paybill', 'cheque', 'cash')),
    institution        text check (institution in ('bank', 'sacco')),                         -- bank transfers: to a bank or a SACCO
    sacco_code         text,
    sacco_branch_code  text,
    country_code       char(2),
    bank_code          text,
    branch_code        text,
    account_number     text check (account_number ~ '^[A-Za-z0-9-]{3,30}$'),   -- a bank's: digits, checked per bank by the app
    paybill            text check (paybill ~ '^[0-9]{5,7}$'),
    paybill_account    text check (char_length(btrim(paybill_account)) between 1 and 40),
    cheque_payable_to  text check (char_length(btrim(cheque_payable_to)) between 2 and 120),
    our_reference      text check (char_length(btrim(our_reference)) between 1 and 40),
    created_at         timestamptz not null default now(),
    updated_at         timestamptz not null default now(),
    unique (organization_id, id),
    foreign key (country_code, bank_code, branch_code) references bank_branches (country_code, bank_code, code),
    foreign key (country_code, sacco_code, sacco_branch_code) references sacco_branches (country_code, sacco_code, code),
    check (
        (method = 'bank' and institution = 'bank' and country_code is not null and bank_code is not null and branch_code is not null
            and account_number is not null and sacco_code is null and sacco_branch_code is null and paybill is null and paybill_account is null
            and cheque_payable_to is null)
     or (method = 'bank' and institution = 'sacco' and country_code is not null and sacco_code is not null and sacco_branch_code is not null
            and account_number is not null and bank_code is null and branch_code is null and paybill is null and paybill_account is null
            and cheque_payable_to is null)
     or (method = 'mpesa_paybill' and paybill is not null and institution is null and sacco_code is null and bank_code is null
            and account_number is null and cheque_payable_to is null)
     or (method = 'cheque' and cheque_payable_to is not null and institution is null and sacco_code is null and bank_code is null
            and account_number is null and paybill is null and paybill_account is null)
     or (method = 'cash' and institution is null and sacco_code is null and bank_code is null and account_number is null and paybill is null
            and paybill_account is null and cheque_payable_to is null)
    )
);

-- One payee of each name in an organization (ignoring case).
create unique index payees_unique_name on payees (organization_id, lower(btrim(name)));

alter table payees enable row level security;

create policy payees_tenant on payees
    using (organization_id = app_org_id());

grant select, insert, update, delete on table payees to pesaroll_app;
