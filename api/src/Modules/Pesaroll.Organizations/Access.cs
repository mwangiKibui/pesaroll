using Pesaroll.Shared.Tenancy;

namespace Pesaroll.Organizations;

/// <summary>
/// Permission checks on the server (ACC-02), from the person's groups (TenantContext.Grants).
/// Organization-wide setup (shared structure, locations, schedules, leave, earnings and deductions,
/// new companies) needs the permission for every company; a company's own records (its employees,
/// paying accounts, payroll history, the company itself) need it for that company.
/// </summary>
internal static class Access
{
    public static void Require(TenantContext tenant, string permission)
    {
        if (!tenant.Can(permission))
        {
            throw OnboardingErrors.NoPermission();
        }
    }

    public static void Require(TenantContext tenant, string permission, Guid companyId)
    {
        if (!tenant.Can(permission, companyId))
        {
            throw OnboardingErrors.NoPermission();
        }
    }
}
