-- Wage orders (PAY-11): a country's minimum wages and working hours from effective_from, reference
-- data like rule packs. Wage orders change on their own dates (usually Labour Day), apart from tax
-- law, so they are kept here rather than in a rule pack. An order applies until the next one for its
-- country starts; a run uses the one in force on its pay date.
--   hours_per_day    turns a daily rate into an hourly one (overtime for people paid by the day)
--   hours_per_month  turns a monthly salary into an hourly one (52 hours a week: about 225)
create table wage_orders (
    country_code     char(2) not null references countries (code),
    effective_from   date not null,
    source           text not null,                                           -- the legal notice
    hours_per_day    numeric(5, 2) not null check (hours_per_day > 0 and hours_per_day <= 24),
    hours_per_month  numeric(6, 2) not null check (hours_per_month > 0 and hours_per_month <= 744),
    primary key (country_code, effective_from)
);

-- The lowest minimum in each wage area (country_wage_areas): Kenya's general labourer, as gazetted.
-- Amounts keep the order's three decimals (144.054 an hour), so a rate is never let through by
-- rounding; monthly excludes the housing allowance, daily and hourly include it.
create table minimum_wages (
    country_code     char(2) not null,
    effective_from   date not null,
    wage_area_code   text not null,
    monthly          numeric(18, 3) not null check (monthly > 0),
    daily            numeric(18, 3) not null check (daily > 0),
    hourly           numeric(18, 3) not null check (hourly > 0),
    primary key (country_code, effective_from, wage_area_code),
    foreign key (country_code, effective_from) references wage_orders (country_code, effective_from) on delete cascade,
    foreign key (country_code, wage_area_code) references country_wage_areas (country_code, code)
);

grant select on table wage_orders to pesaroll_app;
grant select on table minimum_wages to pesaroll_app;
