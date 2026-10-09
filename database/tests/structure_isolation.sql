-- ONB-05: the org structure (divisions, departments, positions, units) stays inside its
-- organization; people limited to some companies see only what those companies use; codes are
-- unique per level; the department tree has no loops; a department in use can't be deleted.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  org2 constant uuid := '00000000-0000-7000-8000-000000000002';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ug1  constant uuid := '00000000-0000-7000-8000-0000000000a2';
  ke2  constant uuid := '00000000-0000-7000-8000-0000000000b1';
  div  constant uuid := '00000000-0000-7000-8000-000000000301';
  ops  constant uuid := '00000000-0000-7000-8000-000000000311';
  kit  constant uuid := '00000000-0000-7000-8000-000000000312';
  grill constant uuid := '00000000-0000-7000-8000-000000000313';
  cook constant uuid := '00000000-0000-7000-8000-000000000321';
  team constant uuid := '00000000-0000-7000-8000-000000000331';
  shared_pos constant uuid := '00000000-0000-7000-8000-000000000322';
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1 || ',' || ug1, true);
  insert into divisions (id, organization_id, code, name, all_companies) values (div, org1, 'RES', 'Restaurants', true);
  insert into departments (id, organization_id, code, name, division_id) values (ops, org1, 'OPS', 'Operations', div);
  insert into departments (id, organization_id, code, name, parent_id) values (kit, org1, 'KIT', 'Kitchen', ops);
  insert into departments (id, organization_id, code, name, parent_id) values (grill, org1, 'GRL', 'Grill', kit);
  insert into department_companies (organization_id, department_id, company_id) values (org1, ops, ke1), (org1, kit, ke1), (org1, grill, ke1);
  insert into positions (id, organization_id, code, title, department_id) values (cook, org1, 'COOK', 'Cook', kit);
  insert into positions (id, organization_id, code, title, all_companies) values (shared_pos, org1, 'CLK', 'Clerk', true);
  insert into position_companies (organization_id, position_id, company_id) values (org1, cook, ke1);
  insert into units (id, organization_id, code, name, department_id) values (team, org1, 'AM', 'Morning shift', kit);
  insert into unit_companies (organization_id, unit_id, company_id) values (org1, team, ke1);

  begin
    insert into departments (id, organization_id, code, name) values ('00000000-0000-7000-8000-000000000319', org1, 'KIT', 'Kitchen two');
    raise exception 'two departments share a code';
  exception when unique_violation then null;
  end;
  begin
    insert into positions (id, organization_id, code, title) values ('00000000-0000-7000-8000-000000000329', org1, 'cook', 'Lower');
    raise exception 'a lower-case code was saved';
  exception when check_violation then null;
  end;

  -- No loops: Operations can't go under Grill, which is under Kitchen, which is under Operations.
  begin
    update departments set parent_id = grill where id = ops;
    raise exception 'a department became its own ancestor';
  exception when check_violation then null;
  end;
  begin
    update departments set parent_id = kit where id = kit;
    raise exception 'a department became its own parent';
  exception when check_violation then null;
  end;

  -- In use: a department with positions, units or sub-departments, a division with departments.
  begin
    delete from departments where id = kit;
    raise exception 'a department with positions and units was deleted';
  exception when foreign_key_violation then null;
  end;
  begin
    delete from divisions where id = div;
    raise exception 'a division with departments was deleted';
  exception when foreign_key_violation then null;
  end;

  -- Someone who only sees the Uganda company sees the all-companies items, not Kenya's own.
  perform set_config('app.company_ids', ug1::text, true);
  if exists (select 1 from departments where id = kit) then raise exception 'company scope ignored for departments'; end if;
  if exists (select 1 from positions where id = cook) then raise exception 'company scope ignored for positions'; end if;
  if exists (select 1 from units where id = team) then raise exception 'company scope ignored for units'; end if;
  if not exists (select 1 from divisions where id = div) then raise exception 'an all-companies division is hidden'; end if;
  if not exists (select 1 from positions where id = shared_pos) then raise exception 'an all-companies position is hidden'; end if;

  -- Organization two sees nothing and can't link its companies to organization one's structure.
  perform set_config('app.org_id', org2::text, true);
  perform set_config('app.company_ids', ke2::text, true);
  if exists (select 1 from departments where id in (ops, kit, grill)) or exists (select 1 from divisions where id = div) then
    raise exception 'org two sees org one''s structure';
  end if;
  insert into departments (id, organization_id, code, name) values ('00000000-0000-7000-8000-000000000391', org2, 'KIT', 'Their kitchen');
  begin
    insert into department_companies (organization_id, department_id, company_id) values (org1, kit, ke2);
    raise exception 'org two linked to org one''s department';
  exception when insufficient_privilege or foreign_key_violation then null;
  end;
  begin
    insert into positions (id, organization_id, code, title, department_id) values ('00000000-0000-7000-8000-000000000392', org2, 'X1', 'Spy', kit);
    raise exception 'org two put a position in org one''s department';
  exception when foreign_key_violation then null;
  end;
end $$;
rollback;
