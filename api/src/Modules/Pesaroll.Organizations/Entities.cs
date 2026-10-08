namespace Pesaroll.Organizations;

// Mapped to the tables in database/schema/. EF Core never creates or changes them.

public sealed class Organization
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Who onboarded the organization (ONB-02). Not an owner: roles come from security groups.</summary>
    public Guid OnboardedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Company
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string LegalName { get; set; }
    public required string CountryCode { get; set; }
    public required string Currency { get; set; }
    public string? RegistrationNo { get; set; }
    public byte[]? KraPinEnc { get; set; }
    public string? NssfEmployerNo { get; set; }
    public string? ShaEmployerNo { get; set; }
    public string? NitaNo { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Membership
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public string? JobRole { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
