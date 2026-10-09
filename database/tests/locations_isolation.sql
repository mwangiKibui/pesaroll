-- ONB-04: locations stay inside their organization; people scoped to some companies see only the
-- locations those companies use (or that all companies use); a company has at most one main
-- location, from its own organization, and that location can't be deleted while it is main.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  org2 constant uuid := '00000000-0000-7000-8000-000000000002';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  ke2  constant uuid := '00000000-0000-7000-8000-0000000000b1';
  hq   constant uuid := '00000000-0000-7000-8000-000000000101';
  shop constant uuid := '00000000-0000-7000-8000-000000000102';
  shared constant uuid := '00000000-0000-7000-8000-000000000103';
  other constant uuid := '00000000-0000-7000-8000-000000000201';
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  insert into locations (id, organization_id, code, name, country_code, region_code, town, wage_area_code)
  values (hq, org1, 'NBO-HQ', 'Head office', 'KE', '047', 'Nairobi', 'cities'),
         (shop, org1, 'MSA-01', 'Mombasa shop', 'KE', '001', 'Mombasa', 'cities');
  insert into locations (id, organization_id, code, name, country_code, all_companies)
  values (shared, org1, 'ALL-01', 'Shared store', 'KE', true);
  insert into location_companies (organization_id, location_id, company_id) values (org1, hq, ke1), (org1, shop, ke1);
  update companies set main_location_id = hq where id = ke1;

  begin
    insert into locations (id, organization_id, code, name, country_code) values ('00000000-0000-7000-8000-000000000104', org1, 'NBO-HQ', 'Twin', 'KE');
    raise exception 'two locations in one organization share a code';
  exception when unique_violation then null;
  end;
  -- A location's type: branch unless said otherwise, and only the known types.
  if (select kind from locations where id = shop) <> 'branch' then raise exception 'a location should default to a branch'; end if;
  begin
    insert into locations (id, organization_id, code, name, country_code, kind) values ('00000000-0000-7000-8000-000000000107', org1, 'XX-3', 'Castle', 'KE', 'castle');
    raise exception 'a location of an unknown type was saved';
  exception when check_violation then null;
  end;
  begin
    insert into locations (id, organization_id, code, name, country_code, region_code) values ('00000000-0000-7000-8000-000000000105', org1, 'XX-1', 'Nowhere', 'KE', '999');
    raise exception 'a location in an unknown county was saved';
  exception when foreign_key_violation then null;
  end;
  begin
    insert into locations (id, organization_id, code, name, country_code, wage_area_code) values ('00000000-0000-7000-8000-000000000106', org1, 'XX-2', 'Nowhere', 'KE', 'moon');
    raise exception 'a location in an unknown wage area was saved';
  exception when foreign_key_violation then null;
  end;
  begin
    delete from locations where id = hq;
    raise exception 'a company''s main location was deleted';
  exception when foreign_key_violation then null;
  end;

  -- Someone who only sees the Uganda company sees the shared location, not Kenya's own ones.
  perform set_config('app.company_ids', ug1::text, true);
  if exists (select 1 from locations where id in (hq, shop)) then raise exception 'company scope ignored for locations'; end if;
  if not exists (select 1 from locations where id = shared) then raise exception 'an all-companies location is hidden'; end if;
  if exists (select 1 from location_companies where company_id = ke1) then raise exception 'company scope ignored for location links'; end if;

  -- Organization two sees nothing and can't link or point at organization one's locations.
  perform set_config('app.org_id', org2::text, true);
  perform set_config('app.company_ids', ke2::text, true);
  if exists (select 1 from locations where id in (hq, shop, shared)) then raise exception 'org two sees org one''s locations'; end if;
  insert into locations (id, organization_id, code, name, country_code) values (other, org2, 'NBO-HQ', 'Their head office', 'KE');
  begin
    update companies set main_location_id = hq where id = ke2;
    raise exception 'a company took another organization''s location as main';
  exception when foreign_key_violation then null;
  end;
  begin
    insert into location_companies (organization_id, location_id, company_id) values (org1, hq, ke2);
    raise exception 'org two linked a company to org one''s location';
  exception when insufficient_privilege or foreign_key_violation then null;
  end;
end $$;
rollback;
