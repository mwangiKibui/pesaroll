-- What can be sent for approval (WF-03). Re-runnable upserts. Leave becomes required when leave
-- requests exist (PRT-03).
insert into workflow_kinds (key, name_en, name_sw, required, suggested_steps, sort_order) values
  ('pay_run', 'Pay runs', 'Malipo ya mishahara', true,
   '[{"approvers": "permission", "permission": "pay_run_approvals.manage", "fallback": "companies.manage"}]', 1),
  ('leave',   'Leave',    'Likizo',              false,
   '[{"approvers": "line_manager", "fallback": "leave.manage"}]', 2)
on conflict (key) do update set
  name_en = excluded.name_en, name_sw = excluded.name_sw, required = excluded.required,
  suggested_steps = excluded.suggested_steps, sort_order = excluded.sort_order;
