namespace Pesaroll.Shared.Tenancy;

/// <summary>
/// The organization and companies the current request may see. Set once per request
/// (by sign-in, from AUTH) and read by <see cref="Data.TenantSessionInterceptor"/>,
/// which hands it to PostgreSQL row-level security.
/// </summary>
public sealed class TenantContext
{
    public Guid? OrganizationId { get; private set; }

    public IReadOnlyList<Guid> CompanyIds { get; private set; } = [];

    public void Set(Guid organizationId, IEnumerable<Guid> companyIds)
    {
        OrganizationId = organizationId;
        CompanyIds = companyIds.Distinct().ToArray();
    }

    public void Clear()
    {
        OrganizationId = null;
        CompanyIds = [];
    }
}
