-- ONB-03: a company's paying accounts. They stay inside their organization and company scope,
-- a company has one current default, the bank and branch must be real, and history is kept:
-- the app can close an account but never rewrite or delete one.
begin;
set local role pesaroll_app;
do $$
declare
  org1 constant uuid := '00000000-0000-7000-8000-000000000001';
  org2 constant uuid := '00000000-0000-7000-8000-000000000002';
  owner1 constant uuid := '00000000-0000-7000-8000-0000000000f1';
  ke1  constant uuid := '00000000-0000-7000-8000-0000000000a1';
  ke2  constant uuid := '00000000-0000-7000-8000-0000000000b1';
  acc1 constant uuid := '00000000-0000-7000-8000-0000000000e1';
begin
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', ke1::text, true);
  insert into company_payment_accounts (id, organization_id, company_id, method, country_code, bank_code, branch_code,
                                        account_name, account_no_enc, account_last4, is_default, created_by)
  values (acc1, org1, ke1, 'bank', 'KE', '68', '047', 'One Kenya Ltd', '\x01', '1234', true, owner1);
  insert into company_payment_accounts (id, organization_id, company_id, method, mpesa_shortcode, is_default, created_by)
  values ('00000000-0000-7000-8000-0000000000e2', org1, ke1, 'mpesa', '600123', false, owner1);

  begin
    insert into company_payment_accounts (id, organization_id, company_id, method, mpesa_shortcode, is_default, created_by)
    values ('00000000-0000-7000-8000-0000000000e3', org1, ke1, 'mpesa', '600124', true, owner1);
    raise exception 'a company got two current default accounts';
  exception when unique_violation then null;
  end;

  begin
    insert into company_payment_accounts (id, organization_id, company_id, method, country_code, bank_code, branch_code,
                                          account_name, account_no_enc, account_last4, is_default, created_by)
    values ('00000000-0000-7000-8000-0000000000e4', org1, ke1, 'bank', 'KE', '68', '999', 'One Kenya Ltd', '\x01', '1234', false, owner1);
    raise exception 'an account at an unknown branch was saved';
  exception when foreign_key_violation then null;
  end;

  begin
    insert into company_payment_accounts (id, organization_id, company_id, method, mpesa_shortcode, is_default, created_by)
    values ('00000000-0000-7000-8000-0000000000e5', org1, ke1, 'bank', null, false, owner1);
    raise exception 'a bank account without bank details was saved';
  exception when check_violation then null;
  end;

  begin
    insert into company_payment_accounts (id, organization_id, company_id, method, mpesa_shortcode, is_default, created_by)
    values ('00000000-0000-7000-8000-0000000000e6', org1, ke1, 'mpesa', '12', false, owner1);
    raise exception 'a two-digit M-Pesa shortcode was saved';
  exception when check_violation then null;
  end;

  -- History: the account number can't be rewritten and rows can't be deleted, only closed.
  begin
    update company_payment_accounts set account_no_enc = '\x02' where id = acc1;
    raise exception 'an account number was rewritten';
  exception when insufficient_privilege then null;
  end;
  begin
    delete from company_payment_accounts where id = acc1;
    raise exception 'an account was deleted';
  exception when insufficient_privilege then null;
  end;
  update company_payment_accounts set closed_at = now(), is_default = false where id = acc1;
  if (select closed_at from company_payment_accounts where id = acc1) is null then raise exception 'closing an account did not stick'; end if;
  -- With the old default closed, another can be the default.
  update company_payment_accounts set is_default = true where id = '00000000-0000-7000-8000-0000000000e2';

  -- Editing is replacing: the new row points at the one it replaced, which must exist.
  insert into company_payment_accounts (id, organization_id, company_id, method, country_code, bank_code, branch_code,
                                        account_name, account_no_enc, account_last4, is_default, created_by, replaces_id)
  values ('00000000-0000-7000-8000-0000000000e8', org1, ke1, 'bank', 'KE', '01', '100', 'One Kenya Ltd', '\x01', '1234', false, owner1, acc1);
  begin
    insert into company_payment_accounts (id, organization_id, company_id, method, mpesa_shortcode, is_default, created_by, replaces_id)
    values ('00000000-0000-7000-8000-0000000000e9', org1, ke1, 'mpesa', '600126', false, owner1, '00000000-0000-7000-8000-0000000000ff');
    raise exception 'a replacement for an unknown account was saved';
  exception when foreign_key_violation then null;
  end;

  -- Organization two sees none of it and can't write into organization one.
  perform set_config('app.org_id', org2::text, true);
  perform set_config('app.company_ids', ke2::text, true);
  if exists (select 1 from company_payment_accounts) then raise exception 'org two sees org one''s paying accounts'; end if;
  begin
    insert into company_payment_accounts (id, organization_id, company_id, method, mpesa_shortcode, is_default, created_by)
    values ('00000000-0000-7000-8000-0000000000e7', org1, ke1, 'mpesa', '600125', false, owner1);
    raise exception 'org two wrote a paying account into org one';
  exception when insufficient_privilege then null;
  end;

  -- Inside organization one, someone without the company in scope doesn't see its accounts.
  perform set_config('app.org_id', org1::text, true);
  perform set_config('app.company_ids', '00000000-0000-7000-8000-0000000000a2', true);
  if exists (select 1 from company_payment_accounts) then raise exception 'company scope ignored for paying accounts'; end if;
end $$;
rollback;
