-- The leave the law gives, per country (ONB-08): the minimum on full and half pay, the longest
-- wait, and how it's usually given. Every organization gets a locked leave type for each, and
-- plans can't give less (the API checks, whatever the way of earning). The back office changes
-- these when the law does (BO-11).
create table statutory_leave (
    country_code     char(2) not null references countries (code),
    key              text not null check (key ~ '^[a-z][a-z_]{1,39}$'),   -- annual, sick, maternity …
    code             text not null,                                       -- its code on leave types
    name_en          text not null,
    name_sw          text not null,
    full_pay_days    numeric(6, 2) not null,                              -- a year, or each time
    half_pay_days    numeric(6, 2) not null default 0,
    counts           text not null check (counts in ('working_days', 'calendar_days')),
    earn_method      text not null check (earn_method in ('yearly', 'monthly', 'weekly', 'each_time')),
    earn_amount      numeric(9, 4) not null,                              -- in days, per earn_method: 1.75 a month
    waiting_months   int not null default 0,                              -- service before it can be used
    needs_proof      boolean not null default false,
    active           boolean not null default true,
    sort_order       int not null default 0,
    primary key (country_code, key)
);

grant select on table statutory_leave to pesaroll_app;
