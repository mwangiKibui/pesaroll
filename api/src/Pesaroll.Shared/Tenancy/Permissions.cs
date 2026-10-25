namespace Pesaroll.Shared.Tenancy;

/// <summary>
/// The actions the app checks (ACC-01), by key; the same keys are listed in the permissions table
/// (post-deploy/0014_access_roles.sql), where roles bundle them. Code checks these keys, never role names.
/// Each is an area's View or Manage (ACC-04).
/// </summary>
public static class Permissions
{
    // Manage (ACC-01).
    public const string Companies = "companies.manage";
    public const string PaymentAccounts = "payment_accounts.manage";
    public const string Locations = "locations.manage";
    public const string Structure = "structure.manage";
    public const string Leave = "leave.manage";
    public const string Schedules = "schedules.manage";
    public const string PayItems = "pay_items.manage";
    public const string Employees = "employees.manage";
    public const string PayrollHistory = "payroll_history.manage";
    public const string Team = "team.manage";
    public const string Pay = "pay.manage";
    public const string Workflows = "workflows.manage";
    public const string PayRunApprovals = "pay_run_approvals.manage";

    // View (ACC-04): each area's Manage brings its View.
    public const string CompaniesView = "companies.view";
    public const string PaymentAccountsView = "payment_accounts.view";
    public const string LocationsView = "locations.view";
    public const string StructureView = "structure.view";
    public const string LeaveView = "leave.view";
    public const string SchedulesView = "schedules.view";
    public const string PayItemsView = "pay_items.view";
    public const string EmployeesView = "employees.view";
    public const string PayrollHistoryView = "payroll_history.view";
    public const string TeamView = "team.view";
    public const string PayView = "pay.view";
    public const string WorkflowsView = "workflows.view";
    public const string PayRunApprovalsView = "pay_run_approvals.view";

    public static readonly IReadOnlyList<string> All =
        [Companies, PaymentAccounts, Locations, Structure, Leave, Schedules, PayItems, Employees, PayrollHistory, Team, Pay, Workflows, PayRunApprovals,
         CompaniesView, PaymentAccountsView, LocationsView, StructureView, LeaveView, SchedulesView, PayItemsView, EmployeesView, PayrollHistoryView, TeamView, PayView, WorkflowsView, PayRunApprovalsView];
}

/// <summary>A permission a person has from their groups: for all companies, or for these.</summary>
public sealed record AccessGrant(string Permission, bool AllCompanies, IReadOnlyList<Guid> CompanyIds)
{
    public bool Covers(Guid companyId) => AllCompanies || CompanyIds.Contains(companyId);
}
