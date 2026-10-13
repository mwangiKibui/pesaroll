namespace Pesaroll.Shared.Tenancy;

/// <summary>
/// The actions the app checks (ACC-01), by key; the same keys are listed in the permissions table
/// (post-deploy/0014_access_roles.sql), where roles bundle them. Code checks these keys, never role names.
/// </summary>
public static class Permissions
{
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

    public static readonly IReadOnlyList<string> All =
        [Companies, PaymentAccounts, Locations, Structure, Leave, Schedules, PayItems, Employees, PayrollHistory, Team];
}

/// <summary>A permission a person has from their groups: for all companies, or for these.</summary>
public sealed record AccessGrant(string Permission, bool AllCompanies, IReadOnlyList<Guid> CompanyIds)
{
    public bool Covers(Guid companyId) => AllCompanies || CompanyIds.Contains(companyId);
}
