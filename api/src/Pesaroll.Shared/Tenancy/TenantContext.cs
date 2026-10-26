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

    /// <summary>What they may do, from their security groups (ACC-02), each for all companies or some.</summary>
    public IReadOnlyList<AccessGrant> Grants { get; private set; } = [];

    /// <summary>The request's reference (or a job's), which the audit log records with every change (PLAT-03).</summary>
    public string? Reference { get; private set; }

    public void SetUser(Guid userId) => UserId = userId;

    public void SetReference(string reference) => Reference = reference;

    public void Set(Guid organizationId, IEnumerable<Guid> companyIds, IEnumerable<AccessGrant>? grants = null)
    {
        OrganizationId = organizationId;
        CompanyIds = companyIds.Distinct().ToArray();
        Grants = grants?.ToArray() ?? [];
    }

    /// <summary>Whether they may do this for every company (organization-wide setup: shared structure, all-company items).</summary>
    public bool Can(string permission) => Grants.Any(g => g.Permission == permission && g.AllCompanies);

    /// <summary>Whether they may do this for this company.</summary>
    public bool Can(string permission, Guid companyId) => Grants.Any(g => g.Permission == permission && g.Covers(companyId));

    /// <summary>The companies they may do this for (ACC-04): reads narrow to these, not to every company they belong to.</summary>
    public IReadOnlyList<Guid> CompaniesFor(string permission) =>
        CompanyIds.Where(id => Can(permission, id)).ToArray();

    public void Clear()
    {
        UserId = null;
        OrganizationId = null;
        CompanyIds = [];
        Grants = [];
    }
}
