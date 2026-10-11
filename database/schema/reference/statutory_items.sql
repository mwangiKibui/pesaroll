-- What the law takes from pay, or adds to the employer's cost, per country (ONB-07, PAY-01).
-- Reference data: every organization gets a locked pay item for each one. Rates, bands and caps
-- live in the country's rule pack (PAY-01), not here.
create table statutory_items (
    country_code         char(2) not null references countries (code),
    key                  text not null check (key ~ '^[a-z][a-z_]{1,39}$'),   -- paye, shif, nssf_employee …
    code                 text not null,                                       -- its code on pay items: PAYE, NSSF-ER …
    name_en              text not null,
    name_sw              text not null,
    kind                 text not null check (kind in ('deduction', 'employer_cost')),
    reduces_taxable_pay  boolean not null default false,                      -- taken before PAYE is worked out
    active               boolean not null default true,
    sort_order           int not null default 0,
    primary key (country_code, key)
);

grant select on table statutory_items to pesaroll_app;
