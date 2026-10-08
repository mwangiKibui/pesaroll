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
    public string? TradingName { get; set; }
    public required string CountryCode { get; set; }
    public required string Currency { get; set; }
    public string? IndustryCode { get; set; }
    public string? AddressLine { get; set; }
    public string? Town { get; set; }
    public string? RegionCode { get; set; }
    public string? PostalAddress { get; set; }
    public string? PostalCode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<CompanyIdentifier> Identifiers { get; set; } = [];
}

/// <summary>A company's statutory or registration number for one of its country's fields.</summary>
public sealed class CompanyIdentifier
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    public required string FieldKey { get; set; }
    public required string Value { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// Reference data (database/schema/reference/countries.sql): read here, edited by the back office (BO-11).

public sealed class Country
{
    public required string Code { get; set; }
    public required string NameEn { get; set; }
    public required string NameSw { get; set; }
    public required string Currency { get; set; }
    public required string PhoneCode { get; set; }
    public required string RegionLabelEn { get; set; }
    public required string RegionLabelSw { get; set; }
    public bool Available { get; set; }
    public int SortOrder { get; set; }
}

public sealed class CountryCompanyField
{
    public required string CountryCode { get; set; }
    public required string Key { get; set; }
    public required string LabelEn { get; set; }
    public required string LabelSw { get; set; }
    public string? HintEn { get; set; }
    public string? HintSw { get; set; }
    public string? Example { get; set; }
    public string? Pattern { get; set; }
    public bool RequiredForPayroll { get; set; }
    public int SortOrder { get; set; }
    public bool Active { get; set; }
}

public sealed class CountryRegion
{
    public required string CountryCode { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
}

public sealed class Industry
{
    public required string Code { get; set; }
    public required string NameEn { get; set; }
    public required string NameSw { get; set; }
    public int SortOrder { get; set; }
    public bool Active { get; set; }
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

public sealed class Bank
{
    public required string CountryCode { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string LegalName { get; set; }
    public bool Active { get; set; }
    public int SortOrder { get; set; }
}

public sealed class BankBranch
{
    public required string CountryCode { get; set; }
    public required string BankCode { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// Where a company's payroll is paid from. Rows are never rewritten: the app may only move the
/// default or close one (database/schema/tenancy/companies.sql grants nothing else).
/// </summary>
public sealed class CompanyPaymentAccount
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>bank or mpesa.</summary>
    public required string Method { get; set; }
    public string? CountryCode { get; set; }
    public string? BankCode { get; set; }
    public string? BranchCode { get; set; }
    public string? AccountName { get; set; }
    /// <summary>Encrypted with <see cref="Pesaroll.Shared.Security.FieldEncryptor"/>; see PaymentAccountsEndpoints.Context.</summary>
    public byte[]? AccountNoEnc { get; set; }
    public string? AccountLast4 { get; set; }
    public string? MpesaShortcode { get; set; }
    public bool IsDefault { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedBy { get; set; }
}
