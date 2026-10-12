-- SACCOs and their branches per country (ONB-07), for payees paid by SACCO transfer and later
-- employees' pay accounts. Data, not code: the back office (BO-11) adds SACCOs and branches
-- without a release. SACCOs have no published branch codes like banks: the codes are Pesaroll's.
create table saccos (
    country_code  char(2) not null references countries (code),
    code          text not null check (code ~ '^[A-Z0-9]{2,12}$'),
    name          text not null,                    -- what people call it: "Stima SACCO"
    legal_name    text not null,
    active        boolean not null default true,
    sort_order    int not null default 0,
    primary key (country_code, code)
);

create table sacco_branches (
    country_code  char(2) not null,
    sacco_code    text not null,
    code          text not null check (code ~ '^[0-9]{3}$'),
    name          text not null,
    active        boolean not null default true,
    sort_order    int not null default 0,
    primary key (country_code, sacco_code, code),
    foreign key (country_code, sacco_code) references saccos (country_code, code)
);

grant select on table saccos to pesaroll_app;
grant select on table sacco_branches to pesaroll_app;
