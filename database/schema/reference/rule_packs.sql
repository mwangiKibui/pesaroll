-- A country's statutory rates, bands, caps and rounding for pay dates from effective_from (PAY-01).
-- Reference data: the payroll engine reads the pack in force on a pay date. A pack applies until the
-- next one for its country starts. Once published a pack never changes (PAY-2011): a change in the
-- law is a new version, so past pay always matches the rules it was worked out with.
create table rule_packs (
    version         text primary key check (version ~ '^[A-Z]{2}-[0-9]{4}\.[0-9]{2}$'),   -- KE-2026.02
    country_code    char(2) not null references countries (code),
    effective_from  date not null,                                                          -- first pay date it applies to
    rules           jsonb not null,
    published_at    timestamptz,
    unique (country_code, effective_from),
    check (left(version, 2) = country_code)
);

grant select on table rule_packs to pesaroll_app;

create or replace function rule_packs_keep_published()
returns trigger
language plpgsql
as $$
begin
    if old.published_at is not null then
        raise exception 'PAY-2011';
    end if;
    return case when tg_op = 'DELETE' then old else new end;
end
$$;

create trigger rule_packs_keep_published before update or delete on rule_packs
    for each row execute function rule_packs_keep_published();

-- The version of the published pack in force for a country on a pay date, or null if there is none.
create or replace function rule_pack_version(p_country char(2), p_on date)
returns text
language sql stable
as $$
    select version from rule_packs
    where country_code = p_country and effective_from <= p_on and published_at is not null
    order by effective_from desc
    limit 1
$$;

-- Publishes a pack from post-deploy. Re-runnable: the same pack again changes nothing; different
-- rules, country or date under a published version are refused (PAY-2011), so the deploy fails.
create or replace function publish_rule_pack(p_country char(2), p_version text, p_from date, p_rules jsonb)
returns void
language plpgsql
as $$
begin
    insert into rule_packs (version, country_code, effective_from, rules, published_at)
    values (p_version, p_country, p_from, p_rules, now())
    on conflict (version) do nothing;
    if not exists (select 1 from rule_packs where version = p_version and country_code = p_country
                   and effective_from = p_from and rules = p_rules and published_at is not null) then
        raise exception 'PAY-2011';
    end if;
end
$$;

revoke all on function publish_rule_pack(char, text, date, jsonb) from public;
