-- PAY-04: pay changed from an earlier date says who made it and why; a reason is never blank; a
-- wrong rate replaced on the same date is kept beside the one that replaces it.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  joy  constant uuid := '00000000-0000-7000-8000-000000000e31';
  someone uuid;
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1::text, true);
  select id into someone from users limit 1;
  insert into employees (id, organization_id, company_id, staff_no, first_name, last_name, hire_date)
  values (joy, org1, ke1, 'CHG-01', 'Joy', 'Wambui', '2024-01-15');

  insert into compensations (id, organization_id, company_id, employee_id, valid_from, valid_to, basic_amount, pay_basis, currency)
  values (uuidv7(), org1, ke1, joy, '2024-01-15', '2026-09-30', 450, 'daily', 'KES');
  insert into compensations (id, organization_id, company_id, employee_id, valid_from, basic_amount, pay_basis, currency, changed_by, reason)
  values (uuidv7(), org1, ke1, joy, '2026-10-01', 800, 'daily', 'KES', someone, 'Raised to the minimum wage');

  begin
    insert into compensations (id, organization_id, company_id, employee_id, valid_from, valid_to, basic_amount, pay_basis, currency, reason)
    values (uuidv7(), org1, ke1, joy, '2023-01-01', '2023-12-31', 400, 'daily', 'KES', '   ');
    raise exception 'a blank reason was accepted';
  exception when check_violation then null;
  end;

  -- A wrong rate replaced on the same date is kept beside the live one; two live rows can't start the same day.
  update compensations set replaced_by = someone, replaced_at = now() where employee_id = joy and valid_from = '2026-10-01';
  insert into compensations (id, organization_id, company_id, employee_id, valid_from, basic_amount, pay_basis, currency, changed_by, reason)
  values (uuidv7(), org1, ke1, joy, '2026-10-01', 850, 'daily', 'KES', someone, 'The agreed rate was 850');
  begin
    insert into compensations (id, organization_id, company_id, employee_id, valid_from, valid_to, basic_amount, pay_basis, currency)
    values (uuidv7(), org1, ke1, joy, '2026-10-01', '2026-10-02', 900, 'daily', 'KES');
    raise exception 'two live rows started on the same day';
  exception when unique_violation then null;
  end;
  begin
    update compensations set replaced_at = now() where employee_id = joy and valid_from = '2024-01-15';
    raise exception 'replaced without saying by whom';
  exception when check_violation then null;
  end;

  if (select count(*) from compensations where employee_id = joy and reason is not null and changed_by = someone) <> 2 then
    raise exception 'the reason and who made the change were not kept';
  end if;
end $$;
rollback;
