using System.Net;

namespace Pesaroll.Identity;

// Mapped to the tables in database/schema/identity/. EF Core never creates or changes them.

public sealed class User
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public required string Name { get; set; }
    public string? PasswordHash { get; set; }
    public Guid? SignupRoleId { get; set; }
    public string? SignupRoleOther { get; set; }
    public int FailedSigninCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public DateTimeOffset? PhoneVerifiedAt { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Session
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? OrganizationId { get; set; }
    public required byte[] TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public IPAddress? Ip { get; set; }
    public string? UserAgent { get; set; }
}

public sealed class MembershipRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A group of sign-up roles, e.g. Leadership (reference data).</summary>
public sealed class SignupRoleGroup
{
    public required string Code { get; set; }
    public required string NameEn { get; set; }
    public required string NameSw { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>An option for "What's your role in the business?" (reference data).</summary>
public sealed class SignupRole
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string GroupCode { get; set; }
    public required string NameEn { get; set; }
    public required string NameSw { get; set; }
    public required string DescriptionEn { get; set; }
    public required string DescriptionSw { get; set; }
    public int SortOrder { get; set; }
    public bool IsOther { get; set; }
    public bool Active { get; set; }
}
