-- PAY-01: statutory rates are data. Kenya's packs cover every pay date from 1 January 2026 (NSSF
-- Year 3 to 31 January, Year 4 from 1 February); a published pack never changes (PAY-2011), so a
-- change is a new version; two packs for a country can't start on the same date; the app reads only.
begin;
do $$
declare
  msg text;
begin
  -- The pack in force on a pay date is the latest one that started on or before it.
  if rule_pack_version('KE', date '2026-01-01') is distinct from 'KE-2025.02' then
    raise exception '1 Jan 2026 is %, not KE-2025.02', rule_pack_version('KE', date '2026-01-01');
  end if;
  if rule_pack_version('KE', date '2026-01-31') is distinct from 'KE-2025.02' then
    raise exception '31 Jan 2026 is %, not KE-2025.02 (NSSF Year 3)', rule_pack_version('KE', date '2026-01-31');
  end if;
  if rule_pack_version('KE', date '2026-02-01') is distinct from 'KE-2026.02' then
    raise exception '1 Feb 2026 is %, not KE-2026.02 (NSSF Year 4)', rule_pack_version('KE', date '2026-02-01');
  end if;
  if rule_pack_version('KE', date '2026-12-31') is distinct from 'KE-2026.02' then
    raise exception '31 Dec 2026 is %, not KE-2026.02', rule_pack_version('KE', date '2026-12-31');
  end if;
  if rule_pack_version('KE', date '2024-12-31') is not null then
    raise exception 'a pack is in force before the first one starts';
  end if;
  if rule_pack_version('UG', date '2026-06-30') is not null then
    raise exception 'Uganda has a pack';
  end if;

  -- What changed in February 2026: NSSF's limits; PAYE and the rest stay.
  if (select rules -> 'nssf' ->> 'upperLimit' from rule_packs where version = 'KE-2026.02') <> '108000'
     or (select rules -> 'nssf' ->> 'upperLimit' from rule_packs where version = 'KE-2025.02') <> '72000' then
    raise exception 'NSSF upper limits are not 72,000 (Year 3) and 108,000 (Year 4)';
  end if;
  if (select rules -> 'paye' from rule_packs where version = 'KE-2026.02') is distinct from (select rules -> 'paye' from rule_packs where version = 'KE-2025.02') then
    raise exception 'PAYE changed between the 2026 packs';
  end if;
  if exists (select 1 from rule_packs where published_at is null and country_code = 'KE') then
    raise exception 'a seeded Kenya pack is not published';
  end if;

  -- A published pack can't be changed or deleted, by anyone.
  begin
    update rule_packs set rules = jsonb_set(rules, '{shif,rate}', '0.03') where version = 'KE-2026.02';
    raise exception 'changed a published pack';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'PAY-2011' then raise exception 'expected PAY-2011 on update, got %', msg; end if;
  end;
  begin
    delete from rule_packs where version = 'KE-2026.02';
    raise exception 'deleted a published pack';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'PAY-2011' then raise exception 'expected PAY-2011 on delete, got %', msg; end if;
  end;

  -- Seeding again with the same rules changes nothing; with different rules it's refused (a new version is needed).
  perform publish_rule_pack('KE', 'KE-2026.02', date '2026-02-01', (select rules from rule_packs where version = 'KE-2026.02'));
  begin
    perform publish_rule_pack('KE', 'KE-2026.02', date '2026-02-01',
      jsonb_set((select rules from rule_packs where version = 'KE-2026.02'), '{shif,rate}', '0.03'));
    raise exception 'seeded different rules under a published version';
  exception when others then
    get stacked diagnostics msg = message_text;
    if msg <> 'PAY-2011' then raise exception 'expected PAY-2011 when re-seeding changed rules, got %', msg; end if;
  end;

  -- Two packs for a country can't start on the same day.
  begin
    insert into rule_packs (country_code, version, effective_from, rules) values ('KE', 'KE-2026.99', date '2026-02-01', '{}');
    raise exception 'two Kenya packs start on 1 Feb 2026';
  exception when unique_violation then null;
  end;

  -- The app reads packs, and can't write them.
  set local role pesaroll_app;
  if rule_pack_version('KE', date '2026-03-31') is distinct from 'KE-2026.02' then raise exception 'the app can''t read the packs'; end if;
  begin
    insert into rule_packs (country_code, version, effective_from, rules) values ('KE', 'KE-2027.01', date '2027-01-01', '{}');
    raise exception 'the app added a pack';
  exception when insufficient_privilege then null;
  end;
  reset role;
end
$$;
rollback;
