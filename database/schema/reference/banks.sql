-- Banks and their branches per country (ONB-03), for companies' paying accounts and later
-- employees' pay accounts (PAY-06). Data, not code: BO-11 adds banks and branches without a
-- release. Kenyan codes come from the Kenya Bankers Association's bank and branch list.
create table banks (
    country_code  char(2) not null references countries (code),
    code          text not null check (code ~ '^[0-9A-Z]{2,6}$'),
    name          text not null,                    -- what people call it: "Equity Bank"
    legal_name    text not null,
    account_digits_min int not null default 6,      -- account numbers are digits only, this long (ONB-09)
    account_digits_max int not null default 20,
    active        boolean not null default true,
    sort_order    int not null default 0,
    primary key (country_code, code),
    check (account_digits_min between 1 and account_digits_max)
);

create table bank_branches (
    country_code  char(2) not null,
    bank_code     text not null,
    code          text not null check (code ~ '^[0-9]{3}$'),
    name          text not null,
    active        boolean not null default true,
    sort_order    int not null default 0,
    primary key (country_code, bank_code, code),
    foreign key (country_code, bank_code) references banks (country_code, code)
);

grant select on table banks to pesaroll_app;
grant select on table bank_branches to pesaroll_app;
