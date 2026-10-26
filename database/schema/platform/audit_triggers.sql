-- PLAT-03: the tables whose changes go to the audit log (platform/audit_log.sql). Every table with
-- organization_id is here or in audit_left_out(); database/tests/audit_log.sql checks it.

create trigger approval_actions_audit after insert or update or delete on approval_actions
    for each row execute function audit_change();

create trigger approval_request_approvers_audit after insert or update or delete on approval_request_approvers
    for each row execute function audit_change();

create trigger approval_requests_audit after insert or update or delete on approval_requests
    for each row execute function audit_change();

create trigger companies_audit after insert or update or delete on companies
    for each row execute function audit_change();

create trigger company_identifiers_audit after insert or update or delete on company_identifiers
    for each row execute function audit_change();

create trigger company_payment_accounts_audit after insert or update or delete on company_payment_accounts
    for each row execute function audit_change();

create trigger compensations_audit after insert or update or delete on compensations
    for each row execute function audit_change();

create trigger department_companies_audit after insert or update or delete on department_companies
    for each row execute function audit_change();

create trigger departments_audit after insert or update or delete on departments
    for each row execute function audit_change();

create trigger division_companies_audit after insert or update or delete on division_companies
    for each row execute function audit_change();

create trigger divisions_audit after insert or update or delete on divisions
    for each row execute function audit_change();

create trigger employee_pay_items_audit after insert or update or delete on employee_pay_items
    for each row execute function audit_change();

create trigger employee_payment_accounts_audit after insert or update or delete on employee_payment_accounts
    for each row execute function audit_change();

create trigger employee_statutory_deductions_audit after insert or update or delete on employee_statutory_deductions
    for each row execute function audit_change();

create trigger employees_audit after insert or update or delete on employees
    for each row execute function audit_change();

create trigger employments_audit after insert or update or delete on employments
    for each row execute function audit_change();

create trigger invitation_companies_audit after insert or update or delete on invitation_companies
    for each row execute function audit_change();

create trigger invitations_audit after insert or update or delete on invitations
    for each row execute function audit_change();

create trigger leave_plan_companies_audit after insert or update or delete on leave_plan_companies
    for each row execute function audit_change();

create trigger leave_plan_rules_audit after insert or update or delete on leave_plan_rules
    for each row execute function audit_change();

create trigger leave_plans_audit after insert or update or delete on leave_plans
    for each row execute function audit_change();

create trigger leave_types_audit after insert or update or delete on leave_types
    for each row execute function audit_change();

create trigger location_companies_audit after insert or update or delete on location_companies
    for each row execute function audit_change();

create trigger locations_audit after insert or update or delete on locations
    for each row execute function audit_change();

create trigger memberships_audit after insert or update or delete on memberships
    for each row execute function audit_change();

create trigger organizations_audit after insert or update or delete on organizations
    for each row execute function audit_change();

create trigger pay_item_companies_audit after insert or update or delete on pay_item_companies
    for each row execute function audit_change();

create trigger pay_items_audit after insert or update or delete on pay_items
    for each row execute function audit_change();

create trigger pay_run_deduction_choices_audit after insert or update or delete on pay_run_deduction_choices
    for each row execute function audit_change();

create trigger pay_run_inputs_audit after insert or update or delete on pay_run_inputs
    for each row execute function audit_change();

create trigger pay_runs_audit after insert or update or delete on pay_runs
    for each row execute function audit_change();

create trigger pay_schedule_companies_audit after insert or update or delete on pay_schedule_companies
    for each row execute function audit_change();

create trigger pay_schedule_deductions_audit after insert or update or delete on pay_schedule_deductions
    for each row execute function audit_change();

create trigger pay_schedule_reminders_audit after insert or update or delete on pay_schedule_reminders
    for each row execute function audit_change();

create trigger pay_schedules_audit after insert or update or delete on pay_schedules
    for each row execute function audit_change();

create trigger payees_audit after insert or update or delete on payees
    for each row execute function audit_change();

create trigger payroll_history_batches_audit after insert or update or delete on payroll_history_batches
    for each row execute function audit_change();

create trigger position_companies_audit after insert or update or delete on position_companies
    for each row execute function audit_change();

create trigger positions_audit after insert or update or delete on positions
    for each row execute function audit_change();

create trigger role_companies_audit after insert or update or delete on role_companies
    for each row execute function audit_change();

create trigger role_customization_permissions_audit after insert or update or delete on role_customization_permissions
    for each row execute function audit_change();

create trigger role_customizations_audit after insert or update or delete on role_customizations
    for each row execute function audit_change();

create trigger roles_audit after insert or update or delete on roles
    for each row execute function audit_change();

create trigger security_group_companies_audit after insert or update or delete on security_group_companies
    for each row execute function audit_change();

create trigger security_group_members_audit after insert or update or delete on security_group_members
    for each row execute function audit_change();

create trigger security_group_roles_audit after insert or update or delete on security_group_roles
    for each row execute function audit_change();

create trigger security_groups_audit after insert or update or delete on security_groups
    for each row execute function audit_change();

create trigger unit_companies_audit after insert or update or delete on unit_companies
    for each row execute function audit_change();

create trigger units_audit after insert or update or delete on units
    for each row execute function audit_change();

create trigger workflow_step_people_audit after insert or update or delete on workflow_step_people
    for each row execute function audit_change();

create trigger workflow_steps_audit after insert or update or delete on workflow_steps
    for each row execute function audit_change();

create trigger workflows_audit after insert or update or delete on workflows
    for each row execute function audit_change();
