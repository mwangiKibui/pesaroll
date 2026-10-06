-- Re-runnable: keeps the error catalogue in step with docs/standards/errors.md.
insert into error_codes (code, module, severity, message_en, fixable_by, playbook) values
  ('AUTH-1001', 'AUTH', 'warning', 'That email or phone number and password don''t match.', 'customer', 'Offer a password reset. Accounts lock after 5 failed tries.'),
  ('AUTH-1002', 'AUTH', 'warning', 'Your account is locked after too many attempts. Try again in 15 minutes or reset your password.', 'customer', 'Confirm identity before unlocking manually.'),
  ('ACC-8001',  'ACC',  'warning', 'You don''t have permission to do this. Ask your organization''s owner.', 'customer', 'Check the person''s role and company scope in the back office.'),
  ('IMP-3001',  'IMP',  'error',   'Row {row}: {column} is required.', 'customer', 'Point them to the template''s required columns.'),
  ('IMP-3002',  'IMP',  'error',   'Row {row}: "{value}" isn''t one of your {column} options.', 'customer', 'Check the option exists in setup, or fix the spelling.'),
  ('PAY-2001',  'PAY',  'error',   '{employee} has no KRA PIN, so PAYE can''t be filed.', 'customer', 'Add the KRA PIN on the employee''s profile, then rerun.'),
  ('PAY-2002',  'PAY',  'error',   '{employee}''s pay is below the minimum wage for {location}.', 'customer', 'Confirm pay and location; the run can''t be approved below the minimum.'),
  ('BILL-6001', 'BILL', 'warning', 'This company has more than {limit} active employees. Choose a plan to approve this pay run.', 'customer', 'Explain the plans; check for complimentary access first.'),
  ('PLAT-9001', 'PLAT', 'critical', 'Something went wrong on our side. Quote reference {reference} to support.', 'engineering', 'Look up the reference; escalate with the request id.')
on conflict (code) do update set
  module = excluded.module, severity = excluded.severity, message_en = excluded.message_en,
  fixable_by = excluded.fixable_by, playbook = excluded.playbook;
