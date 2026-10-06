namespace Pesaroll.Shared.Tenancy;

/// <summary>
/// Who the current request is for: the signed-in person, their current organization and the
/// companies they may see. Filled once per request by the session middleware and read by
/// <see cref="Data.TenantSessionInterceptor"/>, which hands it to PostgreSQL row-level security.
/// </summary>
public sealed class TenantContext
{
    public Guid? UserId { get; private set; }

    public Guid? OrganizationId { get; private set; }

    public IReadOnlyList<Guid> CompanyIds { get; private set; } = [];

    public void SetUser(Guid userId) => UserId = userId;

    public void Set(Guid organizationId, IEnumerable<Guid> companyIds)
    {
        OrganizationId = organizationId;
        CompanyIds = companyIds.Distinct().ToArray();
    }

    public void Clear()
    {
        UserId = null;
        OrganizationId = null;
        CompanyIds = [];
    }
}
